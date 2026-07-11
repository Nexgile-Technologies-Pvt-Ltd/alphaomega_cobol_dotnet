using CardDemo.Application.Abstractions;
using CardDemo.Console.Interactive.Screens;
using CardDemo.Domain.Entities;

namespace CardDemo.Console.Interactive;

/// <summary>
/// The primary human interface: a menu-driven text terminal that mirrors the AWS
/// CardDemo online flow (sign-on -> main menu -> screens). Admin users additionally
/// see the user-administration options. All service outcomes are rendered as plain
/// <c>OperationResult</c> messages; bad input is validated and re-prompted, never thrown.
/// </summary>
public sealed class InteractiveApp
{
    private readonly IAuthService _auth;
    private readonly IDatabaseManager _database;
    private readonly ConsoleIo _io;

    private readonly AccountScreens _accountScreens;
    private readonly CardScreens _cardScreens;
    private readonly TransactionScreens _transactionScreens;
    private readonly ServiceScreens _serviceScreens;
    private readonly UserAdminScreens _userAdminScreens;
    private readonly TransactionTypeScreens _transactionTypeScreens;
    private readonly AuthorizationScreens _authorizationScreens;

    public InteractiveApp(
        IAuthService auth,
        IAccountService accounts,
        ICardService cards,
        ITransactionService transactions,
        IBillPayService billPay,
        IUserAdminService userAdmin,
        IReportRequestService reports,
        IDatabaseManager database,
        TimeProvider timeProvider,
        ITransactionTypeService transactionTypes,
        IAuthorizationService authorizations)
    {
        _auth = auth;
        _database = database;
        _io = new ConsoleIo();

        _accountScreens = new AccountScreens(_io, accounts);
        _cardScreens = new CardScreens(_io, cards);
        _transactionScreens = new TransactionScreens(_io, transactions, timeProvider);
        _serviceScreens = new ServiceScreens(_io, billPay, reports);
        _userAdminScreens = new UserAdminScreens(_io, userAdmin);
        _transactionTypeScreens = new TransactionTypeScreens(_io, transactionTypes);
        _authorizationScreens = new AuthorizationScreens(_io, authorizations);
    }

    private static string DefaultFixtureRoot => Path.Combine(AppContext.BaseDirectory, "fixtures", "ASCII");

