using CardDemo.Application.Abstractions;
using CardDemo.Domain.Entities;
using CardDemo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardDemo.Infrastructure.Fixtures;

/// <summary>
/// Loads the fixed-width ASCII fixtures into the relational store. Seeds the master
/// and reference tables plus the bootstrap users; the daily transaction file is a
/// posting INPUT and is deliberately NOT seeded into TRANSACT.
/// </summary>
public sealed class FixtureSeeder(CardDemoDbContext db, IPasswordHasher hasher)
{
    private readonly FixtureLoader _loader = new();

    // Fixture file names as delivered under the ASCII fixture root.
    private const string AcctFile = "acctdata.txt";
    private const string CardFile = "carddata.txt";
    private const string XrefFile = "cardxref.txt";
    private const string CustFile = "custdata.txt";
    private const string DiscFile = "discgrp.txt";
    private const string TcatFile = "tcatbal.txt";
    private const string TrancatgFile = "trancatg.txt";
    private const string TrantypeFile = "trantype.txt";

    /// <summary>
    /// Insert all master/reference tables and users from <paramref name="fixtureRoot"/>.
    /// When <paramref name="clearFirst"/> is set the same tables are cleared first
    /// (used by reseed). Daily transactions are not loaded here.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> LoadAllAsync(
        string fixtureRoot, bool clearFirst, CancellationToken ct = default)
    {
        var accounts = _loader.LoadAccounts(Resolve(fixtureRoot, AcctFile));
        var customers = _loader.LoadCustomers(Resolve(fixtureRoot, CustFile));
        var cards = _loader.LoadCards(Resolve(fixtureRoot, CardFile));
        var xrefs = _loader.LoadXrefs(Resolve(fixtureRoot, XrefFile));
        var catBalances = _loader.LoadCategoryBalances(Resolve(fixtureRoot, TcatFile));
        var discGroups = _loader.LoadDisclosureGroups(Resolve(fixtureRoot, DiscFile));
        var types = _loader.LoadTypes(Resolve(fixtureRoot, TrantypeFile));
        var categories = _loader.LoadCategories(Resolve(fixtureRoot, TrancatgFile));
        var users = _loader.SeedUsers(hasher);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            if (clearFirst)
                await ClearMastersAsync(ct).ConfigureAwait(false);

            await db.Accounts.AddRangeAsync(accounts, ct).ConfigureAwait(false);
            await db.Customers.AddRangeAsync(customers, ct).ConfigureAwait(false);
            await db.Cards.AddRangeAsync(cards, ct).ConfigureAwait(false);
            await db.CardXrefs.AddRangeAsync(xrefs, ct).ConfigureAwait(false);
            await db.TransactionCategoryBalances.AddRangeAsync(catBalances, ct).ConfigureAwait(false);
            await db.DisclosureGroups.AddRangeAsync(discGroups, ct).ConfigureAwait(false);
            await db.TransactionTypes.AddRangeAsync(types, ct).ConfigureAwait(false);
            await db.TransactionCategories.AddRangeAsync(categories, ct).ConfigureAwait(false);
            await db.Users.AddRangeAsync(users, ct).ConfigureAwait(false);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }).ConfigureAwait(false);

        counts["Accounts"] = accounts.Count;
        counts["Customers"] = customers.Count;
        counts["Cards"] = cards.Count;
        counts["CardXrefs"] = xrefs.Count;
        counts["TransactionCategoryBalances"] = catBalances.Count;
        counts["DisclosureGroups"] = discGroups.Count;
        counts["TransactionTypes"] = types.Count;
        counts["TransactionCategories"] = categories.Count;
        counts["Users"] = users.Count;

        return counts;
    }

    /// <summary>Clear and reload the master/reference tables (batch refresh-masters).</summary>
    public Task<IReadOnlyDictionary<string, int>> RefreshMastersAsync(
        string fixtureRoot, CancellationToken ct = default) =>
        LoadAllAsync(fixtureRoot, clearFirst: true, ct);

    private async Task ClearMastersAsync(CancellationToken ct)
    {
        // Reseed/refresh rebuilds the world from the fixtures, so any transaction
        // data derived from a prior posting/interest run is cleared too. This keeps
        // initialize / refresh-masters / full-cycle idempotent and re-runnable
        // (otherwise re-posting the same daily file would hit duplicate keys).
        db.PendingReportRequests.RemoveRange(await db.PendingReportRequests.ToListAsync(ct).ConfigureAwait(false));
        db.Transactions.RemoveRange(await db.Transactions.ToListAsync(ct).ConfigureAwait(false));

        // Remove children before parents to respect referential ordering.
        db.TransactionCategoryBalances.RemoveRange(await db.TransactionCategoryBalances.ToListAsync(ct).ConfigureAwait(false));
        db.CardXrefs.RemoveRange(await db.CardXrefs.ToListAsync(ct).ConfigureAwait(false));
        db.Cards.RemoveRange(await db.Cards.ToListAsync(ct).ConfigureAwait(false));
        db.DisclosureGroups.RemoveRange(await db.DisclosureGroups.ToListAsync(ct).ConfigureAwait(false));
        db.TransactionCategories.RemoveRange(await db.TransactionCategories.ToListAsync(ct).ConfigureAwait(false));
        db.TransactionTypes.RemoveRange(await db.TransactionTypes.ToListAsync(ct).ConfigureAwait(false));
        db.Accounts.RemoveRange(await db.Accounts.ToListAsync(ct).ConfigureAwait(false));
        db.Customers.RemoveRange(await db.Customers.ToListAsync(ct).ConfigureAwait(false));
        db.Users.RemoveRange(await db.Users.ToListAsync(ct).ConfigureAwait(false));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static string Resolve(string fixtureRoot, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureRoot);
        return Path.Combine(fixtureRoot, fileName);
    }
}
