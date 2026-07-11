namespace CardDemo.Domain.Entities;

/// <summary>
/// Card cross-reference record. Source layout: CVACT03Y.cpy (CARD-XREF-RECORD, RECLN 50).
/// The supplied ASCII fixture (cardxref.txt) is the known 36-byte compatibility form
/// (card number + customer ID + account ID, no trailing filler).
/// </summary>
public sealed class CardXref
{
    /// <summary>XREF-CARD-NUM PIC X(16) — primary key.</summary>
    public string CardNumber { get; set; } = string.Empty;

    /// <summary>XREF-CUST-ID PIC 9(09).</summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>XREF-ACCT-ID PIC 9(11) — non-unique alternate access path (CXACAIX).</summary>
    public string AccountId { get; set; } = string.Empty;
}
