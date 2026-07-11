using CardDemo.Application.Services;
using CardDemo.Console.Interactive;
using CardDemo.Infrastructure.Fixtures;
using CardDemo.Infrastructure.Persistence;
using CardDemo.Infrastructure.Security;
using Xunit;

namespace CardDemo.Tests;

/// <summary>
/// Drives the real interactive terminal (<see cref="InteractiveApp"/>) end-to-end with
/// scripted standard input over a seeded in-memory database — the same wiring the host
/// uses, exercising sign-on, the menu and a screen. Runs sequentially because it
/// redirects the process console streams.
/// </summary>
[Collection("Console")]
public sealed class InteractiveAppTests
{
    private static readonly TimeProvider Clock =
        new FixedTimeProvider(new DateTimeOffset(2024, 6, 11, 12, 0, 0, TimeSpan.Zero));

    private static async Task<(int Exit, string Output)> RunScriptAsync(SqliteTestDatabase testDb, string script)
    {
        var db = testDb.NewContext();
        var hasher = new Pbkdf2PasswordHasher();
        var seeder = new FixtureSeeder(db, hasher);
        var manager = new DatabaseManager(db, seeder);
        await manager.InitializeAsync(FixturePaths.AsciiRoot(), reseed: false, CancellationToken.None);

        var store = new CardDemoStore(db);
        var app = new InteractiveApp(
            new AuthService(store, hasher),
            new AccountService(store, Clock),
            new CardService(store),
            new TransactionService(store, Clock),
            new BillPayService(store, Clock),
            new UserAdminService(store, hasher),
            new ReportRequestService(store, Clock),
            manager,
            Clock,
            new CardDemo.Infrastructure.Optional.TransactionTypeService(db),
            new CardDemo.Infrastructure.Optional.AuthorizationService(db, Clock));

        var savedIn = System.Console.In;
        var savedOut = System.Console.Out;
        using var outWriter = new StringWriter();
        try
        {
            System.Console.SetIn(new StringReader(script));
            System.Console.SetOut(outWriter);
            var exit = await app.RunAsync(CancellationToken.None);
            return (exit, outWriter.ToString());
        }
        finally
        {
            System.Console.SetIn(savedIn);
            System.Console.SetOut(savedOut);
            await db.DisposeAsync();
        }
    }

    [Fact]
    public async Task Admin_SignOn_ThenViewAccount_RendersAccount_AndExitsOk()
    {
        using var testDb = new SqliteTestDatabase();
        // Sign on as admin, choose account view (1), view account 00000000050, then EOF.
        var script = string.Join("\n", "ADMIN001", "PASSWORD", "1", "00000000050", "") + "\n";

        var (exit, output) = await RunScriptAsync(testDb, script);

        Assert.Equal(0, exit);
        Assert.Contains("Sign On", output);
        Assert.Contains("00000000050", output); // the viewed account is rendered
    }

    [Fact]
    public async Task BadCredentials_ShowsFailure_AndExitsOk()
    {
        using var testDb = new SqliteTestDatabase();
        var script = string.Join("\n", "NOSUCH99", "wrongpass") + "\n";

        var (exit, output) = await RunScriptAsync(testDb, script);

        Assert.Equal(0, exit);
        Assert.Contains("not found", output, StringComparison.OrdinalIgnoreCase);
    }
}
