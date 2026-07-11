using System.Globalization;
using CardDemo.Application.Abstractions;
using CardDemo.Application.Dtos;
using CardDemo.Domain.Entities;

namespace CardDemo.Console.Interactive.Screens;

/// <summary>Account view and update screens (COACTVWC / COACTUPC).</summary>
internal sealed class AccountScreens(ConsoleIo io, IAccountService accounts)
{
    private readonly ConsoleIo _io = io;
    private readonly IAccountService _accounts = accounts;

    public async Task ViewAsync(CancellationToken ct)
    {
        _io.Header("Account View (COACTVWC)");
        _io.Line("Enter the 11-digit account id, or 0 to go back.");
        _io.Line();

        var accountId = _io.PromptRequired("Account id: ");
        if (accountId is null || accountId == ConsoleIo.BackMarker)
        {
            return;
        }

        var result = await _accounts.ViewAsync(accountId, ct);
        if (!result.Success || result.Value is null)
        {
            _io.ShowError(result.Message.Length == 0 ? "Account not found." : result.Message);
            _io.PressEnter();
            return;
        }

        RenderAccount(result.Value);
        _io.PressEnter();
    }

    public async Task UpdateAsync(CancellationToken ct)
    {
        _io.Header("Account Update (COACTUPC)");
        _io.Line("Enter the account id to load, or 0 to go back.");
        _io.Line();

        var accountId = _io.PromptRequired("Account id: ");
        if (accountId is null || accountId == ConsoleIo.BackMarker)
        {
            return;
        }

        var loaded = await _accounts.ViewAsync(accountId, ct);
        if (!loaded.Success || loaded.Value is null)
        {
            _io.ShowError(loaded.Message.Length == 0 ? "Account not found." : loaded.Message);
            _io.PressEnter();
            return;
        }

        var view = loaded.Value;
        var account = view.Account;
        var customer = view.Customer;

        RenderAccount(view);
        _io.Line();
        _io.Line("Press ENTER at any field to keep the current value. Type 0 to cancel.");
        _io.Line();

        var activeStatus = Ask("Active status", account.ActiveStatus);
        if (Cancelled(activeStatus)) { return; }

        var creditLimit = AskDecimal("Credit limit", account.CreditLimit);
        if (creditLimit is null) { return; }

        var cashLimit = AskDecimal("Cash credit limit", account.CashCreditLimit);
        if (cashLimit is null) { return; }

        var expiration = Ask("Expiration date (yyyy-MM-dd)", account.ExpirationDate);
        if (Cancelled(expiration)) { return; }

        var reissue = Ask("Reissue date (yyyy-MM-dd)", account.ReissueDate);
        if (Cancelled(reissue)) { return; }

        var groupId = Ask("Group id", account.GroupId);
        if (Cancelled(groupId)) { return; }

        var firstName = Ask("Customer first name", customer.FirstName);
        if (Cancelled(firstName)) { return; }

        var middleName = Ask("Customer middle name", customer.MiddleName);
        if (Cancelled(middleName)) { return; }

        var lastName = Ask("Customer last name", customer.LastName);
        if (Cancelled(lastName)) { return; }

        var addr1 = Ask("Address line 1", customer.AddressLine1);
        if (Cancelled(addr1)) { return; }

        var addr2 = Ask("Address line 2", customer.AddressLine2);
        if (Cancelled(addr2)) { return; }

        var addr3 = Ask("Address line 3", customer.AddressLine3);
        if (Cancelled(addr3)) { return; }

        var state = Ask("State code", customer.StateCode);
        if (Cancelled(state)) { return; }

        var country = Ask("Country code", customer.CountryCode);
        if (Cancelled(country)) { return; }

        var zip = Ask("ZIP", customer.Zip);
        if (Cancelled(zip)) { return; }

        var phone1 = Ask("Phone 1", customer.PhoneNumber1);
        if (Cancelled(phone1)) { return; }

        var phone2 = Ask("Phone 2", customer.PhoneNumber2);
        if (Cancelled(phone2)) { return; }

        var fico = AskInt("FICO score", customer.FicoCreditScore);
        if (fico is null) { return; }

        var ssn = Ask("SSN (9 digits)", customer.Ssn);
        if (Cancelled(ssn)) { return; }

        var dob = Ask("Date of birth (yyyy-MM-dd)", customer.DateOfBirth);
        if (Cancelled(dob)) { return; }

        var request = new AccountUpdateRequest(
            AccountId: account.AccountId,
            ActiveStatus: activeStatus!,
            CreditLimit: creditLimit.Value,
            CashCreditLimit: cashLimit.Value,
            ExpirationDate: expiration!,
            ReissueDate: reissue!,
            GroupId: groupId!,
            FirstName: firstName!,
            MiddleName: middleName!,
            LastName: lastName!,
            AddressLine1: addr1!,
            AddressLine2: addr2!,
            AddressLine3: addr3!,
            StateCode: state!,
            CountryCode: country!,
            Zip: zip!,
            PhoneNumber1: phone1!,
            PhoneNumber2: phone2!,
            FicoCreditScore: fico.Value,
            Ssn: ssn!,
            DateOfBirth: dob!,
            AccountRowVersion: account.RowVersion);

        var result = await _accounts.UpdateAsync(request, ct);
        _io.ShowResult(result);
        _io.PressEnter();
    }

