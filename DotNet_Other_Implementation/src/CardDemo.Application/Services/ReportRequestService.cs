using System.Globalization;
using CardDemo.Application.Abstractions;
using CardDemo.Domain.Common;
using CardDemo.Domain.Entities;
using CardDemo.Domain.Services;

namespace CardDemo.Application.Services;

/// <summary>
/// Report request (CORPT00C). Validates that both dates are valid ISO calendar
/// dates and that the range is non-inverted (from &lt;= to), then writes a durable
/// <see cref="PendingReportRequest"/> for the batch job (run-pending-reports) to consume.
/// The legacy screen wrote constructed JCL to an internal reader; the safe target
/// records a typed request instead of executing submitted JCL.
/// </summary>
public sealed class ReportRequestService(ICardDemoStore store, TimeProvider timeProvider) : IReportRequestService
{
    private readonly ICardDemoStore _store = store;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<OperationResult> RequestAsync(string fromDate, string toDate, string requestedByUserId, CancellationToken ct = default)
    {
        var from = (fromDate ?? string.Empty).Trim();
        var to = (toDate ?? string.Empty).Trim();

        if (!FieldValidation.IsValidIsoDate(from))
            return OperationResult.Fail("Start Date - Not a valid date...");

        if (!FieldValidation.IsValidIsoDate(to))
            return OperationResult.Fail("End Date - Not a valid date...");

        var fromValue = DateOnly.ParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toValue = DateOnly.ParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (fromValue > toValue)
            return OperationResult.Fail("Start Date must not be after End Date...");

        var request = new PendingReportRequest
        {
            FromDate = from,
            ToDate = to,
            RequestedByUserId = requestedByUserId ?? string.Empty,
            RequestedAt = _timeProvider.GetUtcNow().ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture),
            Status = "PENDING",
        };

        await _store.AddPendingReportRequestAsync(request, ct);
        await _store.SaveChangesAsync(ct);

        return OperationResult.Ok($"Report requested for {from}..{to} (queued as PENDING).");
    }
}
