namespace CardDemo.Domain;

/// <summary>
/// TRAN_CATEGORY base table. Copybook CVTRA04Y, RECLN 60.
/// Composite PK (tran_type_cd X(2), tran_cat_cd 9(4)).
/// </summary>
public class TranCategory
{
    /// <summary>tran_type_cd X(2) — composite PK.</summary>
    public string TranTypeCd { get; set; } = "";

    /// <summary>tran_cat_cd 9(4) — composite PK, small code.</summary>
    public int TranCatCd { get; set; }

    /// <summary>tran_cat_type_desc X(50).</summary>
    public string TranCatTypeDesc { get; set; } = "";
}
