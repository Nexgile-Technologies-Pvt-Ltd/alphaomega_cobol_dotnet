using CardDemo.Application.Abstractions;
using CardDemo.Application.Dtos;
using CardDemo.Domain.Common;
using CardDemo.Domain.Entities;
using CardDemo.Domain.Services;

namespace CardDemo.Application.Services;

/// <summary>
/// Account view and update (COACTVWC / COACTUPC). The view joins the account to
/// its owning customer (via the card cross-reference) and its cards. The update
/// applies the safe-target editable subset with optimistic concurrency.
/// </summary>
public sealed class AccountService(ICardDemoStore store, TimeProvider timeProvider) : IAccountService
{
    private readonly ICardDemoStore _store = store;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<OperationResult<AccountView>> ViewAsync(string accountId, CancellationToken ct = default)
    {
        var id = (accountId ?? string.Empty).Trim();
        if (!FieldValidation.IsRequiredNonZeroNumber(id))
            return OperationResult<AccountView>.Fail("Account number must be a non zero 11 digit number");

        var account = await _store.FindAccountAsync(id, ct).ConfigureAwait(false);
        if (account is null)
            return OperationResult<AccountView>.Fail("Account not found.");

        var xref = await _store.XrefByAccountAsync(id, ct).ConfigureAwait(false);
        if (xref is null)
            return OperationResult<AccountView>.Fail("Cross reference not found for this account.");

        var customer = await _store.FindCustomerAsync(xref.CustomerId, ct).ConfigureAwait(false);
        if (customer is null)
            return OperationResult<AccountView>.Fail("Customer not found for this account.");

        var cards = await _store.CardsByAccountAsync(id, ct).ConfigureAwait(false);

        return OperationResult<AccountView>.Ok(new AccountView(account, customer, cards));
    }

    public async Task<OperationResult> UpdateAsync(AccountUpdateRequest request, CancellationToken ct = default)
    {
        var id = (request.AccountId ?? string.Empty).Trim();
        if (!FieldValidation.IsRequiredNonZeroNumber(id))
            return OperationResult.Fail("Account number must be a non zero 11 digit number");

        if (!FieldValidation.IsYesNo(request.ActiveStatus))
            return OperationResult.Fail("Account Active Status must be Y or N");

        if (request.CreditLimit < 0)
            return OperationResult.Fail("Credit Limit must not be negative");

        if (request.CashCreditLimit < 0)
            return OperationResult.Fail("Cash Credit Limit must not be negative");

        if (!FieldValidation.IsBlank(request.ExpirationDate) && !FieldValidation.IsValidIsoDate(request.ExpirationDate))
            return OperationResult.Fail("Expiry date must be in format YYYY-MM-DD");

        if (!FieldValidation.IsBlank(request.ReissueDate) && !FieldValidation.IsValidIsoDate(request.ReissueDate))
            return OperationResult.Fail("Reissue date must be in format YYYY-MM-DD");

        // Customer validation order per COACTUPC (first failure wins, specific message).
        if (FieldValidation.IsBlank(request.FirstName))
            return OperationResult.Fail("First Name must be supplied.");

        if (FieldValidation.IsBlank(request.LastName))
            return OperationResult.Fail("Last Name must be supplied.");

        if (!FieldValidation.IsValidSsn(request.Ssn))
            return OperationResult.Fail("SSN: should not be 000, 666, or between 900 and 999");

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        if (!FieldValidation.IsValidDateOfBirth(request.DateOfBirth, today))
            return OperationResult.Fail("Date of Birth: cannot be in the future");

        if (!FieldValidation.IsValidFico(request.FicoCreditScore))
            return OperationResult.Fail("FICO credit score must be between 300 and 850");

        if (!FieldValidation.IsValidUsState(request.StateCode))
            return OperationResult.Fail("State: is not a valid state code");

        if (!FieldValidation.IsStateZipConsistent(request.StateCode, request.Zip))
            return OperationResult.Fail("Invalid zip code for state");

        if (!IsPhoneAreaCodeValid(request.PhoneNumber1))
            return OperationResult.Fail("Phone Number 1: Not valid North America general purpose area code");

        if (!IsPhoneAreaCodeValid(request.PhoneNumber2))
            return OperationResult.Fail("Phone Number 2: Not valid North America general purpose area code");

        var account = await _store.FindAccountAsync(id, ct).ConfigureAwait(false);
        if (account is null)
            return OperationResult.Fail("Account not found.");

        if (account.RowVersion != request.AccountRowVersion)
            return OperationResult.Fail("Record changed by another user; reload.");

        var xref = await _store.XrefByAccountAsync(id, ct).ConfigureAwait(false);
        if (xref is null)
            return OperationResult.Fail("Cross reference not found for this account.");

        var customer = await _store.FindCustomerAsync(xref.CustomerId, ct).ConfigureAwait(false);
        if (customer is null)
            return OperationResult.Fail("Customer not found for this account.");

        account.ActiveStatus = request.ActiveStatus.Trim().ToUpperInvariant();
        account.CreditLimit = request.CreditLimit;
        account.CashCreditLimit = request.CashCreditLimit;
        account.ExpirationDate = request.ExpirationDate ?? string.Empty;
        account.ReissueDate = request.ReissueDate ?? string.Empty;
        account.GroupId = request.GroupId ?? string.Empty;

        customer.FirstName = request.FirstName ?? string.Empty;
        customer.MiddleName = request.MiddleName ?? string.Empty;
        customer.LastName = request.LastName ?? string.Empty;
        customer.AddressLine1 = request.AddressLine1 ?? string.Empty;
        customer.AddressLine2 = request.AddressLine2 ?? string.Empty;
        customer.AddressLine3 = request.AddressLine3 ?? string.Empty;
        customer.StateCode = request.StateCode ?? string.Empty;
        customer.CountryCode = request.CountryCode ?? string.Empty;
        customer.Zip = request.Zip ?? string.Empty;
        customer.PhoneNumber1 = request.PhoneNumber1 ?? string.Empty;
        customer.PhoneNumber2 = request.PhoneNumber2 ?? string.Empty;
        customer.FicoCreditScore = request.FicoCreditScore;
        customer.Ssn = request.Ssn ?? string.Empty;
        customer.DateOfBirth = request.DateOfBirth ?? string.Empty;

        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return OperationResult.Ok("Account updated successfully.");
    }

    /// <summary>
    /// A blank phone number is accepted (COACTUPC treats an all-blank phone as valid);
    /// otherwise the leading 3-digit area code is extracted and checked against CSLKPCDY.
    /// </summary>
    private static bool IsPhoneAreaCodeValid(string? phone)
    {
        if (FieldValidation.IsBlank(phone)) return true;

        Span<char> area = stackalloc char[3];
        var count = 0;
        foreach (var c in phone!)
        {
            if (c is >= '0' and <= '9')
            {
                area[count++] = c;
                if (count == 3) break;
            }
        }

        if (count < 3) return false;
        return FieldValidation.IsValidPhoneAreaCode(new string(area));
    }
}
