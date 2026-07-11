using CardDemo.Domain.Common;

namespace CardDemo.Domain.Entities;

/// <summary>
/// Account master record. Source layout: CVACT01Y.cpy (ACCOUNT-RECORD, RECLN 300).
/// Money fields are S9(10)V99; identifiers keep leading zeros as strings.
/// </summary>
public sealed class Account : IVersioned
{
    /// <summary>ACCT-ID PIC 9(11) — primary key, leading zeros preserved.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>ACCT-ACTIVE-STATUS PIC X(01).</summary>
    public string ActiveStatus { get; set; } = string.Empty;

    /// <summary>ACCT-CURR-BAL PIC S9(10)V99.</summary>
    public decimal CurrentBalance { get; set; }

    /// <summary>ACCT-CREDIT-LIMIT PIC S9(10)V99.</summary>
    public decimal CreditLimit { get; set; }

    /// <summary>ACCT-CASH-CREDIT-LIMIT PIC S9(10)V99.</summary>
    public decimal CashCreditLimit { get; set; }

    /// <summary>ACCT-OPEN-DATE PIC X(10) — text date yyyy-MM-dd.</summary>
    public string OpenDate { get; set; } = string.Empty;

    /// <summary>ACCT-EXPIRAION-DATE PIC X(10) — source spelling retained.</summary>
    public string ExpirationDate { get; set; } = string.Empty;

    /// <summary>ACCT-REISSUE-DATE PIC X(10).</summary>
    public string ReissueDate { get; set; } = string.Empty;

    /// <summary>ACCT-CURR-CYC-CREDIT PIC S9(10)V99.</summary>
    public decimal CurrentCycleCredit { get; set; }

    /// <summary>ACCT-CURR-CYC-DEBIT PIC S9(10)V99.</summary>
    public decimal CurrentCycleDebit { get; set; }

    /// <summary>ACCT-ADDR-ZIP PIC X(10).</summary>
    public string AddressZip { get; set; } = string.Empty;

    /// <summary>ACCT-GROUP-ID PIC X(10) — joins to disclosure-group pricing.</summary>
    public string GroupId { get; set; } = string.Empty;

    public long RowVersion { get; set; }
}
