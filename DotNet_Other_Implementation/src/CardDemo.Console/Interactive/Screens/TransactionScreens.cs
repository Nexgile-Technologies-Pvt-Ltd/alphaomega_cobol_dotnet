using System.Globalization;
using CardDemo.Application.Abstractions;
using CardDemo.Application.Dtos;
using CardDemo.Domain.Entities;

namespace CardDemo.Console.Interactive.Screens;

/// <summary>Transaction list, view and add screens (COTRN00C / COTRN01C / COTRN02C).</summary>
internal sealed class TransactionScreens(ConsoleIo io, ITransactionService transactions, TimeProvider clock)
{
    private const int PageSize = 10;
    private readonly ConsoleIo _io = io;
    private readonly ITransactionService _transactions = transactions;
    private readonly TimeProvider _clock = clock;

    public async Task ListAsync(CancellationToken ct)
    {
        var page = 1;
        while (true)
        {
            var result = await _transactions.ListAsync(page, PageSize, ct);
            if (!result.Success || result.Value is null)
            {
                _io.Header("Transaction List (COTRN00C)");
                _io.ShowError(result.Message.Length == 0 ? "No transactions available." : result.Message);
                _io.PressEnter();
                return;
            }

            var data = result.Value;
            _io.Header("Transaction List (COTRN00C)");
            _io.Line($"Page {data.Page}");
            _io.Rule('-');
            _io.Line($"{"Transaction Id",-18} {"Ty",-3} {"Cat",-5} {"Amount",14}  {"Card",-18} Description");
            _io.Rule('-');
            if (data.Items.Count == 0)
            {
                _io.Line("(no transactions)");
            }
            foreach (var t in data.Items)
            {
                _io.Line($"{t.TransactionId,-18} {t.TypeCode.Trim(),-3} {t.CategoryCode.Trim(),-5} {Money(t.Amount),14}  {t.CardNumber,-18} {Trunc(t.Description, 20)}");
            }
            _io.Rule('-');
            _io.Line(Nav(data));

            var choice = _io.MenuChoice();
            if (choice is null || choice == "0" || choice == "X")
            {
                return;
            }
            if (choice == "N" && data.HasNext) { page++; continue; }
            if (choice == "P" && data.HasPrevious) { page--; continue; }
        }
    }

    public async Task ViewAsync(CancellationToken ct)
    {
        _io.Header("Transaction View (COTRN01C)");
        _io.Line("Enter the 16-char transaction id, or 0 to go back.");
        _io.Line();

        var id = _io.PromptRequired("Transaction id: ");
        if (id is null || id == ConsoleIo.BackMarker) { return; }

        var result = await _transactions.ViewAsync(id, ct);
        if (!result.Success || result.Value is null)
        {
            _io.ShowError(result.Message.Length == 0 ? "Transaction not found." : result.Message);
            _io.PressEnter();
            return;
        }

        RenderTransaction(result.Value);
        _io.PressEnter();
    }

