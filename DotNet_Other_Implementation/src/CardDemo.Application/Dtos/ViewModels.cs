using CardDemo.Domain.Entities;

namespace CardDemo.Application.Dtos;

/// <summary>A page of results plus whether a further page exists (keyset-style paging).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, bool HasNext, bool HasPrevious);

/// <summary>Account view joins the account, its customer and card count (COACTVWC).</summary>
public sealed record AccountView(Account Account, Customer Customer, IReadOnlyList<Card> Cards);

/// <summary>A card together with its owning account (COCRDSLC / COCRDUPC).</summary>
public sealed record CardWithAccount(Card Card, Account? Account);

/// <summary>Fields the account-update screen may change (COACTUPC safe-target subset).</summary>
public sealed record AccountUpdateRequest(
    string AccountId,
    string ActiveStatus,
    decimal CreditLimit,
    decimal CashCreditLimit,
    string ExpirationDate,
    string ReissueDate,
    string GroupId,
    // Customer fields editable from the same screen
    string FirstName,
    string MiddleName,
    string LastName,
    string AddressLine1,
    string AddressLine2,
    string AddressLine3,
    string StateCode,
    string CountryCode,
    string Zip,
    string PhoneNumber1,
    string PhoneNumber2,
    int FicoCreditScore,
    string Ssn,
    string DateOfBirth,
    long AccountRowVersion);

/// <summary>Fields the card-update screen may change (COCRDUPC): name, status, expiry.</summary>
public sealed record CardUpdateRequest(
    string CardNumber,
    string EmbossedName,
    string ActiveStatus,
    int ExpirationMonth,
    int ExpirationYear,
    long CardRowVersion);

/// <summary>Transaction-add input (COTRN02C).</summary>
public sealed record TransactionAddRequest(
    string AccountId,
    string CardNumber,
    string TypeCode,
    string CategoryCode,
    string Source,
    string Description,
    decimal Amount,
    string MerchantId,
    string MerchantName,
    string MerchantCity,
    string MerchantZip,
    string OriginDate);

/// <summary>User add/update input (COUSR01C/COUSR02C).</summary>
public sealed record UserUpsertRequest(
    string UserId,
    string FirstName,
    string LastName,
    string Password,
    string UserType);
