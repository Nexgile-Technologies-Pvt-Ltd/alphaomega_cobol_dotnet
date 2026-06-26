using System.Text.RegularExpressions;
using CardDemo.Batch;
using CardDemo.Cobol.Runtime;
using CardDemo.Data;
using CardDemo.Tooling;
using Xunit;

namespace CardDemo.Parity.Tests;

/// <summary>
/// Byte-for-byte golden master for CBIMPORT (splits the export file back into the master files). A
/// deterministic export is produced by the .NET CBEXPORT; both the patched COBOL (under GnuCOBOL) and
/// the .NET port import it and the five output files are compared exactly. CBIMPORT writes no
/// clock-derived fields into the data records, so the comparison needs no masking.
/// </summary>
public class CbimportGoldenMasterTests
{
    private static readonly HostKind Host = HostKind.Ascii;
    private static readonly DateTime Clock = new(2022, 7, 18, 12, 0, 0);

    [Fact]
    public void Cbimport_outputs_match_gnucobol_byte_for_byte()
    {
        GnuCobolInstall? install = GnuCobolInstall.TryLocate();
        if (install is null) return;

        RecordLayout custL = Parse("CVCUS01Y.cpy"), acctL = Parse("CVACT01Y.cpy"), xrefL = Parse("CVACT03Y.cpy");
        RecordLayout cardL = Parse("CVACT02Y.cpy"), tranL = Parse("CVTRA05Y.cpy");
        RecordLayout dalyL = Parse("CVTRA06Y.cpy"), tcatL = Parse("CVTRA01Y.cpy");
        CopybookModel exp = CopybookParser.ParseModel(File.ReadAllText(CardDemoPaths.Copybook("CVEXPORT.cpy")));

        string work = NewWork("cbimport");
        byte[] cust = ToAscii("AWS.M2.CARDDEMO.CUSTDATA.PS", custL);
        byte[] acct = ToAscii("AWS.M2.CARDDEMO.ACCTDATA.PS", acctL);
        byte[] xref = ToAscii("AWS.M2.CARDDEMO.CARDXREF.PS", xrefL);
        byte[] card = ToAscii("AWS.M2.CARDDEMO.CARDDATA.PS", cardL);
        byte[] daly = ToAscii("AWS.M2.CARDDEMO.DALYTRAN.PS", dalyL);
        byte[] tcat = ToAscii("AWS.M2.CARDDEMO.TCATBALF.PS", tcatL);
        byte[] posted = ProducePostedTransact(dalyL, tranL, xrefL, acctL, tcatL, daly, xref, acct, tcat);

        // Deterministic export (fixed timestamp) shared by both import runs.
        byte[] exportImg = ProduceExport(exp, custL, acctL, xrefL, tranL, cardL, cust, acct, xref, posted, card);
        File.WriteAllBytes(Path.Combine(work, "export.seq"), exportImg);

        string patched = Regex.Replace(
            File.ReadAllText(CardDemoPaths.Program("CBIMPORT.cbl")),
            @"(SELECT EXPORT-INPUT\s+ASSIGN TO EXPFILE\s+)ORGANIZATION IS INDEXED\s+ACCESS MODE IS SEQUENTIAL\s+RECORD KEY IS EXPORT-SEQUENCE-NUM(\s+FILE STATUS)",
            "$1ORGANIZATION IS SEQUENTIAL$2");
        Assert.DoesNotContain("RECORD KEY IS EXPORT-SEQUENCE-NUM", patched);
        string patchedSrc = Path.Combine(work, "CBIMPORT.cbl");
        File.WriteAllText(patchedSrc, patched);

        var h = new GnuCobolHarness(install);
        Assert.True(h.CompileExecutable(patchedSrc, [CardDemoPaths.CopybookDir], "CBIMPORT.exe", work).Success, "compile CBIMPORT");

        string[] outs = ["CUSTOUT", "ACCTOUT", "XREFOUT", "TRNXOUT", "CARDOUT", "ERROUT"];
        var env = new Dictionary<string, string> { ["EXPFILE"] = Path.Combine(work, "export.seq") };
        foreach (string o in outs) env[o] = Path.Combine(work, o + ".out");
        RunOk(h, work, "CBIMPORT", env);

        (byte[] c, byte[] a, byte[] x, byte[] t, byte[] d) dotnet = RunDotNet(exp, custL, acctL, xrefL, tranL, cardL, exportImg);

        Compare("CUSTOMER", File.ReadAllBytes(Path.Combine(work, "CUSTOUT.out")), dotnet.c, 500);
        Compare("ACCOUNT", File.ReadAllBytes(Path.Combine(work, "ACCTOUT.out")), dotnet.a, 300);
        Compare("XREF", File.ReadAllBytes(Path.Combine(work, "XREFOUT.out")), dotnet.x, 50);
        Compare("TRANSACTION", File.ReadAllBytes(Path.Combine(work, "TRNXOUT.out")), dotnet.t, 350);
        Compare("CARD", File.ReadAllBytes(Path.Combine(work, "CARDOUT.out")), dotnet.d, 150);
    }

