using System.Globalization;
using CardDemo.Application.Abstractions;
using CardDemo.Application.Dtos;
using CardDemo.Domain.Entities;

namespace CardDemo.Console.Interactive.Screens;

/// <summary>
/// Pending-authorization operator screens (COPAUS0C summary / COPAUS1C detail).
/// Lists pending-auth summaries (paged), drills into an account's details
/// (newest first) and toggles the fraud flag on a chosen detail (CPVD PF5).
/// </summary>
internal sealed class AuthorizationScreens(ConsoleIo io, IAuthorizationService svc)
{
    private const int PageSize = 10;
    private readonly ConsoleIo _io = io;
    private readonly IAuthorizationService _svc = svc;

    /// <summary>Summary list with a drill-in to an account's details.</summary>
    public async Task ListSummariesAsync(CancellationToken ct)
    {
        var page = 1;
        while (true)
        {
            var result = await _svc.ListSummariesAsync(page, PageSize, ct);
            if (!result.Success || result.Value is null)
            {
                _io.Header("Pending Authorizations (COPAUS0C)");
                _io.ShowError(result.Message.Length == 0 ? "No pending authorizations available." : result.Message);
                _io.PressEnter();
                return;
            }

            var data = result.Value;
            _io.Header("Pending Authorizations (COPAUS0C)");
            _io.Line($"Page {data.Page}");
            _io.Rule('-');
            _io.Line($"{"Account",-13} {"Appr#",6} {"Decl#",6} {"Credit Bal",16} {"Credit Limit",16}");
            _io.Rule('-');
            if (data.Items.Count == 0)
            {
                _io.Line("(no pending authorizations)");
            }
            foreach (var s in data.Items)
            {
                _io.Line($"{s.AccountId,-13} {s.ApprovedAuthCount,6} {s.DeclinedAuthCount,6} {Money(s.CreditBalance),16} {Money(s.CreditLimit),16}");
            }
            _io.Rule('-');
            _io.Line(Nav(data.HasPrevious, data.HasNext));
            _io.Line("D=Drill into an account's details");

            var choice = _io.MenuChoice();
            if (choice is null || choice == "0" || choice == "X")
            {
                return;
            }
            if (choice == "N" && data.HasNext) { page++; continue; }
            if (choice == "P" && data.HasPrevious) { page--; continue; }
            if (choice == "D")
            {
                await DetailsAsync(ct);
            }
        }
    }

    private async Task DetailsAsync(CancellationToken ct)
    {
        _io.Header("Authorization Details (COPAUS1C)");
        _io.Line("Enter the account id to drill into, or 0 to go back.");
        _io.Line();

        var accountId = _io.PromptRequired("Account id: ");
        if (accountId is null || accountId == ConsoleIo.BackMarker) { return; }

        while (true)
        {
            var result = await _svc.GetDetailsAsync(accountId, ct);
            if (!result.Success || result.Value is null)
            {
                _io.ShowError(result.Message.Length == 0 ? "No details for that account." : result.Message);
                _io.PressEnter();
                return;
            }

            var details = result.Value;
            _io.Header($"Authorization Details — account {accountId} (COPAUS1C)");
            _io.Line("Newest first.");
            _io.Rule('-');
            _io.Line($"{"#",-3} {"Card",-18} {"Amount",14} {"Appr",14} {"Rsp",4} {"Rsn",5} {"Fraud",6} Created");
            _io.Rule('-');
            if (details.Count == 0)
            {
                _io.Line("(no details)");
            }
            var index = 1;
            foreach (var d in details)
            {
                _io.Line($"{index,-3} {d.CardNumber.Trim(),-18} {Money(d.TransactionAmount),14} {Money(d.ApprovedAmount),14} {d.AuthRespCode.Trim(),4} {d.AuthRespReason.Trim(),5} {FraudLabel(d.AuthFraud),6} {d.CreatedTimestamp.Trim()}");
                index++;
            }
            _io.Rule('-');
            _io.Line("F=Toggle fraud on a detail    0=Back");

            var choice = _io.MenuChoice();
            if (choice is null || choice == "0" || choice == "X")
            {
                return;
            }
            if (choice == "F")
            {
                if (details.Count == 0)
                {
                    _io.ShowError("There are no details to flag.");
                    _io.PressEnter();
                    continue;
                }
                await ToggleFraudAsync(details, ct);
            }
        }
    }

    private async Task ToggleFraudAsync(IReadOnlyList<PendingAuthDetail> details, CancellationToken ct)
    {
        var line = _io.PromptInt($"Which detail number (1-{details.Count})? ");
        if (line is null) { return; }
        if (line < 1 || line > details.Count)
        {
            _io.ShowError("That number is out of range.");
            _io.PressEnter();
            return;
        }

        var detail = details[line.Value - 1];
        var currentlyFraud = string.Equals(detail.AuthFraud.Trim(), "F", StringComparison.OrdinalIgnoreCase);
        var setFraud = !currentlyFraud;

        var action = setFraud ? "flag as FRAUD" : "clear fraud (mark REMOVED)";
        var confirm = _io.Prompt($"Confirm: {action} detail #{line} (card {detail.CardNumber.Trim()})? (Y/N): ");
        if (confirm is null || confirm == ConsoleIo.BackMarker) { return; }
        if (!string.Equals(confirm, "Y", StringComparison.OrdinalIgnoreCase))
        {
            _io.ShowInfo("No change made.");
            _io.PressEnter();
            return;
        }

        var result = await _svc.SetFraudAsync(detail.Id, setFraud, ct);
        _io.ShowResult(result);
        _io.PressEnter();
    }

    private static string FraudLabel(string authFraud)
    {
        var value = authFraud.Trim();
        return value.Length == 0 ? "-" : value;
    }

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string Nav(bool hasPrevious, bool hasNext)
    {
        var options = new List<string>();
        if (hasPrevious) { options.Add("P=Prev"); }
        if (hasNext) { options.Add("N=Next"); }
        options.Add("0=Back");
        return string.Join("    ", options);
    }
}
