using CardDemo.Application.Abstractions;
using CardDemo.Domain.Common;
using CardDemo.Domain.Entities;

namespace CardDemo.Application.Services;

/// <summary>
/// Sign-on service (COSGN00C). Trims and uppercases the entered user id before
/// lookup, mirroring the legacy screen behaviour, then verifies the password hash.
/// </summary>
public sealed class AuthService(ICardDemoStore store, IPasswordHasher passwordHasher) : IAuthService
{
    private readonly ICardDemoStore _store = store;
    private readonly IPasswordHasher _passwordHasher = passwordHasher;

    public async Task<OperationResult<UserSecurity>> SignInAsync(string userId, string password, CancellationToken ct = default)
    {
        var id = (userId ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(id))
            return OperationResult<UserSecurity>.Fail("User not found. Try again.");

        var user = await _store.FindUserAsync(id, ct).ConfigureAwait(false);
        if (user is null)
            return OperationResult<UserSecurity>.Fail("User not found. Try again.");

        if (!_passwordHasher.Verify(password ?? string.Empty, user.PasswordHash))
            return OperationResult<UserSecurity>.Fail("Wrong password. Try again.");

        return OperationResult<UserSecurity>.Ok(user);
    }
}
