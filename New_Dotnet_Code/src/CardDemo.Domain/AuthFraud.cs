namespace CardDemo.Domain;

/// <summary>
/// AUTHFRDS optional-module table. DDL AUTHFRDS.ddl (already relational; alt index XAUTHFRD.ddl).
/// Composite PK (CARD_NUM CHAR(16), AUTH_TS TIMESTAMP).
/// </summary>
public class AuthFraud
{
    /// <summary>CARD_NUM CHAR(16) — composite PK.</summary>
    public string CardNum { get; set; } = "";

    /// <summary>AUTH_TS TIMESTAMP — composite PK.</summary>
    public string AuthTs { get; set; } = "";

    /// <summary>AUTH_TYPE CHAR(4).</summary>
    public string AuthType { get; set; } = "";

    /// <summary>CARD_EXPIRY_DATE CHAR(4).</summary>
    public string CardExpiryDate { get; set; } = "";

    /// <summary>MESSAGE_TYPE CHAR(6).</summary>
    public string MessageType { get; set; } = "";

    /// <summary>MESSAGE_SOURCE CHAR(6).</summary>
    public string MessageSource { get; set; } = "";

    /// <summary>AUTH_ID_CODE CHAR(6).</summary>
    public string AuthIdCode { get; set; } = "";

    /// <summary>AUTH_RESP_CODE CHAR(2).</summary>
    public string AuthRespCode { get; set; } = "";

    /// <summary>AUTH_RESP_REASON CHAR(4).</summary>
    public string AuthRespReason { get; set; } = "";

    /// <summary>PROCESSING_CODE CHAR(6).</summary>
    public string ProcessingCode { get; set; } = "";

    /// <summary>TRANSACTION_AMT DECIMAL(12,2).</summary>
    public decimal TransactionAmt { get; set; }

    /// <summary>APPROVED_AMT DECIMAL(12,2).</summary>
    public decimal ApprovedAmt { get; set; }

    /// <summary>MERCHANT_CATAGORY_CODE CHAR(4).</summary>
    public string MerchantCatagoryCode { get; set; } = "";

    /// <summary>ACQR_COUNTRY_CODE CHAR(3).</summary>
    public string AcqrCountryCode { get; set; } = "";

    /// <summary>POS_ENTRY_MODE SMALLINT — small code.</summary>
    public int PosEntryMode { get; set; }

    /// <summary>MERCHANT_ID CHAR(15).</summary>
    public string MerchantId { get; set; } = "";

    /// <summary>MERCHANT_NAME VARCHAR(22).</summary>
    public string MerchantName { get; set; } = "";

    /// <summary>MERCHANT_CITY CHAR(13).</summary>
    public string MerchantCity { get; set; } = "";

    /// <summary>MERCHANT_STATE CHAR(2).</summary>
    public string MerchantState { get; set; } = "";

    /// <summary>MERCHANT_ZIP CHAR(9).</summary>
    public string MerchantZip { get; set; } = "";

    /// <summary>TRANSACTION_ID CHAR(15).</summary>
    public string TransactionId { get; set; } = "";

    /// <summary>MATCH_STATUS CHAR(1).</summary>
    public string MatchStatus { get; set; } = "";

    /// <summary>AUTH_FRAUD CHAR(1).</summary>
    public string AuthFraudInd { get; set; } = "";

    /// <summary>FRAUD_RPT_DATE DATE.</summary>
    public string FraudRptDate { get; set; } = "";

    /// <summary>ACCT_ID DECIMAL(11) — id key.</summary>
    public long AcctId { get; set; }

    /// <summary>CUST_ID DECIMAL(9) — id key.</summary>
    public long CustId { get; set; }
}
