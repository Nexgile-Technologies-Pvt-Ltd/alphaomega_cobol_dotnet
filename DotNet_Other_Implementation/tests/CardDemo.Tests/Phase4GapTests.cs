using CardDemo.Application;
using CardDemo.Application.Abstractions;
using CardDemo.Application.Dtos;
using CardDemo.Application.Services;
using CardDemo.Console.Cli;
using CardDemo.Console.Interactive;
using CardDemo.Domain.Entities;
using CardDemo.Domain.Services;
using CardDemo.Infrastructure;
using CardDemo.Infrastructure.Batch;
using CardDemo.Infrastructure.Fixtures;
using CardDemo.Infrastructure.Optional;
using CardDemo.Infrastructure.Persistence;
using CardDemo.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CardDemo.Tests;

/// <summary>
/// Phase-4 gap-closure coverage: service-layer administrator authorization
/// (FR-AUTH-005 / FR-USER-008), the transaction-report golden and pending-report
/// runner (FR-RPT-001/002), the F5 transaction prefill (FR-TRAN-008), the
/// reference-data batch apply (FR-OPT-003), the documented CommandRouter exit
/// codes, and the remaining untested online/validation edges. Every database test
/// seeds a fresh in-memory SQLite database via <see cref="DatabaseManager"/> and
/// drives time through a <see cref="FixedTimeProvider"/> so results are deterministic.
/// </summary>
public sealed class Phase4GapTests
{
    private const string SeededCard = "0500024453765740";
    private const string SeededAccount = "00000000050";

    private static readonly TimeProvider Clock =
        new FixedTimeProvider(new DateTimeOffset(2024, 6, 11, 12, 0, 0, TimeSpan.Zero));

    private static async Task<(CardDemoDbContext Db, ICardDemoStore Store, IPasswordHasher Hasher)> SeedAsync(SqliteTestDatabase testDb)
    {
        var db = testDb.NewContext();
        var hasher = new Pbkdf2PasswordHasher();
        var manager = new DatabaseManager(db, new FixtureSeeder(db, hasher));
        await manager.InitializeAsync(FixturePaths.AsciiRoot(), reseed: false, CancellationToken.None);
        return (db, new CardDemoStore(db), hasher);
    }

    private static async Task PostFixtureAsync(SqliteTestDatabase testDb, IPasswordHasher hasher)
    {
        await using var db = testDb.NewContext();
        var runner = new BatchRunner(db, new FixtureSeeder(db, hasher), Clock);
        await runner.PostTransactionsAsync(FixturePaths.DailyTran(), TempPath("rejects"), CancellationToken.None);
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "carddemo-phase4-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string TempPath(string prefix, string ext = "txt") =>
        Path.Combine(TempDir(), $"{prefix}.{ext}");

    // ==================== Administrator authorization (FR-AUTH-005 / FR-USER-008) ====================

    [Fact]
    public async Task UserAdmin_Add_NonAdminActor_IsRejected_AndDoesNotPersist()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, hasher) = await SeedAsync(testDb);
        await using var _d = db;
        var users = new UserAdminService(store, hasher);

        var result = await users.AddAsync(
            new UserUpsertRequest("NEWUSER1", "New", "User", "pass1234", "U"), "USER0001", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Not authorized", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await store.FindUserAsync("NEWUSER1", CancellationToken.None));
    }

