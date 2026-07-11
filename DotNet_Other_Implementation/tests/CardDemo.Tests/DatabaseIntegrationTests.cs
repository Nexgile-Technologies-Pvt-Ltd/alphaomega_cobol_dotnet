using CardDemo.Infrastructure.Batch;
using CardDemo.Infrastructure.Fixtures;
using CardDemo.Infrastructure.Security;
using CardDemo.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CardDemo.Tests;

/// <summary>
/// End-to-end tests over a real <see cref="CardDemo.Infrastructure.Persistence.CardDemoDbContext"/>
/// backed by a shared in-memory SQLite database, exercising FixtureSeeder,
/// DatabaseManager and BatchRunner against the delivered fixtures.
/// </summary>
public sealed class DatabaseIntegrationTests
{
    [Fact]
    public async Task Initialize_Then_Verify_MatchesSeedOracle()
    {
        using var testDb = new SqliteTestDatabase();
        var fixtureRoot = FixturePaths.AsciiRoot();

        await using var db = testDb.NewContext();
        var seeder = new FixtureSeeder(db, new Pbkdf2PasswordHasher());
        var manager = new DatabaseManager(db, seeder);

        var seed = await manager.InitializeAsync(fixtureRoot, reseed: false, CancellationToken.None);

        // Exact seed-count oracle.
        Assert.Equal(50, seed.Counts["Accounts"]);
        Assert.Equal(50, seed.Counts["Customers"]);
        Assert.Equal(50, seed.Counts["Cards"]);
        Assert.Equal(50, seed.Counts["CardXrefs"]);
        Assert.Equal(50, seed.Counts["TransactionCategoryBalances"]);
        Assert.Equal(51, seed.Counts["DisclosureGroups"]);
        Assert.Equal(7, seed.Counts["TransactionTypes"]);
        Assert.Equal(18, seed.Counts["TransactionCategories"]);
        Assert.Equal(10, seed.Counts["Users"]);

        var verify = await manager.VerifyAsync(CancellationToken.None);

        Assert.True(verify.Ok, "VerifyAsync issues: " + string.Join("; ", verify.Issues));
        Assert.Empty(verify.Issues);
    }

    [Fact]
    public async Task PostTransactions_Fixture_262Accepted_38RejectsAll102()
    {
        using var testDb = new SqliteTestDatabase();
        var fixtureRoot = FixturePaths.AsciiRoot();

        await using var db = testDb.NewContext();
        var seeder = new FixtureSeeder(db, new Pbkdf2PasswordHasher());
        var manager = new DatabaseManager(db, seeder);
        await manager.InitializeAsync(fixtureRoot, reseed: false, CancellationToken.None);

        var time = new FixedTimeProvider(new DateTimeOffset(2024, 6, 11, 0, 0, 0, TimeSpan.Zero));
        var runner = new BatchRunner(db, seeder, time);

        var inputPath = FixturePaths.DailyTran();
        var rejectsPath = Path.Combine(
            Path.GetTempPath(), "carddemo-tests", $"rejects-{Guid.NewGuid():N}.txt");

        var report = await runner.PostTransactionsAsync(inputPath, rejectsPath, CancellationToken.None);

        Assert.Equal(262, report.Accepted);
        Assert.Equal(38, report.Rejected);
        Assert.True(report.HasRejects);

        // All 38 rejects share reason 102 (overlimit).
        var only = Assert.Single(report.RejectsByReason);
        Assert.Equal(PostingRejectReason.Overlimit, only.Key);
        Assert.Equal(38, only.Value);
    }

    [Fact]
    public async Task Flow_IsRerunnable_AfterReseed_NoDuplicateKeyFailure()
    {
        using var testDb = new SqliteTestDatabase();
        var fixtureRoot = FixturePaths.AsciiRoot();
        var hasher = new Pbkdf2PasswordHasher();
        var time = new FixedTimeProvider(new DateTimeOffset(2024, 6, 11, 0, 0, 0, TimeSpan.Zero));
        var input = FixturePaths.DailyTran();
        var rejects = Path.Combine(Path.GetTempPath(), "carddemo-tests", $"rej-{Guid.NewGuid():N}.txt");

        // Each command uses a fresh context, exactly like the scoped CLI does.
        await using (var c = testDb.NewContext())
            await new DatabaseManager(c, new FixtureSeeder(c, hasher)).InitializeAsync(fixtureRoot, reseed: false, CancellationToken.None);

        await using (var c = testDb.NewContext())
        {
            var first = await new BatchRunner(c, new FixtureSeeder(c, hasher), time).PostTransactionsAsync(input, rejects, CancellationToken.None);
            Assert.Equal(262, first.Accepted);
        }

        // Reseed (clean slate) then post again — must not throw on duplicate keys.
        await using (var c = testDb.NewContext())
            await new DatabaseManager(c, new FixtureSeeder(c, hasher)).InitializeAsync(fixtureRoot, reseed: true, CancellationToken.None);

        await using (var c = testDb.NewContext())
        {
            var second = await new BatchRunner(c, new FixtureSeeder(c, hasher), time).PostTransactionsAsync(input, rejects, CancellationToken.None);
            Assert.Equal(262, second.Accepted);
            Assert.Equal(38, second.Rejected);
        }

        // Exactly 262 posted transactions remain (not 524).
        await using (var c = testDb.NewContext())
            Assert.Equal(262, await c.Transactions.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Verify_StillPasses_AfterPostingAndInterest()
    {
        using var testDb = new SqliteTestDatabase();
        var root = FixturePaths.AsciiRoot();
        var hasher = new Pbkdf2PasswordHasher();
        var time = new FixedTimeProvider(new DateTimeOffset(2024, 6, 11, 0, 0, 0, TimeSpan.Zero));
        var input = FixturePaths.DailyTran();
        var rejects = Path.Combine(Path.GetTempPath(), "carddemo-tests", $"rej-{Guid.NewGuid():N}.txt");

        await using (var c = testDb.NewContext())
            await new DatabaseManager(c, new FixtureSeeder(c, hasher)).InitializeAsync(root, reseed: false, CancellationToken.None);
        await using (var c = testDb.NewContext())
            await new BatchRunner(c, new FixtureSeeder(c, hasher), time).PostTransactionsAsync(input, rejects, CancellationToken.None);
        await using (var c = testDb.NewContext())
            await new BatchRunner(c, new FixtureSeeder(c, hasher), time).CalculateInterestAsync("2022071800", CancellationToken.None);

        // Posting creates new category balances (>50); verify must still pass.
        await using (var c = testDb.NewContext())
        {
            var verify = await new DatabaseManager(c, new FixtureSeeder(c, hasher)).VerifyAsync(CancellationToken.None);
            Assert.True(verify.Ok, "Verify issues: " + string.Join("; ", verify.Issues));
        }
    }
}
