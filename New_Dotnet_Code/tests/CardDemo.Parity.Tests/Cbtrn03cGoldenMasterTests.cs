using CardDemo.Batch;
using CardDemo.Cobol.Runtime;
using CardDemo.Data;
using CardDemo.Tooling;
using Xunit;

namespace CardDemo.Parity.Tests;

/// <summary>
/// Byte-for-byte golden master for CBTRN03C (transaction detail report). A deterministic posted
/// TRANSACT file is produced by the .NET CBTRN02C (fixed clock), then both the original COBOL (under
/// GnuCOBOL) and the .NET port produce the report from the same inputs; the 133-column report file is
/// compared exactly. CBTRN03C uses no clock, so the report is fully deterministic.
/// </summary>
public class Cbtrn03cGoldenMasterTests
{
    private static readonly HostKind Host = HostKind.Ascii;
    private const string StartDate = "2022-07-18";
    private const string EndDate = "2022-07-18";
    private static readonly DateTime Clock = new(2022, 7, 18, 12, 0, 0);

    [Fact]
    public void Cbtrn03c_report_matches_gnucobol_byte_for_byte()
    {
        GnuCobolInstall? install = GnuCobolInstall.TryLocate();
        if (install is null) return;

        var l = new
        {
            Tran = Parse("CVTRA05Y.cpy"),
            Xref = Parse("CVACT03Y.cpy"),
            Type = Parse("CVTRA03Y.cpy"),
            Catg = Parse("CVTRA04Y.cpy"),
            Daly = Parse("CVTRA06Y.cpy"),
            Acct = Parse("CVACT01Y.cpy"),
            Tcat = Parse("CVTRA01Y.cpy"),
        };

        string work = NewWork("cbtrn03c");
        byte[] daly = ToAscii("AWS.M2.CARDDEMO.DALYTRAN.PS", l.Daly);
        byte[] xref = ToAscii("AWS.M2.CARDDEMO.CARDXREF.PS", l.Xref);
        byte[] acct = ToAscii("AWS.M2.CARDDEMO.ACCTDATA.PS", l.Acct);
        byte[] tcat = ToAscii("AWS.M2.CARDDEMO.TCATBALF.PS", l.Tcat);
        byte[] ttype = ToAscii("AWS.M2.CARDDEMO.TRANTYPE.PS", l.Type);
        byte[] tcatg = ToAscii("AWS.M2.CARDDEMO.TRANCATG.PS", l.Catg);

        // 1) Deterministic posted TRANSACT via the .NET CBTRN02C.
        byte[] posted = ProducePostedTransact(l.Daly, l.Tran, l.Xref, l.Acct, l.Tcat, daly, xref, acct, tcat);
        File.WriteAllBytes(Path.Combine(work, "transact.seq"), posted);
        File.WriteAllBytes(Path.Combine(work, "type.seq"), ttype);
        File.WriteAllBytes(Path.Combine(work, "catg.seq"), tcatg);
        File.WriteAllBytes(Path.Combine(work, "xref.seq"), xref);
        File.WriteAllText(Path.Combine(work, "dateparm.txt"),
            (StartDate + " " + EndDate).PadRight(80));

        // 2) Compile + load indexed inputs.
        var h = new GnuCobolHarness(install);
        CompileExeFile(h, work, "CBTRN03C", CardDemoPaths.Program("CBTRN03C.cbl"), [CardDemoPaths.CopybookDir]);
        CompileExeSrc(h, work, "LOADXREF", OracleCobolFixtures.LoadXref);
        CompileExeSrc(h, work, "LOADTYPE", OracleCobolFixtures.LoadType);
        CompileExeSrc(h, work, "LOADCATG", OracleCobolFixtures.LoadCatg);
        string Idx(string n) => Path.Combine(work, n + ".idx");
        RunOk(h, work, "LOADXREF", new() { ["LDIN"] = Path.Combine(work, "xref.seq"), ["XREFFILE"] = Idx("CARDXREF") });
        RunOk(h, work, "LOADTYPE", new() { ["LDIN"] = Path.Combine(work, "type.seq"), ["TRANTYPE"] = Idx("TRANTYPE") });
        RunOk(h, work, "LOADCATG", new() { ["LDIN"] = Path.Combine(work, "catg.seq"), ["TRANCATG"] = Idx("TRANCATG") });

        // 3) Run GnuCOBOL CBTRN03C.
        string reptPath = Path.Combine(work, "report.out");
        RunOk(h, work, "CBTRN03C", new()
        {
            ["TRANFILE"] = Path.Combine(work, "transact.seq"),
            ["CARDXREF"] = Idx("CARDXREF"),
            ["TRANTYPE"] = Idx("TRANTYPE"),
            ["TRANCATG"] = Idx("TRANCATG"),
            ["TRANREPT"] = reptPath,
            ["DATEPARM"] = Path.Combine(work, "dateparm.txt"),
        });
        byte[] cobolReport = File.ReadAllBytes(reptPath);

        // 4) Run the .NET port.
        byte[] dotnetReport = RunDotNetReport(l.Tran, l.Xref, l.Type, l.Catg, posted, xref, ttype, tcatg);

        // 5) Compare the 133-column report byte-for-byte.
        Assert.True(cobolReport.Length == dotnetReport.Length,
            $"report length {cobolReport.Length} (GnuCOBOL) != {dotnetReport.Length} (.NET)");
        Assert.True(cobolReport.Length % 133 == 0, $"report length {cobolReport.Length} not a multiple of 133");
        for (int i = 0; i < cobolReport.Length; i++)
            if (cobolReport[i] != dotnetReport[i])
            {
                int rec = i / 133, off = i % 133;
                string c = System.Text.Encoding.Latin1.GetString(cobolReport, rec * 133, 133);
                string d = System.Text.Encoding.Latin1.GetString(dotnetReport, rec * 133, 133);
                Assert.Fail($"report line {rec} byte {off}:\n  GnuCOBOL [{c}]\n  .NET     [{d}]");
            }
        Assert.True(cobolReport.Length > 0, "expected a non-empty report");
    }

