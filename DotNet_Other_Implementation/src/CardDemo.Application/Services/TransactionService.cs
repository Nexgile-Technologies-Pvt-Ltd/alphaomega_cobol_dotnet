using System.Globalization;
using CardDemo.Application.Abstractions;
using CardDemo.Application.Dtos;
using CardDemo.Domain.Common;
using CardDemo.Domain.Entities;
using CardDemo.Domain.Services;

namespace CardDemo.Application.Services;

/// <summary>
/// Transaction list, view and add (COTRN00C / COTRN01C / COTRN02C). Add validates
/// the account/card key fields then the data fields in COTRN02C order, and assigns
/// the next transaction id as (max existing numeric id + 1) formatted to 16 chars.
/// </summary>
public sealed class TransactionService(ICardDemoStore store, TimeProvider timeProvider) : ITransactionService
{
    private readonly ICardDemoStore _store = store;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<OperationResult<PagedResult<Transaction>>> ListAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var safePage = page < 1 ? 1 : page;
        var safeSize = pageSize < 1 ? 10 : pageSize;

        var skip = (safePage - 1) * safeSize;
        var rows = await _store.ListTransactionsAsync(skip, safeSize + 1, ct).ConfigureAwait(false);

        var hasNext = rows.Count > safeSize;
        var items = hasNext ? rows.Take(safeSize).ToList() : rows.ToList();

