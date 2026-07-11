using System.Globalization;
using CardDemo.Application.Abstractions;
using CardDemo.Domain.Common;
using CardDemo.Domain.Entities;
using CardDemo.Domain.Services;

namespace CardDemo.Application.Services;

/// <summary>
/// Bill payment (COBIL00C). Pays the full current account balance: creates a
/// payment transaction (type '02', category '0002') for the balance amount, then
/// zeroes the account balance (COMPUTE ACCT-CURR-BAL = ACCT-CURR-BAL - TRAN-AMT).
/// </summary>
public sealed class BillPayService(ICardDemoStore store, TimeProvider timeProvider) : IBillPayService
{
    private readonly ICardDemoStore _store = store;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<OperationResult<Transaction>> PayFullBalanceAsync(string accountId, CancellationToken ct = default)
    {
        var acct = (accountId ?? string.Empty).Trim();
        if (!FieldValidation.IsRequiredNonZeroNumber(acct))
            return OperationResult<Transaction>.Fail("Account number must be a non zero 11 digit number");

        var account = await _store.FindAccountAsync(acct, ct).ConfigureAwait(false);
        if (account is null)
            return OperationResult<Transaction>.Fail("Account not found.");

        if (account.CurrentBalance <= 0)
            return OperationResult<Transaction>.Fail("You have nothing to pay...");

        var xref = await _store.XrefByAccountAsync(acct, ct).ConfigureAwait(false);
        if (xref is null)
            return OperationResult<Transaction>.Fail("Cross reference not found for this account.");

        var amount = MoneyMath.Truncate2(account.CurrentBalance);

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
            TypeCode = "02",
            CategoryCode = "0002",
            Source = "POS TERM",
            Description = "BILL PAYMENT - ONLINE",
            Amount = amount,
            MerchantId = "999999999",
            MerchantName = "BILL PAYMENT",
            MerchantCity = "N/A",
            MerchantZip = "N/A",
            CardNumber = xref.CardNumber,
            OriginTimestamp = timestamp,
            ProcessTimestamp = timestamp,
        };

        await _store.AddTransactionAsync(transaction, ct).ConfigureAwait(false);

        // COBIL00C: ACCT-CURR-BAL = ACCT-CURR-BAL - TRAN-AMT (full balance -> 0).
        account.CurrentBalance = MoneyMath.Truncate2(account.CurrentBalance - amount);

        await _store.SaveChangesAsync(ct).ConfigureAwait(false);

        return OperationResult<Transaction>.Ok(transaction, $"Bill payment successful. Transaction {newId} added.");
    }

    private static string FormatTimestamp(DateTimeOffset now) =>
        now.UtcDateTime.ToString("yyyy-MM-dd-HH.mm.ss.ffffff", CultureInfo.InvariantCulture);
}
