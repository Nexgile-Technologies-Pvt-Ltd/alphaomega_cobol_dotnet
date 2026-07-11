namespace CardDemo.Domain.Entities;

/// <summary>
/// Transaction category reference. Source layout: CVTRA04Y.cpy (TRAN-CAT-RECORD, RECLN 60).
/// Composite key is transaction type + category code.
/// </summary>
public sealed class TransactionCategory
{
    /// <summary>TRAN-TYPE-CD PIC X(02).</summary>
    public string TypeCode { get; set; } = string.Empty;

    /// <summary>TRAN-CAT-CD PIC 9(04) — kept as string to preserve leading zeros.</summary>
    public string CategoryCode { get; set; } = string.Empty;

    /// <summary>TRAN-CAT-TYPE-DESC PIC X(50).</summary>
    public string Description { get; set; } = string.Empty;
}
