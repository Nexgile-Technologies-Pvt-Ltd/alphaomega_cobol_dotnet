namespace CardDemo.Domain.Entities;

/// <summary>
/// Transaction type reference. Source layout: CVTRA03Y.cpy (TRAN-TYPE-RECORD, RECLN 60).
/// </summary>
public sealed class TransactionType
{
    /// <summary>TRAN-TYPE PIC X(02) — primary key.</summary>
    public string TypeCode { get; set; } = string.Empty;

    /// <summary>TRAN-TYPE-DESC PIC X(50).</summary>
    public string Description { get; set; } = string.Empty;
}