    private static byte[] ProducePostedTransact(RecordLayout dalyL, RecordLayout tranL, RecordLayout xrefL,
        RecordLayout acctL, RecordLayout tcatL, byte[] daly, byte[] xref, byte[] acct, byte[] tcat)
    {
        using var db = new CardDemoDatabase();
        SequentialFile dalyTran = db.DefineSequentialFile("DALYTRAN", 350);
        VsamFile transact = db.DefineFile(CardDemoFiles.Transaction.Definition);
        VsamFile xrefF = db.DefineFile(CardDemoFiles.CardXref.Definition);
        SequentialFile dalyRejs = db.DefineSequentialFile("DALYREJS", 430);
        VsamFile acctF = db.DefineFile(CardDemoFiles.Account.Definition);
        VsamFile tcatF = db.DefineFile(CardDemoFiles.TranCatBal.Definition);
        dalyTran.LoadImage(daly);
        LoadVsam(xrefF, xref, 50); LoadVsam(acctF, acct, 300); LoadVsam(tcatF, tcat, 50);

        new Cbtrn02cTransactionPoster(new Cbtrn02cContext(dalyTran, transact, xrefF, dalyRejs, acctF, tcatF,
            dalyL, tranL, xrefL, acctL, tcatL, new FixedClock(Clock), Host)).Run();

        var ms = new MemoryStream();
        transact.StartBrowse();
        while (transact.ReadNext(out byte[]? img) == FileStatus.Ok) ms.Write(img!);
        transact.EndBrowse();
        return ms.ToArray();
    }

    private static byte[] RunDotNetReport(RecordLayout tranL, RecordLayout xrefL, RecordLayout typeL,
        RecordLayout catgL, byte[] posted, byte[] xref, byte[] ttype, byte[] tcatg)
    {
        using var db = new CardDemoDatabase();
        SequentialFile transact = db.DefineSequentialFile("TRANSACT", 350);
        VsamFile xrefF = db.DefineFile(CardDemoFiles.CardXref.Definition);
        VsamFile typeF = db.DefineFile(CardDemoFiles.TranType.Definition);
        VsamFile catgF = db.DefineFile(CardDemoFiles.TranCategory.Definition);
        SequentialFile report = db.DefineSequentialFile("TRANREPT", 133);
        transact.LoadImage(posted);
        LoadVsam(xrefF, xref, 50); LoadVsam(typeF, ttype, 60); LoadVsam(catgF, tcatg, 60);

        new Cbtrn03cReporter(new Cbtrn03cContext(transact, xrefF, typeF, catgF, report,
            tranL, xrefL, typeL, catgL, StartDate, EndDate, Host)).Run();
        return report.ToImage();
    }

    private static string NewWork(string name)
    {
        string work = Path.Combine(Path.GetTempPath(), "carddemo_gm_" + name);
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        return work;
    }

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

    private static void LoadVsam(VsamFile f, byte[] data, int reclen)
    {
        for (int i = 0; i < data.Length; i += reclen) Assert.Equal(FileStatus.Ok, f.Write(data[i..(i + reclen)]));
    }

    private static void CompileExeFile(GnuCobolHarness h, string work, string name, string src, string[] copyDirs) =>
        Assert.True(h.CompileExecutable(src, copyDirs, name + ".exe", work).Success, $"compile {name}");

    private static void CompileExeSrc(GnuCobolHarness h, string work, string name, string source)
    {
        string src = Path.Combine(work, name + ".cob");
        File.WriteAllText(src, source);
        Assert.True(h.CompileExecutable(src, [], name + ".exe", work).Success, $"compile {name}");
    }

    private static void RunOk(GnuCobolHarness h, string work, string name, Dictionary<string, string> env)
    {
        ProcessResult r = h.Run(Path.Combine(work, name + ".exe"), work, env);
        Assert.True(r.Success, $"run {name} (exit {r.ExitCode}): {r.StdErr}\n{r.StdOut}");
    }
}
