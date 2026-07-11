namespace CardDemo.Console;

/// <summary>
/// Process exit codes (09-DotNet-Target-Architecture.md#exit-code-contract).
/// </summary>
public static class ExitCodes
{
    public const int Ok = 0;                 // completed successfully
    public const int UsageError = 2;         // command-line/configuration error
    public const int BusinessRejects = 4;    // completed with business rejects (posting RC=4)
    public const int Unavailable = 8;        // requested resource/state unavailable
    public const int Fatal = 12;             // fatal data/I/O/integration failure
    public const int Cancelled = 130;        // cancelled by operator (Ctrl+C)
}