        var result = new PagedResult<Transaction>(items, safePage, safeSize, hasNext, safePage > 1);
        return OperationResult<PagedResult<Transaction>>.Ok(result);
    }

    public async Task<OperationResult<Transaction>> ViewAsync(string transactionId, CancellationToken ct = default)
    {
        var id = (transactionId ?? string.Empty).Trim();
        if (FieldValidation.IsBlank(id))
            return OperationResult<Transaction>.Fail("Tran ID can NOT be empty...");

        var tran = await _store.FindTransactionAsync(id, ct).ConfigureAwait(false);
        if (tran is null)
            return OperationResult<Transaction>.Fail("Transaction ID NOT found...");

        return OperationResult<Transaction>.Ok(tran);
    }

    public async Task<OperationResult<Transaction>> AddAsync(TransactionAddRequest request, CancellationToken ct = default)
    {
        var acct = (request.AccountId ?? string.Empty).Trim();
        var card = (request.CardNumber ?? string.Empty).Trim();

        // --- Key fields (VALIDATE-INPUT-KEY-FIELDS): account or card must resolve a xref. ---
        if (FieldValidation.IsBlank(acct) && FieldValidation.IsBlank(card))
            return OperationResult<Transaction>.Fail("Account or Card Number must be entered...");

        if (!FieldValidation.IsBlank(acct) && !FieldValidation.IsAllDigits(acct))
            return OperationResult<Transaction>.Fail("Account ID must be Numeric...");

        if (!FieldValidation.IsBlank(card) && !FieldValidation.IsAllDigits(card))
            return OperationResult<Transaction>.Fail("Card Number must be Numeric...");

        CardXref? xref;
        if (!FieldValidation.IsBlank(acct))
        {
            xref = await _store.XrefByAccountAsync(acct, ct).ConfigureAwait(false);
            if (xref is null)
                return OperationResult<Transaction>.Fail("Account or Card Number must be entered...");
            if (FieldValidation.IsBlank(card))
                card = xref.CardNumber;
        }
        else
        {
            xref = await _store.XrefByCardAsync(card, ct).ConfigureAwait(false);
            if (xref is null)
                return OperationResult<Transaction>.Fail("Account or Card Number must be entered...");
            acct = xref.AccountId;
        }

        // Confirm the resolved account and card actually exist.
        var account = await _store.FindAccountAsync(acct, ct).ConfigureAwait(false);
        if (account is null)
            return OperationResult<Transaction>.Fail("Account not found.");

        var cardEntity = await _store.FindCardAsync(card, ct).ConfigureAwait(false);
        if (cardEntity is null)
            return OperationResult<Transaction>.Fail("Card not found.");

        // --- Data fields (VALIDATE-INPUT-DATA-FIELDS), in COTRN02C order. ---
        if (FieldValidation.IsBlank(request.TypeCode))
            return OperationResult<Transaction>.Fail("Type CD can NOT be empty...");
        if (FieldValidation.IsBlank(request.CategoryCode))
            return OperationResult<Transaction>.Fail("Category CD can NOT be empty...");
        if (FieldValidation.IsBlank(request.Source))
            return OperationResult<Transaction>.Fail("Source can NOT be empty...");
        if (FieldValidation.IsBlank(request.Description))
            return OperationResult<Transaction>.Fail("Description can NOT be empty...");
        if (FieldValidation.IsBlank(request.OriginDate))
            return OperationResult<Transaction>.Fail("Orig Date can NOT be empty...");
        if (FieldValidation.IsBlank(request.MerchantId))
            return OperationResult<Transaction>.Fail("Merchant ID can NOT be empty...");
        if (FieldValidation.IsBlank(request.MerchantName))
            return OperationResult<Transaction>.Fail("Merchant Name can NOT be empty...");
        if (FieldValidation.IsBlank(request.MerchantCity))
            return OperationResult<Transaction>.Fail("Merchant City can NOT be empty...");
        if (FieldValidation.IsBlank(request.MerchantZip))
            return OperationResult<Transaction>.Fail("Merchant Zip can NOT be empty...");

        if (!FieldValidation.IsAllDigits(request.TypeCode.Trim()))
            return OperationResult<Transaction>.Fail("Type CD must be Numeric...");
        if (!FieldValidation.IsAllDigits(request.CategoryCode.Trim()))
            return OperationResult<Transaction>.Fail("Category CD must be Numeric...");

        if (!FieldValidation.IsValidIsoDate(request.OriginDate.Trim()))
            return OperationResult<Transaction>.Fail("Orig Date - Not a valid date...");

        if (!FieldValidation.IsAllDigits(request.MerchantId.Trim()))
            return OperationResult<Transaction>.Fail("Merchant ID must be Numeric...");

        // --- Allocate next id = max numeric id + 1, formatted to 16 chars. ---
        var maxId = await _store.MaxTransactionIdAsync(ct).ConfigureAwait(false);
        long next = 1;
        if (!string.IsNullOrWhiteSpace(maxId) &&
            long.TryParse(maxId.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var maxNum))
        {
            next = maxNum + 1;
        }
        var newId = next.ToString(CultureInfo.InvariantCulture).PadLeft(16, '0');

        var timestamp = FormatTimestamp(_timeProvider.GetUtcNow());

        var transaction = new Transaction
        {
            TransactionId = newId,
            TypeCode = request.TypeCode.Trim(),
            CategoryCode = request.CategoryCode.Trim(),
            Source = request.Source ?? string.Empty,
            Description = request.Description ?? string.Empty,
            Amount = MoneyMath.Truncate2(request.Amount),
            MerchantId = request.MerchantId.Trim(),
            MerchantName = request.MerchantName ?? string.Empty,
            MerchantCity = request.MerchantCity ?? string.Empty,
            MerchantZip = request.MerchantZip ?? string.Empty,
            CardNumber = card,
            OriginTimestamp = string.IsNullOrWhiteSpace(request.OriginDate) ? timestamp : request.OriginDate.Trim(),
            ProcessTimestamp = timestamp,
        };

        await _store.AddTransactionAsync(transaction, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);

        return OperationResult<Transaction>.Ok(transaction, "Transaction added successfully.");
    }

    public async Task<OperationResult<TransactionAddRequest>> PrefillFromLatestAsync(CancellationToken ct = default)
    {
        // COTRN02C COPY-LAST-TRAN-DATA: STARTBR HIGH-VALUES + READPREV reads the
        // greatest-transaction-ID record and copies its non-key fields to the entry form.
        var maxId = await _store.MaxTransactionIdAsync(ct).ConfigureAwait(false);
        Transaction? latest = null;
        if (!string.IsNullOrWhiteSpace(maxId))
        {
            latest = await _store.FindTransactionAsync(maxId.Trim(), ct).ConfigureAwait(false);
        }

        if (latest is null)
            return OperationResult<TransactionAddRequest>.Fail("No transactions to copy from.");

        // Resolve the account from the card number via the xref (READ-CCXREF-FILE).
        var card = latest.CardNumber.Trim();
        var xref = await _store.XrefByCardAsync(card, ct).ConfigureAwait(false);
        var acct = xref?.AccountId ?? string.Empty;

        var request = new TransactionAddRequest(
            AccountId: acct,
            CardNumber: card,
            TypeCode: latest.TypeCode.Trim(),
            CategoryCode: latest.CategoryCode.Trim(),
            Source: latest.Source.Trim(),
            Description: latest.Description.TrimEnd(),
            Amount: latest.Amount,
            MerchantId: latest.MerchantId.Trim(),
            MerchantName: latest.MerchantName.TrimEnd(),
            MerchantCity: latest.MerchantCity.TrimEnd(),
            MerchantZip: latest.MerchantZip.Trim(),
            OriginDate: latest.OriginTimestamp.Trim());

        return OperationResult<TransactionAddRequest>.Ok(request);
    }

    /// <summary>Renders the 26-char legacy timestamp text (yyyy-MM-dd-HH.mm.ss.ffffff).</summary>
    private static string FormatTimestamp(DateTimeOffset now) =>
        now.UtcDateTime.ToString("yyyy-MM-dd-HH.mm.ss.ffffff", CultureInfo.InvariantCulture);
}
