using System.Diagnostics;

namespace CardDemo.Tooling;

/// <summary>
/// Drives GnuCOBOL (<c>cobc</c>) to compile and run a COBOL program, capturing its console output and
/// exit code. This is the reference oracle: the original programs are run on the same inputs as the
/// .NET port so their results can be compared.
/// </summary>
/// <remarks>
/// GnuCOBOL defaults to an ASCII / native-binary runtime, so it validates <em>business logic</em>
/// (computed values, control flow), not IBM byte images; EBCDIC byte-exactness is proven separately by
/// the codec round-trip suite. Dialect deltas are documented as specified emulation boundaries.
/// </remarks>
public sealed class GnuCobolHarness
{
    /// <summary>Path to the <c>cobc</c> compiler (resolved from PATH by default).</summary>
    public string CompilerPath { get; }

    /// <summary>COBOL dialect / format options passed to <c>cobc</c>.</summary>
    public IReadOnlyList<string> DialectArgs { get; }

    public GnuCobolHarness(string? compilerPath = null, IReadOnlyList<string>? dialectArgs = null)
    {
        CompilerPath = compilerPath ?? "cobc";
        // CardDemo source is fixed-format; -std=ibm enables the IBM extensions the programs use.
        DialectArgs = dialectArgs ?? ["-std=ibm", "-fformat=fixed"];
    }

    /// <summary>True if <c>cobc</c> can be invoked (used to skip oracle tests when GnuCOBOL is absent).</summary>
    public bool IsAvailable()
    {
        try
        {
            ProcessResult r = RunProcess(CompilerPath, ["--version"], workingDirectory: null, env: null);
            return r.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The <c>cobc</c> version banner (first line of <c>cobc --version</c>).</summary>
    public string Version()
    {
        ProcessResult r = RunProcess(CompilerPath, ["--version"], null, null);
        return r.StdOut.Split('\n').FirstOrDefault()?.Trim() ?? "";
    }

    /// <summary>
    /// Compiles <paramref name="sourceFile"/> into an executable named <paramref name="outputExe"/> in
    /// <paramref name="workingDirectory"/>, adding each <paramref name="copyDirectories"/> entry as a
    /// copybook include path (<c>-I</c>).
    /// </summary>
    public ProcessResult Compile(string sourceFile, IEnumerable<string> copyDirectories, string outputExe, string workingDirectory)
    {
        var args = new List<string> { "-x" };
        args.AddRange(DialectArgs);
        foreach (string dir in copyDirectories) { args.Add("-I"); args.Add(dir); }
        args.Add("-o");
        args.Add(outputExe);
        args.Add(sourceFile);
        return RunProcess(CompilerPath, args, workingDirectory, env: null);
    }

    /// <summary>
    /// Runs a compiled executable in <paramref name="workingDirectory"/> with the given environment
    /// variables (used to map COBOL file ASSIGN names to data files), capturing stdout/stderr/exit code.
    /// </summary>
    public ProcessResult Run(string exePath, string workingDirectory, IReadOnlyDictionary<string, string>? env = null)
        => RunProcess(exePath, [], workingDirectory, env);

    private static ProcessResult RunProcess(string fileName, IReadOnlyList<string> args, string? workingDirectory, IReadOnlyDictionary<string, string>? env)
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
        if (env is not null)
            foreach ((string k, string v) in env) psi.Environment[k] = v;

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
