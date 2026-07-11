using CardDemo.Domain.Common;

namespace CardDemo.Domain.Entities;

/// <summary>
/// Pending-authorization summary per account. Relational model of the IMS root
/// segment PAUTSUM0 (CIPAUSMY.cpy) — one row per account. Amounts are the packed
/// COMP-3 fields modelled as decimal.
/// </summary>
public sealed class PendingAuthSummary : IVersioned
{
    public string AccountId { get; set; } = string.Empty;   // PA-ACCT-ID (PK)
    public string CustomerId { get; set; } = string.Empty;  // PA-CUST-ID
    public decimal CreditLimit { get; set; }                // PA-CREDIT-LIMIT
    public decimal CashLimit { get; set; }                  // PA-CASH-LIMIT
    public decimal CreditBalance { get; set; }              // PA-CREDIT-BALANCE (+= approved amount)
    public decimal CashBalance { get; set; }                // PA-CASH-BALANCE
    public int ApprovedAuthCount { get; set; }              // PA-APPROVED-AUTH-CNT
    public int DeclinedAuthCount { get; set; }              // PA-DECLINED-AUTH-CNT
    public decimal ApprovedAuthAmount { get; set; }         // PA-APPROVED-AUTH-AMT
    public decimal DeclinedAuthAmount { get; set; }         // PA-DECLINED-AUTH-AMT
    public long RowVersion { get; set; }
}

/// <summary>
/// Pending-authorization detail. Relational model of the IMS child segment
/// PAUTDTL1 (CIPAUDTY.cpy). Newest-first is reproduced by ordering on
/// <see cref="AuthKey"/> descending (a reverse-encoded timestamp key).
/// </summary>
public sealed class PendingAuthDetail
{
    public int Id { get; set; }
    public string AccountId { get; set; } = string.Empty;   // parent linkage
    public string AuthKey { get; set; } = string.Empty;     // reverse timestamp key (newest sorts first)
    public string CardNumber { get; set; } = string.Empty;  // PA-CARD-NUM
    public string AuthType { get; set; } = string.Empty;    // PA-AUTH-TYPE
    public string CardExpiryDate { get; set; } = string.Empty; // PA-CARD-EXPIRY-DATE MMYY
    public decimal TransactionAmount { get; set; }          // PA-TRANSACTION-AMT
    public decimal ApprovedAmount { get; set; }             // PA-APPROVED-AMT
    public string AuthRespCode { get; set; } = string.Empty;   // 00 approved, 05 declined
    public string AuthRespReason { get; set; } = string.Empty; // decline reason
    public string MatchStatus { get; set; } = string.Empty;    // P/D/E/M
    public string AuthFraud { get; set; } = string.Empty;      // space/F/R
    public string FraudReportDate { get; set; } = string.Empty;
    public string ProcessingCode { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string MerchantName { get; set; } = string.Empty;
    public string MerchantCity { get; set; } = string.Empty;
    public string MerchantState { get; set; } = string.Empty;
    public string MerchantZip { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public string OrigDate { get; set; } = string.Empty;    // PA-AUTH-ORIG-DATE YYMMDD
    public string OrigTime { get; set; } = string.Empty;    // PA-AUTH-ORIG-TIME HHMMSS
    public string CreatedTimestamp { get; set; } = string.Empty; // 26-char
}

/// <summary>
/// Fraud history. Relational model of the Db2 CARDDEMO.AUTHFRDS table (AUTHFRDS.ddl),
/// keyed by (card number, authorization timestamp). A fraud "removal" keeps an R row.
/// </summary>
public sealed class AuthFraudHistory
{
    public int Id { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string AuthTimestamp { get; set; } = string.Empty; // AUTH_TS
    public string AuthType { get; set; } = string.Empty;
    public string AuthRespCode { get; set; } = string.Empty;
    public decimal TransactionAmount { get; set; }
    public decimal ApprovedAmount { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
    public string AuthFraud { get; set; } = string.Empty;    // F reported, R removed
    public string FraudReportDate { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
}

/// <summary>
/// Inbound authorization request. Relational model of the MQ request queue drained
/// by COPAUA0C (tran CP00). A durable local queue replaces IBM MQ.
/// </summary>
public sealed class AuthorizationRequest
{
    public int Id { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public decimal TransactionAmount { get; set; }
    public string AuthType { get; set; } = string.Empty;
    public string ProcessingCode { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string MerchantName { get; set; } = string.Empty;
    public string MerchantCity { get; set; } = string.Empty;
    public string MerchantState { get; set; } = string.Empty;
    public string MerchantZip { get; set; } = string.Empty;
    public string MerchantCategoryCode { get; set; } = string.Empty;
    public string OrigDate { get; set; } = string.Empty;   // YYMMDD
    public string OrigTime { get; set; } = string.Empty;   // HHMMSS
    public string CorrelId { get; set; } = string.Empty;
    public string ReplyToQueue { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING";        // PENDING | PROCESSED
    public string CreatedTimestamp { get; set; } = string.Empty;
}

/// <summary>Authorization reply (outbox). CSV/summary reply produced per request.</summary>
public sealed class AuthorizationReply
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string AuthRespCode { get; set; } = string.Empty;   // 00 approved / 05 declined
    public string AuthRespReason { get; set; } = string.Empty;
    public decimal ApprovedAmount { get; set; }
    public string Payload { get; set; } = string.Empty;
    public string CreatedTimestamp { get; set; } = string.Empty;
}
