using CardDemo.Application.Abstractions;
using CardDemo.Application.Dtos;
using CardDemo.Domain.Common;
using CardDemo.Domain.Entities;

namespace CardDemo.Console.Interactive.Screens;

/// <summary>Security-user administration screens (COUSR00C/01C/02C/03C). Admin only.</summary>
internal sealed class UserAdminScreens(ConsoleIo io, IUserAdminService users)
{
    private const int PageSize = 10;
    private readonly ConsoleIo _io = io;
    private readonly IUserAdminService _users = users;

    public async Task ListAsync(CancellationToken ct)
    {
        var page = 1;
        while (true)
        {
            var result = await _users.ListAsync(page, PageSize, ct);
            if (!result.Success || result.Value is null)
            {
                _io.Header("User List (COUSR00C)");
                _io.ShowError(result.Message.Length == 0 ? "No users available." : result.Message);
                _io.PressEnter();
                return;
            }

            var data = result.Value;
            _io.Header("User List (COUSR00C)");
            _io.Line($"Page {data.Page}");
            _io.Rule('-');
            _io.Line($"{"User Id",-10} {"Type",-5} {"First Name",-22} Last Name");
            _io.Rule('-');
            if (data.Items.Count == 0)
            {
                _io.Line("(no users)");
            }
            foreach (var u in data.Items)
            {
                var type = u.IsAdmin ? "Admin" : "User";
                _io.Line($"{u.UserId,-10} {type,-5} {u.FirstName.TrimEnd(),-22} {u.LastName.TrimEnd()}");
            }
            _io.Rule('-');
            _io.Line(Nav(data));

            var choice = _io.MenuChoice();
            if (choice is null || choice == "0" || choice == "X")
            {
                return;
            }
            if (choice == "N" && data.HasNext) { page++; continue; }
            if (choice == "P" && data.HasPrevious) { page--; continue; }
        }
    }

    public async Task AddAsync(string actingUserId, CancellationToken ct)
    {
        _io.Header("Add User (COUSR01C)");
        _io.Line("Enter the new user's details. Type 0 at any prompt to cancel.");
        _io.Line();

        var userId = _io.PromptRequired("User id (up to 8 chars): ");
        if (userId is null || userId == ConsoleIo.BackMarker) { return; }

        var firstName = _io.PromptRequired("First name: ");
        if (firstName is null || firstName == ConsoleIo.BackMarker) { return; }

        var lastName = _io.PromptRequired("Last name: ");
        if (lastName is null || lastName == ConsoleIo.BackMarker) { return; }

        var password = _io.PromptRequired("Password (up to 8 chars): ");
        if (password is null || password == ConsoleIo.BackMarker) { return; }

        var userType = _io.PromptRequired("Type (A=admin, U=user): ");
        if (userType is null || userType == ConsoleIo.BackMarker) { return; }

        var request = new UserUpsertRequest(userId, firstName, lastName, password, userType.ToUpperInvariant());
        var result = await _users.AddAsync(request, actingUserId, ct);
        _io.ShowResult(result);
        _io.PressEnter();
    }

    public async Task UpdateAsync(string actingUserId, CancellationToken ct)
    {
        _io.Header("Update User (COUSR02C)");
        _io.Line("Enter the user id to load, or 0 to go back.");
        _io.Line();

        var userId = _io.PromptRequired("User id: ");
        if (userId is null || userId == ConsoleIo.BackMarker) { return; }

        var loaded = await _users.GetAsync(userId, ct);
        if (!loaded.Success || loaded.Value is null)
        {
            _io.ShowError(loaded.Message.Length == 0 ? "User not found." : loaded.Message);
            _io.PressEnter();
            return;
        }

        var user = loaded.Value;
        _io.Line();
        _io.Line($"Current: {user.UserId}  {user.FirstName.TrimEnd()} {user.LastName.TrimEnd()}  type {user.UserType.Trim()}");
        _io.Line();
        _io.Line("Press ENTER to keep the current value. Type 0 to cancel.");
        _io.Line();

        var firstName = _io.Prompt($"First name [{user.FirstName.TrimEnd()}]: ");
        if (firstName is null || firstName == ConsoleIo.BackMarker) { return; }
        if (firstName.Length == 0) { firstName = user.FirstName; }

        var lastName = _io.Prompt($"Last name [{user.LastName.TrimEnd()}]: ");
        if (lastName is null || lastName == ConsoleIo.BackMarker) { return; }
        if (lastName.Length == 0) { lastName = user.LastName; }

        var userType = _io.Prompt($"Type (A/U) [{user.UserType.Trim()}]: ");
        if (userType is null || userType == ConsoleIo.BackMarker) { return; }
        userType = userType.Length == 0 ? user.UserType : userType.ToUpperInvariant();

        var password = _io.Prompt("New password (ENTER to keep existing): ");
        if (password is null || password == ConsoleIo.BackMarker) { return; }

        var request = new UserUpsertRequest(user.UserId, firstName, lastName, password, userType);
        var result = await _users.UpdateAsync(request, actingUserId, ct);
        _io.ShowResult(result);
        _io.PressEnter();
    }

    public async Task DeleteAsync(string actingUserId, CancellationToken ct)
    {
        _io.Header("Delete User (COUSR03C)");
        _io.Line("Enter the user id to delete, or 0 to go back.");
        _io.Line();

        var userId = _io.PromptRequired("User id: ");
        if (userId is null || userId == ConsoleIo.BackMarker) { return; }

        var confirm = _io.Prompt($"Confirm deletion of user {userId}? (Y/N): ");
        if (confirm is null || confirm == ConsoleIo.BackMarker) { return; }
        if (!string.Equals(confirm, "Y", StringComparison.OrdinalIgnoreCase))
        {
            _io.ShowInfo("Deletion cancelled.");
            _io.PressEnter();
            return;
        }

        var result = await _users.DeleteAsync(userId, actingUserId, ct);
        _io.ShowResult(result);
        _io.PressEnter();
    }

    private static string Nav(PagedResult<UserSecurity> page)
    {
        var options = new List<string>();
        if (page.HasPrevious) { options.Add("P=Prev"); }
        if (page.HasNext) { options.Add("N=Next"); }
        options.Add("0=Back");
        return string.Join("    ", options);
    }
}
