namespace CardDemo.Application.Abstractions;

/// <summary>Row counts and location after a seed/refresh.</summary>
public sealed record SeedReport(IReadOnlyDictionary<string, int> Counts, string DatabasePath);

/// <summary>Result of database verification.</summary>
public sealed record VerifyReport(bool Ok, IReadOnlyList<string> Issues, IReadOnlyDictionary<string, int> Counts);

/// <summary>Result of a daily-transaction posting run (CBTRN02C).</summary>
public sealed record PostingReport(int Accepted, int Rejected, IReadOnlyDictionary<int, int> RejectsByReason)
{
    public bool HasRejects => Rejected > 0;
}

/// <summary>Result of an interest-calculation run (CBACT04C).</summary>
public sealed record InterestReport(int InterestTransactions, decimal TotalInterest, int AccountsUpdated);

/// <summary>Result of a transaction report run (CBTRN03C).</summary>
public sealed record ReportRunResult(int TransactionsIncluded, decimal TotalAmount, string OutputPath);

/// <summary>
/// Result of combining transaction generations (COMBTRAN). In the relational store the
/// posted daily transactions and the interest system transactions already share one
/// transaction master, so combine reconciles and reports the combined counts.
/// </summary>
public sealed record CombineResult(int TotalTransactions, int PostedTransactions, int InterestTransactions);

/// <summary>
/// Database lifecycle commands (carddemo database initialize/migrate/verify).
/// Implemented in Infrastructure over EF Core + SQLite.
/// </summary>
public interface IDatabaseManager
{
    /// <summary>Ensure the schema exists (explicit migrate, never on-startup in production).</summary>
    Task MigrateAsync(CancellationToken ct = default);

    /// <summary>Create the schema if needed and load the supplied fixtures.</summary>
    Task<SeedReport> InitializeAsync(string fixtureRoot, bool reseed, CancellationToken ct = default);

    /// <summary>Reconcile row counts and referential integrity against the documented fixture oracle.</summary>
    Task<VerifyReport> VerifyAsync(CancellationToken ct = default);
}

/// <summary>
/// Non-interactive batch commands. Implemented in Infrastructure using the pure
/// domain engines (PostingEngine, InterestEngine) plus the DbContext.
/// </summary>
public interface IBatchRunner
{
    /// <summary>Reload master/reference tables from fixtures (batch refresh-masters).</summary>
    Task<SeedReport> RefreshMastersAsync(string fixtureRoot, CancellationToken ct = default);

    /// <summary>Post daily transactions from the input file into TRANSACT, writing rejects (batch post-transactions).</summary>
    Task<PostingReport> PostTransactionsAsync(string inputPath, string rejectsPath, CancellationToken ct = default);

    /// <summary>Calculate interest for the cycle (batch calculate-interest).</summary>
    Task<InterestReport> CalculateInterestAsync(string cycleId, CancellationToken ct = default);

    /// <summary>Produce a dated transaction report (batch generate-report).</summary>
    Task<ReportRunResult> GenerateReportAsync(string fromDate, string toDate, string outputPath, CancellationToken ct = default);

    /// <summary>Process durable pending report requests (batch run-pending-reports).</summary>
    Task<int> RunPendingReportsAsync(string outputDirectory, CancellationToken ct = default);

    /// <summary>Combine/reconcile the transaction generations into the master (batch combine-transactions, COMBTRAN).</summary>
    Task<CombineResult> CombineTransactionsAsync(CancellationToken ct = default);
}
