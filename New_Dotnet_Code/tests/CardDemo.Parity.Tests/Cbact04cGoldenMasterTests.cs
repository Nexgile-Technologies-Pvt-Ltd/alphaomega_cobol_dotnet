using CardDemo.Batch;
using CardDemo.Cobol.Runtime;
using CardDemo.Data;
using CardDemo.Tooling;
using Xunit;

namespace CardDemo.Parity.Tests;

/// <summary>
/// The real proof for CBACT04C: compile and run the ORIGINAL COBOL under GnuCOBOL on ASCII inputs
/// generated from the EBCDIC source, then run the .NET port on the same inputs, and diff the resulting
/// transaction dataset byte-for-byte (masking the two clock-derived timestamp fields). Skipped when
/// GnuCOBOL is not installed.
/// </summary>
public class Cbact04cGoldenMasterTests
{
    private const string ParmDate = "2022071800";
    private static readonly HostKind Host = HostKind.Ascii; // GnuCOBOL runs ASCII/native

    [Fact]
    public void Cbact04c_systran_output_matches_gnucobol_byte_for_byte()
    {
        GnuCobolInstall? install = GnuCobolInstall.TryLocate();
        if (install is null) return; // oracle not available here

        var l = new
        {
            TcatBal = Parse("CVTRA01Y.cpy"),
            Xref = Parse("CVACT03Y.cpy"),
            Account = Parse("CVACT01Y.cpy"),
            DiscGrp = Parse("CVTRA02Y.cpy"),
            Tran = Parse("CVTRA05Y.cpy"),
        };

        string work = Path.Combine(Path.GetTempPath(), "carddemo_gm_cbact04c");
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);

        // 1) Generate ASCII input images from the EBCDIC source of truth.
        byte[] tcat = ToAscii("AWS.M2.CARDDEMO.TCATBALF.PS", l.TcatBal);
        byte[] xref = ToAscii("AWS.M2.CARDDEMO.CARDXREF.PS", l.Xref);
        byte[] acct = ToAscii("AWS.M2.CARDDEMO.ACCTDATA.PS", l.Account);
        byte[] disc = ToAscii("AWS.M2.CARDDEMO.DISCGRP.PS", l.DiscGrp);
        File.WriteAllBytes(Path.Combine(work, "tcat.seq"), tcat);
        File.WriteAllBytes(Path.Combine(work, "xref.seq"), xref);
        File.WriteAllBytes(Path.Combine(work, "acct.seq"), acct);
        File.WriteAllBytes(Path.Combine(work, "disc.seq"), disc);

        // 2) Compile CBACT04C as a module + the loaders/driver as executables.
        var harness = new GnuCobolHarness(install);
        CompileModule(harness, work, "CBACT04C", CardDemoPaths.Program("CBACT04C.cbl"), [CardDemoPaths.CopybookDir]);
        CompileExe(harness, work, "LOADTCAT", OracleCobolFixtures.LoadTcat);
        CompileExe(harness, work, "LOADXREF", OracleCobolFixtures.LoadXref);
        CompileExe(harness, work, "LOADACCT", OracleCobolFixtures.LoadAcct);
        CompileExe(harness, work, "LOADDISC", OracleCobolFixtures.LoadDisc);
        CompileExe(harness, work, "RUNB04", OracleCobolFixtures.RunB04);

        // 3) Load the indexed files, then run CBACT04C via the driver.
        string Idx(string n) => Path.Combine(work, n + ".idx");
        RunOk(harness, work, "LOADTCAT", new() { ["LDIN"] = Path.Combine(work, "tcat.seq"), ["TCATBALF"] = Idx("TCATBALF") });
        RunOk(harness, work, "LOADXREF", new() { ["LDIN"] = Path.Combine(work, "xref.seq"), ["XREFFILE"] = Idx("XREFFILE") });
        RunOk(harness, work, "LOADACCT", new() { ["LDIN"] = Path.Combine(work, "acct.seq"), ["ACCTFILE"] = Idx("ACCTFILE") });
        RunOk(harness, work, "LOADDISC", new() { ["LDIN"] = Path.Combine(work, "disc.seq"), ["DISCGRP"] = Idx("DISCGRP") });

        string sysTranPath = Path.Combine(work, "systran.out");
        RunOk(harness, work, "RUNB04", new()
        {
            ["TCATBALF"] = Idx("TCATBALF"),
            ["XREFFILE"] = Idx("XREFFILE"),
            ["ACCTFILE"] = Idx("ACCTFILE"),
            ["DISCGRP"] = Idx("DISCGRP"),
            ["TRANSACT"] = sysTranPath,
            ["COB_LIBRARY_PATH"] = work + Path.PathSeparator + Path.Combine(install.HomeDir, "extras"),
        });

        byte[] cobolOut = File.ReadAllBytes(sysTranPath);

        // 4) Run the .NET port on the same ASCII inputs.
        byte[] dotnetOut = RunDotNetPort(l.TcatBal, l.Xref, l.Account, l.DiscGrp, l.Tran, tcat, xref, acct, disc);

