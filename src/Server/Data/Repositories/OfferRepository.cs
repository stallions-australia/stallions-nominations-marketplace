using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public class OfferRepository : IOfferRepository
{
    private readonly AppDbContext _db;
    public OfferRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(Offer offer)
    {
        _db.Offers.Add(offer);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(Offer offer)
    {
        _db.Offers.Update(offer);
        await _db.SaveChangesAsync();
    }

    public async Task<Offer?> GetByIdAsync(Guid id) =>
        await WithDetails(_db.Offers).FirstOrDefaultAsync(o => o.Id == id);

    public async Task<Offer?> GetPendingAsync(Guid listingId, Guid buyerUserId) =>
        await _db.Offers.FirstOrDefaultAsync(o =>
            o.ListingId == listingId && o.BuyerUserId == buyerUserId && o.Status == OfferStatus.Pending);

    public async Task<Offer?> GetLatestForBuyerAsync(Guid listingId, Guid buyerUserId) =>
        await WithDetails(_db.Offers)
            .Where(o => o.ListingId == listingId && o.BuyerUserId == buyerUserId)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

    public async Task<IReadOnlyList<Offer>> GetByBuyerAsync(Guid buyerUserId) =>
        await WithDetails(_db.Offers)
            .Where(o => o.BuyerUserId == buyerUserId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

    public async Task<IReadOnlyList<Offer>> GetForListingAsync(Guid listingId) =>
        await _db.Offers.Include(o => o.Buyer)
            .Where(o => o.ListingId == listingId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

    public async Task<IReadOnlyList<Guid>> GetIdsToExpireAsync(DateTime now, int max) =>
        await _db.Offers
            .Where(o => o.Status == OfferStatus.Pending && (o.ExpiresAt <= now || o.Listing.Status != ListingStatus.Active))
            .OrderBy(o => o.ExpiresAt)
            .Take(max)
            .Select(o => o.Id)
            .ToListAsync();

    public async Task<IReadOnlyList<Guid>> GetPendingIdsForListingAsync(Guid listingId) =>
        await _db.Offers
            .Where(o => o.ListingId == listingId && o.Status == OfferStatus.Pending)
            .Select(o => o.Id)
            .ToListAsync();

    private static IQueryable<Offer> WithDetails(IQueryable<Offer> q) =>
        q.Include(o => o.Buyer)
         .Include(o => o.Listing).ThenInclude(l => l.Stallion)
         .Include(o => o.Listing).ThenInclude(l => l.Season)
         .Include(o => o.Listing).ThenInclude(l => l.StudFarm).ThenInclude(f => f.User);
}