    public async Task AddAsync(CancellationToken ct)
    {
        _io.Header("Transaction Add (COTRN02C)");
        _io.Line("Enter the transaction details. Type 0 at any prompt to cancel.");
        _io.Line("At the first prompt, enter F5 to copy the non-key fields of the last transaction.");
        _io.Line();

        // COTRN02C defaults; F5 (COPY-LAST-TRAN-DATA) overwrites these from the greatest-ID record.
        var defAccountId = string.Empty;
        var defCardNumber = string.Empty;
        var defTypeCode = string.Empty;
        var defCategoryCode = string.Empty;
        var defSource = "POS";
        var defDescription = string.Empty;
        decimal? defAmount = null;
        var defMerchantId = "000000000";
        var defMerchantName = string.Empty;
        var defMerchantCity = string.Empty;
        var defMerchantZip = string.Empty;
        var today = _clock.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var defOriginDate = today;

        var first = _io.Prompt("Account id (F5=copy last): ");
        if (first is null || first == ConsoleIo.BackMarker) { return; }
        if (string.Equals(first, "F5", StringComparison.OrdinalIgnoreCase))
        {
            var prefill = await _transactions.PrefillFromLatestAsync(ct);
            if (prefill.Success && prefill.Value is not null)
            {
                var p = prefill.Value;
                defAccountId = p.AccountId;
                defCardNumber = p.CardNumber;
                defTypeCode = p.TypeCode;
                defCategoryCode = p.CategoryCode;
                defSource = p.Source.Length == 0 ? defSource : p.Source;
                defDescription = p.Description;
                defAmount = p.Amount;
                defMerchantId = p.MerchantId.Length == 0 ? defMerchantId : p.MerchantId;
                defMerchantName = p.MerchantName;
                defMerchantCity = p.MerchantCity;
                defMerchantZip = p.MerchantZip;
                defOriginDate = p.OriginDate.Length == 0 ? defOriginDate : p.OriginDate;
                _io.ShowInfo("Copied last transaction. Press ENTER to keep a value, or type a new one.");
            }
            else
            {
                _io.ShowError(prefill.Message.Length == 0 ? "No transactions to copy from." : prefill.Message);
            }
            first = null;
        }

        var accountId = PromptWithDefault("Account id", defAccountId, first);
        if (accountId is null) { return; }

        var cardNumber = PromptWithDefault("Card number", defCardNumber, null);
        if (cardNumber is null) { return; }

        var typeCode = PromptWithDefault("Type code (2 chars, e.g. 01)", defTypeCode, null);
        if (typeCode is null) { return; }

        var categoryCode = PromptWithDefault("Category code (4 digits, e.g. 0001)", defCategoryCode, null);
        if (categoryCode is null) { return; }

        var source = PromptWithDefault("Source", defSource, null);
        if (source is null) { return; }

        var description = PromptWithDefault("Description", defDescription, null);
        if (description is null) { return; }

        decimal? amount;
        if (defAmount is not null)
        {
            amount = _io.PromptDecimal($"Amount [{Money(defAmount.Value)}]: ", defAmount);
        }
        else
        {
            amount = _io.PromptDecimal("Amount (e.g. 123.45): ");
        }
        if (amount is null) { return; }

        var merchantId = PromptWithDefault("Merchant id", defMerchantId, null);
        if (merchantId is null) { return; }

        var merchantName = PromptWithDefault("Merchant name", defMerchantName, null);
        if (merchantName is null) { return; }

        var merchantCity = PromptWithDefault("Merchant city", defMerchantCity, null);
        if (merchantCity is null) { return; }

        var merchantZip = PromptWithDefault("Merchant ZIP", defMerchantZip, null);
        if (merchantZip is null) { return; }

        var originDate = PromptWithDefault("Origin date (yyyy-MM-dd)", defOriginDate, null);
        if (originDate is null) { return; }

        var request = new TransactionAddRequest(
            AccountId: accountId,
            CardNumber: cardNumber,
            TypeCode: typeCode,
            CategoryCode: categoryCode,
            Source: source,
            Description: description,
            Amount: amount.Value,
            MerchantId: merchantId,
            MerchantName: merchantName,
            MerchantCity: merchantCity,
            MerchantZip: merchantZip,
            OriginDate: originDate);

        var result = await _transactions.AddAsync(request, ct);
        if (result.Success && result.Value is not null)
        {
            _io.ShowInfo($"Transaction {result.Value.TransactionId} added.");
        }
        else
        {
            _io.ShowError(result.Message.Length == 0 ? "Could not add transaction." : result.Message);
        }
        _io.PressEnter();
    }

    /// <summary>
    /// Prompt for a field, offering <paramref name="def"/> as a default (blank ENTER keeps it).
    /// When <paramref name="prefilled"/> is supplied it is used instead of reading a fresh line
    /// (the F5 case, where the first line was already consumed). Returns null on back/EOF.
    /// </summary>
    private string? PromptWithDefault(string label, string def, string? prefilled)
    {
        if (prefilled is not null)
        {
            return prefilled.Length == 0 ? def : prefilled;
        }

        var suffix = def.Length == 0 ? ": " : $" [{def}]: ";
        var value = _io.Prompt(label + suffix);
        if (value is null || value == ConsoleIo.BackMarker) { return null; }
        return value.Length == 0 ? def : value;
    }

    private void RenderTransaction(Transaction t)
    {
        _io.Line();
        _io.Rule('-');
        _io.Line($"Transaction id : {t.TransactionId}");
        _io.Line($"Type / category: {t.TypeCode.Trim()} / {t.CategoryCode.Trim()}");
        _io.Line($"Amount         : {Money(t.Amount)}");
        _io.Line($"Card number    : {t.CardNumber}");
        _io.Line($"Source         : {t.Source.Trim()}");
        _io.Line($"Description    : {t.Description.TrimEnd()}");
        _io.Line($"Merchant       : {t.MerchantId.Trim()} {t.MerchantName.TrimEnd()}");
        _io.Line($"Merchant loc   : {t.MerchantCity.TrimEnd()} {t.MerchantZip.Trim()}".TrimEnd());
        _io.Line($"Origin ts      : {t.OriginTimestamp.Trim()}");
        _io.Line($"Process ts     : {t.ProcessTimestamp.Trim()}");
        _io.Rule('-');
    }

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string Trunc(string value, int width)
    {
        var text = value.TrimEnd();
        return text.Length > width ? text[..width] : text;
    }

    private static string Nav(PagedResult<Transaction> page)
    {
        var options = new List<string>();
        if (page.HasPrevious) { options.Add("P=Prev"); }
        if (page.HasNext) { options.Add("N=Next"); }
        options.Add("0=Back");
        return string.Join("    ", options);
    }
}
