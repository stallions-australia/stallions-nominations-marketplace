using Stallions.Shared.DTOs.Payments;

namespace Stallions.Server.Services;

public interface ICardService
{
    Task<ServiceResult<SavedCardDto>> GetMineAsync();
    Task<ServiceResult<PaymentRedirectDto>> StartSetupAsync(string successUrl, string cancelUrl);
    /// <summary>True when the user has a saved card that hasn't passed the end of its expiry month.</summary>
    Task<bool> HasValidCardAsync(Guid userId);
}
