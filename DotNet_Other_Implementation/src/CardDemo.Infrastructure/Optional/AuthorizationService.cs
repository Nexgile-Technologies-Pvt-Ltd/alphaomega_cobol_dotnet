using System.Globalization;
using CardDemo.Application.Abstractions;
using CardDemo.Application.Dtos;
using CardDemo.Domain.Common;
using CardDemo.Domain.Entities;
using CardDemo.Infrastructure.Batch;
using CardDemo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardDemo.Infrastructure.Optional;

/// <summary>
/// Pending-authorization module (COPAUA0C / COPAUS0C / COPAUS1C / CBPAUP0C) modelled
/// relationally. Requests are decisioned against available credit, recorded as a
/// summary + detail + reply, tagged for fraud, and purged by age. Time comes from the
/// injected <see cref="TimeProvider"/>; each processed request is wrapped in a
/// transaction so the summary, detail and reply commit atomically.
/// </summary>
public sealed class AuthorizationService(CardDemoDbContext db, TimeProvider timeProvider) : IAuthorizationService
{
    // Reverse-encoded key anchor: newest (largest ticks) yields the smallest key, so
    // ordering by AuthKey ascending returns newest-first (mirrors the IMS reverse key).
    private const long KeyAnchor = long.MaxValue;

    private const string RespApproved = "00";
    private const string RespDeclined = "05";
    private const string ReasonApproved = "0000";
    private const string ReasonInvalidCard = "3100";
    private const string ReasonInsufficientFunds = "1100";

