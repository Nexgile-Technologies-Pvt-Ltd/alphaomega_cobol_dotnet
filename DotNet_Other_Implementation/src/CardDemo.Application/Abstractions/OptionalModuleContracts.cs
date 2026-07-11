using CardDemo.Application.Dtos;
using CardDemo.Domain.Common;
using CardDemo.Domain.Entities;

namespace CardDemo.Application.Abstractions;

// ===================== Transaction-type maintenance (CTLI / CTTU) =====================

/// <summary>
/// Optional transaction-type / category maintenance (COTRTLIC list, COTRTUPC update).
/// Operates on the relational TransactionType / TransactionCategory tables with the
/// legacy FK-restrict-on-delete behaviour.
/// </summary>
public interface ITransactionTypeService
{
    Task<OperationResult<PagedResult<TransactionType>>> ListTypesAsync(int page, int pageSize, CancellationToken ct = default);
    Task<OperationResult<TransactionType>> GetTypeAsync(string typeCode, CancellationToken ct = default);
    Task<OperationResult> UpsertTypeAsync(string typeCode, string description, CancellationToken ct = default);
    Task<OperationResult> DeleteTypeAsync(string typeCode, CancellationToken ct = default);

    Task<OperationResult<PagedResult<TransactionCategory>>> ListCategoriesAsync(string? typeCode, int page, int pageSize, CancellationToken ct = default);
    Task<OperationResult> UpsertCategoryAsync(string typeCode, string categoryCode, string description, CancellationToken ct = default);
    Task<OperationResult> DeleteCategoryAsync(string typeCode, string categoryCode, CancellationToken ct = default);

    /// <summary>
    /// Apply a 53-byte A/U/D/* transaction-type maintenance file (COBTUPDT):
    /// each record is action(1) + type(2) + description(50); continue past record
    /// errors and report the applied/failed counts.
    /// </summary>
    Task<BatchApplyResult> ApplyBatchAsync(string inputPath, CancellationToken ct = default);

    /// <summary>Export the transaction type + category reference data to a file (TRANEXTR).</summary>
    Task<int> ExportReferencesAsync(string outputPath, CancellationToken ct = default);
}

/// <summary>Result of a file-driven transaction-type batch apply (COBTUPDT).</summary>
public sealed record BatchApplyResult(int Applied, int Failed);

// ===================== Statements (CBSTM03A) =====================

public sealed record StatementRunResult(int Statements, int TransactionLines, string TextPath, string HtmlPath);

/// <summary>Account statement generation — fixed-width text plus HTML (CBSTM03A/CREASTMT).</summary>
public interface IStatementService
{
    Task<StatementRunResult> GenerateAsync(string textPath, string htmlPath, CancellationToken ct = default);
}

// ===================== Branch export / import (CBEXPORT / CBIMPORT) =====================

public sealed record TransferResult(int Records, IReadOnlyDictionary<string, int> RecordsByType, string Path);

/// <summary>500-byte branch export/import over record types C/A/X/T/D (CBEXPORT/CBIMPORT).</summary>
public interface ITransferService
{
    Task<TransferResult> ExportAsync(string outputPath, CancellationToken ct = default);
    Task<TransferResult> ImportAsync(string inputPath, string errorPath, CancellationToken ct = default);
}

// ===================== Authorization (COPAUA0C / COPAUS0C / COPAUS1C / CBPAUP0C) =====================

public sealed record AuthProcessResult(int Processed, int Approved, int Declined);

/// <summary>
/// Pending-authorization module: request decisioning, operator inquiry (summary/detail),
/// fraud tagging and expiry purge. Models the IMS/Db2/MQ pieces relationally.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>Enqueue an authorization request (replaces the inbound MQ put).</summary>
    Task<OperationResult<AuthorizationRequest>> SubmitAsync(AuthorizationRequest request, CancellationToken ct = default);

    /// <summary>Decision the pending requests: approve/decline against available credit and record summary+detail+reply.</summary>
    Task<AuthProcessResult> ProcessPendingAsync(int maxMessages, CancellationToken ct = default);

    Task<OperationResult<PagedResult<PendingAuthSummary>>> ListSummariesAsync(int page, int pageSize, CancellationToken ct = default);
    Task<OperationResult<IReadOnlyList<PendingAuthDetail>>> GetDetailsAsync(string accountId, CancellationToken ct = default);

    /// <summary>Toggle the fraud flag on a detail (CPVD PF5) and record fraud history.</summary>
    Task<OperationResult> SetFraudAsync(int detailId, bool fraud, CancellationToken ct = default);

    /// <summary>Purge pending-auth details older than the given number of days (CBPAUP0C).</summary>
    Task<int> PurgeExpiredAsync(int days, CancellationToken ct = default);

    /// <summary>Unload pending-auth summaries + details to a file (PAUDBUNL). Returns records written.</summary>
    Task<int> UnloadAsync(string outputPath, CancellationToken ct = default);

    /// <summary>Load pending-auth summaries + details from a file (PAUDBLOD). Returns records read.</summary>
    Task<int> LoadAsync(string inputPath, CancellationToken ct = default);
}

// ===================== Inquiry / date workers (COACCT01 / CODATE01) =====================

public sealed record InquiryProcessResult(int Processed);

/// <summary>Account-inquiry and system-date request/reply services (COACCT01/CODATE01).</summary>
public interface IInquiryService
{
    Task<OperationResult> SubmitAccountInquiryAsync(string accountId, CancellationToken ct = default);
    Task<OperationResult> SubmitDateInquiryAsync(CancellationToken ct = default);

    /// <summary>Drain and answer pending requests for a service ("INQA" or "DATE").</summary>
    Task<InquiryProcessResult> ProcessPendingAsync(string service, int maxMessages, CancellationToken ct = default);

    Task<IReadOnlyList<InquiryReply>> RecentRepliesAsync(int take, CancellationToken ct = default);
}
