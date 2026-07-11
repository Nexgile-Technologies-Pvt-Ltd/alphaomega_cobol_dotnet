namespace CardDemo.Domain;

/// <summary>
/// TRANSACTION_TYPE_CATEGORY optional-module table. DDL TRNTYCAT.ddl (already relational).
/// Composite PK (TRC_TYPE_CODE CHAR(2), TRC_TYPE_CATEGORY CHAR(4)).
/// </summary>
public class TransactionTypeCategory
{
    /// <summary>TRC_TYPE_CODE CHAR(2) — composite PK.</summary>
    public string TrcTypeCode { get; set; } = "";

    /// <summary>TRC_TYPE_CATEGORY CHAR(4) — composite PK.</summary>
    public string TrcTypeCategory { get; set; } = "";

    /// <summary>TRC_CAT_DATA VARCHAR(50).</summary>
    public string TrcCatData { get; set; } = "";
}
