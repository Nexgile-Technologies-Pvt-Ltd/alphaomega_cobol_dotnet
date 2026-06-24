using CardDemo.Batch;
using CardDemo.Cobol.Runtime;
using CardDemo.Data;
using CardDemo.Tooling;
using Xunit;

namespace CardDemo.Parity.Tests;

/// <summary>
/// SYSOUT golden masters for the read-and-print batch programs (CBACT02C, CBACT03C): run the original
/// COBOL under GnuCOBOL and compare its console output, line-for-line, with the .NET port's DISPLAY
/// stream. Skipped when GnuCOBOL is not installed.
/// </summary>
public class PrinterGoldenMasterTests
{
    private static readonly HostKind Host = HostKind.Ascii;

    [Fact]
    public void Cbact02c_card_printer_sysout_matches_gnucobol()
    {
        GnuCobolInstall? install = GnuCobolInstall.TryLocate();
        if (install is null) return;

        RecordLayout layout = Parse("CVACT02Y.cpy");
        string work = NewWork("cbact02c");
        byte[] ascii = ToAscii("AWS.M2.CARDDEMO.CARDDATA.PS", layout);
        File.WriteAllBytes(Path.Combine(work, "card.seq"), ascii);

        var h = new GnuCobolHarness(install);
        CompileExeFile(h, work, "CBACT02C", CardDemoPaths.Program("CBACT02C.cbl"), [CardDemoPaths.CopybookDir]);
        CompileExeSrc(h, work, "LOADCARD", OracleCobolFixtures.LoadCard);
        RunOk(h, work, "LOADCARD", new() { ["LDIN"] = Path.Combine(work, "card.seq"), ["CARDFILE"] = Idx(work, "CARDFILE") });
        string[] cobol = Lines(RunCapture(h, work, "CBACT02C", new() { ["CARDFILE"] = Idx(work, "CARDFILE") }));

        using var db = new CardDemoDatabase();
        VsamFile cardFile = db.DefineFile(CardDemoFiles.Card.Definition);
        Load(cardFile, ascii, 150);
        IReadOnlyList<string> dotnet = new Cbact02cCardPrinter(cardFile, layout, Host).Run();

        AssertLinesEqual(cobol, dotnet);
    }

    [Fact]
    public void Cbact03c_xref_printer_sysout_matches_gnucobol()
    {
        GnuCobolInstall? install = GnuCobolInstall.TryLocate();
        if (install is null) return;

        RecordLayout layout = Parse("CVACT03Y.cpy");
        string work = NewWork("cbact03c");
        byte[] ascii = ToAscii("AWS.M2.CARDDEMO.CARDXREF.PS", layout);
        File.WriteAllBytes(Path.Combine(work, "xref.seq"), ascii);

        var h = new GnuCobolHarness(install);
        CompileExeFile(h, work, "CBACT03C", CardDemoPaths.Program("CBACT03C.cbl"), [CardDemoPaths.CopybookDir]);
        CompileExeSrc(h, work, "LOADXREF", OracleCobolFixtures.LoadXref);
        RunOk(h, work, "LOADXREF", new() { ["LDIN"] = Path.Combine(work, "xref.seq"), ["XREFFILE"] = Idx(work, "XREFFILE") });
        string[] cobol = Lines(RunCapture(h, work, "CBACT03C", new() { ["XREFFILE"] = Idx(work, "XREFFILE") }));

        using var db = new CardDemoDatabase();
        VsamFile xrefFile = db.DefineFile(CardDemoFiles.CardXref.Definition);
        Load(xrefFile, ascii, 50);
        IReadOnlyList<string> dotnet = new Cbact03cXrefPrinter(xrefFile, layout, Host).Run();

        AssertLinesEqual(cobol, dotnet);
    }

    // --- helpers ----------------------------------------------------------------------------------

    private static void AssertLinesEqual(string[] cobol, IReadOnlyList<string> dotnet)
    {
        Assert.Equal(cobol.Length, dotnet.Count);
        for (int i = 0; i < cobol.Length; i++)
            Assert.True(cobol[i] == dotnet[i],
                $"SYSOUT line {i} differs:\n  GnuCOBOL: [{cobol[i]}]\n  .NET    : [{dotnet[i]}]");
    }

    private static string[] Lines(string stdout) =>
        stdout.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    private static string Idx(string work, string n) => Path.Combine(work, n + ".idx");

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
        for (int i = 0; i < data.Length; i += reclen)
            Assert.Equal(FileStatus.Ok, f.Write(data[i..(i + reclen)]));
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
