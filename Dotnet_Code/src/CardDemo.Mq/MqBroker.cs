namespace CardDemo.Mq;

/// <summary>
/// A handle to an opened queue (the in-proc analogue of an MQ object handle, HOBJ). Returned by
/// <see cref="MqBroker.Open"/> and passed to <see cref="MqBroker.Get"/> / <see cref="MqBroker.Put"/>.
/// Carries the open options purely as metadata; the in-proc shim treats the connection as a single
/// ambient context (MQ_SHIM.md "wrong HCONN" faithful bug — the handle is not tied to a real HCONN).
/// </summary>
public sealed class MqQueueHandle
{
    /// <summary>The queue this handle is open against.</summary>
    public MqQueue Queue { get; }

    /// <summary>The MQOO-* open options passed to <see cref="MqBroker.Open"/> (metadata only).</summary>
    public int OpenOptions { get; }

    /// <summary>True once <see cref="MqBroker.Close"/> has been called; further GET/PUT throw.</summary>
    public bool Closed { get; internal set; }

    internal MqQueueHandle(MqQueue queue, int openOptions)
    {
        Queue = queue;
        OpenOptions = openOptions;
    }
}

/// <summary>The completion/reason pair an MQ verb returns (MQCC + MQRC). See MQ_SHIM.md §4.</summary>
/// <param name="CompletionCode">MQCC-* (Ok=0, Warning=1, Failed=2).</param>
/// <param name="ReasonCode">MQRC-* (None=0; NoMsgAvailable=2033 on an empty GET).</param>
public readonly record struct MqResult(int CompletionCode, int ReasonCode)
{
    /// <summary>True when the verb completed normally (MQCC-OK).</summary>
    public bool Ok => CompletionCode == MqConstants.MqccOk;

    /// <summary>True when a GET found no message after the wait (drives the drain-loop exit).</summary>
    public bool NoMessage => ReasonCode == MqConstants.MqrcNoMsgAvailable;

    /// <summary>A successful (MQCC-OK / MQRC-NONE) result.</summary>
    public static MqResult Success => new(MqConstants.MqccOk, MqConstants.MqrcNone);

    /// <summary>An empty-queue (MQCC-WARNING / MQRC-NO-MSG-AVAILABLE) result.</summary>
    public static MqResult NoMsgAvailable => new(MqConstants.MqccWarning, MqConstants.MqrcNoMsgAvailable);
}

/// <summary>
/// The in-process MQ shim: an instance replaces the IBM MQ queue manager + the CICS trigger monitor for
/// CardDemo's two optional MQ modules. It is a single-threaded, deterministic request/response broker
/// with no external dependency (MQ_SHIM.md §6). It exposes:
/// <list type="bullet">
///   <item>The verb-level API the server programs sit behind:
///         <see cref="Open"/> (MQOPEN), <see cref="Get"/> (MQGET), <see cref="Put"/> (MQPUT),
///         <see cref="Put1"/> (MQPUT1), <see cref="Close"/> (MQCLOSE).</item>
///   <item>The dispatcher: <see cref="RegisterServer"/> maps a request queue to its server handler, and
///         <see cref="Drain"/> / <see cref="Request"/> trigger that handler (there is no background
///         trigger monitor — triggering is synchronous, MQ_SHIM.md §6.4).</item>
/// </list>
/// Queues are created on first reference (<see cref="GetOrCreateQueue"/>). The 5 s GET "wait" is modeled
/// as immediate: an empty queue returns no-message right away (a real-MQ blocking detail only).
/// </summary>
public sealed class MqBroker
{
    private readonly Dictionary<string, MqQueue> _queues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IMqServer> _servers = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _nowUtc;

    /// <summary>Creates a broker. <paramref name="nowUtc"/> supplies "now" for expiry (defaults to UTC clock).</summary>
    public MqBroker(Func<DateTime>? nowUtc = null) => _nowUtc = nowUtc ?? (() => DateTime.UtcNow);

    // ---- Queue management ----------------------------------------------------------------------------

    /// <summary>Returns the named queue, creating it on first use (names trimmed per MQ_SHIM.md §1).</summary>
    public MqQueue GetOrCreateQueue(string name)
    {
        string key = (name ?? "").Trim();
        if (!_queues.TryGetValue(key, out MqQueue? q))
        {
            q = new MqQueue(key);
            _queues[key] = q;
        }
        return q;
    }

    /// <summary>True if a queue with the (trimmed) name has been created.</summary>
    public bool HasQueue(string name) => _queues.ContainsKey((name ?? "").Trim());

    // ---- Verb-level API (MQOPEN / MQGET / MQPUT / MQPUT1 / MQCLOSE) -----------------------------------

    /// <summary>MQOPEN: opens the named queue with the given MQOO-* options, returning a handle.</summary>
    public MqQueueHandle Open(string queueName, int openOptions)
        => new(GetOrCreateQueue(queueName), openOptions);