    private void RenderAccount(AccountView view)
    {
        var a = view.Account;
        var c = view.Customer;

        _io.Line();
        _io.Rule('-');
        _io.Line($"Account   : {a.AccountId}    Status: {Show(a.ActiveStatus)}    Group: {Show(a.GroupId)}");
        _io.Line($"Balance   : {Money(a.CurrentBalance)}");
        _io.Line($"Cr limit  : {Money(a.CreditLimit)}    Cash limit: {Money(a.CashCreditLimit)}");
        _io.Line($"Cyc credit: {Money(a.CurrentCycleCredit)}    Cyc debit : {Money(a.CurrentCycleDebit)}");
        _io.Line($"Opened    : {Show(a.OpenDate)}    Expires: {Show(a.ExpirationDate)}    Reissue: {Show(a.ReissueDate)}");
        _io.Rule('-');
        _io.Line($"Customer  : {c.CustomerId}    {c.FirstName.TrimEnd()} {c.MiddleName.TrimEnd()} {c.LastName.TrimEnd()}".TrimEnd());
        _io.Line($"FICO      : {c.FicoCreditScore}");
        _io.Line($"Address   : {c.AddressLine1.TrimEnd()}");
        if (c.AddressLine2.Trim().Length > 0) { _io.Line($"            {c.AddressLine2.TrimEnd()}"); }
        if (c.AddressLine3.Trim().Length > 0) { _io.Line($"            {c.AddressLine3.TrimEnd()}"); }
        _io.Line($"            {c.StateCode.Trim()} {c.CountryCode.Trim()} {c.Zip.Trim()}".TrimEnd());
        _io.Line($"Phone     : {Show(c.PhoneNumber1)}   {Show(c.PhoneNumber2)}");
        _io.Rule('-');
        _io.Line($"Cards on account: {view.Cards.Count}");
        foreach (var card in view.Cards)
        {
            _io.Line($"  {card.CardNumber}  status {Show(card.ActiveStatus)}  exp {Show(card.ExpirationDate)}  {card.EmbossedName.TrimEnd()}");
        }
        _io.Rule('-');
    }

    private string? Ask(string label, string current)
    {
        var value = _io.Prompt($"{label} [{current.TrimEnd()}]: ");
        if (value is null || value == ConsoleIo.BackMarker)
        {
            return null;
        }
        return value.Length == 0 ? current : value;
    }

    private decimal? AskDecimal(string label, decimal current)
    {
        var value = _io.PromptDecimal($"{label} [{Money(current)}]: ", current);
        return value;
    }

    private int? AskInt(string label, int current)
    {
        var value = _io.PromptInt($"{label} [{current}]: ", current);
        return value;
    }

    private static bool Cancelled(string? value) => value is null;

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string Show(string? value) => (value ?? string.Empty).Trim();
}
