namespace CardDemo.Domain;

/// <summary>
/// TRANSACTION_TYPE optional-module table. DDL TRNTYPE.ddl (already relational).
/// PK TR_TYPE CHAR(2).
/// </summary>
public class TransactionType
{
    /// <summary>TR_TYPE CHAR(2) — primary key.</summary>
    public string TrType { get; set; } = "";

    /// <summary>TR_DESCRIPTION VARCHAR(50).</summary>
    public string TrDescription { get; set; } = "";
}
