namespace CardDemo.Domain;

/// <summary>
/// TRAN_CAT_BAL base table. Copybook CVTRA01Y, RECLN 50.
/// Composite PK (acct_id 9(11), type_cd X(2), cat_cd 9(4)). TRAN-CAT-KEY = 17 bytes.
/// </summary>
public class TranCatBalance
{
    /// <summary>acct_id 9(11) — composite PK / id key.</summary>
    public long AcctId { get; set; }

    /// <summary>type_cd X(2) — composite PK.</summary>
    public string TypeCd { get; set; } = "";

    /// <summary>cat_cd 9(4) — composite PK, small code.</summary>
    public int CatCd { get; set; }

    /// <summary>tran_cat_bal S9(9)V99 — category balance.</summary>
    public decimal TranCatBal { get; set; }
}