    public async Task<int> RunAsync(CancellationToken ct)
    {
        try
        {
            await InitializeDatabaseAsync(ct);

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var user = await SignOnAsync(ct);
                if (user is null)
                {
                    // Operator quit the sign-on screen.
                    return ExitCodes.Ok;
                }

                var signedOff = await MainMenuAsync(user, ct);
                if (signedOff)
                {
                    // Return to the sign-on screen for another operator.
                    continue;
                }

                // Reaching here means a hard exit request.
                return ExitCodes.Ok;
            }
        }
        catch (OperationCanceledException)
        {
            _io.Line();
            _io.Line("Cancelled.");
            return ExitCodes.Cancelled;
        }
    }

    private async Task InitializeDatabaseAsync(CancellationToken ct)
    {
        _io.Header("AWS CardDemo — .NET 10 Terminal");
        _io.Line();
        _io.Line("Preparing the database (first run seeds fixtures)...");
        var report = await _database.InitializeAsync(DefaultFixtureRoot, reseed: false, ct);
        _io.Line($"Database ready at {report.DatabasePath}.");
    }

    /// <summary>Sign-on loop (COSGN00C). Returns the authenticated user, or null if the operator quit.</summary>
    private async Task<UserSecurity?> SignOnAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            _io.Header("Sign On (COSGN00C)");
            _io.Line();
            _io.Line("Enter your credentials. Type X at the User id prompt to quit.");
            _io.Line("(Default users: ADMIN001 / USER0001 — password PASSWORD)");
            _io.Line();

            var userId = _io.PromptRaw("User id: ");
            if (userId is null)
            {
                return null; // EOF
            }
            if (string.Equals(userId, "X", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (userId.Length == 0)
            {
                _io.ShowError("User id is required.");
                _io.PressEnter();
                continue;
            }

            var password = _io.PromptPassword("Password: ");
            if (password is null)
            {
                return null;
            }

            var result = await _auth.SignInAsync(userId, password, ct);
            if (result.Success && result.Value is not null)
            {
                return result.Value;
            }

            _io.ShowError(result.Message.Length == 0 ? "Sign-on failed." : result.Message);
            _io.PressEnter();
        }
    }

    /// <summary>Main menu loop. Returns true on sign-off (return to sign-on), false on hard exit.</summary>
    private async Task<bool> MainMenuAsync(UserSecurity user, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            RenderMainMenu(user);
            var choice = _io.MenuChoice();
            if (choice is null)
            {
                return false; // EOF -> hard exit
            }

            switch (choice)
            {
                case "1":
                    await _accountScreens.ViewAsync(ct);
                    break;
                case "2":
                    await _accountScreens.UpdateAsync(ct);
                    break;
                case "3":
                    await _cardScreens.ListAsync(ct);
                    break;
                case "4":
                    await _cardScreens.ViewAsync(ct);
                    break;
                case "5":
                    await _cardScreens.UpdateAsync(ct);
                    break;
                case "6":
                    await _transactionScreens.ListAsync(ct);
                    break;
                case "7":
                    await _transactionScreens.ViewAsync(ct);
                    break;
                case "8":
                    await _transactionScreens.AddAsync(ct);
                    break;
                case "9":
                    await _serviceScreens.BillPaymentAsync(ct);
                    break;
                case "10":
                    await _serviceScreens.ReportRequestAsync(user.UserId, ct);
                    break;
                case "11" when user.IsAdmin:
                    await _userAdminScreens.ListAsync(ct);
                    break;
                case "12" when user.IsAdmin:
                    await _userAdminScreens.AddAsync(user.UserId, ct);
                    break;
                case "13" when user.IsAdmin:
                    await _userAdminScreens.UpdateAsync(user.UserId, ct);
                    break;
                case "14" when user.IsAdmin:
                    await _userAdminScreens.DeleteAsync(user.UserId, ct);
                    break;
                case "15":
                    await _transactionTypeScreens.MenuAsync(ct);
                    break;
                case "16":
                    await _authorizationScreens.ListSummariesAsync(ct);
                    break;
                case "X":
                    // Sign off -> back to the sign-on screen.
                    return true;
                case "0":
                    // Same as sign off from the top-level menu.
                    return true;
                default:
                    _io.ShowError("Unknown option. Please choose a number from the menu.");
                    _io.PressEnter();
                    break;
            }
        }
    }

    private void RenderMainMenu(UserSecurity user)
    {
        var role = user.IsAdmin ? "Admin" : "User";
        _io.Header("Main Menu");
        _io.Line($"Signed on as {user.UserId} ({user.FirstName.TrimEnd()} {user.LastName.TrimEnd()}) — {role}");
        _io.Rule('-');
        _io.Line("  Account");
        _io.Line("    1) View account");
        _io.Line("    2) Update account");
        _io.Line("  Card");
        _io.Line("    3) List cards");
        _io.Line("    4) View card");
        _io.Line("    5) Update card");
        _io.Line("  Transaction");
        _io.Line("    6) List transactions");
        _io.Line("    7) View transaction");
        _io.Line("    8) Add transaction");
        _io.Line("  Services");
        _io.Line("    9) Bill payment (pay full balance)");
        _io.Line("   10) Request transaction report");
        _io.Line("  Reference data");
        _io.Line("   15) Transaction-type maintenance");
        _io.Line("   16) Pending authorizations");
        if (user.IsAdmin)
        {
            _io.Line("  User Administration");
            _io.Line("   11) List users");
            _io.Line("   12) Add user");
            _io.Line("   13) Update user");
            _io.Line("   14) Delete user");
        }
        _io.Rule('-');
        _io.Line("    X) Sign off");
        _io.Line();
    }
}
