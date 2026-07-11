namespace CardDemo.Mq;

/// <summary>
/// The small subset of IBM MQ constant values the CardDemo MQ programs reference (CMQV copybook). Only
/// the names the three server programs actually touch are reproduced; values match the IBM constants so
/// the shim's externally-visible contract lines up with the COBOL. See MQ_SHIM.md §3–§4.
/// </summary>
public static class MqConstants
{
    // ---- Completion codes (MQCC-*) -------------------------------------------------------------------
    public const int MqccOk = 0;
    public const int MqccWarning = 1;
    public const int MqccFailed = 2;

    // ---- Reason codes (MQRC-*) -----------------------------------------------------------------------
    public const int MqrcNone = 0;
    /// <summary>Returned by GET when the (request) queue is empty after the wait — drives the drain-loop exit.</summary>
    public const int MqrcNoMsgAvailable = 2033;

    // ---- Message types (MQMT-*) ----------------------------------------------------------------------
    public const int MqmtRequest = 1;
    public const int MqmtReply = 2;
    public const int MqmtDatagram = 8;

    // ---- Persistence (MQPER-*) -----------------------------------------------------------------------
    public const int MqperNotPersistent = 0;
    public const int MqperPersistent = 1;

    // ---- Format (MQFMT-*) ----------------------------------------------------------------------------
    /// <summary>MQFMT-STRING — all CardDemo replies set this (character payload). MQ_SHIM.md §3/§6.5.</summary>
    public const string MqfmtString = "MQSTR   ";
    public const string MqfmtNone = "        ";

    // ---- Coded char set (MQCCSI-*) -------------------------------------------------------------------
    /// <summary>MQCCSI-Q-MGR — VSAM-MQ replies set the reply charset to the queue-manager default.</summary>
    public const int MqccsiQMgr = 0;

    // ---- "None" id sentinels (24-byte fields; the shim models them as empty arrays) ------------------
    /// <summary>MQMI-NONE — a fresh/absent MsgId (COPAUA0C sets reply MsgId to this).</summary>
    public static byte[] MqmiNone => new byte[24];
    /// <summary>MQCI-NONE — match any CorrelId on GET.</summary>
    public static byte[] MqciNone => new byte[24];

    // ---- Expiry --------------------------------------------------------------------------------------
    /// <summary>Unlimited expiry.</summary>
    public const int MqeiUnlimited = -1;
    /// <summary>COPAUA0C reply expiry = 50 (1/10 s units = 5.0 s).</summary>
    public const int AuthReplyExpiry = 50;

    // ---- Wait interval (ms) --------------------------------------------------------------------------
    /// <summary>Both modules GET with a 5000 ms (5 s) wait.</summary>
    public const int DefaultWaitMs = 5000;

    // ---- Loop cap ------------------------------------------------------------------------------------
    /// <summary>COPAUA0C caps its drain loop at 500 messages per trigger (WS-REQSTS-PROCESS-LIMIT).</summary>
    public const int AuthProcessLimit = 500;
}

/// <summary>
/// The fixed CardDemo queue names (verbatim from the COBOL literals / README / CSD). See MQ_SHIM.md §1.
/// Logical keys are the trimmed mainframe object names (case-sensitive).
/// </summary>
public static class MqQueues
{
    // VSAM-MQ pair (CODATE01 / COACCT01)
    public const string RequestDate = "CARD.DEMO.REQUEST.DATE";
    public const string ReplyDate = "CARD.DEMO.REPLY.DATE";
    public const string RequestAcct = "CARD.DEMO.REQUEST.ACCT";
    public const string ReplyAcct = "CARD.DEMO.REPLY.ACCT";
    public const string Error = "CARD.DEMO.ERROR";

    // Authorization (COPAUA0C)
    public const string PauthRequest = "AWS.M2.CARDDEMO.PAUTH.REQUEST";
    public const string PauthReply = "AWS.M2.CARDDEMO.PAUTH.REPLY";
}
