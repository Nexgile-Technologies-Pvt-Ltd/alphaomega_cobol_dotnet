namespace CardDemo.Domain;

/// <summary>
/// PAUT_SUMMARY optional-module table — the IMS root segment PAUTSUM0 ("Pending Authorization Summary",
/// copybook CIPAUSMY) re-hosted as a relational row. One per account. PK <c>ACCT_ID</c> (= IMS root key
/// ACCNTID; replaces the HIDAM primary index DBPAUTX0/PAUTINDX). The COBOL OCCURS 5 PA-ACCOUNT-STATUS
/// is flattened to <see cref="AccountStatus1"/>..<see cref="AccountStatus5"/>. See IMS_SCHEMA.md.
/// </summary>
public class PautSummary
{
    /// <summary>PA-ACCT-ID S9(11) COMP-3 — primary key (= IMS root key ACCNTID).</summary>
    public long AcctId { get; set; }

    /// <summary>PA-CUST-ID 9(09).</summary>
    public long CustId { get; set; }

    /// <summary>PA-AUTH-STATUS X(01).</summary>
    public string AuthStatus { get; set; } = "";

    /// <summary>PA-ACCOUNT-STATUS X(02) OCCURS 5 — element 1.</summary>
    public string AccountStatus1 { get; set; } = "";

    /// <summary>PA-ACCOUNT-STATUS X(02) OCCURS 5 — element 2.</summary>
    public string AccountStatus2 { get; set; } = "";

    /// <summary>PA-ACCOUNT-STATUS X(02) OCCURS 5 — element 3.</summary>
    public string AccountStatus3 { get; set; } = "";

    /// <summary>PA-ACCOUNT-STATUS X(02) OCCURS 5 — element 4.</summary>
    public string AccountStatus4 { get; set; } = "";

    /// <summary>PA-ACCOUNT-STATUS X(02) OCCURS 5 — element 5.</summary>
    public string AccountStatus5 { get; set; } = "";

    /// <summary>PA-CREDIT-LIMIT S9(09)V99 COMP-3.</summary>
    public decimal CreditLimit { get; set; }

    /// <summary>PA-CASH-LIMIT S9(09)V99 COMP-3.</summary>
    public decimal CashLimit { get; set; }

    /// <summary>PA-CREDIT-BALANCE S9(09)V99 COMP-3 — running pending credit balance.</summary>
    public decimal CreditBalance { get; set; }

    /// <summary>PA-CASH-BALANCE S9(09)V99 COMP-3.</summary>
    public decimal CashBalance { get; set; }

    /// <summary>PA-APPROVED-AUTH-CNT S9(04) COMP — small count.</summary>
    public int ApprovedAuthCnt { get; set; }

    /// <summary>PA-DECLINED-AUTH-CNT S9(04) COMP — small count.</summary>
    public int DeclinedAuthCnt { get; set; }

    /// <summary>PA-APPROVED-AUTH-AMT S9(09)V99 COMP-3.</summary>
    public decimal ApprovedAuthAmt { get; set; }

    /// <summary>PA-DECLINED-AUTH-AMT S9(09)V99 COMP-3.</summary>
    public decimal DeclinedAuthAmt { get; set; }
}
