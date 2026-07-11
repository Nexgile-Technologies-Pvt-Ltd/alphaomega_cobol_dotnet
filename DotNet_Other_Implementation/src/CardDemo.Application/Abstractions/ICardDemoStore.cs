using CardDemo.Domain.Entities;

namespace CardDemo.Application.Abstractions;

/// <summary>
/// Provider-neutral persistence port used by the online application services.
/// Implemented in the Infrastructure layer over EF Core + SQLite. Entities
/// returned by the Find/List methods are change-tracked, so mutating them and
/// calling <see cref="SaveChangesAsync"/> persists the change.
/// Batch orchestrators use the DbContext directly and do not go through this port.
/// </summary>
public interface ICardDemoStore
{
    // --- Users (USRSEC) ---
    Task<UserSecurity?> FindUserAsync(string userId, CancellationToken ct = default);
    Task<IReadOnlyList<UserSecurity>> ListUsersAsync(int skip, int take, CancellationToken ct = default);
    Task AddUserAsync(UserSecurity user, CancellationToken ct = default);
    void RemoveUser(UserSecurity user);
    Task<int> CountAdminsAsync(CancellationToken ct = default);

    // --- Accounts (ACCTDAT) ---
    Task<Account?> FindAccountAsync(string accountId, CancellationToken ct = default);
    Task<IReadOnlyList<Account>> ListAccountsAsync(int skip, int take, CancellationToken ct = default);

    // --- Customers (CUSTDAT) ---
    Task<Customer?> FindCustomerAsync(string customerId, CancellationToken ct = default);

    // --- Cards (CARDDAT) ---
    Task<Card?> FindCardAsync(string cardNumber, CancellationToken ct = default);
    Task<IReadOnlyList<Card>> ListCardsAsync(string? accountId, string? cardNumber, int skip, int take, CancellationToken ct = default);
    Task<IReadOnlyList<Card>> CardsByAccountAsync(string accountId, CancellationToken ct = default);

    // --- Cross-reference (CCXREF) ---
    Task<CardXref?> XrefByCardAsync(string cardNumber, CancellationToken ct = default);
    Task<CardXref?> XrefByAccountAsync(string accountId, CancellationToken ct = default);

    // --- Transactions (TRANSACT) ---
    Task<Transaction?> FindTransactionAsync(string transactionId, CancellationToken ct = default);
    Task<IReadOnlyList<Transaction>> ListTransactionsAsync(int skip, int take, CancellationToken ct = default);
    Task<string?> MaxTransactionIdAsync(CancellationToken ct = default);
    Task AddTransactionAsync(Transaction transaction, CancellationToken ct = default);

    // --- Reference data ---
    Task<IReadOnlyList<TransactionType>> ListTypesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TransactionCategory>> ListCategoriesAsync(CancellationToken ct = default);

    // --- Report requests (durable pending-report queue) ---
    Task AddPendingReportRequestAsync(PendingReportRequest request, CancellationToken ct = default);

    // --- Unit of work ---
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct = default);
}