    public async Task<OperationResult<AuthorizationRequest>> SubmitAsync(AuthorizationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.CardNumber))
            return OperationResult<AuthorizationRequest>.Fail("Card number is required.");

        request.Status = "PENDING";
        request.CreatedTimestamp = LegacyTimestamp.Now(timeProvider);

        await db.AuthorizationRequests.AddAsync(request, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return OperationResult<AuthorizationRequest>.Ok(request);
    }

    public async Task<AuthProcessResult> ProcessPendingAsync(int maxMessages, CancellationToken ct = default)
    {
        if (maxMessages <= 0)
            return new AuthProcessResult(0, 0, 0);

        var pending = await db.AuthorizationRequests
            .Where(r => r.Status == "PENDING")
            .OrderBy(r => r.Id)
            .Take(maxMessages)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var processed = 0;
        var approved = 0;
        var declined = 0;
        var counter = 0;

        foreach (var request in pending)
        {
            var wasApproved = await ProcessOneAsync(request, counter++, ct).ConfigureAwait(false);
            processed++;
            if (wasApproved)
                approved++;
            else
                declined++;
        }

        return new AuthProcessResult(processed, approved, declined);
    }

    private async Task<bool> ProcessOneAsync(AuthorizationRequest request, int sequence, CancellationToken ct)
    {
        var approved = false;

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            approved = await DecideAsync(request, sequence, ct).ConfigureAwait(false);

            request.Status = "PROCESSED";

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return approved;
    }

    private async Task<bool> DecideAsync(AuthorizationRequest request, int sequence, CancellationToken ct)
    {
        var now = timeProvider.GetLocalNow();
        var createdTimestamp = LegacyTimestamp.Format26(now);
        var authKey = BuildAuthKey(now, sequence);

        var xref = await db.CardXrefs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CardNumber == request.CardNumber, ct)
            .ConfigureAwait(false);

        // Card not on file: decline with invalid-card, still record a detail + reply.
        if (xref is null)
        {
            RecordDecision(
                request, accountId: string.Empty, authKey, createdTimestamp,
                respCode: RespDeclined, reason: ReasonInvalidCard, approvedAmount: 0m);
            return false;
        }

        var summary = await EnsureSummaryAsync(xref.AccountId, xref.CustomerId, ct).ConfigureAwait(false);
        var available = summary.CreditLimit - summary.CreditBalance;

        if (request.TransactionAmount > 0m && request.TransactionAmount <= available)
        {
            summary.CreditBalance += request.TransactionAmount;
            summary.ApprovedAuthCount++;
            summary.ApprovedAuthAmount += request.TransactionAmount;

            RecordDecision(
                request, xref.AccountId, authKey, createdTimestamp,
                respCode: RespApproved, reason: ReasonApproved, approvedAmount: request.TransactionAmount);
            return true;
        }

        summary.DeclinedAuthCount++;
        summary.DeclinedAuthAmount += request.TransactionAmount;

        RecordDecision(
            request, xref.AccountId, authKey, createdTimestamp,
            respCode: RespDeclined, reason: ReasonInsufficientFunds, approvedAmount: 0m);
        return false;
    }

    private async Task<PendingAuthSummary> EnsureSummaryAsync(string accountId, string customerId, CancellationToken ct)
    {
        var summary = await db.PendingAuthSummaries
            .FirstOrDefaultAsync(s => s.AccountId == accountId, ct)
            .ConfigureAwait(false);

        if (summary is not null)
            return summary;

        var account = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AccountId == accountId, ct)
            .ConfigureAwait(false);

        summary = new PendingAuthSummary
        {
            AccountId = accountId,
            CustomerId = customerId,
            CreditLimit = account?.CreditLimit ?? 0m,
            CashLimit = account?.CashCreditLimit ?? 0m,
            CreditBalance = 0m,
            CashBalance = 0m,
            ApprovedAuthCount = 0,
            DeclinedAuthCount = 0,
            ApprovedAuthAmount = 0m,
            DeclinedAuthAmount = 0m,
        };

        await db.PendingAuthSummaries.AddAsync(summary, ct).ConfigureAwait(false);
        return summary;
    }

    private void RecordDecision(
        AuthorizationRequest request,
        string accountId,
        string authKey,
        string createdTimestamp,
        string respCode,
        string reason,
        decimal approvedAmount)
    {
        var detail = new PendingAuthDetail
        {
            AccountId = accountId,
            AuthKey = authKey,
            CardNumber = request.CardNumber,
            AuthType = request.AuthType,
            TransactionAmount = request.TransactionAmount,
            ApprovedAmount = approvedAmount,
            AuthRespCode = respCode,
            AuthRespReason = reason,
            MatchStatus = "P",
            AuthFraud = " ",
            FraudReportDate = string.Empty,
            ProcessingCode = request.ProcessingCode,
            MerchantId = request.MerchantId,
            MerchantName = request.MerchantName,
            MerchantCity = request.MerchantCity,
            MerchantState = request.MerchantState,
            MerchantZip = request.MerchantZip,
            OrigDate = request.OrigDate,
            OrigTime = request.OrigTime,
            CreatedTimestamp = createdTimestamp,
        };
        db.PendingAuthDetails.Add(detail);

        var payload = string.Join(',',
            request.CardNumber.Trim(),
            request.TransactionAmount.ToString("F2", CultureInfo.InvariantCulture),
            respCode,
            reason,
            approvedAmount.ToString("F2", CultureInfo.InvariantCulture));

        var reply = new AuthorizationReply
        {
            RequestId = request.Id,
            CardNumber = request.CardNumber,
            AuthRespCode = respCode,
            AuthRespReason = reason,
            ApprovedAmount = approvedAmount,
            Payload = payload,
            CreatedTimestamp = createdTimestamp,
        };
        db.AuthorizationReplies.Add(reply);
    }

    public async Task<OperationResult<PagedResult<PendingAuthSummary>>> ListSummariesAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var safePage = page < 1 ? 1 : page;
        var safeSize = pageSize < 1 ? 10 : pageSize;
        var skip = (safePage - 1) * safeSize;

        var rows = await db.PendingAuthSummaries
            .AsNoTracking()
            .OrderBy(s => s.AccountId)
            .Skip(skip)
            .Take(safeSize + 1)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var hasNext = rows.Count > safeSize;
        var items = hasNext ? rows.Take(safeSize).ToList() : rows;

        var result = new PagedResult<PendingAuthSummary>(items, safePage, safeSize, hasNext, safePage > 1);
        return OperationResult<PagedResult<PendingAuthSummary>>.Ok(result);
    }

    public async Task<OperationResult<IReadOnlyList<PendingAuthDetail>>> GetDetailsAsync(string accountId, CancellationToken ct = default)
    {
        // AuthKey is reverse-encoded: ascending AuthKey order returns newest first.
        var details = await db.PendingAuthDetails
            .AsNoTracking()
            .Where(d => d.AccountId == accountId)
            .OrderBy(d => d.AuthKey)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return OperationResult<IReadOnlyList<PendingAuthDetail>>.Ok(details);
    }

    public async Task<OperationResult> SetFraudAsync(int detailId, bool fraud, CancellationToken ct = default)
    {
        var detail = await db.PendingAuthDetails
            .FirstOrDefaultAsync(d => d.Id == detailId, ct)
            .ConfigureAwait(false);

        if (detail is null)
            return OperationResult.Fail("Authorization detail not found.");

        var reportDate = timeProvider.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        detail.AuthFraud = fraud ? "F" : "R";
        detail.FraudReportDate = reportDate;

        var customerId = await db.CardXrefs
            .AsNoTracking()
            .Where(x => x.CardNumber == detail.CardNumber)
            .Select(x => x.CustomerId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var history = new AuthFraudHistory
        {
            CardNumber = detail.CardNumber,
            AuthTimestamp = detail.CreatedTimestamp,
            AuthType = detail.AuthType,
            AuthRespCode = detail.AuthRespCode,
            TransactionAmount = detail.TransactionAmount,
            ApprovedAmount = detail.ApprovedAmount,
            MatchStatus = detail.MatchStatus,
            AuthFraud = detail.AuthFraud,
            FraudReportDate = reportDate,
            AccountId = detail.AccountId,
            CustomerId = customerId ?? string.Empty,
        };

        await db.AuthFraudHistories.AddAsync(history, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return OperationResult.Ok("Fraud flag updated.");
    }

    public async Task<int> PurgeExpiredAsync(int days, CancellationToken ct = default)
    {
        var cutoff = timeProvider.GetLocalNow().Date.AddDays(-days);

        var details = await db.PendingAuthDetails
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var expired = details
            .Where(d => DetailDate(d) is { } created && created <= cutoff)
            .ToList();

        if (expired.Count == 0)
            return 0;

        db.PendingAuthDetails.RemoveRange(expired);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Delete summaries that have no remaining details.
        var remainingAccounts = await db.PendingAuthDetails
            .AsNoTracking()
            .Select(d => d.AccountId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var remaining = new HashSet<string>(remainingAccounts, StringComparer.Ordinal);

        var summaries = await db.PendingAuthSummaries
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var orphaned = summaries
            .Where(s => !remaining.Contains(s.AccountId))
            .ToList();

        if (orphaned.Count > 0)
        {
            db.PendingAuthSummaries.RemoveRange(orphaned);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return expired.Count;
    }

    public async Task<int> UnloadAsync(string outputPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        // Read the summaries (root) and details (child) in a stable order so the export
        // is reproducible: summaries by account, details by account then reverse key.
        var summaries = await db.PendingAuthSummaries
            .AsNoTracking()
            .OrderBy(s => s.AccountId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var details = await db.PendingAuthDetails
            .AsNoTracking()
            .OrderBy(d => d.AccountId)
            .ThenBy(d => d.AuthKey)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var lines = new List<string>(summaries.Count + details.Count);

        // PAUDBUNL writes the root summary segment ahead of its child details.
        foreach (var s in summaries)
        {
            lines.Add(Join(
                "S",
                s.AccountId,
                s.CustomerId,
                Money(s.CreditLimit),
                Money(s.CashLimit),
                Money(s.CreditBalance),
                Money(s.CashBalance),
                Int(s.ApprovedAuthCount),
                Int(s.DeclinedAuthCount),
                Money(s.ApprovedAuthAmount),
                Money(s.DeclinedAuthAmount)));
        }

        foreach (var d in details)
        {
            lines.Add(Join(
                "D",
                d.AccountId,
                d.AuthKey,
                d.CardNumber,
                d.AuthType,
                d.CardExpiryDate,
                Money(d.TransactionAmount),
                Money(d.ApprovedAmount),
                d.AuthRespCode,
                d.AuthRespReason,
                d.MatchStatus,
                d.AuthFraud,
                d.FraudReportDate,
                d.ProcessingCode,
                d.MerchantId,
                d.MerchantName,
                d.MerchantCity,
                d.MerchantState,
                d.MerchantZip,
                d.TransactionId,
                d.OrigDate,
                d.OrigTime,
                d.CreatedTimestamp));
        }

        await File.WriteAllLinesAsync(outputPath, lines, ct).ConfigureAwait(false);

        return summaries.Count + details.Count;
    }

    public async Task<int> LoadAsync(string inputPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(inputPath);

        var lines = await File.ReadAllLinesAsync(inputPath, ct).ConfigureAwait(false);

        var read = 0;

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            read = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line))
                    continue;

                var fields = Split(line);
                switch (fields[0])
                {
                    case "S":
                        await UpsertSummaryAsync(fields, ct).ConfigureAwait(false);
                        read++;
                        break;
                    case "D":
                        await UpsertDetailAsync(fields, ct).ConfigureAwait(false);
                        read++;
                        break;
                    default:
                        // Unknown tag: skip (not counted).
                        break;
                }
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return read;
    }

    private async Task UpsertSummaryAsync(string[] f, CancellationToken ct)
    {
        var accountId = f[1];

        var summary = await db.PendingAuthSummaries
            .FirstOrDefaultAsync(s => s.AccountId == accountId, ct)
            .ConfigureAwait(false);

        if (summary is null)
        {
            summary = new PendingAuthSummary { AccountId = accountId };
            await db.PendingAuthSummaries.AddAsync(summary, ct).ConfigureAwait(false);
        }

        summary.CustomerId = f[2];
        summary.CreditLimit = ParseMoney(f[3]);
        summary.CashLimit = ParseMoney(f[4]);
        summary.CreditBalance = ParseMoney(f[5]);
        summary.CashBalance = ParseMoney(f[6]);
        summary.ApprovedAuthCount = ParseInt(f[7]);
        summary.DeclinedAuthCount = ParseInt(f[8]);
        summary.ApprovedAuthAmount = ParseMoney(f[9]);
        summary.DeclinedAuthAmount = ParseMoney(f[10]);
    }

    private async Task UpsertDetailAsync(string[] f, CancellationToken ct)
    {
        var accountId = f[1];
        var authKey = f[2];

        var detail = await db.PendingAuthDetails
            .FirstOrDefaultAsync(d => d.AccountId == accountId && d.AuthKey == authKey, ct)
            .ConfigureAwait(false);

        if (detail is null)
        {
            detail = new PendingAuthDetail { AccountId = accountId, AuthKey = authKey };
            db.PendingAuthDetails.Add(detail);
        }

        detail.CardNumber = f[3];
        detail.AuthType = f[4];
        detail.CardExpiryDate = f[5];
        detail.TransactionAmount = ParseMoney(f[6]);
        detail.ApprovedAmount = ParseMoney(f[7]);
        detail.AuthRespCode = f[8];
        detail.AuthRespReason = f[9];
        detail.MatchStatus = f[10];
        detail.AuthFraud = f[11];
        detail.FraudReportDate = f[12];
        detail.ProcessingCode = f[13];
        detail.MerchantId = f[14];
        detail.MerchantName = f[15];
        detail.MerchantCity = f[16];
        detail.MerchantState = f[17];
        detail.MerchantZip = f[18];
        detail.TransactionId = f[19];
        detail.OrigDate = f[20];
        detail.OrigTime = f[21];
        detail.CreatedTimestamp = f[22];
    }

    private static string Money(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(string text) =>
        decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static int ParseInt(string text) =>
        int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);

    // Records are '|'-delimited with a leading type tag. Escape the delimiter, the escape
    // character itself and any control chars so free-text merchant fields round-trip exactly.
    private static string Join(params string[] fields)
    {
        var encoded = new string[fields.Length];
        for (var i = 0; i < fields.Length; i++)
            encoded[i] = Encode(fields[i]);
        return string.Join('|', encoded);
    }

    private static string[] Split(string line)
    {
        var parts = line.Split('|');
        for (var i = 0; i < parts.Length; i++)
            parts[i] = Decode(parts[i]);
        return parts;
    }

    private static string Encode(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("|", "\\p", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);

    private static string Decode(string value)
    {
        if (value.IndexOf('\\', StringComparison.Ordinal) < 0)
            return value;

        var sb = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                sb.Append(next switch
                {
                    '\\' => '\\',
                    'p' => '|',
                    'r' => '\r',
                    'n' => '\n',
                    _ => next,
                });
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Build a descending-sortable key from the instant plus a per-run sequence, so that
    /// newer authorizations sort ahead of older ones when ordering by AuthKey ascending.
    /// </summary>
    private static string BuildAuthKey(DateTimeOffset instant, int sequence)
    {
        // Later instants (and, within the same instant, later sequence numbers) must
        // produce smaller keys. Blend a per-run sequence into the low digits so two
        // requests decided in the same tick still order deterministically newest-first.
        var reversed = KeyAnchor - instant.UtcTicks;
        var seqSlot = 999 - Math.Clamp(sequence, 0, 999);
        return reversed.ToString("D19", CultureInfo.InvariantCulture)
            + seqSlot.ToString("D3", CultureInfo.InvariantCulture);
    }

    private static DateTime? DetailDate(PendingAuthDetail detail)
    {
        var text = detail.CreatedTimestamp;
        if (string.IsNullOrWhiteSpace(text) || text.Length < 10)
            return null;

        return DateTime.TryParseExact(
            text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}
