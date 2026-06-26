namespace CardDemo.Domain;

/// <summary>
/// ACCOUNT base table. Copybook CVACT01Y, RECLN 300.
/// PK acct_id 9(11). One property per COBOL elementary field.
/// </summary>
public class Account
{
    /// <summary>acct_id 9(11) — primary key.</summary>
    public long AcctId { get; set; }

    /// <summary>active_status X(1).</summary>
    public string ActiveStatus { get; set; } = "";

    /// <summary>curr_bal S9(10)V99 — current balance.</summary>
    public decimal CurrBal { get; set; }

    /// <summary>credit_limit S9(10)V99.</summary>
    public decimal CreditLimit { get; set; }

    /// <summary>cash_credit_limit S9(10)V99.</summary>
    public decimal CashCreditLimit { get; set; }

    /// <summary>open_date X(10).</summary>
    public string OpenDate { get; set; } = "";

    /// <summary>expiration_date X(10) — COBOL field name EXPIRAION.</summary>
    public string ExpirationDate { get; set; } = "";

    /// <summary>reissue_date X(10).</summary>
    public string ReissueDate { get; set; } = "";

    /// <summary>curr_cyc_credit S9(10)V99.</summary>
    public decimal CurrCycCredit { get; set; }

    /// <summary>curr_cyc_debit S9(10)V99.</summary>
    public decimal CurrCycDebit { get; set; }

    /// <summary>addr_zip X(10).</summary>
    public string AddrZip { get; set; } = "";

    /// <summary>group_id X(10).</summary>
    public string GroupId { get; set; } = "";
}
