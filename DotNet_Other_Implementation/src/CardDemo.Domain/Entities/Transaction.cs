namespace CardDemo.Domain.Entities;

/// <summary>
/// Transaction record. Source layout: CVTRA05Y.cpy (TRAN-RECORD, RECLN 350).
/// Same layout is used by the daily transaction file (dailytran.txt).
/// </summary>
public sealed class Transaction
{
    /// <summary>TRAN-ID PIC X(16) — primary key.</summary>
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>TRAN-TYPE-CD PIC X(02).</summary>
    public string TypeCode { get; set; } = string.Empty;

    /// <summary>TRAN-CAT-CD PIC 9(04) — kept as string to preserve leading zeros.</summary>
    public string CategoryCode { get; set; } = string.Empty;

    /// <summary>TRAN-SOURCE PIC X(10).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>TRAN-DESC PIC X(100).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>TRAN-AMT PIC S9(09)V99.</summary>
    public decimal Amount { get; set; }

    /// <summary>TRAN-MERCHANT-ID PIC 9(09).</summary>
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>TRAN-MERCHANT-NAME PIC X(50).</summary>
    public string MerchantName { get; set; } = string.Empty;

    /// <summary>TRAN-MERCHANT-CITY PIC X(50).</summary>
    public string MerchantCity { get; set; } = string.Empty;

    /// <summary>TRAN-MERCHANT-ZIP PIC X(10).</summary>
    public string MerchantZip { get; set; } = string.Empty;

    /// <summary>TRAN-CARD-NUM PIC X(16).</summary>
    public string CardNumber { get; set; } = string.Empty;

    /// <summary>TRAN-ORIG-TS PIC X(26) — canonical 26-char legacy timestamp text.</summary>
    public string OriginTimestamp { get; set; } = string.Empty;

    /// <summary>TRAN-PROC-TS PIC X(26).</summary>
    public string ProcessTimestamp { get; set; } = string.Empty;
}
