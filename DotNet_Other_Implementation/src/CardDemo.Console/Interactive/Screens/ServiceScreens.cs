using System.Globalization;
using CardDemo.Application.Abstractions;

namespace CardDemo.Console.Interactive.Screens;

/// <summary>Bill payment (COBIL00C) and report request (CORPT00C) screens.</summary>
internal sealed class ServiceScreens(
    ConsoleIo io,
    IBillPayService billPay,
    IReportRequestService reports)
{
    private readonly ConsoleIo _io = io;
    private readonly IBillPayService _billPay = billPay;
    private readonly IReportRequestService _reports = reports;

    public async Task BillPaymentAsync(CancellationToken ct)
    {
        _io.Header("Bill Payment (COBIL00C)");
        _io.Line("Pays the full current balance for an account. Type 0 to go back.");
        _io.Line();

        var accountId = _io.PromptRequired("Account id: ");
        if (accountId is null || accountId == ConsoleIo.BackMarker) { return; }

        var confirm = _io.Prompt($"Confirm full balance payment for account {accountId}? (Y/N): ");
        if (confirm is null || confirm == ConsoleIo.BackMarker) { return; }
        if (!string.Equals(confirm, "Y", StringComparison.OrdinalIgnoreCase))
        {
            _io.ShowInfo("Payment cancelled.");
            _io.PressEnter();
            return;
        }

        var result = await _billPay.PayFullBalanceAsync(accountId, ct);
        if (result.Success && result.Value is not null)
        {
            var t = result.Value;
            _io.ShowInfo($"Payment transaction {t.TransactionId} posted for {t.Amount.ToString("N2", CultureInfo.InvariantCulture)}.");
        }
        else
        {
            _io.ShowError(result.Message.Length == 0 ? "Payment could not be processed." : result.Message);
        }
        _io.PressEnter();
    }

    public async Task ReportRequestAsync(string requestedByUserId, CancellationToken ct)
    {
        _io.Header("Report Request (CORPT00C)");
        _io.Line("Select ONE report type. Type 0 at any prompt to go back.");
        _io.Line("  Monthly : enter a month (yyyy-MM) -> first..last day of that month.");
        _io.Line("  Yearly  : enter a year (yyyy)      -> Jan 1..Dec 31 of that year.");
        _io.Line("  Custom  : enter explicit from/to dates (yyyy-MM-dd).");
        _io.Line();

        var monthly = _io.Prompt("Monthly (yyyy-MM, blank to skip): ");
        if (monthly is null || monthly == ConsoleIo.BackMarker) { return; }

        var yearly = _io.Prompt("Yearly (yyyy, blank to skip): ");
        if (yearly is null || yearly == ConsoleIo.BackMarker) { return; }

        var custom = _io.Prompt("Custom (Y to enter from/to, blank to skip): ");
        if (custom is null || custom == ConsoleIo.BackMarker) { return; }

        string fromDate;
        string toDate;

        // CORPT00C priority: Monthly -> Yearly -> Custom.
        if (monthly.Length != 0)
        {
            if (!TryMonthRange(monthly, out fromDate, out toDate))
            {
                _io.ShowError("Monthly - Not a valid month (expected yyyy-MM)...");
                _io.PressEnter();
                return;
            }
        }
        else if (yearly.Length != 0)
        {
            if (!TryYearRange(yearly, out fromDate, out toDate))
            {
                _io.ShowError("Yearly - Not a valid year (expected yyyy)...");
                _io.PressEnter();
                return;
            }
        }
        else if (string.Equals(custom, "Y", StringComparison.OrdinalIgnoreCase))
        {
            var from = _io.PromptRequired("From date (yyyy-MM-dd): ");
            if (from is null || from == ConsoleIo.BackMarker) { return; }

            var to = _io.PromptRequired("To date (yyyy-MM-dd): ");
            if (to is null || to == ConsoleIo.BackMarker) { return; }

            fromDate = from;
            toDate = to;
        }
        else
        {
            _io.ShowError("Select a report type to print report...");
            _io.PressEnter();
            return;
        }

        var result = await _reports.RequestAsync(fromDate, toDate, requestedByUserId, ct);
        _io.ShowResult(result.Success && result.Message.Length == 0
            ? CardDemo.Domain.Common.OperationResult.Ok("Report request queued (Status PENDING).")
            : result);
        _io.PressEnter();
    }

    /// <summary>Monthly selector: yyyy-MM -> first day .. last day of that month.</summary>
    private static bool TryMonthRange(string input, out string fromDate, out string toDate)
    {
        fromDate = string.Empty;
        toDate = string.Empty;
        if (!DateTime.TryParseExact(input.Trim(), "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var month))
        {
            return false;
        }

        var lastDay = DateTime.DaysInMonth(month.Year, month.Month);
        fromDate = new DateTime(month.Year, month.Month, 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        toDate = new DateTime(month.Year, month.Month, lastDay).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>Yearly selector: yyyy -> Jan 1 .. Dec 31 of that year.</summary>
    private static bool TryYearRange(string input, out string fromDate, out string toDate)
    {
        fromDate = string.Empty;
        toDate = string.Empty;
        if (!int.TryParse(input.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            year < 1 || year > 9999)
        {
            return false;
        }

        fromDate = new DateTime(year, 1, 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        toDate = new DateTime(year, 12, 31).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return true;
    }
}
