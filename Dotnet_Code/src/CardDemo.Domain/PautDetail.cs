namespace CardDemo.Domain;

/// <summary>
/// PAUT_DETAIL optional-module table — the IMS child segment PAUTDTL1 ("Pending Authorization Detail",
/// copybook CIPAUDTY) re-hosted as a relational row. Many per summary. Composite PK
/// (<c>ACCT_ID</c>, <c>AUTH_KEY</c>); <c>ACCT_ID</c> is the FK to <see cref="PautSummary"/> (IMS parentage).
/// <see cref="AuthKey"/> = PAUT9CTS (date-9C + time-9C, 9s-complement) so ascending key order is
/// newest-first, matching the IMS twin-chain GN/GNP scan. See IMS_SCHEMA.md.
/// </summary>
public class PautDetail
{
    /// <summary>Parent PA-ACCT-ID — composite PK part 1 / FK to PAUT_SUMMARY.ACCT_ID.</summary>
    public long AcctId { get; set; }

    /// <summary>PAUT9CTS / PA-AUTHORIZATION-KEY CHAR(8) — composite PK part 2 (child sequence key).</summary>
    public string AuthKey { get; set; } = "";

    /// <summary>PA-AUTH-DATE-9C S9(05) COMP-3 — descending sort component (99999 − yyddd).</summary>
    public int AuthDate9c { get; set; }

    /// <summary>PA-AUTH-TIME-9C S9(09) COMP-3 — descending sort component.</summary>
    public long AuthTime9c { get; set; }

    /// <summary>PA-AUTH-ORIG-DATE X(06) — YYMMDD original request date.</summary>
    public string AuthOrigDate { get; set; } = "";

    /// <summary>PA-AUTH-ORIG-TIME X(06) — HHMMSS original request time.</summary>
    public string AuthOrigTime { get; set; } = "";

    /// <summary>PA-CARD-NUM X(16).</summary>
    public string CardNum { get; set; } = "";

    /// <summary>PA-AUTH-TYPE X(04).</summary>
    public string AuthType { get; set; } = "";

    /// <summary>PA-CARD-EXPIRY-DATE X(04).</summary>
    public string CardExpiryDate { get; set; } = "";

    /// <summary>PA-MESSAGE-TYPE X(06).</summary>
    public string MessageType { get; set; } = "";

    /// <summary>PA-MESSAGE-SOURCE X(06).</summary>
    public string MessageSource { get; set; } = "";

    /// <summary>PA-AUTH-ID-CODE X(06).</summary>
    public string AuthIdCode { get; set; } = "";

    /// <summary>PA-AUTH-RESP-CODE X(02) — '00' = approved.</summary>
    public string AuthRespCode { get; set; } = "";

    /// <summary>PA-AUTH-RESP-REASON X(04) — decline reason code.</summary>
    public string AuthRespReason { get; set; } = "";

    /// <summary>PA-PROCESSING-CODE 9(06).</summary>
    public int ProcessingCode { get; set; }

    /// <summary>PA-TRANSACTION-AMT S9(10)V99 COMP-3.</summary>
    public decimal TransactionAmt { get; set; }

    /// <summary>PA-APPROVED-AMT S9(10)V99 COMP-3.</summary>
    public decimal ApprovedAmt { get; set; }

    /// <summary>PA-MERCHANT-CATAGORY-CODE X(04) — [sic spelling 'CATAGORY' from source].</summary>
    public string MerchantCatagoryCode { get; set; } = "";

    /// <summary>PA-ACQR-COUNTRY-CODE X(03).</summary>
    public string AcqrCountryCode { get; set; } = "";

    /// <summary>PA-POS-ENTRY-MODE 9(02) — small code.</summary>
    public int PosEntryMode { get; set; }

    /// <summary>PA-MERCHANT-ID X(15).</summary>
    public string MerchantId { get; set; } = "";

    /// <summary>PA-MERCHANT-NAME X(22).</summary>
    public string MerchantName { get; set; } = "";

    /// <summary>PA-MERCHANT-CITY X(13).</summary>
    public string MerchantCity { get; set; } = "";

    /// <summary>PA-MERCHANT-STATE X(02).</summary>
    public string MerchantState { get; set; } = "";

    /// <summary>PA-MERCHANT-ZIP X(09).</summary>
    public string MerchantZip { get; set; } = "";

    /// <summary>PA-TRANSACTION-ID X(15).</summary>
    public string TransactionId { get; set; } = "";

    /// <summary>PA-MATCH-STATUS X(01) — 88s: P=pending, D=declined, E=pending-expired, M=matched.</summary>
    public string MatchStatus { get; set; } = "";

    /// <summary>PA-AUTH-FRAUD X(01) — 88s: F=confirmed, R=removed.</summary>
    public string AuthFraud { get; set; } = "";

    /// <summary>PA-FRAUD-RPT-DATE X(08).</summary>
    public string FraudRptDate { get; set; } = "";
}
