using System.Globalization;
using System.Text;
using CardDemo.Application.Abstractions;
using CardDemo.Domain.Common;
using CardDemo.Domain.Entities;
using CardDemo.Infrastructure.Batch;
using CardDemo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardDemo.Infrastructure.Optional;

/// <summary>
/// Account-inquiry (COACCT01, service "INQA") and system-date (CODATE01, service "DATE")
/// request/reply workers. The inbound MQ request queue is modelled by the durable
/// <see cref="InquiryRequest"/> table and the reply outbox by <see cref="InquiryReply"/>.
///
/// Account inquiry validates the 11-digit numeric key (COACCT01 requires WS-FUNC="INQA"
/// and a strictly positive numeric WS-KEY) and, on a hit, emits the contiguous labeled
/// account group (WS-ACCT-RESPONSE) with no delimiters between fields; a miss yields the
/// "INVALID REQUEST PARAMETERS" text. The date service ignores the request body and always
/// replies with the current date/time from the injected <see cref="TimeProvider"/>,
/// reproducing the legacy quirk that "SYSTEM TIME : " immediately follows the 10-char date
/// with no separator (CODATE01 STRING ... DELIMITED BY SIZE).
/// </summary>
public sealed class InquiryService(CardDemoDbContext db, TimeProvider timeProvider) : IInquiryService
{
    private const int AccountIdLength = 11;

    public async Task<OperationResult> SubmitAccountInquiryAsync(string accountId, CancellationToken ct = default)
    {
        var key = (accountId ?? string.Empty).Trim();
        if (key.Length != AccountIdLength || !IsAllDigits(key))
            return OperationResult.Fail("Account id must be an 11-digit number.");

        db.InquiryRequests.Add(new InquiryRequest
        {
            Service = "INQA",
            AccountId = key,
            Status = "PENDING",
            CreatedTimestamp = LegacyTimestamp.Now(timeProvider),
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return OperationResult.Ok("Account inquiry queued.");
    }

    public async Task<OperationResult> SubmitDateInquiryAsync(CancellationToken ct = default)
    {
        db.InquiryRequests.Add(new InquiryRequest
        {
            Service = "DATE",
            Status = "PENDING",
            CreatedTimestamp = LegacyTimestamp.Now(timeProvider),
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return OperationResult.Ok("Date inquiry queued.");
    }

    public async Task<InquiryProcessResult> ProcessPendingAsync(string service, int maxMessages, CancellationToken ct = default)
    {
        if (maxMessages <= 0)
            return new InquiryProcessResult(0);

        var svc = (service ?? string.Empty).Trim().ToUpperInvariant();

        var pending = await db.InquiryRequests
            .Where(r => r.Service == svc && r.Status == "PENDING")
            .OrderBy(r => r.Id)
            .Take(maxMessages)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (pending.Count == 0)
            return new InquiryProcessResult(0);

        var processed = 0;
        foreach (var request in pending)
        {
            var payload = svc switch
            {
                "INQA" => await BuildAccountReplyAsync(request.AccountId, ct).ConfigureAwait(false),
                "DATE" => BuildDateReply(),
                _ => $"INVALID REQUEST PARAMETERS FUNCTION : {svc}",
            };

            db.InquiryReplies.Add(new InquiryReply
            {
                RequestId = request.Id,
                Service = request.Service,
                Payload = payload,
                LogicalLength = payload.Length,
                CreatedTimestamp = LegacyTimestamp.Now(timeProvider),
            });
            request.Status = "DONE";
            processed++;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new InquiryProcessResult(processed);
    }

    public async Task<IReadOnlyList<InquiryReply>> RecentRepliesAsync(int take, CancellationToken ct = default)
    {
        if (take <= 0)
            return Array.Empty<InquiryReply>();

        return await db.InquiryReplies
            .AsNoTracking()
            .OrderByDescending(r => r.Id)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Build the labeled account reply (WS-ACCT-RESPONSE) — labels concatenated with values,
    /// no delimiters — or the "INVALID REQUEST PARAMETERS" text on a not-found key.
    /// </summary>
    private async Task<string> BuildAccountReplyAsync(string accountId, CancellationToken ct)
    {
        var account = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AccountId == accountId, ct)
            .ConfigureAwait(false);

        if (account is null)
            return $"INVALID REQUEST PARAMETERS ACCT ID : {accountId}";

        var sb = new StringBuilder();
        sb.Append("ACCOUNT ID : ").Append(account.AccountId);
        sb.Append("ACCOUNT STATUS : ").Append(account.ActiveStatus);
        sb.Append("BALANCE : ").Append(Money(account.CurrentBalance));
        sb.Append("CREDIT LIMIT : ").Append(Money(account.CreditLimit));
        sb.Append("CASH LIMIT : ").Append(Money(account.CashCreditLimit));
        sb.Append("OPEN DATE : ").Append(account.OpenDate);
        sb.Append("EXPR DATE : ").Append(account.ExpirationDate);
        sb.Append("REIS DATE : ").Append(account.ReissueDate);
        sb.Append("CREDIT BAL : ").Append(Money(account.CurrentCycleCredit));
        sb.Append("DEBIT BAL : ").Append(Money(account.CurrentCycleDebit));
        sb.Append("GROUP ID : ").Append(account.GroupId);
        return sb.ToString();
    }

    /// <summary>
    /// System-date reply: "SYSTEM DATE : MM-DD-YYYYSYSTEM TIME : HH:MM:SS". Note the legacy
    /// quirk — no separator between the 10-char date and the "SYSTEM TIME : " label.
    /// </summary>
    private string BuildDateReply()
    {
        var now = timeProvider.GetLocalNow();
        var date = now.ToString("MM-dd-yyyy", CultureInfo.InvariantCulture);
        var time = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return $"SYSTEM DATE : {date}SYSTEM TIME : {time}";
    }

    private static string Money(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    private static bool IsAllDigits(string value)
    {
        foreach (var ch in value)
        {
            if (ch is < '0' or > '9')
                return false;
        }

        return true;
    }
}
