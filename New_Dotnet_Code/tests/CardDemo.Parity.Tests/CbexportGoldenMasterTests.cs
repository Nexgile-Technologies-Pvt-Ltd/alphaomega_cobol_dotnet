using System.Text.RegularExpressions;
using CardDemo.Batch;
using CardDemo.Cobol.Runtime;
using CardDemo.Data;
using CardDemo.Tooling;
using Xunit;

namespace CardDemo.Parity.Tests;

/// <summary>
/// Byte-for-byte golden master for CBEXPORT (multi-record branch-migration export, with COMP/COMP-3
/// fields). CBEXPORT's <c>RECORD KEY</c> references a working-storage field, which the COBOL standard
/// (and GnuCOBOL) rejects, so the test applies a single content-neutral patch — the EXPORT file's
/// ORGANIZATION is changed from INDEXED to SEQUENTIAL (the written record bytes are identical). Both the
/// patched COBOL (under GnuCOBOL) and the .NET port export the same inputs, and the export file is
/// compared exactly, masking only the clock-derived 26-byte EXPORT-TIMESTAMP.
/// </summary>
public class CbexportGoldenMasterTests
{
    private static readonly HostKind Host = HostKind.Ascii;
    private static readonly DateTime Clock = new(2022, 7, 18, 12, 0, 0);

    [Fact]
    public void Cbexport_output_matches_gnucobol_byte_for_byte()
    {
        GnuCobolInstall? install = GnuCobolInstall.TryLocate();
        if (install is null) return;

        RecordLayout custL = Parse("CVCUS01Y.cpy"), acctL = Parse("CVACT01Y.cpy"), xrefL = Parse("CVACT03Y.cpy");
        RecordLayout cardL = Parse("CVACT02Y.cpy"), tranL = Parse("CVTRA05Y.cpy");
        RecordLayout dalyL = Parse("CVTRA06Y.cpy"), tcatL = Parse("CVTRA01Y.cpy");
        CopybookModel exp = CopybookParser.ParseModel(File.ReadAllText(CardDemoPaths.Copybook("CVEXPORT.cpy")));

        string work = NewWork("cbexport");
        byte[] cust = ToAscii("AWS.M2.CARDDEMO.CUSTDATA.PS", custL);
        byte[] acct = ToAscii("AWS.M2.CARDDEMO.ACCTDATA.PS", acctL);
        byte[] xref = ToAscii("AWS.M2.CARDDEMO.CARDXREF.PS", xrefL);
        byte[] card = ToAscii("AWS.M2.CARDDEMO.CARDDATA.PS", cardL);
        byte[] daly = ToAscii("AWS.M2.CARDDEMO.DALYTRAN.PS", dalyL);
        byte[] tcat = ToAscii("AWS.M2.CARDDEMO.TCATBALF.PS", tcatL);
        byte[] posted = ProducePostedTransact(dalyL, tranL, xrefL, acctL, tcatL, daly, xref, acct, tcat);
        foreach (var (n, d) in new[] { ("cust", cust), ("acct", acct), ("xref", xref), ("card", card), ("tran", posted) })
            File.WriteAllBytes(Path.Combine(work, n + ".seq"), d);

        // Content-neutral patch: make the EXPORT file SEQUENTIAL so it compiles under GnuCOBOL.
        string patched = Regex.Replace(
            File.ReadAllText(CardDemoPaths.Program("CBEXPORT.cbl")),
            @"(SELECT EXPORT-OUTPUT\s+ASSIGN TO EXPFILE\s+)ORGANIZATION IS INDEXED\s+ACCESS MODE IS SEQUENTIAL\s+RECORD KEY IS EXPORT-SEQUENCE-NUM(\s+FILE STATUS)",
            "$1ORGANIZATION IS SEQUENTIAL$2");
        Assert.DoesNotContain("RECORD KEY IS EXPORT-SEQUENCE-NUM", patched);
        string patchedSrc = Path.Combine(work, "CBEXPORT.cbl");
        File.WriteAllText(patchedSrc, patched);

        var h = new GnuCobolHarness(install);
        Assert.True(h.CompileExecutable(patchedSrc, [CardDemoPaths.CopybookDir], "CBEXPORT.exe", work).Success, "compile CBEXPORT");
        CompileExeSrc(h, work, "LOADCUST", OracleCobolFixtures.LoadCust);
        CompileExeSrc(h, work, "LOADACCT", OracleCobolFixtures.LoadAcct);
        CompileExeSrc(h, work, "LOADXREF", OracleCobolFixtures.LoadXref);
        CompileExeSrc(h, work, "LOADCARD", OracleCobolFixtures.LoadCard);
        CompileExeSrc(h, work, "LOADTRAN", OracleCobolFixtures.LoadTran);

        string Idx(string n) => Path.Combine(work, n + ".idx");
        string Seq(string n) => Path.Combine(work, n + ".seq");
        RunOk(h, work, "LOADCUST", new() { ["LDIN"] = Seq("cust"), ["CUSTFILE"] = Idx("CUSTFILE") });
        RunOk(h, work, "LOADACCT", new() { ["LDIN"] = Seq("acct"), ["ACCTFILE"] = Idx("ACCTFILE") });
        RunOk(h, work, "LOADXREF", new() { ["LDIN"] = Seq("xref"), ["XREFFILE"] = Idx("XREFFILE") });
        RunOk(h, work, "LOADCARD", new() { ["LDIN"] = Seq("card"), ["CARDFILE"] = Idx("CARDFILE") });
        RunOk(h, work, "LOADTRAN", new() { ["LDIN"] = Seq("tran"), ["TRANFILE"] = Idx("TRANSACT") });

        string expOut = Path.Combine(work, "export.out");
        RunOk(h, work, "CBEXPORT", new()
        {
            ["CUSTFILE"] = Idx("CUSTFILE"), ["ACCTFILE"] = Idx("ACCTFILE"), ["XREFFILE"] = Idx("XREFFILE"),
            ["TRANSACT"] = Idx("TRANSACT"), ["CARDFILE"] = Idx("CARDFILE"), ["EXPFILE"] = expOut,
        });
        byte[] cobol = File.ReadAllBytes(expOut);

        byte[] dotnet = RunDotNet(exp, custL, acctL, xrefL, tranL, cardL, cust, acct, xref, posted, card);

        Assert.Equal(cobol.Length, dotnet.Length);
        Assert.True(cobol.Length % 500 == 0, $"export length {cobol.Length} not a multiple of 500");
        for (int r = 0; r < cobol.Length; r += 500) { Array.Fill(cobol, (byte)'#', r + 1, 26); Array.Fill(dotnet, (byte)'#', r + 1, 26); }
        for (int i = 0; i < cobol.Length; i++)
            if (cobol[i] != dotnet[i])
            {
                int rec = i / 500, off = i % 500;
                Assert.Fail($"EXPORT record {rec} (type '{(char)cobol[rec * 500]}') byte {off}: GnuCOBOL 0x{cobol[i]:X2} vs .NET 0x{dotnet[i]:X2}");
            }
        Assert.True(cobol.Length > 0, "expected a non-empty export");
    }

