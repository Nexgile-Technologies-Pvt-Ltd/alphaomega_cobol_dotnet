using CardDemo.Batch;
using CardDemo.Cobol.Runtime;
using CardDemo.Data;
using CardDemo.Tooling;
using Xunit;

namespace CardDemo.Parity.Tests;

/// <summary>
/// Byte-for-byte golden master for CBTRN02C (transaction posting). Runs the original COBOL under
/// GnuCOBOL and the .NET port on the same ASCII inputs, then compares both output datasets: the rejects
/// file (fully deterministic) and the unloaded transaction file (masking only TRAN-PROC-TS, the one
/// clock-derived field). Skipped when GnuCOBOL is not installed.
/// </summary>
public class Cbtrn02cGoldenMasterTests
{
    private static readonly HostKind Host = HostKind.Ascii;

    [Fact]
    public void Cbtrn02c_outputs_match_gnucobol_byte_for_byte()
    {
        GnuCobolInstall? install = GnuCobolInstall.TryLocate();
        if (install is null) return;

        var l = new
        {
            DalyTran = Parse("CVTRA06Y.cpy"),
            Tran = Parse("CVTRA05Y.cpy"),
            Xref = Parse("CVACT03Y.cpy"),
            Account = Parse("CVACT01Y.cpy"),
            TcatBal = Parse("CVTRA01Y.cpy"),
        };

        string work = Path.Combine(Path.GetTempPath(), "carddemo_gm_cbtrn02c");
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);

        // 1) ASCII inputs from the EBCDIC source.
        byte[] daly = ToAscii("AWS.M2.CARDDEMO.DALYTRAN.PS", l.DalyTran);
        byte[] xref = ToAscii("AWS.M2.CARDDEMO.CARDXREF.PS", l.Xref);
        byte[] acct = ToAscii("AWS.M2.CARDDEMO.ACCTDATA.PS", l.Account);
        byte[] tcat = ToAscii("AWS.M2.CARDDEMO.TCATBALF.PS", l.TcatBal);
        File.WriteAllBytes(Path.Combine(work, "daly.seq"), daly);
        File.WriteAllBytes(Path.Combine(work, "xref.seq"), xref);
        File.WriteAllBytes(Path.Combine(work, "acct.seq"), acct);
        File.WriteAllBytes(Path.Combine(work, "tcat.seq"), tcat);

        // 2) Compile CBTRN02C (-x main) + loaders + unloader.
        var h = new GnuCobolHarness(install);
        CompileExeFromFile(h, work, "CBTRN02C", CardDemoPaths.Program("CBTRN02C.cbl"), [CardDemoPaths.CopybookDir]);
        CompileExeFromSource(h, work, "LOADXREF", OracleCobolFixtures.LoadXref);
        CompileExeFromSource(h, work, "LOADACCT", OracleCobolFixtures.LoadAcct);
        CompileExeFromSource(h, work, "LOADTCAT", OracleCobolFixtures.LoadTcat);
        CompileExeFromSource(h, work, "UNLDTRAN", OracleCobolFixtures.UnloadTran);

        // 3) Build indexed inputs.
        string Idx(string n) => Path.Combine(work, n + ".idx");
        RunOk(h, work, "LOADXREF", new() { ["LDIN"] = P(work, "xref.seq"), ["XREFFILE"] = Idx("XREFFILE") });
        RunOk(h, work, "LOADACCT", new() { ["LDIN"] = P(work, "acct.seq"), ["ACCTFILE"] = Idx("ACCTFILE") });
        RunOk(h, work, "LOADTCAT", new() { ["LDIN"] = P(work, "tcat.seq"), ["TCATBALF"] = Idx("TCATBALF") });

        // 4) Run CBTRN02C, producing TRANSACT (indexed) + DALYREJS (sequential).
        // RETURN-CODE is 4 when any record is rejected, so 0 and 4 are both valid exits.
        string rejPath = P(work, "dalyrejs.out");
        RunOk(h, work, "CBTRN02C", new()
        {
            ["DALYTRAN"] = P(work, "daly.seq"),
            ["XREFFILE"] = Idx("XREFFILE"),
            ["ACCTFILE"] = Idx("ACCTFILE"),
            ["TCATBALF"] = Idx("TCATBALF"),
            ["TRANFILE"] = Idx("TRANSACT"),
            ["DALYREJS"] = rejPath,
        }, 0, 4);

        // Unload the indexed TRANSACT to a sequential file in key order.
        string tranOut = P(work, "transact.seq");
        RunOk(h, work, "UNLDTRAN", new() { ["TRANFILE"] = Idx("TRANSACT"), ["UNLOAD"] = tranOut });

        byte[] cobolRejects = File.ReadAllBytes(rejPath);
        byte[] cobolTrans = File.ReadAllBytes(tranOut);

        // 5) Run the .NET port on the same inputs.
        (byte[] dotnetRejects, byte[] dotnetTrans, int rc) = RunDotNet(l.DalyTran, l.Tran, l.Xref, l.Account, l.TcatBal, daly, xref, acct, tcat);

        // 6a) Rejects are fully deterministic — exact match.
        AssertEqualRecords(cobolRejects, dotnetRejects, 430, "DALYREJS", procTsOffset: -1, l.Tran);

        // 6b) Transactions match after masking TRAN-PROC-TS (the only clock-derived field).
        AssertEqualRecords(cobolTrans, dotnetTrans, 350, "TRANSACT", l.Tran.Field("TRAN-PROC-TS").Offset, l.Tran);

