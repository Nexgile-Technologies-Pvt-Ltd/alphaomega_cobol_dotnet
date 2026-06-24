namespace CardDemo.Cobol.Runtime;

/// <summary>
/// Single source of "now" for every COBOL <c>FUNCTION CURRENT-DATE</c> / CICS <c>ASKTIME</c> /
/// <c>FORMATTIME</c> site. Injecting it makes batch and online output deterministic, so golden-master
/// diffs need only mask the known timestamp byte-ranges rather than chase wall-clock drift.
/// </summary>
public interface IClock
{
    /// <summary>Current local date/time, as <c>FUNCTION CURRENT-DATE</c> would observe it.</summary>
    DateTime Now { get; }
}

/// <summary>Real wall-clock implementation for production runs.</summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();
    public DateTime Now => DateTime.Now;
}

/// <summary>Frozen clock for tests and golden capture; returns a fixed instant.</summary>
public sealed class FixedClock(DateTime now) : IClock
{
    public DateTime Now { get; } = now;
}
