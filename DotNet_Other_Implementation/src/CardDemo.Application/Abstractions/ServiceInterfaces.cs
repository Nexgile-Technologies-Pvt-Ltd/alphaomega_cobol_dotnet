using CardDemo.Application.Dtos;
using CardDemo.Domain.Common;
using CardDemo.Domain.Entities;

namespace CardDemo.Application.Abstractions;

/// <summary>Sign-on (COSGN00C). Returns the authenticated user or a failure message.</summary>
public interface IAuthService
{
    Task<OperationResult<UserSecurity>> SignInAsync(string userId, string password, CancellationToken ct = default);
}

/// <summary>Account view and update (COACTVWC / COACTUPC).</summary>
public interface IAccountService
{
    Task<OperationResult<AccountView>> ViewAsync(string accountId, CancellationToken ct = default);
    Task<OperationResult> UpdateAsync(AccountUpdateRequest request, CancellationToken ct = default);
}

/// <summary>Card list, view and update (COCRDLIC / COCRDSLC / COCRDUPC).</summary>
public interface ICardService
{
    Task<OperationResult<PagedResult<Card>>> ListAsync(string? accountId, string? cardNumber, int page, int pageSize, CancellationToken ct = default);
    Task<OperationResult<CardWithAccount>> ViewAsync(string accountId, string cardNumber, CancellationToken ct = default);
    Task<OperationResult> UpdateAsync(CardUpdateRequest request, CancellationToken ct = default);
}

/// <summary>Transaction list, view and add (COTRN00C / COTRN01C / COTRN02C).</summary>
public interface ITransactionService
{
    Task<OperationResult<PagedResult<Transaction>>> ListAsync(int page, int pageSize, CancellationToken ct = default);
    Task<OperationResult<Transaction>> ViewAsync(string transactionId, CancellationToken ct = default);
    Task<OperationResult<Transaction>> AddAsync(TransactionAddRequest request, CancellationToken ct = default);

    /// <summary>
    /// F5 prefill (COTRN02C): return the non-key fields of the greatest-transaction-ID
    /// record as add-form defaults, or a failure if no transactions exist.
    /// </summary>
    Task<OperationResult<TransactionAddRequest>> PrefillFromLatestAsync(CancellationToken ct = default);
}

/// <summary>Bill payment (COBIL00C) — pays the full account balance.</summary>
public interface IBillPayService
{
    Task<OperationResult<Transaction>> PayFullBalanceAsync(string accountId, CancellationToken ct = default);
}

/// <summary>
/// Security-user administration (COUSR00C/01C/02C/03C). Every mutating use case takes
/// the acting user's id and enforces administrator authorization at the service
/// boundary — navigation route/session state is not trusted (FR-AUTH-005/FR-USER-008).
/// </summary>
public interface IUserAdminService
{
    Task<OperationResult<PagedResult<UserSecurity>>> ListAsync(int page, int pageSize, CancellationToken ct = default);
    Task<OperationResult<UserSecurity>> GetAsync(string userId, CancellationToken ct = default);
    Task<OperationResult> AddAsync(UserUpsertRequest request, string actingUserId, CancellationToken ct = default);
    Task<OperationResult> UpdateAsync(UserUpsertRequest request, string actingUserId, CancellationToken ct = default);
    Task<OperationResult> DeleteAsync(string userId, string actingUserId, CancellationToken ct = default);
}

/// <summary>Report request (CORPT00C). Validates dates and records a durable request.</summary>
public interface IReportRequestService
{
    Task<OperationResult> RequestAsync(string fromDate, string toDate, string requestedByUserId, CancellationToken ct = default);
}
