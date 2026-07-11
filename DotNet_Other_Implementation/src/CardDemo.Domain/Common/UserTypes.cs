namespace CardDemo.Domain.Common;

/// <summary>
/// User-type codes from CSUSR01Y (SEC-USR-TYPE) and the COCOM01Y 88-levels.
/// 'A' routes to the admin menu (COADM01C); anything else routes to the
/// regular menu (COMEN01C). Evidence: COSGN00C sign-on routing.
/// </summary>
public static class UserTypes
{
    public const string Admin = "A";
    public const string Regular = "U";

    public static bool IsAdmin(string? userType) =>
        string.Equals(userType?.Trim(), Admin, StringComparison.OrdinalIgnoreCase);
}
