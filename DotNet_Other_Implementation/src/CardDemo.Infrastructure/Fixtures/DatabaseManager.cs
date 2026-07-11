using CardDemo.Application.Abstractions;
using CardDemo.Infrastructure.Batch;
using CardDemo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardDemo.Infrastructure.Fixtures;

/// <summary>
/// Database lifecycle over EF Core + SQLite. Schema is created with
/// <see cref="DatabaseFacade.EnsureCreatedAsync"/> (a real EF migration is out of
/// scope for this build). Verification reconciles row counts and referential
/// integrity against the documented fixture oracle.
/// </summary>
public sealed class DatabaseManager(CardDemoDbContext db, FixtureSeeder seeder) : IDatabaseManager
{
    // Documented fixture oracle (CONTRACT seed-count oracle).
    private static readonly IReadOnlyDictionary<string, int> ExpectedCounts =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Accounts"] = 50,
            ["Customers"] = 50,
            ["Cards"] = 50,
            ["CardXrefs"] = 50,
            ["TransactionCategoryBalances"] = 50,
            ["DisclosureGroups"] = 51,
            ["TransactionTypes"] = 7,
            ["TransactionCategories"] = 18,
            ["Users"] = 10,
        };

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);
    }

    public async Task<SeedReport> InitializeAsync(string fixtureRoot, bool reseed, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);

        var usersEmpty = !await db.Users.AnyAsync(ct).ConfigureAwait(false);

        IReadOnlyDictionary<string, int> counts;
        if (reseed || usersEmpty)
        {
            counts = await seeder.LoadAllAsync(fixtureRoot, clearFirst: reseed, ct).ConfigureAwait(false);
        }
        else
        {
            counts = await CountAllAsync(ct).ConfigureAwait(false);
        }

        return new SeedReport(counts, DatabasePath());
    }

    public async Task<VerifyReport> VerifyAsync(CancellationToken ct = default)
    {
        var counts = await CountAllAsync(ct).ConfigureAwait(false);
        var issues = new List<string>();

        // Count oracle. The master/reference tables are invariant after seeding.
        // Category balances are seeded at 50 but legitimately GROW as posting creates
        // new (account, type, category) balances, so that table is a lower bound.
        foreach (var (table, expected) in ExpectedCounts)
        {
            var actual = counts.TryGetValue(table, out var c) ? c : 0;
            if (table == "TransactionCategoryBalances")
            {
                if (actual < expected)
                    issues.Add($"{table}: expected at least {expected} rows but found {actual}.");
            }
            else if (actual != expected)
            {
                issues.Add($"{table}: expected {expected} rows but found {actual}.");
            }
        }

        // Referential checks.
        var accountIds = await db.Accounts.Select(a => a.AccountId).ToListAsync(ct).ConfigureAwait(false);
        var accountSet = accountIds.ToHashSet(StringComparer.Ordinal);
        var cardNumbers = await db.Cards.Select(c => c.CardNumber).ToListAsync(ct).ConfigureAwait(false);
        var cardSet = cardNumbers.ToHashSet(StringComparer.Ordinal);

        var cards = await db.Cards.Select(c => new { c.CardNumber, c.AccountId }).ToListAsync(ct).ConfigureAwait(false);
        foreach (var card in cards)
        {
            if (!accountSet.Contains(card.AccountId))
                issues.Add($"Card {card.CardNumber} references missing account {card.AccountId}.");
        }

        var xrefs = await db.CardXrefs.Select(x => new { x.CardNumber, x.AccountId }).ToListAsync(ct).ConfigureAwait(false);
        foreach (var xref in xrefs)
        {
            if (!cardSet.Contains(xref.CardNumber))
                issues.Add($"CardXref card {xref.CardNumber} references missing card.");
            if (!accountSet.Contains(xref.AccountId))
                issues.Add($"CardXref card {xref.CardNumber} references missing account {xref.AccountId}.");
        }

        return new VerifyReport(issues.Count == 0, issues, counts);
    }

    private async Task<IReadOnlyDictionary<string, int>> CountAllAsync(CancellationToken ct)
    {
        return new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Accounts"] = await db.Accounts.CountAsync(ct).ConfigureAwait(false),
            ["Customers"] = await db.Customers.CountAsync(ct).ConfigureAwait(false),
            ["Cards"] = await db.Cards.CountAsync(ct).ConfigureAwait(false),
            ["CardXrefs"] = await db.CardXrefs.CountAsync(ct).ConfigureAwait(false),
            ["TransactionCategoryBalances"] = await db.TransactionCategoryBalances.CountAsync(ct).ConfigureAwait(false),
            ["DisclosureGroups"] = await db.DisclosureGroups.CountAsync(ct).ConfigureAwait(false),
            ["TransactionTypes"] = await db.TransactionTypes.CountAsync(ct).ConfigureAwait(false),
            ["TransactionCategories"] = await db.TransactionCategories.CountAsync(ct).ConfigureAwait(false),
            ["Users"] = await db.Users.CountAsync(ct).ConfigureAwait(false),
        };
    }

    private string DatabasePath() => SqliteDataSource.Resolve(db.Database.GetConnectionString());
}
