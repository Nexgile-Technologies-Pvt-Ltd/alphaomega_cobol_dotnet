using CardDemo.Application.Abstractions;
using CardDemo.Application.Dtos;
using CardDemo.Domain.Entities;

namespace CardDemo.Console.Interactive.Screens;

/// <summary>Card list, view and update screens (COCRDLIC / COCRDSLC / COCRDUPC).</summary>
internal sealed class CardScreens(ConsoleIo io, ICardService cards)
{
    private const int PageSize = 10;
    private readonly ConsoleIo _io = io;
    private readonly ICardService _cards = cards;

    public async Task ListAsync(CancellationToken ct)
    {
        _io.Header("Card List (COCRDLIC)");
        _io.Line("Optional filters. Press ENTER to skip a filter, 0 to go back.");
        _io.Line();

        var accountId = _io.Prompt("Filter by account id (optional): ");
        if (accountId == ConsoleIo.BackMarker || accountId is null)
        {
            return;
        }
        var cardNumber = _io.Prompt("Filter by card number (optional): ");
        if (cardNumber == ConsoleIo.BackMarker || cardNumber is null)
        {
            return;
        }

        var acct = accountId.Length == 0 ? null : accountId;
        var card = cardNumber.Length == 0 ? null : cardNumber;

        var page = 1;
        while (true)
        {
            var result = await _cards.ListAsync(acct, card, page, PageSize, ct);
            if (!result.Success || result.Value is null)
            {
                _io.ShowError(result.Message.Length == 0 ? "No cards available." : result.Message);
                _io.PressEnter();
                return;
            }

            var pageData = result.Value;
            _io.Header("Card List (COCRDLIC)");
            _io.Line($"Page {pageData.Page}    Filters: account={Show(acct)} card={Show(card)}");
            _io.Rule('-');
            _io.Line($"{"Card Number",-18} {"Account",-13} {"St",-3} {"Expires",-11} Name");
            _io.Rule('-');
            if (pageData.Items.Count == 0)
            {
                _io.Line("(no cards match)");
            }
            foreach (var c in pageData.Items)
            {
                _io.Line($"{c.CardNumber,-18} {c.AccountId,-13} {c.ActiveStatus.Trim(),-3} {c.ExpirationDate.Trim(),-11} {c.EmbossedName.TrimEnd()}");
            }
            _io.Rule('-');
            _io.Line(Nav(pageData));

            var choice = _io.MenuChoice();
            if (choice is null || choice == "0" || choice == "X")
            {
                return;
            }
            if (choice == "N" && pageData.HasNext) { page++; continue; }
            if (choice == "P" && pageData.HasPrevious) { page--; continue; }
            // Any other key just refreshes the current page.
        }
    }

    public async Task ViewAsync(CancellationToken ct)
    {
        _io.Header("Card View (COCRDSLC)");
        _io.Line("Enter account id and card number, or 0 to go back.");
        _io.Line();

        var accountId = _io.PromptRequired("Account id: ");
        if (accountId is null || accountId == ConsoleIo.BackMarker) { return; }
        var cardNumber = _io.PromptRequired("Card number: ");
        if (cardNumber is null || cardNumber == ConsoleIo.BackMarker) { return; }

        var result = await _cards.ViewAsync(accountId, cardNumber, ct);
        if (!result.Success || result.Value is null)
        {
            _io.ShowError(result.Message.Length == 0 ? "Card not found." : result.Message);
            _io.PressEnter();
            return;
        }

        RenderCard(result.Value);
        _io.PressEnter();
    }

    public async Task UpdateAsync(CancellationToken ct)
    {
        _io.Header("Card Update (COCRDUPC)");
        _io.Line("Enter account id and card number to load, or 0 to go back.");
        _io.Line();

        var accountId = _io.PromptRequired("Account id: ");
        if (accountId is null || accountId == ConsoleIo.BackMarker) { return; }
        var cardNumber = _io.PromptRequired("Card number: ");
        if (cardNumber is null || cardNumber == ConsoleIo.BackMarker) { return; }

        var loaded = await _cards.ViewAsync(accountId, cardNumber, ct);
        if (!loaded.Success || loaded.Value is null)
        {
            _io.ShowError(loaded.Message.Length == 0 ? "Card not found." : loaded.Message);
            _io.PressEnter();
            return;
        }

        var card = loaded.Value.Card;
        RenderCard(loaded.Value);
        _io.Line();
        _io.Line("Press ENTER to keep the current value. Type 0 to cancel.");
        _io.Line();

        var name = _io.Prompt($"Embossed name [{card.EmbossedName.TrimEnd()}]: ");
        if (name is null || name == ConsoleIo.BackMarker) { return; }
        if (name.Length == 0) { name = card.EmbossedName; }

        var status = _io.Prompt($"Active status [{card.ActiveStatus.Trim()}]: ");
        if (status is null || status == ConsoleIo.BackMarker) { return; }
        if (status.Length == 0) { status = card.ActiveStatus; }

        var (curMonth, curYear) = SplitExpiry(card.ExpirationDate);

        var month = _io.PromptInt($"Expiration month (1-12) [{curMonth}]: ", curMonth);
        if (month is null) { return; }
        var year = _io.PromptInt($"Expiration year [{curYear}]: ", curYear);
        if (year is null) { return; }

        var request = new CardUpdateRequest(
            CardNumber: card.CardNumber,
            EmbossedName: name,
            ActiveStatus: status,
            ExpirationMonth: month.Value,
            ExpirationYear: year.Value,
            CardRowVersion: card.RowVersion);

        var result = await _cards.UpdateAsync(request, ct);
        _io.ShowResult(result);
        _io.PressEnter();
    }

    private void RenderCard(CardWithAccount view)
    {
        var c = view.Card;
        _io.Line();
        _io.Rule('-');
        _io.Line($"Card number  : {c.CardNumber}");
        _io.Line($"Account id   : {c.AccountId}");
        _io.Line($"Embossed name: {c.EmbossedName.TrimEnd()}");
        _io.Line($"Active status: {c.ActiveStatus.Trim()}");
        _io.Line($"Expires      : {c.ExpirationDate.Trim()}");
        _io.Line($"CVV          : {c.Cvv.Trim()}");
        if (view.Account is not null)
        {
            _io.Rule('-');
            _io.Line($"Acct status  : {view.Account.ActiveStatus.Trim()}");
            _io.Line($"Acct balance : {view.Account.CurrentBalance.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)}");
        }
        _io.Rule('-');
    }

    private static (int Month, int Year) SplitExpiry(string expiration)
    {
        // Expiration text is yyyy-MM-dd (or blank). Extract MM and yyyy best-effort.
        var text = expiration.Trim();
        var year = 0;
        var month = 0;
        var parts = text.Split('-');
        if (parts.Length >= 2)
        {
            _ = int.TryParse(parts[0], out year);
            _ = int.TryParse(parts[1], out month);
        }
        return (month, year);
    }

    private static string Nav(PagedResult<Card> page)
    {
        var options = new List<string>();
        if (page.HasPrevious) { options.Add("P=Prev"); }
        if (page.HasNext) { options.Add("N=Next"); }
        options.Add("0=Back");
        return string.Join("    ", options);
    }

    private static string Show(string? value) => value is null || value.Length == 0 ? "(any)" : value;
}
