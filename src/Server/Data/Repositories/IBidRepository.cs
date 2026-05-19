using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IBidRepository
{
    Task<Bid?> GetByIdAsync(Guid id);
    Task<Bid?> GetHighestBidAsync(Guid auctionListingId);
    Task<Bid?> GetSecondHighestBidAsync(Guid auctionListingId);
    Task<IReadOnlyList<Bid>> GetByAuctionListingIdAsync(Guid auctionListingId);
    Task<IReadOnlyList<Bid>> GetByBuyerIdAsync(Guid buyerUserId);
    Task<Bid> AddAsync(Bid bid);
    Task UpdateAsync(Bid bid);
    Task UpdateRangeAsync(IEnumerable<Bid> bids);
}
