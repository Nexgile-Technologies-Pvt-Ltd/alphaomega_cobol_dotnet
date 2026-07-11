namespace CardDemo.Domain.Entities;

/// <summary>
/// Durable pending report request. The interactive report screen (CORPT00C) writes
/// a request rather than executing embedded JCL; a batch command consumes it.
/// </summary>
public sealed class PendingReportRequest
{
    public int Id { get; set; }
    public string FromDate { get; set; } = string.Empty;   // yyyy-MM-dd
    public string ToDate { get; set; } = string.Empty;     // yyyy-MM-dd
    public string RequestedByUserId { get; set; } = string.Empty;
    public string RequestedAt { get; set; } = string.Empty; // 26-char legacy timestamp text
    public string Status { get; set; } = "PENDING";         // PENDING | COMPLETED
}
