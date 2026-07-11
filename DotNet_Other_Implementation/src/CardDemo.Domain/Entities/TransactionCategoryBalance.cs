using CardDemo.Domain.Common;

namespace CardDemo.Domain.Entities;

/// <summary>
/// Transaction category balance. Source layout: CVTRA01Y.cpy (TRAN-CAT-BAL-RECORD, RECLN 50).
/// Composite key is account ID + transaction type + category code (17 bytes).
/// </summary>
public sealed class TransactionCategoryBalance : IVersioned
{
    /// <summary>TRANCAT-ACCT-ID PIC 9(11).</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>TRANCAT-TYPE-CD PIC X(02).</summary>
    public string TypeCode { get; set; } = string.Empty;

    /// <summary>TRANCAT-CD PIC 9(04) — kept as string to preserve leading zeros.</summary>
    public string CategoryCode { get; set; } = string.Empty;

    /// <summary>TRAN-CAT-BAL PIC S9(09)V99.</summary>
    public decimal Balance { get; set; }

    public long RowVersion { get; set; }
}
