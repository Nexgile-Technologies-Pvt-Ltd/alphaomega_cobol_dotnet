namespace CardDemo.Cobol.Runtime;

/// <summary>
/// Models an abnormal termination: a batch <c>CALL 'CEE3ABD'</c> (e.g. ABCODE 999 in
/// <c>CBTRN02C</c>/<c>CBACT04C</c>) or an online <c>EXEC CICS ABEND</c>.
/// </summary>
/// <remarks>
/// The handler that catches this is responsible for the faithful side effects: log <c>ABEND-DATA</c>,
/// honour the file recovery policy (CardDemo VSAM files are <c>RECOVERY(NONE)</c>, so committed record
/// updates are <em>not</em> backed out), and terminate the step with the correct process exit code.
/// </remarks>
public sealed class AbendException : Exception
{
    /// <summary>The abend code (e.g. "999" for CEE3ABD, or a CICS ABCODE such as "9999").</summary>
    public string AbendCode { get; }

    public AbendException(string abendCode, string? message = null)
        : base(message ?? $"ABEND {abendCode}")
    {
        AbendCode = abendCode;
    }
}
