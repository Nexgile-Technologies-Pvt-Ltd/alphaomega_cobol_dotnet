namespace CardDemo.Domain;

/// <summary>
/// USER_SECURITY base table. Copybook CSUSR01Y, RECLN 80.
/// PK usr_id X(8).
/// </summary>
public class UserSecurity
{
    /// <summary>usr_id X(8) — primary key.</summary>
    public string UsrId { get; set; } = "";

    /// <summary>first_name X(20).</summary>
    public string FirstName { get; set; } = "";

    /// <summary>last_name X(20).</summary>
    public string LastName { get; set; } = "";

    /// <summary>pwd X(8).</summary>
    public string Pwd { get; set; } = "";

    /// <summary>usr_type X(1).</summary>
    public string UsrType { get; set; } = "";
}
