using Stallions.Shared.DTOs.Listings;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public interface IListingService
{
    Task<ServiceResult<IReadOnlyList<ListingDto>>> GetActiveAsync(Guid? seasonId, ListingType? type, bool isStaff);
    Task<ServiceResult<IReadOnlyList<ListingCardDto>>> GetListingCardsAsync(Guid? seasonId, Guid? studFarmId, string? type);
    Task<ServiceResult<ListingDto>> GetByIdAsync(Guid id, bool isStaff);
    Task<ServiceResult<IReadOnlyList<ListingDto>>> GetMineAsync();
    Task<ServiceResult<ListingDto>> CreateAuctionListingAsync(CreateAuctionListingRequest request);
    Task<ServiceResult<ListingDto>> CreateFixedPriceListingAsync(CreateFixedPriceListingRequest request);
    Task<ServiceResult<ListingDto>> UpdateListingAsync(Guid id, UpdateListingRequest request);
    Task<ServiceResult> PublishListingAsync(Guid id);
    Task<ServiceResult> CancelListingAsync(Guid id);
    Task<ServiceResult<ListingDto>> RelistAsync(Guid id);
}
