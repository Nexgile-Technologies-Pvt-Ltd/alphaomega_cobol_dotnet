using CardDemo.Cobol.Runtime;
using CardDemo.Tooling;
using CardDemo.Utilities;
using Xunit;

namespace CardDemo.Parity.Tests;

/// <summary>
/// Validates the <see cref="Cobdatft"/> shim against a COBOL transcription of the COBDATFT assembler
/// routine under GnuCOBOL (no HLASM toolchain exists on this platform). Both process the same
/// CODATECN-REC inputs and the 80-byte results must match. Skipped when GnuCOBOL is not installed.
/// </summary>
public class CobdatftTests
{
    private static readonly HostKind Host = HostKind.Ascii;

    // (type, outtype, input date) cases: conversions and every error path.
    private static readonly (string Type, string Out, string Inp)[] Cases =
    {
        ("2", "2", "2014-11-20"), // YYYY-MM-DD -> YYYYMMDD
        ("1", "1", "20141120"),   // YYYYMMDD   -> YYYY-MM-DD
        ("2", "2", "2025-05-20"),
        ("1", "1", "20250520"),
        ("1", "2", "20141120"),   // error: output type mismatch
        ("2", "1", "2014-11-20"), // error: output type mismatch
        ("3", "2", "20141120"),   // error: invalid input type
        ("1", "1", "2014-1120"),  // error: separator in a type-1 input
    };

    [Fact]
    public void Cobdatft_matches_cobol_transcription()
    {
        GnuCobolInstall? install = GnuCobolInstall.TryLocate();
        if (install is null) return;

        byte[][] inputs = Cases.Select(c => BuildRec(c.Type, c.Out, c.Inp)).ToArray();

        // .NET shim.
        var dotnet = inputs.Select(r => { var copy = (byte[])r.Clone(); Cobdatft.Convert(copy, Host); return copy; }).ToArray();

        // COBOL (DATDRV calls COBDATFT).
        string work = Path.Combine(Path.GetTempPath(), "carddemo_cobdatft");
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        var datin = new byte[inputs.Length * 80];
        for (int i = 0; i < inputs.Length; i++) inputs[i].CopyTo(datin, i * 80);
        File.WriteAllBytes(Path.Combine(work, "datin.seq"), datin);

        var h = new GnuCobolHarness(install);
        CompileModule(h, work, "COBDATFT", OracleCobolFixtures.Cobdatft);
        CompileExe(h, work, "DATDRV", OracleCobolFixtures.DatDrv);
        ProcessResult run = h.Run(Path.Combine(work, "DATDRV.exe"), work, new Dictionary<string, string>
        {
            ["DATIN"] = Path.Combine(work, "datin.seq"),
            ["DATOUT"] = Path.Combine(work, "datout.seq"),
            ["COB_LIBRARY_PATH"] = work + Path.PathSeparator + Path.Combine(install.HomeDir, "extras"),
        });
        Assert.True(run.Success, run.StdErr);

        byte[] datout = File.ReadAllBytes(Path.Combine(work, "datout.seq"));
        Assert.Equal(inputs.Length * 80, datout.Length);

        for (int i = 0; i < inputs.Length; i++)
        {
            byte[] cobol = datout[(i * 80)..((i + 1) * 80)];
            Assert.True(cobol.SequenceEqual(dotnet[i]),
                $"case {i} ({Cases[i].Type}/{Cases[i].Out}/{Cases[i].Inp}):\n" +
                $"  COBOL [{Latin(cobol)}]\n  .NET  [{Latin(dotnet[i])}]");
        }
    }

    private static byte[] BuildRec(string type, string outType, string inp)
    {
        var enc = HostEncoding.For(Host);
        var rec = new byte[80];
        enc.GetBytes(type).CopyTo(rec, 0);
        enc.GetBytes(inp.PadRight(20)).CopyTo(rec, 1);
        enc.GetBytes(outType).CopyTo(rec, 21);
        enc.GetBytes(new string('Z', 20)).CopyTo(rec, 22);   // OUT-DATE marker to reveal modified bytes
        enc.GetBytes(new string(' ', 38)).CopyTo(rec, 42);   // ERROR-MSG
        return rec;
    }

    private static string Latin(byte[] b) => System.Text.Encoding.Latin1.GetString(b);

    private static void CompileModule(GnuCobolHarness h, string work, string name, string source)
    {
        string src = Path.Combine(work, name + ".cob");
        File.WriteAllText(src, source);
        Assert.True(h.CompileModule(src, [CardDemoPaths.CopybookDir], work).Success, $"compile {name}");
    }

    private static void CompileExe(GnuCobolHarness h, string work, string name, string source)
    {
        string src = Path.Combine(work, name + ".cob");
        File.WriteAllText(src, source);
        Assert.True(h.CompileExecutable(src, [CardDemoPaths.CopybookDir], name + ".exe", work).Success, $"compile {name}");
    }
}
