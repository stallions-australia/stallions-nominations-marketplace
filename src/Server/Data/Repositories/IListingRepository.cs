using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public interface IListingRepository
{
    Task<Listing?> GetByIdAsync(Guid id);
    Task<AuctionListing?> GetAuctionByIdAsync(Guid id);
    Task<IReadOnlyList<Listing>> GetActiveAsync(Guid? seasonId = null, Guid? studFarmId = null, ListingType? type = null);
    Task<Dictionary<Guid, (int Count, decimal? Highest)>> GetBidAggregatesAsync(IEnumerable<Guid> auctionIds);
    Task<IReadOnlyList<Listing>> GetByStudFarmIdAsync(Guid studFarmId);
    /// <summary>Active auctions that ended at or before the cutoff, oldest first.</summary>
    Task<IReadOnlyList<Guid>> GetAuctionIdsDueToCloseAsync(DateTime endedAtOrBefore, int max);
    /// <summary>The auction with Stallion, Season, StudFarm and the farm's owner (for emails).</summary>
    Task<AuctionListing?> GetAuctionWithDetailsAsync(Guid id);
    Task<IReadOnlyList<Listing>> GetAllStaffAsync();
    Task<Listing> AddAsync(Listing listing);
    Task UpdateAsync(Listing listing);
}