    [Fact]
    public async Task UserAdmin_Update_NonAdminActor_IsRejected_AndDoesNotPersist()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, hasher) = await SeedAsync(testDb);
        await using var _d = db;
        var users = new UserAdminService(store, hasher);

        var result = await users.UpdateAsync(
            new UserUpsertRequest("USER0002", "Changed", "Name", "", "U"), "USER0001", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Not authorized", result.Message, StringComparison.OrdinalIgnoreCase);

        var after = await store.FindUserAsync("USER0002", CancellationToken.None);
        Assert.NotNull(after);
        Assert.Equal("AJITH", after!.FirstName); // unchanged seed value
    }

    [Fact]
    public async Task UserAdmin_Delete_NonAdminActor_IsRejected_AndDoesNotPersist()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, hasher) = await SeedAsync(testDb);
        await using var _d = db;
        var users = new UserAdminService(store, hasher);

        var result = await users.DeleteAsync("USER0002", "USER0001", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Not authorized", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await store.FindUserAsync("USER0002", CancellationToken.None));
    }

    [Fact]
    public async Task UserAdmin_Add_Update_Delete_AdminActor_Succeeds()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, hasher) = await SeedAsync(testDb);
        await using var _d = db;
        var users = new UserAdminService(store, hasher);

        var add = await users.AddAsync(
            new UserUpsertRequest("AUTHOK01", "Auth", "Ok", "pass1234", "U"), "ADMIN001", CancellationToken.None);
        Assert.True(add.Success, add.Message);
        Assert.NotNull(await store.FindUserAsync("AUTHOK01", CancellationToken.None));

        var update = await users.UpdateAsync(
            new UserUpsertRequest("AUTHOK01", "Auth", "Renamed", "", "A"), "ADMIN001", CancellationToken.None);
        Assert.True(update.Success, update.Message);
        var updated = await store.FindUserAsync("AUTHOK01", CancellationToken.None);
        Assert.Equal("Renamed", updated!.LastName);
        Assert.Equal("A", updated.UserType);

        var delete = await users.DeleteAsync("AUTHOK01", "ADMIN001", CancellationToken.None);
        Assert.True(delete.Success, delete.Message);
        Assert.Null(await store.FindUserAsync("AUTHOK01", CancellationToken.None));
    }

    // ==================== Transaction report golden (FR-RPT-001) ====================

    [Fact]
    public async Task ReportGolden_2022_262Transactions_Total77954_70_AllLines133()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, _, hasher) = await SeedAsync(testDb);
        await using var _d = db;
        await PostFixtureAsync(testDb, hasher);

        var outputPath = TempPath("tranreport");
        await using var c = testDb.NewContext();
        var result = await new BatchRunner(c, new FixtureSeeder(c, hasher), Clock)
            .GenerateReportAsync("2022-01-01", "2022-12-31", outputPath, CancellationToken.None);

        Assert.Equal(262, result.TransactionsIncluded);
        Assert.Equal(77954.70m, result.TotalAmount);

        var lines = await File.ReadAllLinesAsync(outputPath);
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Equal(133, line.Length));

        Assert.Contains(lines, l => l.Contains("Daily Transaction Report", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("Account Total", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("Grand Total", StringComparison.Ordinal));
    }

    // ==================== RunPendingReportsAsync (FR-RPT-002) ====================

    [Fact]
    public async Task RunPendingReports_ProcessesQueuedRequest_MarksCompleted_AndWritesFile()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, hasher) = await SeedAsync(testDb);
        await using var _d = db;

        // Queue a request through the online service (CORPT00C).
        var reports = new ReportRequestService(store, Clock);
        var request = await reports.RequestAsync("2022-01-01", "2022-12-31", "ADMIN001", CancellationToken.None);
        Assert.True(request.Success, request.Message);

        var outputDir = TempDir();
        await using var c = testDb.NewContext();
        var processed = await new BatchRunner(c, new FixtureSeeder(c, hasher), Clock)
            .RunPendingReportsAsync(outputDir, CancellationToken.None);

        Assert.Equal(1, processed);

        var pending = await db.PendingReportRequests.AsNoTracking().SingleAsync();
        Assert.Equal("COMPLETED", pending.Status);

        var files = Directory.GetFiles(outputDir);
        Assert.Single(files);
        Assert.True(File.Exists(files[0]));
    }

    // ==================== CommandRouter exit codes ====================

    [Collection("Console")]
    public sealed class CommandRouterExitCodes
    {
        private static async Task<(int Exit, string Output)> RunRouterAsync(CommandRouter router, params string[] args)
        {
            var saved = global::System.Console.Out;
            var savedErr = global::System.Console.Error;
            using var writer = new StringWriter();
            try
            {
                global::System.Console.SetOut(writer);
                global::System.Console.SetError(writer);
                var exit = await router.RunAsync(args, CancellationToken.None);
                return (exit, writer.ToString());
            }
            finally
            {
                global::System.Console.SetOut(saved);
                global::System.Console.SetError(savedErr);
            }
        }

        private static CommandRouter BuildRouter(IServiceProvider services) =>
            new(services, new ConfigurationBuilder().AddInMemoryCollection().Build());

        [Fact]
        public async Task Help_ReturnsOk()
        {
            var services = new ServiceCollection().BuildServiceProvider();
            var router = BuildRouter(services);

            var (exit, _) = await RunRouterAsync(router, "--help");

            Assert.Equal(0, exit);
        }

        [Fact]
        public async Task UnknownVerb_ReturnsUsageError()
        {
            var services = new ServiceCollection().BuildServiceProvider();
            var router = BuildRouter(services);

            var (exit, _) = await RunRouterAsync(router, "not-a-command");

            Assert.Equal(2, exit);
        }

        [Fact]
        public async Task GenerateReport_NoDates_ReturnsUsageError()
        {
            // Build a DI container so the batch runner resolves; the missing --from/--to
            // must be rejected before any work happens.
            using var world = new SharedSqliteWorld();
            using var scope = world.Provider.CreateScope();
            var router = BuildRouter(scope.ServiceProvider);

            var (exit, _) = await RunRouterAsync(router, "batch", "generate-report");

            Assert.Equal(2, exit);
        }

        [Fact]
        public async Task DatabaseVerify_ReturnsOk_AndPostTransactions_ReturnsBusinessRejects()
        {
            using var world = new SharedSqliteWorld();
            await world.InitializeAsync();

            using var scope = world.Provider.CreateScope();
            var router = BuildRouter(scope.ServiceProvider);

            var (verifyExit, _) = await RunRouterAsync(router, "database", "verify");
            Assert.Equal(0, verifyExit);

            var (postExit, _) = await RunRouterAsync(router, "batch", "post-transactions");
            Assert.Equal(4, postExit); // ExitCodes.BusinessRejects
        }
    }

    /// <summary>
    /// A real DI container over a shared in-memory SQLite database, wired exactly like the
    /// host (<see cref="InfrastructureServiceCollectionExtensions.AddCardDemoInfrastructure"/>
    /// + AddCardDemoApplication + AddSingleton(TimeProvider.System) + AddScoped&lt;InteractiveApp&gt;).
    /// The shared-cache connection is held open for the lifetime of the container so schema and
    /// data survive across scopes/contexts.
    /// </summary>
    private sealed class SharedSqliteWorld : IDisposable
    {
        private readonly SqliteConnection _connection;

        public ServiceProvider Provider { get; }

        public SharedSqliteWorld()
        {
            var connectionString =
                $"Data Source=file:carddemo-phase4-{Guid.NewGuid():N}?mode=memory&cache=shared";
            _connection = new SqliteConnection(connectionString);
            _connection.Open();

            var services = new ServiceCollection();
            services.AddCardDemoInfrastructure(connectionString);
            services.AddCardDemoApplication();
            services.AddSingleton(TimeProvider.System);
            services.AddScoped<InteractiveApp>();
            Provider = services.BuildServiceProvider();
        }

        public async Task InitializeAsync()
        {
            using var scope = Provider.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<IDatabaseManager>();
            await manager.InitializeAsync(FixturePaths.AsciiRoot(), reseed: true, CancellationToken.None);
        }

        public void Dispose()
        {
            Provider.Dispose();
            _connection.Dispose();
        }
    }

    // ==================== BillPayService (COBIL00C) ====================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("00000000000")]
    [InlineData("0000000000A")]
    public async Task BillPay_BlankZeroOrNonNumericAccount_Fails(string account)
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var billpay = new BillPayService(store, Clock);

        var result = await billpay.PayFullBalanceAsync(account, CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task BillPay_UnknownAccount_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var billpay = new BillPayService(store, Clock);

        var result = await billpay.PayFullBalanceAsync("99999999999", CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task BillPay_ZeroBalanceAccount_Fails_AndWritesNoTransaction()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;

        // Force the seeded account's balance to zero.
        var account = await db.Accounts.SingleAsync(a => a.AccountId == SeededAccount);
        account.CurrentBalance = 0m;
        await db.SaveChangesAsync();

        var before = await db.Transactions.AsNoTracking().CountAsync();

        var billpay = new BillPayService(store, Clock);
        var result = await billpay.PayFullBalanceAsync(SeededAccount, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("nothing to pay", result.Message, StringComparison.OrdinalIgnoreCase);

        var after = await db.Transactions.AsNoTracking().CountAsync();
        Assert.Equal(before, after);
    }

    // ==================== AuthorizationService.ProcessPendingAsync boundary ====================

    [Fact]
    public async Task Auth_ZeroAmount_IsDeclined()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, _, _) = await SeedAsync(testDb);
        await using var _d = db;
        var svc = new AuthorizationService(db, Clock);

        await svc.SubmitAsync(new AuthorizationRequest
        {
            CardNumber = SeededCard,
            TransactionAmount = 0m,
            AuthType = "01",
        }, CancellationToken.None);

        var result = await svc.ProcessPendingAsync(maxMessages: 10, CancellationToken.None);
        Assert.Equal(1, result.Processed);
        Assert.Equal(0, result.Approved);
        Assert.Equal(1, result.Declined);

        var detail = await db.PendingAuthDetails.AsNoTracking().SingleAsync();
        Assert.Equal("05", detail.AuthRespCode);
    }

    [Fact]
    public async Task Auth_NegativeAmount_IsDeclined()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, _, _) = await SeedAsync(testDb);
        await using var _d = db;
        var svc = new AuthorizationService(db, Clock);

        await svc.SubmitAsync(new AuthorizationRequest
        {
            CardNumber = SeededCard,
            TransactionAmount = -10.00m,
            AuthType = "01",
        }, CancellationToken.None);

        var result = await svc.ProcessPendingAsync(maxMessages: 10, CancellationToken.None);
        Assert.Equal(1, result.Declined);

        var detail = await db.PendingAuthDetails.AsNoTracking().SingleAsync();
        Assert.Equal("05", detail.AuthRespCode);
    }

    [Fact]
    public async Task Auth_AmountExactlyEqualToAvailable_IsApproved()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, _, _) = await SeedAsync(testDb);
        await using var _d = db;
        var svc = new AuthorizationService(db, Clock);

        // First submission establishes the summary; available credit == full credit limit.
        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.AccountId == SeededAccount);
        var exact = account.CreditLimit; // credit balance starts at 0, so available == limit
        Assert.True(exact > 0m);

        await svc.SubmitAsync(new AuthorizationRequest
        {
            CardNumber = SeededCard,
            TransactionAmount = exact,
            AuthType = "01",
        }, CancellationToken.None);

        var result = await svc.ProcessPendingAsync(maxMessages: 10, CancellationToken.None);
        Assert.Equal(1, result.Approved);
        Assert.Equal(0, result.Declined);

        var detail = await db.PendingAuthDetails.AsNoTracking().SingleAsync();
        Assert.Equal("00", detail.AuthRespCode);
        Assert.Equal(exact, detail.ApprovedAmount);
    }

    // ==================== ReportRequestService invalid dates ====================

    [Fact]
    public async Task ReportRequest_MalformedFromDate_Fails_StartDateMessage()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var reports = new ReportRequestService(store, Clock);

        var result = await reports.RequestAsync("2022-99-99", "2022-12-31", "ADMIN001", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Start Date - Not a valid date", result.Message);
    }

    [Fact]
    public async Task ReportRequest_MalformedToDate_Fails_EndDateMessage()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var reports = new ReportRequestService(store, Clock);

        // 2022 is not a leap year, so 2022-02-29 is not a real calendar date.
        var result = await reports.RequestAsync("2022-01-01", "2022-02-29", "ADMIN001", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("End Date - Not a valid date", result.Message);
    }

    // ==================== FieldValidation.IsValidCalendarExpiration ====================

    [Theory]
    [InlineData(12, 2030, true)]
    [InlineData(13, 2030, false)]  // month out of range
    [InlineData(12, 1900, false)]  // year below 1950
    [InlineData(1, 1950, true)]    // lower year boundary
    [InlineData(12, 2099, true)]   // upper year boundary
    [InlineData(1, 2100, false)]   // above upper year boundary
    public void IsValidCalendarExpiration_MatchesBoundaries(int month, int year, bool expected)
    {
        Assert.Equal(expected, FieldValidation.IsValidCalendarExpiration(month, year));
    }

    // ==================== F5 prefill (FR-TRAN-008) ====================

    [Fact]
    public async Task Prefill_AfterAddingTransaction_ReturnsCopiedNonKeyFields()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var txns = new TransactionService(store, Clock);

        var added = await txns.AddAsync(new TransactionAddRequest(
            AccountId: SeededAccount,
            CardNumber: SeededCard,
            TypeCode: "01",
            CategoryCode: "0001",
            Source: "POS",
            Description: "Prefill source purchase",
            Amount: 42.50m,
            MerchantId: "000000001",
            MerchantName: "Prefill Merchant",
            MerchantCity: "Prefill City",
            MerchantZip: "12345",
            OriginDate: "2022-06-01"), CancellationToken.None);
        Assert.True(added.Success, added.Message);

        var prefill = await txns.PrefillFromLatestAsync(CancellationToken.None);

        Assert.True(prefill.Success, prefill.Message);
        var copy = prefill.Value!;
        Assert.Equal(SeededAccount, copy.AccountId);
        Assert.Equal(SeededCard, copy.CardNumber);
        Assert.Equal("01", copy.TypeCode);
        Assert.Equal("0001", copy.CategoryCode);
        Assert.Equal("POS", copy.Source);
        Assert.Equal("Prefill source purchase", copy.Description);
        Assert.Equal(42.50m, copy.Amount);
        Assert.Equal("000000001", copy.MerchantId);
        Assert.Equal("Prefill Merchant", copy.MerchantName);
        Assert.Equal("Prefill City", copy.MerchantCity);
        Assert.Equal("12345", copy.MerchantZip);
    }

    [Fact]
    public async Task Prefill_NoTransactions_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var txns = new TransactionService(store, Clock);

        var prefill = await txns.PrefillFromLatestAsync(CancellationToken.None);

        Assert.False(prefill.Success);
        Assert.Contains("No transactions to copy from", prefill.Message);
    }

    // ==================== reference-data ApplyBatchAsync (FR-OPT-003) ====================

    [Fact]
    public async Task ApplyBatch_AppliesAddUpdateDelete_AndCountsFailures()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = testDb.NewContext();
        var seeder = new FixtureSeeder(db, new Pbkdf2PasswordHasher());
        var manager = new DatabaseManager(db, seeder);
        await manager.InitializeAsync(FixturePaths.AsciiRoot(), reseed: false, CancellationToken.None);
        var svc = new TransactionTypeService(db);

        // Record layout: action(1) + type(2) + description(<=50).
        //   A 08 -> add type "08"
        //   U 08 -> update the just-added type's description
        //   A 09 -> add type "09"
        //   D 09 -> delete type "09"
        //   * .. -> comment line, ignored
        //   (blank line) -> ignored
        //   Z 07 -> invalid action, counted as a failure
        //   D 01 -> delete a type that has category children -> failure (FK restrict)
        var lines = new[]
        {
            "A08PROMO ADJUSTMENT",
            "U08PROMO CREDIT",
            "A09ADJUSTMENTS",
            "D09ADJUSTMENTS",
            "* a commented record",
            string.Empty,
            "Z07SHOULD FAIL",
            "D01HAS CHILDREN",
        };
        var inputPath = TempPath("cobtupdt");
        await File.WriteAllLinesAsync(inputPath, lines);

        var result = await svc.ApplyBatchAsync(inputPath, CancellationToken.None);

        // Applied: A08, U08, A09, D09 = 4. Failed: Z07 (bad action) + D01 (FK restrict) = 2.
        Assert.Equal(4, result.Applied);
        Assert.Equal(2, result.Failed);

        // "08" was added then updated to "PROMO CREDIT".
        var type08 = await db.TransactionTypes.AsNoTracking().SingleAsync(t => t.TypeCode == "08");
        Assert.Equal("PROMO CREDIT", type08.Description);

        // "09" was added then deleted, so it must not remain.
        Assert.False(await db.TransactionTypes.AsNoTracking().AnyAsync(t => t.TypeCode == "09"));

        // "01" survives the failed FK-restricted delete.
        Assert.True(await db.TransactionTypes.AsNoTracking().AnyAsync(t => t.TypeCode == "01"));
    }
}
