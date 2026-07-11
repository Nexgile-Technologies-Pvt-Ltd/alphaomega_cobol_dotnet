using CardDemo.Domain.Common;

namespace CardDemo.Domain.Entities;

/// <summary>
/// Security user record. Source layout: CSUSR01Y.cpy (SEC-USER-DATA, RECLN 80).
/// The legacy field stores a plaintext 8-character password (DEF-SEC-001); the
/// safe-target console stores a password hash instead (see PasswordHasher) and
/// never persists cleartext.
/// </summary>
public sealed class UserSecurity : IVersioned
{
    /// <summary>SEC-USR-ID PIC X(08) — primary key.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>SEC-USR-FNAME PIC X(20).</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>SEC-USR-LNAME PIC X(20).</summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Safe-target password hash (never the legacy plaintext). Format is produced
    /// by the infrastructure PasswordHasher.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>SEC-USR-TYPE PIC X(01) — 'A' admin, 'U' regular.</summary>
    public string UserType { get; set; } = string.Empty;

    public long RowVersion { get; set; }

    public bool IsAdmin => UserTypes.IsAdmin(UserType);
}
