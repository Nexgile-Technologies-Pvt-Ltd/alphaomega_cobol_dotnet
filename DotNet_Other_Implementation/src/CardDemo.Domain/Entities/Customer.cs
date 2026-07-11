namespace CardDemo.Domain.Entities;

/// <summary>
/// Customer master record. Source layout: CVCUS01Y.cpy (CUSTOMER-RECORD, RECLN 500).
/// </summary>
public sealed class Customer
{
    /// <summary>CUST-ID PIC 9(09) — primary key.</summary>
    public string CustomerId { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;   // CUST-FIRST-NAME  X(25)
    public string MiddleName { get; set; } = string.Empty;  // CUST-MIDDLE-NAME X(25)
    public string LastName { get; set; } = string.Empty;    // CUST-LAST-NAME   X(25)
    public string AddressLine1 { get; set; } = string.Empty; // X(50)
    public string AddressLine2 { get; set; } = string.Empty; // X(50)
    public string AddressLine3 { get; set; } = string.Empty; // X(50)
    public string StateCode { get; set; } = string.Empty;    // CUST-ADDR-STATE-CD   X(02)
    public string CountryCode { get; set; } = string.Empty;  // CUST-ADDR-COUNTRY-CD X(03)
    public string Zip { get; set; } = string.Empty;          // CUST-ADDR-ZIP        X(10)
    public string PhoneNumber1 { get; set; } = string.Empty; // X(15)
    public string PhoneNumber2 { get; set; } = string.Empty; // X(15)
    public string Ssn { get; set; } = string.Empty;          // CUST-SSN PIC 9(09)
    public string GovtIssuedId { get; set; } = string.Empty; // X(20)
    public string DateOfBirth { get; set; } = string.Empty;  // CUST-DOB-YYYY-MM-DD X(10)
    public string EftAccountId { get; set; } = string.Empty; // X(10)
    public string PrimaryCardHolderIndicator { get; set; } = string.Empty; // X(01)

    /// <summary>CUST-FICO-CREDIT-SCORE PIC 9(03).</summary>
    public int FicoCreditScore { get; set; }
}
