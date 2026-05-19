using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public class BidRepository : IBidRepository
{
    private readonly AppDbContext _db;
    public BidRepository(AppDbContext db) => _db = db;

    public async Task<Bid?> GetByIdAsync(Guid id) =>
        await _db.Bids.FindAsync(id);

    public async Task<Bid?> GetHighestBidAsync(Guid auctionListingId) =>
        await _db.Bids
            .Where(b => b.AuctionListingId == auctionListingId && b.Status == BidStatus.Active)
            .OrderByDescending(b => b.AmountIncGst)
            .FirstOrDefaultAsync();

    public async Task<Bid?> GetSecondHighestBidAsync(Guid auctionListingId, Guid winnerBuyerUserId) =>
        await _db.Bids
            .Where(b => b.AuctionListingId == auctionListingId
                     && b.Status == BidStatus.Outbid
                     && b.BuyerUserId != winnerBuyerUserId)
            .OrderByDescending(b => b.AmountIncGst)
            .FirstOrDefaultAsync();

    public async Task<IReadOnlyList<Bid>> GetByAuctionListingIdAsync(Guid auctionListingId) =>
        await _db.Bids
            .Where(b => b.AuctionListingId == auctionListingId)
            .OrderByDescending(b => b.AmountIncGst)
            .ToListAsync();

    public async Task<IReadOnlyList<Bid>> GetByBuyerIdAsync(Guid buyerUserId) =>
        await _db.Bids
            .Where(b => b.BuyerUserId == buyerUserId)
            .OrderByDescending(b => b.PlacedAt)
            .ToListAsync();

    public async Task<Bid> AddAsync(Bid bid)
    {
        _db.Bids.Add(bid);
        await _db.SaveChangesAsync();
        return bid;
    }

    public async Task UpdateAsync(Bid bid)
    {
        _db.Bids.Update(bid);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateRangeAsync(IEnumerable<Bid> bids)
    {
        _db.Bids.UpdateRange(bids);
        await _db.SaveChangesAsync();
    }
}