    /// <summary>
    /// MQGET: removes and returns the head message of the open queue (both modules GET "any next" with
    /// MsgId/CorrelId = NONE). On an empty queue returns <see cref="MqResult.NoMsgAvailable"/> with
    /// <paramref name="message"/> = null (the no-message-after-wait case that ends the drain loop).
    /// </summary>
    public MqResult Get(MqQueueHandle handle, out MqMessage? message)
    {
        EnsureOpen(handle);
        message = handle.Queue.Dequeue(_nowUtc());
        return message is null ? MqResult.NoMsgAvailable : MqResult.Success;
    }

    /// <summary>MQPUT: appends <paramref name="message"/> to a pre-opened queue handle (VSAM-MQ reply path).</summary>
    public MqResult Put(MqQueueHandle handle, MqMessage message)
    {
        EnsureOpen(handle);
        Stamp(message);
        handle.Queue.Enqueue(message);
        return MqResult.Success;
    }

    /// <summary>
    /// MQPUT1: open-put-close by name in one step (COPAUA0C reply path — targets MQMD-REPLYTOQ of the
    /// request). Reduces to "enqueue on the named queue" in-proc.
    /// </summary>
    public MqResult Put1(string queueName, MqMessage message)
    {
        Stamp(message);
        GetOrCreateQueue(queueName).Enqueue(message);
        return MqResult.Success;
    }

    /// <summary>MQCLOSE: marks the handle closed (MQCO-NONE). Idempotent.</summary>
    public MqResult Close(MqQueueHandle handle)
    {
        handle.Closed = true;
        return MqResult.Success;
    }

    // ---- Dispatcher (trigger + requester) ------------------------------------------------------------

    /// <summary>
    /// Maps a server handler to its request queue (MQ_SHIM.md §6.4). The handler's
    /// <see cref="IMqServer.RequestQueue"/> is the dispatcher key.
    /// </summary>
    public void RegisterServer(IMqServer server) => _servers[server.RequestQueue] = server;

    /// <summary>True if a server handler is registered for the (trimmed) request queue.</summary>
    public bool HasServer(string requestQueue) => _servers.ContainsKey((requestQueue ?? "").Trim());

    /// <summary>
    /// Triggers the server registered for <paramref name="requestQueue"/>, passing a
    /// <see cref="TriggerMessage"/> carrying that queue name (trigger-on-first-message semantics). The
    /// handler drains the queue and PUTs replies. Returns the number of messages it processed. Throws if
    /// no server is registered for the queue.
    /// </summary>
    public int Drain(string requestQueue, string triggerData = "")
    {
        string key = (requestQueue ?? "").Trim();
        if (!_servers.TryGetValue(key, out IMqServer? server))
            throw new InvalidOperationException($"No MQ server registered for request queue '{key}'.");
        return server.Handle(new TriggerMessage { QueueName = key, TriggerData = triggerData }, this);
    }

    /// <summary>
    /// The shim-supplied requester (no real client exists in the repo — MQ_SHIM.md §6.3). PUTs the request
    /// (setting ReplyToQueue / MsgId / CorrelId), triggers the matching server, then GETs the reply from
    /// <paramref name="replyToQueue"/> filtering by <paramref name="correlId"/> and returns it. Returns
    /// null if the server produced no matching reply.
    /// </summary>
    /// <param name="requestQueue">One of the MQ_SHIM.md §1 request queues.</param>
    /// <param name="body">The payload per MQ_SHIM.md §5.</param>
    /// <param name="replyToQueue">
    /// The requester's reply queue. Honored as the PUT target only by COPAUA0C; the VSAM-MQ servers
    /// ignore it and reply to their literal queue (faithful bug), so pass that literal to receive a
    /// VSAM-MQ reply.
    /// </param>
    /// <param name="correlId">Correlation token; echoed back on reply.CorrelId and used to match the reply.</param>
    /// <param name="msgId">Optional request MsgId (VSAM-MQ echoes it onto the reply).</param>
    public MqMessage? Request(
        string requestQueue,
        string body,
        string replyToQueue,
        byte[] correlId,
        byte[]? msgId = null)
    {
        var request = new MqMessage
        {
            Body = body,
            ReplyToQueue = (replyToQueue ?? "").Trim(),
            CorrelId = correlId ?? MqConstants.MqciNone,
            MsgId = msgId ?? MqConstants.MqmiNone,
            MsgType = MqConstants.MqmtRequest,
            Format = MqConstants.MqfmtString,
        };
        GetOrCreateQueue(requestQueue).Enqueue(request);

        Drain(requestQueue);

        // A real client browses the reply queue for its CorrelId; the shim matches reply.CorrelId.
        MqQueue replyQ = GetOrCreateQueue(request.ReplyToQueue);
        return replyQ.DequeueByCorrelId(request.CorrelId, _nowUtc());
    }

    // ---- internals -----------------------------------------------------------------------------------

    private void Stamp(MqMessage message)
    {
        // Materialize the expiry window so a later GET can drop an expired (e.g. 5 s auth) reply.
        message.ExpiresAtUtc = message.Expiry == MqConstants.MqeiUnlimited
            ? null
            : _nowUtc().AddMilliseconds(message.Expiry * 100.0); // MQ expiry is in 1/10 s units
    }

    private static void EnsureOpen(MqQueueHandle handle)
    {
        if (handle.Closed)
            throw new InvalidOperationException($"Queue handle for '{handle.Queue.Name}' is closed.");
    }
}