        // 5) Compare byte-for-byte, masking the two 26-byte timestamp fields per 350-byte record.
        Assert.Equal(cobolOut.Length, dotnetOut.Length);
        Assert.True(cobolOut.Length % 350 == 0, $"SYSTRAN length {cobolOut.Length} not a multiple of 350");

        int origTs = l.Tran.Field("TRAN-ORIG-TS").Offset;
        int procTs = l.Tran.Field("TRAN-PROC-TS").Offset;
        MaskTimestamps(cobolOut, origTs, procTs);
        MaskTimestamps(dotnetOut, origTs, procTs);

        for (int i = 0; i < cobolOut.Length; i++)
        {
            if (cobolOut[i] != dotnetOut[i])
            {
                int rec = i / 350, off = i % 350;
                FieldDef? f = l.Tran.Fields.FirstOrDefault(x => off >= x.Offset && off < x.Offset + x.Length);
                Assert.Fail($"SYSTRAN mismatch at record {rec} byte {off} (field '{f?.Name}'): " +
                            $"GnuCOBOL 0x{cobolOut[i]:X2} vs .NET 0x{dotnetOut[i]:X2}");
            }
        }
        Assert.True(cobolOut.Length > 0, "expected non-empty SYSTRAN output");
    }

    private static byte[] RunDotNetPort(RecordLayout tcatL, RecordLayout xrefL, RecordLayout acctL,
        RecordLayout discL, RecordLayout tranL, byte[] tcat, byte[] xref, byte[] acct, byte[] disc)
    {
        using var db = new CardDemoDatabase();
        VsamFile tcatF = db.DefineFile(CardDemoFiles.TranCatBal.Definition);
        VsamFile xrefF = db.DefineFile(CardDemoFiles.CardXref.Definition);
        VsamFile acctF = db.DefineFile(CardDemoFiles.Account.Definition);
        VsamFile discF = db.DefineFile(CardDemoFiles.DiscGroup.Definition);
        SequentialFile sysTran = db.DefineSequentialFile("SYSTRAN", 350);

        Load(tcatF, tcat, 50);
        Load(xrefF, xref, 50);
        Load(acctF, acct, 300);
        Load(discF, disc, 50);

        var ctx = new Cbact04cContext(tcatF, xrefF, acctF, discF, sysTran,
            tcatL, xrefL, acctL, discL, tranL,
            new FixedClock(new DateTime(2022, 7, 18, 12, 0, 0)), Host, ParmDate);
        new Cbact04cInterestCalculator(ctx).Run();
        return sysTran.ToImage();
    }

    private static void Load(VsamFile f, byte[] data, int reclen)
    {
        for (int i = 0; i < data.Length; i += reclen)
            Assert.Equal(FileStatus.Ok, f.Write(data[i..(i + reclen)]));
    }

    private static RecordLayout Parse(string cpy) =>
        CopybookParser.Parse(File.ReadAllText(CardDemoPaths.Copybook(cpy)));

    private static byte[] ToAscii(string ebcdicFile, RecordLayout layout)
    {
        byte[] eb = File.ReadAllBytes(CardDemoPaths.EbcdicData(ebcdicFile));
        var outBuf = new byte[eb.Length];
        for (int i = 0; i < eb.Length; i += layout.Length)
        {
            var rec = new ReadOnlySpan<byte>(eb, i, layout.Length);
            FixedRecord.Parse(layout, rec, HostKind.Ebcdic).ToBytes(HostKind.Ascii).CopyTo(outBuf, i);
        }
        return outBuf;
    }

    private static void CompileModule(GnuCobolHarness h, string work, string name, string src, string[] copyDirs)
    {
        ProcessResult r = h.CompileModule(src, copyDirs, work);
        Assert.True(r.Success && File.Exists(Path.Combine(work, name + ".dll")),
            $"compile {name} failed: {r.StdErr}\n{r.StdOut}");
    }

    private static void CompileExe(GnuCobolHarness h, string work, string name, string source)
    {
        string src = Path.Combine(work, name + ".cob");
        File.WriteAllText(src, source);
        ProcessResult r = h.CompileExecutable(src, [], name + ".exe", work);
        Assert.True(r.Success && File.Exists(Path.Combine(work, name + ".exe")),
            $"compile {name} failed: {r.StdErr}\n{r.StdOut}");
    }

    private static void RunOk(GnuCobolHarness h, string work, string name, Dictionary<string, string> env)
    {
        ProcessResult r = h.Run(Path.Combine(work, name + ".exe"), work, env);
        Assert.True(r.Success, $"run {name} failed (exit {r.ExitCode}): {r.StdErr}\n{r.StdOut}");
    }

    private static void MaskTimestamps(byte[] data, int origTs, int procTs)
    {
        for (int rec = 0; rec < data.Length; rec += 350)
        {
            Array.Fill(data, (byte)'#', rec + origTs, 26);
            Array.Fill(data, (byte)'#', rec + procTs, 26);
        }
    }
}
