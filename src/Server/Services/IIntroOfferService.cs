using Stallions.Shared.DTOs.IntroOffers;

namespace Stallions.Server.Services;

/// <summary>Staff-only management of intro offers (fee settings). Every change is audited.</summary>
public interface IIntroOfferService
{
    Task<ServiceResult<IReadOnlyList<IntroOfferDto>>> GetAllAsync();
    Task<ServiceResult<IntroOfferDto>> CreateAsync(SaveIntroOfferRequest request);
    Task<ServiceResult<IntroOfferDto>> UpdateAsync(Guid id, SaveIntroOfferRequest request);
    Task<ServiceResult<IntroOfferDto>> RetireAsync(Guid id);
    /// <summary>The fee a new subscription for this stud and season would get right now.</summary>
    Task<ServiceResult<IntroOfferPreviewDto>> PreviewAsync(Guid studFarmId, Guid seasonId);
}
