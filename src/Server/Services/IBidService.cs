using Stallions.Shared.DTOs.Bids;

namespace Stallions.Server.Services;

public interface IBidService
{
    Task<ServiceResult<CurrentBidDto>> GetCurrentBidAsync(Guid auctionListingId);
    Task<ServiceResult<BidDto>> PlaceBidAsync(Guid auctionListingId, PlaceBidRequest request);
    Task<ServiceResult<IReadOnlyList<BidDto>>> GetHistoryAsync(Guid auctionListingId);
    Task<ServiceResult<IReadOnlyList<BidDto>>> GetMineAsync();
}
