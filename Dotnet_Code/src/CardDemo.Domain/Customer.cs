namespace CardDemo.Domain;

/// <summary>
/// CUSTOMER base table. Copybook CVCUS01Y, RECLN 500.
/// PK cust_id 9(9).
/// </summary>
public class Customer
{
    /// <summary>cust_id 9(9) — primary key / id key.</summary>
    public long CustId { get; set; }

    /// <summary>first_name X(25).</summary>
    public string FirstName { get; set; } = "";

    /// <summary>middle_name X(25).</summary>
    public string MiddleName { get; set; } = "";

    /// <summary>last_name X(25).</summary>
    public string LastName { get; set; } = "";

    /// <summary>addr_line_1 X(50).</summary>
    public string AddrLine1 { get; set; } = "";

    /// <summary>addr_line_2 X(50).</summary>
    public string AddrLine2 { get; set; } = "";

    /// <summary>addr_line_3 X(50).</summary>
    public string AddrLine3 { get; set; } = "";

    /// <summary>addr_state_cd X(2).</summary>
    public string AddrStateCd { get; set; } = "";

    /// <summary>addr_country_cd X(3).</summary>
    public string AddrCountryCd { get; set; } = "";

    /// <summary>addr_zip X(10).</summary>
    public string AddrZip { get; set; } = "";

    /// <summary>phone_num_1 X(15).</summary>
    public string PhoneNum1 { get; set; } = "";

    /// <summary>phone_num_2 X(15).</summary>
    public string PhoneNum2 { get; set; } = "";

    /// <summary>ssn 9(9) — id key.</summary>
    public long Ssn { get; set; }

    /// <summary>govt_issued_id X(20).</summary>
    public string GovtIssuedId { get; set; } = "";

    /// <summary>dob_yyyy_mm_dd X(10).</summary>
    public string DobYyyyMmDd { get; set; } = "";

    /// <summary>eft_account_id X(10).</summary>
    public string EftAccountId { get; set; } = "";

    /// <summary>pri_card_holder_ind X(1).</summary>
    public string PriCardHolderInd { get; set; } = "";

    /// <summary>fico_credit_score 9(3) — small code.</summary>
    public int FicoCreditScore { get; set; }
}
