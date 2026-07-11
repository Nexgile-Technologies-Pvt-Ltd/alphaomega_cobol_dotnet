namespace CardDemo.Domain.Entities;

/// <summary>
/// Inbound inquiry request. Relational model of the MQ request queue drained by the
/// optional VSAM/MQ services COACCT01 (account inquiry, service INQA) and CODATE01
/// (system date, service DATE). A durable local queue replaces IBM MQ.
/// </summary>
public sealed class InquiryRequest
{
    public int Id { get; set; }
    public string Service { get; set; } = string.Empty;      // "INQA" (account) | "DATE"
    public string AccountId { get; set; } = string.Empty;    // 11-digit for INQA; ignored for DATE
    public string CorrelId { get; set; } = string.Empty;
    public string ReplyToQueue { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING";          // PENDING | DONE
    public string CreatedTimestamp { get; set; } = string.Empty;
}

/// <summary>Inquiry reply (outbox). Fixed 1000-byte-style labeled payload.</summary>
public sealed class InquiryReply
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public string Service { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;      // labeled reply text (logical length)
    public int LogicalLength { get; set; }
    public string CreatedTimestamp { get; set; } = string.Empty;
}
