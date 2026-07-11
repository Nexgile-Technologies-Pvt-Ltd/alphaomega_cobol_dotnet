namespace CardDemo.Mq;

/// <summary>
/// The in-proc message envelope. Carries the payload plus the subset of MQMD fields the CardDemo
/// programs read or set (MQ_SHIM.md §3/§6.1). The body is a string because every CardDemo payload is
/// MQFMT-STRING character data; <see cref="Body"/> is what each side parses/builds per MQ_SHIM.md §5.
/// </summary>
public sealed class MqMessage
{
    /// <summary>The character payload (free-text date reply, labeled account block, or trailing-comma CSV).</summary>
    public string Body { get; set; } = "";

    /// <summary>MQMD-MSGID (24 bytes). VSAM-MQ echoes the request MsgId into the reply; COPAUA0C uses MQMI-NONE.</summary>
    public byte[] MsgId { get; set; } = MqConstants.MqmiNone;

    /// <summary>MQMD-CORRELID (24 bytes) — the primary request/response correlation key (both modules echo it).</summary>
    public byte[] CorrelId { get; set; } = MqConstants.MqciNone;

    /// <summary>MQMD-REPLYTOQ (48 bytes, trimmed name) — where the requester wants the reply.</summary>
    public string ReplyToQueue { get; set; } = "";

    /// <summary>MQMD-REPLYTOQMGR — reply queue manager (blank = local).</summary>
    public string ReplyToQMgr { get; set; } = "";

    /// <summary>MQMD-FORMAT — defaults to MQFMT-STRING (all CardDemo replies are character data).</summary>
    public string Format { get; set; } = MqConstants.MqfmtString;

    /// <summary>MQMD-MSGTYPE — COPAUA0C reply sets MQMT-REPLY.</summary>
    public int MsgType { get; set; } = MqConstants.MqmtDatagram;

    /// <summary>MQMD-PERSISTENCE — COPAUA0C reply sets MQPER-NOT-PERSISTENT.</summary>
    public int Persistence { get; set; } = MqConstants.MqperNotPersistent;

    /// <summary>MQMD-EXPIRY (1/10 s units; -1 = unlimited). COPAUA0C reply sets 50 (= 5 s).</summary>
    public int Expiry { get; set; } = MqConstants.MqeiUnlimited;

    /// <summary>MQMD-CODEDCHARSETID — VSAM-MQ replies set MQCCSI-Q-MGR.</summary>
    public int CodedCharSetId { get; set; } = MqConstants.MqccsiQMgr;

    /// <summary>
    /// Absolute time (UTC) at which this message expires, or null for unlimited. Computed from
    /// <see cref="Expiry"/> when the message is enqueued so a test can simulate the 5 s auth-reply expiry.
    /// </summary>
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>Creates a 24-byte MQ id from a token string (UTF-8, right-padded/truncated to 24).</summary>
    public static byte[] MakeId(string token)
    {
        var id = new byte[24];
        if (!string.IsNullOrEmpty(token))
        {
            byte[] src = System.Text.Encoding.UTF8.GetBytes(token);
            Array.Copy(src, id, Math.Min(src.Length, 24));
        }
        return id;
    }

    /// <summary>True when two 24-byte id fields are byte-equal (used for CorrelId matching).</summary>
    public static bool IdEquals(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);
}
