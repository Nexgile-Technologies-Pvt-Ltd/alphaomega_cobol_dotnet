namespace CardDemo.Domain;

/// <summary>
/// CARD_XREF base table. Copybook CVACT03Y, RECLN 50.
/// PK xref_card_num X(16); idx acct_id 9(11).
/// </summary>
public class CardXref
{
    /// <summary>xref_card_num X(16) — primary key.</summary>
    public string XrefCardNum { get; set; } = "";

    /// <summary>cust_id 9(9) — id key.</summary>
    public long CustId { get; set; }

    /// <summary>acct_id 9(11) — indexed id key.</summary>
    public long AcctId { get; set; }
}
