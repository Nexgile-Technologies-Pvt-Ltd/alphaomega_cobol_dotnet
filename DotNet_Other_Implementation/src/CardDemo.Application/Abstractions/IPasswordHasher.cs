namespace CardDemo.Application.Abstractions;

/// <summary>
/// Safe-target password hashing. The legacy system stored plaintext 8-char
/// passwords (DEF-SEC-001); the console never persists cleartext. Fixture users
/// are migrated by hashing their known bootstrap password once at seed time.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
