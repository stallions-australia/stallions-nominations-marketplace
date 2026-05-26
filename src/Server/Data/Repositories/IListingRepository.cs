using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public interface IListingRepository
{
    Task<Listing?> GetByIdAsync(Guid id);
    Task<AuctionListing?> GetAuctionByIdAsync(Guid id);
    Task<FixedPriceListing?> GetFixedPriceByIdAsync(Guid id);
    Task<IReadOnlyList<Listing>> GetActiveAsync(Guid? seasonId = null, Guid? studFarmId = null, ListingType? type = null);
    Task<Dictionary<Guid, (int Count, decimal? Highest)>> GetBidAggregatesAsync(IEnumerable<Guid> auctionIds);
    Task<IReadOnlyList<Listing>> GetByStudFarmIdAsync(Guid studFarmId);
    Task<IReadOnlyList<AuctionListing>> GetExpiredAuctionsAsync();
    Task<IReadOnlyList<Listing>> GetAllStaffAsync();
    Task<Listing> AddAsync(Listing listing);
    Task UpdateAsync(Listing listing);
}
