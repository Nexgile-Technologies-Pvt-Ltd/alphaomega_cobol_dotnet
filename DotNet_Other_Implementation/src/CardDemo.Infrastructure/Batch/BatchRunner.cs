using System.Globalization;
using System.Text;
using CardDemo.Application.Abstractions;
using CardDemo.Domain.Entities;
using CardDemo.Domain.Services;
using CardDemo.Infrastructure.Fixtures;
using CardDemo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardDemo.Infrastructure.Batch;

/// <summary>
/// Non-interactive batch commands driven by the pure domain engines
/// (<see cref="PostingEngine"/>, <see cref="InterestEngine"/>) over the DbContext.
/// Timestamps come from the injected <see cref="TimeProvider"/>.
/// </summary>
public sealed class BatchRunner(
    CardDemoDbContext db,
    FixtureSeeder seeder,
    TimeProvider timeProvider) : IBatchRunner
{
    private const int ReportLineWidth = 133;
    private const int DailyRecordLength = 350;

    private readonly FixtureLoader _loader = new();

    public Task<SeedReport> RefreshMastersAsync(string fixtureRoot, CancellationToken ct = default) =>
        RefreshMastersInternalAsync(fixtureRoot, ct);

    private async Task<SeedReport> RefreshMastersInternalAsync(string fixtureRoot, CancellationToken ct)
    {
        var counts = await seeder.RefreshMastersAsync(fixtureRoot, ct).ConfigureAwait(false);
        return new SeedReport(counts, DatabasePath());
    }

    public async Task<PostingReport> PostTransactionsAsync(string inputPath, string rejectsPath, CancellationToken ct = default)
    {
        var daily = _loader.LoadDailyTransactions(inputPath);
        var rawByRecord = BuildRawLineMap(inputPath, daily);

        var xrefs = await db.CardXrefs.ToListAsync(ct).ConfigureAwait(false);
        var accounts = await db.Accounts.ToListAsync(ct).ConfigureAwait(false);
        var catBalances = await db.TransactionCategoryBalances.ToListAsync(ct).ConfigureAwait(false);

        var world = new PostingWorld(xrefs, accounts, catBalances);
        var processTimestamp = LegacyTimestamp.Now(timeProvider);

        var engine = new PostingEngine();
        var outcome = engine.Post(daily, world, processTimestamp);

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            await db.Transactions.AddRangeAsync(outcome.Posted, ct).ConfigureAwait(false);
            await db.TransactionCategoryBalances.AddRangeAsync(outcome.CreatedCategoryBalances, ct).ConfigureAwait(false);
            // Account and existing category-balance mutations are tracked and flow on save.

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }).ConfigureAwait(false);

        await WriteRejectsAsync(rejectsPath, outcome.Rejects, rawByRecord, ct).ConfigureAwait(false);

        var byReason = outcome.Rejects
            .GroupBy(r => r.ReasonCode)
            .ToDictionary(g => g.Key, g => g.Count());

        return new PostingReport(outcome.AcceptedCount, outcome.RejectedCount, byReason);
    }

    public async Task<InterestReport> CalculateInterestAsync(string cycleId, CancellationToken ct = default)
    {
        var accounts = await db.Accounts.ToListAsync(ct).ConfigureAwait(false);
        var discGroups = await db.DisclosureGroups.ToListAsync(ct).ConfigureAwait(false);
        var xrefs = await db.CardXrefs.ToListAsync(ct).ConfigureAwait(false);

        var orderedBalances = await db.TransactionCategoryBalances
            .OrderBy(b => b.AccountId)
            .ThenBy(b => b.TypeCode)
            .ThenBy(b => b.CategoryCode)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var world = new InterestWorld(accounts, discGroups, xrefs);
        var processTimestamp = LegacyTimestamp.Now(timeProvider);

        var engine = new InterestEngine();
        var outcome = engine.Run(orderedBalances, world, cycleId, processTimestamp, startingSuffix: 0, updateFinalAccount: true);

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            await db.Transactions.AddRangeAsync(outcome.InterestTransactions, ct).ConfigureAwait(false);
            // Account balance/cycle updates are tracked and flow on save.

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return new InterestReport(
            outcome.InterestTransactions.Count,
            outcome.TotalInterest,
            outcome.UpdatedAccounts.Count);
    }

    public async Task<ReportRunResult> GenerateReportAsync(string fromDate, string toDate, string outputPath, CancellationToken ct = default)
    {
        var transactions = await db.Transactions.ToListAsync(ct).ConfigureAwait(false);
        var typeDescriptions = await db.TransactionTypes
            .ToDictionaryAsync(t => t.TypeCode, t => t.Description, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);
        var categoryDescriptions = await db.TransactionCategories
            .ToDictionaryAsync(c => c.TypeCode + "|" + c.CategoryCode, c => c.Description, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);
        var accountByCard = await db.CardXrefs
            .ToDictionaryAsync(x => x.CardNumber, x => x.AccountId, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);

        // CBTRN03C reads the sequential transaction master; card grouping mirrors that
        // sequential order, so order by card number then process timestamp.
        var included = transactions
            .Where(t => IsWithin(TransactionDate(t), fromDate, toDate))
            .OrderBy(t => t.CardNumber, StringComparer.Ordinal)
            .ThenBy(t => t.ProcessTimestamp, StringComparer.Ordinal)
            .ThenBy(t => t.TransactionId, StringComparer.Ordinal)
            .ToList();

        var total = included.Sum(t => t.Amount);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var lines = new List<string>();

        // WS-REPORT-VARS state (CBTRN03C).
        var lineCounter = 0;          // WS-LINE-COUNTER
        const int pageSize = 20;      // WS-PAGE-SIZE
        var pageTotal = 0m;           // WS-PAGE-TOTAL
        var accountTotal = 0m;        // WS-ACCOUNT-TOTAL
        var grandTotal = 0m;          // WS-GRAND-TOTAL
        var firstTime = true;         // WS-FIRST-TIME = 'Y'
        var currCardNum = string.Empty; // WS-CURR-CARD-NUM

        foreach (var t in included)
        {
            // 1120-WRITE-ACCOUNT-TOTALS on card change (skipped on the very first card).
            if (!string.Equals(currCardNum, t.CardNumber, StringComparison.Ordinal))
            {
                if (!firstTime)
                {
                    WriteAccountTotals(lines, ref accountTotal, ref lineCounter);
                }
                currCardNum = t.CardNumber;
            }

            // 1100-WRITE-TRANSACTION-REPORT.
            if (firstTime)
            {
                firstTime = false;
                WriteHeaders(lines, fromDate, toDate, ref lineCounter);
            }

            if (lineCounter % pageSize == 0)
            {
                WritePageTotals(lines, ref pageTotal, ref grandTotal, ref lineCounter);
                WriteHeaders(lines, fromDate, toDate, ref lineCounter);
            }

            pageTotal += t.Amount;
            accountTotal += t.Amount;

            var accountId = accountByCard.TryGetValue(t.CardNumber, out var acct) ? acct : string.Empty;
            var typeDesc = typeDescriptions.TryGetValue(t.TypeCode, out var td) ? td : string.Empty;
            var catDesc = categoryDescriptions.TryGetValue(t.TypeCode + "|" + t.CategoryCode, out var cd) ? cd : string.Empty;

            lines.Add(DetailLine(t, accountId, typeDesc, catDesc));
            lineCounter++;
        }

        // End of file: final account totals, final page total, then grand total.
        if (!firstTime)
        {
            WriteAccountTotals(lines, ref accountTotal, ref lineCounter);
            WritePageTotals(lines, ref pageTotal, ref grandTotal, ref lineCounter);
        }

        WriteGrandTotals(lines, grandTotal);

        await File.WriteAllTextAsync(outputPath, string.Concat(lines.Select(l => l + Environment.NewLine)), ct)
            .ConfigureAwait(false);

        return new ReportRunResult(included.Count, total, Path.GetFullPath(outputPath));
    }

    // 1120-WRITE-HEADERS: name header, blank line, header-1, header-2.
    private static void WriteHeaders(List<string> lines, string fromDate, string toDate, ref int lineCounter)
    {
        lines.Add(Line($"Daily Transaction Report{new string(' ', 33)}Date Range: {fromDate} to {toDate}"));
        lineCounter++;
        lines.Add(Line(string.Empty));
        lineCounter++;
        lines.Add(Line($"{"Transaction ID",-17}{"Account ID",-12}{"Transaction Type",-19}{"Tran Category",-35}{"Tran Source",-11} {"        Amount",-16}"));
        lineCounter++;
        lines.Add(Line(new string('-', ReportLineWidth)));
        lineCounter++;
    }

    // 1120-WRITE-DETAIL: TRANSACTION-DETAIL-REPORT layout (CVTRA07Y).
    private static string DetailLine(Transaction t, string accountId, string typeDesc, string catDesc)
    {
        var trans = Fit(t.TransactionId, 16);
        var acct = Fit(accountId, 11);
        var typeCd = Fit(t.TypeCode, 2);
        var typeD = Fit(typeDesc, 15);
        var catCd = Fit(CategoryCodeDigits(t.CategoryCode), 4);
        var catD = Fit(catDesc, 29);
        var source = Fit(t.Source, 10);
        var amt = FormatSignedAmount(t.Amount);

        return Line($"{trans} {acct} {typeCd}-{typeD} {catCd}-{catD} {source}    {amt}  ");
    }

    // 1110-WRITE-PAGE-TOTALS: page total line, roll into grand total, reset, then header-2.
    private static void WritePageTotals(List<string> lines, ref decimal pageTotal, ref decimal grandTotal, ref int lineCounter)
    {
        lines.Add(Line($"Page Total{new string('.', 86)}{FormatSignedAmount(pageTotal)}"));
        grandTotal += pageTotal;
        pageTotal = 0m;
        lineCounter++;
        lines.Add(Line(new string('-', ReportLineWidth)));
        lineCounter++;
    }

    // 1120-WRITE-ACCOUNT-TOTALS: account total line, reset, then header-2.
    private static void WriteAccountTotals(List<string> lines, ref decimal accountTotal, ref int lineCounter)
    {
        lines.Add(Line($"Account Total{new string('.', 84)}{FormatSignedAmount(accountTotal)}"));
        accountTotal = 0m;
        lineCounter++;
        lines.Add(Line(new string('-', ReportLineWidth)));
        lineCounter++;
    }

    // 1110-WRITE-GRAND-TOTALS.
    private static void WriteGrandTotals(List<string> lines, decimal grandTotal)
    {
        lines.Add(Line($"Grand Total{new string('.', 86)}{FormatSignedAmount(grandTotal)}"));
    }

    private static string CategoryCodeDigits(string categoryCode)
    {
        // TRAN-REPORT-CAT-CD is PIC 9(04); render as 4 digits.
        return int.TryParse(categoryCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n.ToString("D4", CultureInfo.InvariantCulture)
            : Fit(categoryCode, 4);
    }

    // PIC +ZZZ,ZZZ,ZZZ.ZZ — leading sign, grouped, two decimals.
    private static string FormatSignedAmount(decimal amount)
    {
        var sign = amount < 0 ? "-" : "+";
        var body = Math.Abs(amount).ToString("#,##0.00", CultureInfo.InvariantCulture);
        return sign + body;
    }

    private static string Fit(string value, int width)
    {
        value ??= string.Empty;
        return value.Length >= width ? value[..width] : value.PadRight(width);
    }

    public async Task<int> RunPendingReportsAsync(string outputDirectory, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var pending = await db.PendingReportRequests
            .Where(r => r.Status == "PENDING")
            .OrderBy(r => r.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var processed = 0;
        foreach (var request in pending)
        {
            var fileName = $"report_{request.Id:D6}_{request.FromDate}_{request.ToDate}.txt";
            var outputPath = Path.Combine(outputDirectory, fileName);

            await GenerateReportAsync(request.FromDate, request.ToDate, outputPath, ct).ConfigureAwait(false);

            request.Status = "COMPLETED";
            processed++;
        }

        if (processed > 0)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return processed;
    }

    public async Task<CombineResult> CombineTransactionsAsync(CancellationToken ct = default)
    {
        var total = await db.Transactions.CountAsync(ct).ConfigureAwait(false);

        var interest = await db.Transactions
            .CountAsync(t => t.TypeCode == "01" && t.CategoryCode == "0005" && t.Source == "System", ct)
            .ConfigureAwait(false);

        var posted = total - interest;

        return new CombineResult(total, posted, interest);
    }

    // --- helpers ---------------------------------------------------------

    private static string TransactionDate(Transaction t)
    {
        // A transaction report is dated by when the transaction occurred
        // (TRAN-ORIG-TS), not when it was posted. Fall back to the processing
        // timestamp only when no origin timestamp is present.
        var orig = t.OriginTimestamp;
        if (!string.IsNullOrWhiteSpace(orig) && orig.Length >= 10)
            return orig[..10];

        var proc = t.ProcessTimestamp;
        return proc.Length >= 10 ? proc[..10] : proc.Trim();
    }

    private static bool IsWithin(string date, string fromDate, string toDate)
    {
        if (string.IsNullOrEmpty(date))
            return false;
        return string.CompareOrdinal(date, fromDate) >= 0 && string.CompareOrdinal(date, toDate) <= 0;
    }

    private static string Line(string content)
    {
        if (content.Length >= ReportLineWidth)
            return content[..ReportLineWidth];
        return content.PadRight(ReportLineWidth);
    }

    /// <summary>
    /// Map each parsed daily transaction (by reference identity) to its original raw
    /// 350-byte record so rejects can be written verbatim.
    /// </summary>
    private static Dictionary<Transaction, string> BuildRawLineMap(string inputPath, IReadOnlyList<Transaction> daily)
    {
        var map = new Dictionary<Transaction, string>(ReferenceEqualityComparer.Instance);
        var raw = File.ReadAllText(inputPath);
        var lines = raw.Split('\n');

        var index = 0;
        foreach (var line in lines)
        {
            var trimmed = line.Length > 0 && line[^1] == '\r' ? line[..^1] : line;
            if (trimmed.Length == 0)
                continue;
            if (index >= daily.Count)
                break;
            map[daily[index]] = trimmed;
            index++;
        }

        return map;
    }

    private static async Task WriteRejectsAsync(
        string rejectsPath,
        IReadOnlyList<PostingReject> rejects,
        IReadOnlyDictionary<Transaction, string> rawByRecord,
        CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(rejectsPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var sb = new StringBuilder();
        foreach (var reject in rejects)
        {
            var original = rawByRecord.TryGetValue(reject.Transaction, out var raw)
                ? raw
                : string.Empty;
            original = original.Length >= DailyRecordLength
                ? original[..DailyRecordLength]
                : original.PadRight(DailyRecordLength);

            var reason = reject.ReasonCode.ToString("D4", CultureInfo.InvariantCulture);
            var description = reject.ReasonDescription.PadRight(76);
            if (description.Length > 76)
                description = description[..76];

            sb.Append(original).Append(reason).Append(description).Append(Environment.NewLine);
        }

        await File.WriteAllTextAsync(rejectsPath, sb.ToString(), ct).ConfigureAwait(false);
    }

    private string DatabasePath() => SqliteDataSource.Resolve(db.Database.GetConnectionString());
}

/// <summary>
/// Extracts the <c>Data Source</c> file path from a SQLite connection string without
/// depending on the provider's connection-string builder. Returns an absolute path
/// when the data source is a file, or the original string as a fallback.
/// </summary>
public static class SqliteDataSource
{
    public static string Resolve(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return string.Empty;

        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = part[..eq].Trim();
            if (!key.Equals("Data Source", StringComparison.OrdinalIgnoreCase)
                && !key.Equals("DataSource", StringComparison.OrdinalIgnoreCase)
                && !key.Equals("Filename", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = part[(eq + 1)..].Trim().Trim('"');
            if (value.Length == 0
                || value.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
                return value;

            try
            {
                return Path.GetFullPath(value);
            }
            catch (ArgumentException)
            {
                return value;
            }
        }

        return connectionString;
    }
}
