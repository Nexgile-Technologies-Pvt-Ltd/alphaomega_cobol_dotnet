using CardDemo.Batch;
using CardDemo.Cobol.Runtime;
using CardDemo.Data;
using CardDemo.Tooling;
using Xunit;

namespace CardDemo.Parity.Tests;

/// <summary>
/// SYSOUT golden master for CBTRN01C (daily-transaction card/account verification). Runs the original
/// COBOL under GnuCOBOL and compares its DISPLAY stream line-for-line with the .NET port. The customer,
/// card, and transaction files are opened but unused, so they are created empty for GnuCOBOL.
/// </summary>
public class Cbtrn01cGoldenMasterTests
{
    private static readonly HostKind Host = HostKind.Ascii;

    [Fact]
    public void Cbtrn01c_sysout_matches_gnucobol()
    {
        GnuCobolInstall? install = GnuCobolInstall.TryLocate();
        if (install is null) return;

        RecordLayout dalyL = Parse("CVTRA06Y.cpy");
        RecordLayout xrefL = Parse("CVACT03Y.cpy");

        string work = NewWork("cbtrn01c");
        byte[] daly = ToAscii("AWS.M2.CARDDEMO.DALYTRAN.PS", dalyL);
        byte[] xref = ToAscii("AWS.M2.CARDDEMO.CARDXREF.PS", xrefL);
        byte[] acct = ToAscii("AWS.M2.CARDDEMO.ACCTDATA.PS", Parse("CVACT01Y.cpy"));
        File.WriteAllBytes(Path.Combine(work, "daly.seq"), daly);
        File.WriteAllBytes(Path.Combine(work, "xref.seq"), xref);
        File.WriteAllBytes(Path.Combine(work, "acct.seq"), acct);
        File.WriteAllBytes(Path.Combine(work, "empty.seq"), []);

        var h = new GnuCobolHarness(install);
        CompileExeFile(h, work, "CBTRN01C", CardDemoPaths.Program("CBTRN01C.cbl"), [CardDemoPaths.CopybookDir]);
        CompileExeSrc(h, work, "LOADXREF", OracleCobolFixtures.LoadXref);
        CompileExeSrc(h, work, "LOADACCT", OracleCobolFixtures.LoadAcct);
        CompileExeSrc(h, work, "LOADCUST", OracleCobolFixtures.LoadCust);
        CompileExeSrc(h, work, "LOADCARD", OracleCobolFixtures.LoadCard);
        CompileExeSrc(h, work, "LOADTRAN", OracleCobolFixtures.LoadTran);

        string Idx(string n) => Path.Combine(work, n + ".idx");
        string Empty = Path.Combine(work, "empty.seq");
        RunOk(h, work, "LOADXREF", new() { ["LDIN"] = Path.Combine(work, "xref.seq"), ["XREFFILE"] = Idx("XREFFILE") });
        RunOk(h, work, "LOADACCT", new() { ["LDIN"] = Path.Combine(work, "acct.seq"), ["ACCTFILE"] = Idx("ACCTFILE") });
        RunOk(h, work, "LOADCUST", new() { ["LDIN"] = Empty, ["CUSTFILE"] = Idx("CUSTFILE") });
        RunOk(h, work, "LOADCARD", new() { ["LDIN"] = Empty, ["CARDFILE"] = Idx("CARDFILE") });
        RunOk(h, work, "LOADTRAN", new() { ["LDIN"] = Empty, ["TRANFILE"] = Idx("TRANSACT") });

        string[] cobol = Lines(RunCapture(h, work, "CBTRN01C", new()
        {
            ["DALYTRAN"] = Path.Combine(work, "daly.seq"),
            ["CUSTFILE"] = Idx("CUSTFILE"),
            ["XREFFILE"] = Idx("XREFFILE"),
            ["CARDFILE"] = Idx("CARDFILE"),
            ["ACCTFILE"] = Idx("ACCTFILE"),
            ["TRANFILE"] = Idx("TRANSACT"),
        }));

        using var db = new CardDemoDatabase();
        SequentialFile dalyTran = db.DefineSequentialFile("DALYTRAN", 350);
        VsamFile xrefF = db.DefineFile(CardDemoFiles.CardXref.Definition);
        VsamFile acctF = db.DefineFile(CardDemoFiles.Account.Definition);
        dalyTran.LoadImage(daly);
        LoadVsam(xrefF, xref, 50);
        LoadVsam(acctF, acct, 300);

        IReadOnlyList<string> dotnet = new Cbtrn01cVerifier(
            new Cbtrn01cContext(dalyTran, xrefF, acctF, dalyL, xrefL, Host)).Run();

        Assert.Equal(cobol.Length, dotnet.Count);
        for (int i = 0; i < cobol.Length; i++)
            Assert.True(cobol[i] == dotnet[i], $"SYSOUT line {i} differs:\n  GnuCOBOL: [{cobol[i]}]\n  .NET    : [{dotnet[i]}]");
    }

    private static string[] Lines(string stdout) => stdout.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
    private static RecordLayout Parse(string cpy) => CopybookParser.Parse(File.ReadAllText(CardDemoPaths.Copybook(cpy)));

    private static string NewWork(string name)
    {
        string work = Path.Combine(Path.GetTempPath(), "carddemo_gm_" + name);
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        return work;
    }

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
        Assert.True(r.Success, $"run {name} (exit {r.ExitCode}): {r.StdErr}");
    }

    private static string RunCapture(GnuCobolHarness h, string work, string name, Dictionary<string, string> env)
    {
        ProcessResult r = h.Run(Path.Combine(work, name + ".exe"), work, env);
        Assert.True(r.Success, $"run {name} (exit {r.ExitCode}): {r.StdErr}");
        return r.StdOut;
    }
}
