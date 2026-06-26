namespace CardDemo.Mq;

/// <summary>
/// The MQ trigger message a server transaction is started with (MQ_SHIM.md §2). Under CICS this is the
/// IBM MQTM structure read via EXEC CICS RETRIEVE; here it is the two fields the programs consume.
/// </summary>
public sealed class TriggerMessage
{
    /// <summary>MQTM-QNAME — the request queue the server should GET from (trigger-driven, not hard-coded).</summary>
    public string QueueName { get; init; } = "";

    /// <summary>MQTM-TRIGGERDATA — captured by COPAUA0C but otherwise ignored.</summary>
    public string TriggerData { get; init; } = "";
}

/// <summary>
/// A server-side, MQ-triggered handler (one per CardDemo MQ program: CODATE01 / COACCT01 / COPAUA0C).
/// The dispatcher invokes <see cref="Handle"/> with the <see cref="TriggerMessage"/> when a request is
/// enqueued on the handler's request queue; the handler drains the queue per MQ_SHIM.md §6.2, building
/// and PUTting replies via <paramref name="mq"/> (the same broker), echoing correlation per §3.
/// </summary>
public interface IMqServer
{
    /// <summary>The request queue name this server is mapped to (the dispatcher key — MQ_SHIM.md §6.4).</summary>
    string RequestQueue { get; }

    /// <summary>Drains the request queue and replies. Returns the number of messages processed.</summary>
    int Handle(TriggerMessage trigger, MqBroker mq);
}

/// <summary>
/// The diagnostic error sink for the VSAM-MQ pair. CODATE01/COACCT01 PUT an MQ-ERR-DISPLAY block to
/// CARD.DEMO.ERROR on MQ/CICS failures (MQ_SHIM.md §5.4); COPAUA0C instead writes to a CICS TD queue
/// (CSSL) — model that as this injected sink rather than a shim queue (MQ_SHIM.md §1.3).
/// </summary>
public interface IErrorLog
{
    /// <summary>Records a diagnostic error line.</summary>
    void Write(string message);
}

/// <summary>A no-op <see cref="IErrorLog"/> for callers that don't need diagnostics.</summary>
public sealed class NullErrorLog : IErrorLog
{
    /// <summary>Shared instance.</summary>
    public static readonly NullErrorLog Instance = new();
    private NullErrorLog() { }
    public void Write(string message) { }
}

/// <summary>An <see cref="IErrorLog"/> that collects lines in memory (handy for tests / the CSSL TD queue).</summary>
public sealed class ListErrorLog : IErrorLog
{
    private readonly List<string> _lines = new();
    /// <summary>The recorded lines, in order.</summary>
    public IReadOnlyList<string> Lines => _lines;
    public void Write(string message) => _lines.Add(message);
}
