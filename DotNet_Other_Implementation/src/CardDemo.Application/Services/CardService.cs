using CardDemo.Application.Abstractions;
using CardDemo.Application.Dtos;
using CardDemo.Domain.Common;
using CardDemo.Domain.Entities;
using CardDemo.Domain.Services;

namespace CardDemo.Application.Services;

/// <summary>
/// Card list, view and update (COCRDLIC / COCRDSLC / COCRDUPC). The update
/// applies the safe-target editable subset (embossed name, active status,
/// expiry month/year) under optimistic concurrency.
/// </summary>
public sealed class CardService(ICardDemoStore store) : ICardService
{
    private readonly ICardDemoStore _store = store;

    public async Task<OperationResult<PagedResult<Card>>> ListAsync(string? accountId, string? cardNumber, int page, int pageSize, CancellationToken ct = default)
    {
        var safePage = page < 1 ? 1 : page;
        var safeSize = pageSize < 1 ? 10 : pageSize;

        var acct = string.IsNullOrWhiteSpace(accountId) ? null : accountId.Trim();
        var card = string.IsNullOrWhiteSpace(cardNumber) ? null : cardNumber.Trim();

        var skip = (safePage - 1) * safeSize;
        // Fetch one extra to determine whether a further page exists (keyset-style).
        var rows = await _store.ListCardsAsync(acct, card, skip, safeSize + 1, ct).ConfigureAwait(false);

        var hasNext = rows.Count > safeSize;
        var items = hasNext ? rows.Take(safeSize).ToList() : rows.ToList();

        var result = new PagedResult<Card>(items, safePage, safeSize, hasNext, safePage > 1);
        return OperationResult<PagedResult<Card>>.Ok(result);
    }

    public async Task<OperationResult<CardWithAccount>> ViewAsync(string accountId, string cardNumber, CancellationToken ct = default)
    {
        var acct = (accountId ?? string.Empty).Trim();
        var card = (cardNumber ?? string.Empty).Trim();

        if (!FieldValidation.IsRequiredNonZeroNumber(acct))
            return OperationResult<CardWithAccount>.Fail("Account number must be a non zero 11 digit number");

        if (!FieldValidation.IsAllDigits(card))
            return OperationResult<CardWithAccount>.Fail("Card number if supplied must be a 16 digit number");

        var found = await _store.FindCardAsync(card, ct).ConfigureAwait(false);
        if (found is null || found.AccountId != acct)
            return OperationResult<CardWithAccount>.Fail("Card not found for this account.");

        var account = await _store.FindAccountAsync(acct, ct).ConfigureAwait(false);
        return OperationResult<CardWithAccount>.Ok(new CardWithAccount(found, account));
    }

    public async Task<OperationResult> UpdateAsync(CardUpdateRequest request, CancellationToken ct = default)
    {
        var card = (request.CardNumber ?? string.Empty).Trim();

        if (!FieldValidation.IsAllDigits(card))
            return OperationResult.Fail("Card number if supplied must be a 16 digit number");

        if (FieldValidation.IsBlank(request.EmbossedName))
            return OperationResult.Fail("Card name can NOT be empty");

        if (!FieldValidation.IsValidEmbossedName(request.EmbossedName))
            return OperationResult.Fail("Card name can only contain letters and spaces.");

        if (!FieldValidation.IsYesNo(request.ActiveStatus))
            return OperationResult.Fail("Card Active Status must be Y or N");

        if (!FieldValidation.IsValidMonth(request.ExpirationMonth))
            return OperationResult.Fail("Card expiry month must be between 1 and 12");

        if (!FieldValidation.IsValidYear(request.ExpirationYear))
            return OperationResult.Fail("Invalid card expiry year");

        if (!FieldValidation.IsValidCalendarExpiration(request.ExpirationMonth, request.ExpirationYear))
            return OperationResult.Fail("Expiration date is not a valid date.");

        var entity = await _store.FindCardAsync(card, ct).ConfigureAwait(false);
        if (entity is null)
            return OperationResult.Fail("Card not found.");

        if (entity.RowVersion != request.CardRowVersion)
            return OperationResult.Fail("Record changed by another user; reload.");

        entity.EmbossedName = request.EmbossedName.Trim();
        entity.ActiveStatus = request.ActiveStatus.Trim().ToUpperInvariant();
        // CARD-EXPIRAION-DATE is X(10); persist a normalized yyyy-MM boundary date.
        entity.ExpirationDate = $"{request.ExpirationYear:D4}-{request.ExpirationMonth:D2}-01";

        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return OperationResult.Ok("Card updated successfully.");
    }
}
