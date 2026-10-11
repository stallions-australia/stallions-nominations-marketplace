using Stallions.Shared.DTOs.Offers;

namespace Stallions.Server.Services;

public interface IOfferService
{
    Task<ServiceResult<OfferDto>> MakeAsync(Guid listingId, MakeOfferRequest request);
    Task<ServiceResult> WithdrawAsync(Guid offerId);
    Task<ServiceResult<IReadOnlyList<OfferDto>>> GetMineAsync();
    /// <summary>The caller's latest offer on the listing; Ok(null) when they have none.</summary>
    Task<ServiceResult<OfferDto?>> GetMyOfferForListingAsync(Guid listingId);
    Task<ServiceResult<IReadOnlyList<StudOfferDto>>> GetForListingAsync(Guid listingId);
    Task<ServiceResult> DeclineAsync(Guid offerId);
    Task<ServiceResult<AcceptOfferResultDto>> AcceptAsync(Guid offerId);
}