    private static void Compare(string name, byte[] cobol, byte[] dotnet, int reclen)
    {
        Assert.True(cobol.Length == dotnet.Length, $"{name} length {cobol.Length} (GnuCOBOL) != {dotnet.Length} (.NET)");
        for (int i = 0; i < cobol.Length; i++)
            if (cobol[i] != dotnet[i])
                Assert.Fail($"{name} mismatch record {i / reclen} byte {i % reclen}: GnuCOBOL 0x{cobol[i]:X2} vs .NET 0x{dotnet[i]:X2}");
    }

    private static (byte[], byte[], byte[], byte[], byte[]) RunDotNet(CopybookModel exp, RecordLayout custL, RecordLayout acctL,
        RecordLayout xrefL, RecordLayout tranL, RecordLayout cardL, byte[] exportImg)
    {
        using var db = new CardDemoDatabase();
        SequentialFile expIn = db.DefineSequentialFile("EXPIN", 500);
        SequentialFile custO = db.DefineSequentialFile("CUSTO", 500);
        SequentialFile acctO = db.DefineSequentialFile("ACCTO", 300);
        SequentialFile xrefO = db.DefineSequentialFile("XREFO", 50);
        SequentialFile tranO = db.DefineSequentialFile("TRANO", 350);
        SequentialFile cardO = db.DefineSequentialFile("CARDO", 150);
        SequentialFile errO = db.DefineSequentialFile("ERRO", 132);
        expIn.LoadImage(exportImg);

        new Cbimport(new Cbimport.Context(expIn, custO, acctO, xrefO, tranO, cardO, errO,
            custL, acctL, xrefL, tranL, cardL,
            exp.Flatten("EXPORT-CUSTOMER-DATA"), exp.Flatten("EXPORT-ACCOUNT-DATA"), exp.Flatten("EXPORT-CARD-XREF-DATA"),
            exp.Flatten("EXPORT-TRANSACTION-DATA"), exp.Flatten("EXPORT-CARD-DATA"),
            new string(' ', 26), Host)).Run();
        return (custO.ToImage(), acctO.ToImage(), xrefO.ToImage(), tranO.ToImage(), cardO.ToImage());
    }

    private static byte[] ProduceExport(CopybookModel exp, RecordLayout custL, RecordLayout acctL, RecordLayout xrefL,
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

    private static void RunOk(GnuCobolHarness h, string work, string name, Dictionary<string, string> env)
    {
        ProcessResult r = h.Run(Path.Combine(work, name + ".exe"), work, env);
        Assert.True(r.Success, $"run {name} (exit {r.ExitCode}): {r.StdErr}\n{r.StdOut}");
    }
}
