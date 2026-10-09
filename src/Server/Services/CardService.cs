using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Payments;
using Stallions.Shared.DTOs.Payments;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

/// <summary>
/// Buyers' saved cards. Reads and starts the hosted setup page only — the card itself is saved by
/// PaymentEventProcessor when the provider confirms it.
/// </summary>
public class CardService : ICardService
{
    private readonly ISavedCardRepository _cards;
    private readonly IUserRepository _userRepo;
    private readonly IUserService _users;
    private readonly IPaymentProvider _provider;

    public CardService(ISavedCardRepository cards, IUserRepository userRepo, IUserService users, IPaymentProvider provider)
    {
        _cards = cards; _userRepo = userRepo; _users = users; _provider = provider;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<ServiceResult<SavedCardDto>> GetMineAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.Buyer) return ServiceResult<SavedCardDto>.Forbidden();

        var card = await _cards.GetByUserIdAsync(caller.Id);
        return card == null
            ? ServiceResult<SavedCardDto>.NotFound("No card saved.")
            : ServiceResult<SavedCardDto>.Ok(new SavedCardDto
            {
                Brand = card.Brand, Last4 = card.Last4, ExpMonth = card.ExpMonth, ExpYear = card.ExpYear,
                IsValid = IsUsable(card), UpdatedAt = card.UpdatedAt
            });
    }

    public async Task<ServiceResult<PaymentRedirectDto>> StartSetupAsync(string successUrl, string cancelUrl)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.Buyer)
            return ServiceResult<PaymentRedirectDto>.Forbidden("Only buyers can save a card.");

        // A customer id issued by a different provider (e.g. the dev fake) can't be used here.
        if (string.IsNullOrEmpty(caller.PaymentCustomerId) || caller.PaymentCustomerProvider != _provider.Name)
        {
            caller.PaymentCustomerId = await _provider.CreateCustomerAsync(caller.Id, caller.Email, caller.DisplayName);
            caller.PaymentCustomerProvider = _provider.Name;
            await _userRepo.UpdateAsync(caller);
        }

        var url = await _provider.CreateCardSetupSessionAsync(caller.Id, caller.PaymentCustomerId, successUrl, cancelUrl);
        return ServiceResult<PaymentRedirectDto>.Ok(new PaymentRedirectDto { Url = url });
    }

    public async Task<bool> HasValidCardAsync(Guid userId)
    {
        var card = await _cards.GetByUserIdAsync(userId);
        return card != null && IsUsable(card);
    }

    // Unexpired, and saved with the provider that will charge it.
    private bool IsUsable(SavedCard card) => card.Provider == _provider.Name && card.IsValidOn(Today);
}