        Assert.True(rc is 0 or 4);
        Assert.True(cobolRejects.Length > 0 || cobolTrans.Length > 0, "expected some output");
    }

    private static (byte[] rejects, byte[] trans, int rc) RunDotNet(
        RecordLayout dalyL, RecordLayout tranL, RecordLayout xrefL, RecordLayout acctL, RecordLayout tcatL,
        byte[] daly, byte[] xref, byte[] acct, byte[] tcat)
    {
        using var db = new CardDemoDatabase();
        SequentialFile dalyTran = db.DefineSequentialFile("DALYTRAN", 350);
        VsamFile transact = db.DefineFile(CardDemoFiles.Transaction.Definition);
        VsamFile xrefF = db.DefineFile(CardDemoFiles.CardXref.Definition);
        SequentialFile dalyRejs = db.DefineSequentialFile("DALYREJS", 430);
        VsamFile acctF = db.DefineFile(CardDemoFiles.Account.Definition);
        VsamFile tcatF = db.DefineFile(CardDemoFiles.TranCatBal.Definition);

        dalyTran.LoadImage(daly);
        LoadVsam(xrefF, xref, 50);
        LoadVsam(acctF, acct, 300);
        LoadVsam(tcatF, tcat, 50);

        var ctx = new Cbtrn02cContext(dalyTran, transact, xrefF, dalyRejs, acctF, tcatF,
            dalyL, tranL, xrefL, acctL, tcatL,
            new FixedClock(new DateTime(2022, 7, 18, 12, 0, 0)), Host);
        int rc = new Cbtrn02cTransactionPoster(ctx).Run();

        return (dalyRejs.ToImage(), MaterializeKeyOrder(transact, 350), rc);
    }

    private static byte[] MaterializeKeyOrder(VsamFile f, int reclen)
    {
        var ms = new MemoryStream();
        f.StartBrowse();
        while (f.ReadNext(out byte[]? img) == FileStatus.Ok) ms.Write(img!);
        f.EndBrowse();
        return ms.ToArray();
    }

    private static void AssertEqualRecords(byte[] cobol, byte[] dotnet, int reclen, string name, int procTsOffset, RecordLayout tranL)
    {
        Assert.True(cobol.Length == dotnet.Length, $"{name} length {cobol.Length} (GnuCOBOL) != {dotnet.Length} (.NET)");
        Assert.True(cobol.Length % reclen == 0, $"{name} length {cobol.Length} not a multiple of {reclen}");
        byte[] a = (byte[])cobol.Clone(), b = (byte[])dotnet.Clone();
        if (procTsOffset >= 0)
            for (int r = 0; r < a.Length; r += reclen)
            {
                Array.Fill(a, (byte)'#', r + procTsOffset, 26);
                Array.Fill(b, (byte)'#', r + procTsOffset, 26);
            }
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i])
            {
                int rec = i / reclen, off = i % reclen;
                Assert.Fail($"{name} mismatch at record {rec} byte {off}: GnuCOBOL 0x{a[i]:X2} vs .NET 0x{b[i]:X2}");
            }
    }

    private static void LoadVsam(VsamFile f, byte[] data, int reclen)
    {
        for (int i = 0; i < data.Length; i += reclen)
            Assert.Equal(FileStatus.Ok, f.Write(data[i..(i + reclen)]));
    }

    private static string P(string dir, string file) => Path.Combine(dir, file);
    private static RecordLayout Parse(string cpy) => CopybookParser.Parse(File.ReadAllText(CardDemoPaths.Copybook(cpy)));

    private static byte[] ToAscii(string ebcdicFile, RecordLayout layout)
    {
        byte[] eb = File.ReadAllBytes(CardDemoPaths.EbcdicData(ebcdicFile));
        var outBuf = new byte[eb.Length];
        for (int i = 0; i < eb.Length; i += layout.Length)
            FixedRecord.Parse(layout, new ReadOnlySpan<byte>(eb, i, layout.Length), HostKind.Ebcdic)
                .ToBytes(HostKind.Ascii).CopyTo(outBuf, i);
        return outBuf;
    }

    private static void CompileExeFromFile(GnuCobolHarness h, string work, string name, string src, string[] copyDirs)
    {
        ProcessResult r = h.CompileExecutable(src, copyDirs, name + ".exe", work);
        Assert.True(r.Success && File.Exists(Path.Combine(work, name + ".exe")), $"compile {name}: {r.StdErr}\n{r.StdOut}");
    }

    private static void CompileExeFromSource(GnuCobolHarness h, string work, string name, string source)
    {
        string src = Path.Combine(work, name + ".cob");
        File.WriteAllText(src, source);
        ProcessResult r = h.CompileExecutable(src, [], name + ".exe", work);
        Assert.True(r.Success && File.Exists(Path.Combine(work, name + ".exe")), $"compile {name}: {r.StdErr}\n{r.StdOut}");
    }

    private static void RunOk(GnuCobolHarness h, string work, string name, Dictionary<string, string> env, params int[] allowed)
    {
        if (allowed.Length == 0) allowed = [0];
        ProcessResult r = h.Run(Path.Combine(work, name + ".exe"), work, env);
        Assert.True(Array.IndexOf(allowed, r.ExitCode) >= 0, $"run {name} (exit {r.ExitCode}): {r.StdErr}\n{r.StdOut}");
    }
}
