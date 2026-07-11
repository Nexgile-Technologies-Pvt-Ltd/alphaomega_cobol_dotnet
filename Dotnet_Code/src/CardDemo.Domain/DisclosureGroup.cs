namespace CardDemo.Domain;

/// <summary>
/// DISCLOSURE_GROUP base table. Copybook CVTRA02Y, RECLN 50.
/// Composite PK (acct_group_id X(10), tran_type_cd X(2), tran_cat_cd 9(4)). DIS-GROUP-KEY = 16 bytes.
/// </summary>
public class DisclosureGroup
{
    /// <summary>acct_group_id X(10) — composite PK.</summary>
    public string AcctGroupId { get; set; } = "";

    /// <summary>tran_type_cd X(2) — composite PK.</summary>
    public string TranTypeCd { get; set; } = "";

    /// <summary>tran_cat_cd 9(4) — composite PK, small code.</summary>
    public int TranCatCd { get; set; }

    /// <summary>int_rate S9(4)V99 — interest rate.</summary>
    public decimal IntRate { get; set; }
}
