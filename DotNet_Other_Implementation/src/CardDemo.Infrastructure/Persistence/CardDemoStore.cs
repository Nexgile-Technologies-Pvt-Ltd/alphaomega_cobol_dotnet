using CardDemo.Application.Abstractions;
using CardDemo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CardDemo.Infrastructure.Persistence;

/// <summary>
/// EF Core + SQLite implementation of <see cref="ICardDemoStore"/>. Find/List methods
/// used to fetch entities that the online services then mutate are change-tracked;
/// read-only reference lookups use <c>AsNoTracking</c>.
/// </summary>
public sealed class CardDemoStore(CardDemoDbContext db) : ICardDemoStore
{
    // --- Users (USRSEC) ---

    public Task<UserSecurity?> FindUserAsync(string userId, CancellationToken ct = default) =>
        db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct);

    public async Task<IReadOnlyList<UserSecurity>> ListUsersAsync(int skip, int take, CancellationToken ct = default) =>
        await db.Users.AsNoTracking()
            .OrderBy(u => u.UserId)
            .Skip(skip).Take(take)
            .ToListAsync(ct);

    public async Task AddUserAsync(UserSecurity user, CancellationToken ct = default) =>
        await db.Users.AddAsync(user, ct);

    public void RemoveUser(UserSecurity user) => db.Users.Remove(user);

    public Task<int> CountAdminsAsync(CancellationToken ct = default) =>
        db.Users.CountAsync(u => u.UserType == "A", ct);

    // --- Accounts (ACCTDAT) ---

    public Task<Account?> FindAccountAsync(string accountId, CancellationToken ct = default) =>
        db.Accounts.FirstOrDefaultAsync(a => a.AccountId == accountId, ct);

    public async Task<IReadOnlyList<Account>> ListAccountsAsync(int skip, int take, CancellationToken ct = default) =>
        await db.Accounts.AsNoTracking()
            .OrderBy(a => a.AccountId)
            .Skip(skip).Take(take)
            .ToListAsync(ct);

    // --- Report requests (durable pending-report queue) ---

    public async Task AddPendingReportRequestAsync(PendingReportRequest request, CancellationToken ct = default) =>
        await db.PendingReportRequests.AddAsync(request, ct);

    // --- Customers (CUSTDAT) ---

    public Task<Customer?> FindCustomerAsync(string customerId, CancellationToken ct = default) =>
        db.Customers.FirstOrDefaultAsync(c => c.CustomerId == customerId, ct);

    // --- Cards (CARDDAT) ---

    public Task<Card?> FindCardAsync(string cardNumber, CancellationToken ct = default) =>
        db.Cards.FirstOrDefaultAsync(c => c.CardNumber == cardNumber, ct);

    public async Task<IReadOnlyList<Card>> ListCardsAsync(string? accountId, string? cardNumber, int skip, int take, CancellationToken ct = default)
    {
        IQueryable<Card> query = db.Cards.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(accountId))
            query = query.Where(c => c.AccountId == accountId);
        if (!string.IsNullOrWhiteSpace(cardNumber))
            query = query.Where(c => c.CardNumber == cardNumber);

        return await query
            .OrderBy(c => c.CardNumber)
            .Skip(skip).Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Card>> CardsByAccountAsync(string accountId, CancellationToken ct = default) =>
        await db.Cards.AsNoTracking()
            .Where(c => c.AccountId == accountId)
            .OrderBy(c => c.CardNumber)
            .ToListAsync(ct);

    // --- Cross-reference (CCXREF) ---

    public Task<CardXref?> XrefByCardAsync(string cardNumber, CancellationToken ct = default) =>
        db.CardXrefs.AsNoTracking().FirstOrDefaultAsync(x => x.CardNumber == cardNumber, ct);

    public Task<CardXref?> XrefByAccountAsync(string accountId, CancellationToken ct = default) =>
        db.CardXrefs.AsNoTracking().FirstOrDefaultAsync(x => x.AccountId == accountId, ct);

    // --- Transactions (TRANSACT) ---

    public Task<Transaction?> FindTransactionAsync(string transactionId, CancellationToken ct = default) =>
        db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.TransactionId == transactionId, ct);

    public async Task<IReadOnlyList<Transaction>> ListTransactionsAsync(int skip, int take, CancellationToken ct = default) =>
        await db.Transactions.AsNoTracking()
            .OrderBy(t => t.TransactionId)
            .Skip(skip).Take(take)
            .ToListAsync(ct);

    public Task<string?> MaxTransactionIdAsync(CancellationToken ct = default) =>
        db.Transactions.AsNoTracking()
            .OrderByDescending(t => t.TransactionId)
            .Select(t => t.TransactionId)
            .FirstOrDefaultAsync(ct);

    public async Task AddTransactionAsync(Transaction transaction, CancellationToken ct = default) =>
        await db.Transactions.AddAsync(transaction, ct);

    // --- Reference data ---

    public async Task<IReadOnlyList<TransactionType>> ListTypesAsync(CancellationToken ct = default) =>
        await db.TransactionTypes.AsNoTracking()
            .OrderBy(t => t.TypeCode)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TransactionCategory>> ListCategoriesAsync(CancellationToken ct = default) =>
        await db.TransactionCategories.AsNoTracking()
            .OrderBy(c => c.TypeCode).ThenBy(c => c.CategoryCode)
            .ToListAsync(ct);

    // --- Unit of work ---

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await work(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
