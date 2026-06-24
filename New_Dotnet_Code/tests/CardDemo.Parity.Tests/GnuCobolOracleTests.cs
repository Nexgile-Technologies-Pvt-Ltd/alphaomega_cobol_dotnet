using CardDemo.Tooling;
using Xunit;

namespace CardDemo.Parity.Tests;

/// <summary>
/// Verifies the GnuCOBOL reference-oracle toolchain: the compiler is present and the CardDemo programs
/// compile under it with our dialect flags and copybooks. These tests are no-ops when GnuCOBOL is not
/// installed (xUnit 2.9 has no Assert.Skip), so they act as an automated gate wherever the oracle exists.
/// </summary>
public class GnuCobolOracleTests
{
    private static string WorkDir(string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "carddemo_oracle", name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Cobc_is_available_and_reports_version()
    {
        GnuCobolInstall? install = GnuCobolInstall.TryLocate();
        if (install is null) return; // GnuCOBOL not installed here; oracle gate skipped

        var harness = new GnuCobolHarness(install);
        string version = harness.Version();
        Assert.Contains("GnuCOBOL", version);
    }

    [Fact]
    public void Cbact04c_compiles_as_a_module_under_gnucobol()
    {
        GnuCobolInstall? install = GnuCobolInstall.TryLocate();
        if (install is null) return;

        string work = WorkDir("cbact04c");
        string dll = Path.Combine(work, "CBACT04C.dll");
        if (File.Exists(dll)) File.Delete(dll);

        var harness = new GnuCobolHarness(install);
        ProcessResult result = harness.CompileModule(
            CardDemoPaths.Program("CBACT04C.cbl"),
            [CardDemoPaths.CopybookDir],
            work);

        Assert.True(result.Success, $"cobc failed (exit {result.ExitCode}):\n{result.StdErr}\n{result.StdOut}");
        Assert.True(File.Exists(dll), "expected CBACT04C.dll to be produced");
    }
}
