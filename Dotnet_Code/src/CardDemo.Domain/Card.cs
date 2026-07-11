namespace CardDemo.Domain;

/// <summary>
/// CARD base table. Copybook CVACT02Y, RECLN 150.
/// PK card_num X(16); idx acct_id 9(11).
/// </summary>
public class Card
{
    /// <summary>card_num X(16) — primary key.</summary>
    public string CardNum { get; set; } = "";

    /// <summary>acct_id 9(11) — indexed.</summary>
    public long AcctId { get; set; }

    /// <summary>cvv_cd 9(3) — small code.</summary>
    public int CvvCd { get; set; }

    /// <summary>embossed_name X(50).</summary>
    public string EmbossedName { get; set; } = "";

    /// <summary>expiration_date X(10).</summary>
    public string ExpirationDate { get; set; } = "";

    /// <summary>active_status X(1).</summary>
    public string ActiveStatus { get; set; } = "";
}
