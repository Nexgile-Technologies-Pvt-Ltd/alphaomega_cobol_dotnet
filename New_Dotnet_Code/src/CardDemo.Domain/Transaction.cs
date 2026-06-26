namespace CardDemo.Domain;

/// <summary>
/// TRANSACTION base table. Copybook CVTRA05Y, RECLN 350.
/// PK tran_id X(16).
/// </summary>
public class Transaction
{
    /// <summary>tran_id X(16) — primary key.</summary>
    public string TranId { get; set; } = "";

    /// <summary>type_cd X(2).</summary>
    public string TypeCd { get; set; } = "";

    /// <summary>cat_cd 9(4) — small code.</summary>
    public int CatCd { get; set; }

    /// <summary>source X(10).</summary>
    public string Source { get; set; } = "";

    /// <summary>desc X(100).</summary>
    public string Desc { get; set; } = "";

    /// <summary>amt S9(9)V99 — amount.</summary>
    public decimal Amt { get; set; }

    /// <summary>merchant_id 9(9) — id key.</summary>
    public long MerchantId { get; set; }

    /// <summary>merchant_name X(50).</summary>
    public string MerchantName { get; set; } = "";

    /// <summary>merchant_city X(50).</summary>
    public string MerchantCity { get; set; } = "";

    /// <summary>merchant_zip X(10).</summary>
    public string MerchantZip { get; set; } = "";

    /// <summary>card_num X(16).</summary>
    public string CardNum { get; set; } = "";

    /// <summary>orig_ts X(26).</summary>
    public string OrigTs { get; set; } = "";

    /// <summary>proc_ts X(26).</summary>
    public string ProcTs { get; set; } = "";
}