    private static byte[] RunDotNet(CopybookModel exp, RecordLayout custL, RecordLayout acctL, RecordLayout xrefL,
        RecordLayout tranL, RecordLayout cardL, byte[] cust, byte[] acct, byte[] xref, byte[] posted, byte[] card)
    {
        using var db = new CardDemoDatabase();
        VsamFile custF = db.DefineFile(CardDemoFiles.Customer.Definition);
        VsamFile acctF = db.DefineFile(CardDemoFiles.Account.Definition);
        VsamFile xrefF = db.DefineFile(CardDemoFiles.CardXref.Definition);
        VsamFile tranF = db.DefineFile(CardDemoFiles.Transaction.Definition);
        VsamFile cardF = db.DefineFile(CardDemoFiles.Card.Definition);
        SequentialFile export = db.DefineSequentialFile("EXPORT", 500);
        Load(custF, cust, 500); Load(acctF, acct, 300); Load(xrefF, xref, 50); Load(tranF, posted, 350); Load(cardF, card, 150);

        new Cbexport(new Cbexport.Context(custF, acctF, xrefF, tranF, cardF, export,
            custL, acctL, xrefL, tranL, cardL,
            exp.Flatten("EXPORT-CUSTOMER-DATA"), exp.Flatten("EXPORT-ACCOUNT-DATA"), exp.Flatten("EXPORT-CARD-XREF-DATA"),
            exp.Flatten("EXPORT-TRANSACTION-DATA"), exp.Flatten("EXPORT-CARD-DATA"),
            new string(' ', 26), Host)).Run();
        return export.ToImage();
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
        Load(xrefF, xref, 50); Load(acctF, acct, 300); Load(tcatF, tcat, 50);
        new Cbtrn02cTransactionPoster(new Cbtrn02cContext(dalyTran, transact, xrefF, dalyRejs, acctF, tcatF,
            dalyL, tranL, xrefL, acctL, tcatL, new FixedClock(Clock), Host)).Run();
        var ms = new MemoryStream();
        transact.StartBrowse();
        while (transact.ReadNext(out byte[]? img) == FileStatus.Ok) ms.Write(img!);
        transact.EndBrowse();
        return ms.ToArray();
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

    private static void Load(VsamFile f, byte[] data, int reclen)
    {
        for (int i = 0; i < data.Length; i += reclen) Assert.Equal(FileStatus.Ok, f.Write(data[i..(i + reclen)]));
    }

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
