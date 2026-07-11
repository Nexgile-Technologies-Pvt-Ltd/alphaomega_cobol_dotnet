using CardDemo.Application.Abstractions;
using CardDemo.Application.Dtos;
using CardDemo.Domain.Common;
using CardDemo.Domain.Entities;
using CardDemo.Domain.Services;

namespace CardDemo.Application.Services;

/// <summary>
/// Security-user administration (COUSR00C/01C/02C/03C). Add/update validate the
/// COUSR01C/02C field order (first name, last name, id, password, type). Delete
/// additionally guards against removing the last admin and against self-delete
/// of the acting user (safe-target hardening over the legacy program).
/// </summary>
public sealed class UserAdminService(ICardDemoStore store, IPasswordHasher passwordHasher) : IUserAdminService
{
    private readonly ICardDemoStore _store = store;
    private readonly IPasswordHasher _passwordHasher = passwordHasher;

    public async Task<OperationResult<PagedResult<UserSecurity>>> ListAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var safePage = page < 1 ? 1 : page;
        var safeSize = pageSize < 1 ? 10 : pageSize;

        var skip = (safePage - 1) * safeSize;
        var rows = await _store.ListUsersAsync(skip, safeSize + 1, ct).ConfigureAwait(false);

        var hasNext = rows.Count > safeSize;
        var items = hasNext ? rows.Take(safeSize).ToList() : rows.ToList();

        var result = new PagedResult<UserSecurity>(items, safePage, safeSize, hasNext, safePage > 1);
        return OperationResult<PagedResult<UserSecurity>>.Ok(result);
    }

    public async Task<OperationResult<UserSecurity>> GetAsync(string userId, CancellationToken ct = default)
    {
        var id = (userId ?? string.Empty).Trim().ToUpperInvariant();
        if (FieldValidation.IsBlank(id))
            return OperationResult<UserSecurity>.Fail("User ID can NOT be empty...");

        var user = await _store.FindUserAsync(id, ct).ConfigureAwait(false);
        if (user is null)
            return OperationResult<UserSecurity>.Fail("User ID NOT found...");

        return OperationResult<UserSecurity>.Ok(user);
    }

    public async Task<OperationResult> AddAsync(UserUpsertRequest request, string actingUserId, CancellationToken ct = default)
    {
        var authorized = await IsAuthorizedAdminAsync(actingUserId, ct).ConfigureAwait(false);
        if (!authorized)
            return OperationResult.Fail("Not authorized: administrator access required.");

        if (FieldValidation.IsBlank(request.FirstName))
            return OperationResult.Fail("First Name can NOT be empty...");
        if (FieldValidation.IsBlank(request.LastName))
            return OperationResult.Fail("Last Name can NOT be empty...");
        if (FieldValidation.IsBlank(request.UserId))
            return OperationResult.Fail("User ID can NOT be empty...");
        if (FieldValidation.IsBlank(request.Password))
            return OperationResult.Fail("Password can NOT be empty...");
        if (FieldValidation.IsBlank(request.UserType))
            return OperationResult.Fail("User Type can NOT be empty...");
        if (!FieldValidation.IsValidUserType(request.UserType))
            return OperationResult.Fail("User Type must be 'A' or 'U'...");

        var id = request.UserId.Trim().ToUpperInvariant();

        var existing = await _store.FindUserAsync(id, ct).ConfigureAwait(false);
        if (existing is not null)
            return OperationResult.Fail("User ID already exist...");

        var user = new UserSecurity
        {
            UserId = id,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PasswordHash = _passwordHasher.Hash(request.Password),
            UserType = request.UserType.Trim().ToUpperInvariant(),
        };

        await _store.AddUserAsync(user, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);

        return OperationResult.Ok($"User {id} has been added ...");
    }

    public async Task<OperationResult> UpdateAsync(UserUpsertRequest request, string actingUserId, CancellationToken ct = default)
    {
        var authorized = await IsAuthorizedAdminAsync(actingUserId, ct).ConfigureAwait(false);
        if (!authorized)
            return OperationResult.Fail("Not authorized: administrator access required.");

        if (FieldValidation.IsBlank(request.UserId))
            return OperationResult.Fail("User ID can NOT be empty...");
        if (FieldValidation.IsBlank(request.FirstName))
            return OperationResult.Fail("First Name can NOT be empty...");
        if (FieldValidation.IsBlank(request.LastName))
            return OperationResult.Fail("Last Name can NOT be empty...");
        if (FieldValidation.IsBlank(request.UserType))
            return OperationResult.Fail("User Type can NOT be empty...");
        if (!FieldValidation.IsValidUserType(request.UserType))
            return OperationResult.Fail("User Type must be 'A' or 'U'...");

        var id = request.UserId.Trim().ToUpperInvariant();

        var user = await _store.FindUserAsync(id, ct).ConfigureAwait(false);
        if (user is null)
            return OperationResult.Fail("User ID NOT found...");

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.UserType = request.UserType.Trim().ToUpperInvariant();
        // Password is optional on update; only re-hash when a new one is supplied.
        if (!FieldValidation.IsBlank(request.Password))
            user.PasswordHash = _passwordHasher.Hash(request.Password);

        await _store.SaveChangesAsync(ct).ConfigureAwait(false);

        return OperationResult.Ok($"User {id} has been updated ...");
    }

    public async Task<OperationResult> DeleteAsync(string userId, string actingUserId, CancellationToken ct = default)
    {
        var authorized = await IsAuthorizedAdminAsync(actingUserId, ct).ConfigureAwait(false);
        if (!authorized)
            return OperationResult.Fail("Not authorized: administrator access required.");

        var id = (userId ?? string.Empty).Trim().ToUpperInvariant();
        var actor = (actingUserId ?? string.Empty).Trim().ToUpperInvariant();

        if (FieldValidation.IsBlank(id))
            return OperationResult.Fail("User ID can NOT be empty...");

        if (string.Equals(id, actor, StringComparison.Ordinal))
            return OperationResult.Fail("You can not delete your own user...");

        var user = await _store.FindUserAsync(id, ct).ConfigureAwait(false);
        if (user is null)
            return OperationResult.Fail("User ID NOT found...");

        if (user.IsAdmin)
        {
            var adminCount = await _store.CountAdminsAsync(ct).ConfigureAwait(false);
            if (adminCount <= 1)
                return OperationResult.Fail("Cannot delete the last administrator...");
        }

        _store.RemoveUser(user);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);

        return OperationResult.Ok($"User {id} has been deleted ...");
    }

    /// <summary>
    /// Resolves the acting user and confirms administrator access. COUSR01C/02C/03C are
    /// reachable only from the admin menu; this enforces the same gate at the service layer.
    /// </summary>
    private async Task<bool> IsAuthorizedAdminAsync(string actingUserId, CancellationToken ct)
    {
        var actor = (actingUserId ?? string.Empty).Trim().ToUpperInvariant();
        if (FieldValidation.IsBlank(actor))
            return false;

        var acting = await _store.FindUserAsync(actor, ct).ConfigureAwait(false);
        return acting is not null && acting.IsAdmin;
    }
}
