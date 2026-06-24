using System.Diagnostics;

namespace CardDemo.Tooling;

/// <summary>
/// A located GnuCOBOL installation (the MinGW build), providing the compiler path and the environment
/// variables its <c>set_env.cmd</c> requires so <c>cobc</c> and the programs it builds can run.
/// </summary>
public sealed class GnuCobolInstall
{
    /// <summary>Root directory of the GnuCOBOL install (contains <c>bin</c>, <c>config</c>, <c>copy</c>, ...).</summary>
    public string HomeDir { get; }

    public GnuCobolInstall(string homeDir) => HomeDir = homeDir;

    public string CompilerPath => Path.Combine(HomeDir, "bin", "cobc.exe");

    /// <summary>Environment variables to apply when invoking <c>cobc</c> or a compiled program.</summary>
    public IReadOnlyDictionary<string, string> Environment()
    {
        string bin = Path.Combine(HomeDir, "bin");
        string existingPath = System.Environment.GetEnvironmentVariable("PATH") ?? "";
        return new Dictionary<string, string>
        {
            ["PATH"] = bin + Path.PathSeparator + existingPath,
            ["COB_CONFIG_DIR"] = Path.Combine(HomeDir, "config"),
            ["COB_COPY_DIR"] = Path.Combine(HomeDir, "copy"),
            ["COB_CFLAGS"] = "-I" + Path.Combine(HomeDir, "include"),
            ["COB_LDFLAGS"] = "-L" + Path.Combine(HomeDir, "lib"),
            ["COB_LIBRARY_PATH"] = Path.Combine(HomeDir, "extras"),
        };
    }

    /// <summary>
    /// Locates a GnuCOBOL install via the <c>GNUCOBOL_HOME</c> environment variable or the default
    /// <c>C:\Tools\GnuCOBOL</c> location; returns null if none is found (so oracle tests can skip).
    /// </summary>
    public static GnuCobolInstall? TryLocate()
    {
        foreach (string? home in new[]
                 {
                     System.Environment.GetEnvironmentVariable("GNUCOBOL_HOME"),
                     @"C:\Tools\GnuCOBOL",
                 })
        {
            if (!string.IsNullOrEmpty(home) && File.Exists(Path.Combine(home, "bin", "cobc.exe")))
                return new GnuCobolInstall(home);
        }
        return null;
    }
}

/// <summary>
/// Drives GnuCOBOL (<c>cobc</c>) to compile and run COBOL programs, capturing console output and exit
/// codes. This is the reference oracle: the original programs run on the same inputs as the .NET port
/// so their results can be compared.
/// </summary>
/// <remarks>
/// GnuCOBOL defaults to an ASCII / native-binary runtime, so it validates <em>business logic</em>
/// (computed values, control flow), not IBM byte images; EBCDIC byte-exactness is proven separately by
/// the codec round-trip suite. Dialect deltas are documented as specified emulation boundaries.
/// </remarks>
public sealed class GnuCobolHarness
{
    private readonly GnuCobolInstall _install;

    /// <summary>COBOL dialect / format options passed to <c>cobc</c> (CardDemo is fixed-format IBM COBOL).</summary>
    public IReadOnlyList<string> DialectArgs { get; }

    public GnuCobolHarness(GnuCobolInstall install, IReadOnlyList<string>? dialectArgs = null)
    {
        _install = install;
        DialectArgs = dialectArgs ?? ["-std=ibm", "-fformat=fixed"];
    }

    /// <summary>The <c>cobc</c> version banner (first line of <c>cobc --version</c>).</summary>
    public string Version()
    {
        ProcessResult r = Run(_install.CompilerPath, ["--version"], workingDirectory: null);
        return r.StdOut.Split('\n').FirstOrDefault()?.Trim() ?? "";
    }

    /// <summary>
    /// Compiles a subprogram into a callable module (<c>cobc -m</c>) in <paramref name="workingDirectory"/>,
    /// adding each copybook directory as an include path. The module is named after the PROGRAM-ID.
    /// </summary>
    public ProcessResult CompileModule(string sourceFile, IEnumerable<string> copyDirectories, string workingDirectory)
        => Compile("-m", sourceFile, copyDirectories, outputName: null, workingDirectory);

    /// <summary>
    /// Compiles a main program into an executable (<c>cobc -x</c>) named <paramref name="outputExe"/>.
    /// </summary>
    public ProcessResult CompileExecutable(string sourceFile, IEnumerable<string> copyDirectories, string outputExe, string workingDirectory)
        => Compile("-x", sourceFile, copyDirectories, outputExe, workingDirectory);

    /// <summary>Runs a compiled executable, with optional extra environment (e.g. file ASSIGN mappings).</summary>
    public ProcessResult Run(string exePath, string workingDirectory, IReadOnlyDictionary<string, string>? extraEnv = null)
        => Run(exePath, [], workingDirectory, extraEnv);

    private ProcessResult Compile(string modeFlag, string sourceFile, IEnumerable<string> copyDirectories, string? outputName, string workingDirectory)
    {
        var args = new List<string> { modeFlag };
        args.AddRange(DialectArgs);
        foreach (string dir in copyDirectories) { args.Add("-I"); args.Add(dir); }
        if (outputName is not null) { args.Add("-o"); args.Add(outputName); }
        args.Add(sourceFile);
        return Run(_install.CompilerPath, args, workingDirectory);
    }

    private ProcessResult Run(string fileName, IReadOnlyList<string> args, string? workingDirectory, IReadOnlyDictionary<string, string>? extraEnv = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (workingDirectory is not null) psi.WorkingDirectory = workingDirectory;
        foreach (string a in args) psi.ArgumentList.Add(a);
        foreach ((string k, string v) in _install.Environment()) psi.Environment[k] = v;
        if (extraEnv is not null)
            foreach ((string k, string v) in extraEnv) psi.Environment[k] = v;

        using var p = new Process { StartInfo = psi };
        p.Start();
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return new ProcessResult(p.ExitCode, stdout, stderr);
    }
}

/// <summary>Result of invoking an external process: exit code and captured output.</summary>
public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}
