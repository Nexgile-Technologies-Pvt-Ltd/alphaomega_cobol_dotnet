namespace CardDemo.Domain.Entities;

/// <summary>
/// Disclosure group interest pricing. Source layout: CVTRA02Y.cpy (DIS-GROUP-RECORD, RECLN 50).
/// Composite key is group ID + transaction type + category code (16 bytes).
/// </summary>
public sealed class DisclosureGroup
{
    /// <summary>DIS-ACCT-GROUP-ID PIC X(10).</summary>
    public string GroupId { get; set; } = string.Empty;

    /// <summary>DIS-TRAN-TYPE-CD PIC X(02).</summary>
    public string TypeCode { get; set; } = string.Empty;

    /// <summary>DIS-TRAN-CAT-CD PIC 9(04) — kept as string to preserve leading zeros.</summary>
    public string CategoryCode { get; set; } = string.Empty;

    /// <summary>DIS-INT-RATE PIC S9(04)V99 — annual interest rate as a percent.</summary>
    public decimal InterestRate { get; set; }
}
