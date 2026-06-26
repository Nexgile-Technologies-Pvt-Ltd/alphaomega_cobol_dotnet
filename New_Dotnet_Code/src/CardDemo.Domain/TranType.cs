namespace CardDemo.Domain;

/// <summary>
/// TRAN_TYPE base table. Copybook CVTRA03Y, RECLN 60.
/// PK tran_type X(2).
/// </summary>
public class TranType
{
    /// <summary>tran_type X(2) — primary key.</summary>
    public string TranTypeCode { get; set; } = "";

    /// <summary>tran_type_desc X(50).</summary>
    public string TranTypeDesc { get; set; } = "";
}
