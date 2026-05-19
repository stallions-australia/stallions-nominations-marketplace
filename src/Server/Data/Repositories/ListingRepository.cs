using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public class ListingRepository : IListingRepository
{
    private readonly AppDbContext _db;
    public ListingRepository(AppDbContext db) => _db = db;

    public async Task<Listing?> GetByIdAsync(Guid id) =>
        await _db.Listings.Include(l => l.Stallion).ThenInclude(s => s.Images)
            .FirstOrDefaultAsync(l => l.Id == id);

    public async Task<AuctionListing?> GetAuctionByIdAsync(Guid id) =>
        await _db.AuctionListings.Include(l => l.Stallion).ThenInclude(s => s.Images)
            .FirstOrDefaultAsync(l => l.Id == id);

    public async Task<FixedPriceListing?> GetFixedPriceByIdAsync(Guid id) =>
        await _db.FixedPriceListings.Include(l => l.Stallion).ThenInclude(s => s.Images)
            .FirstOrDefaultAsync(l => l.Id == id);

    public async Task<IReadOnlyList<Listing>> GetActiveAsync(Guid? seasonId = null, ListingType? type = null)
    {
        var query = _db.Listings.Where(l => l.Status == ListingStatus.Active)
            .Include(l => l.Stallion).ThenInclude(s => s.Images)
            .Include(l => l.Season).AsQueryable();
        if (seasonId.HasValue) query = query.Where(l => l.SeasonId == seasonId.Value);
        if (type.HasValue) query = query.Where(l => l.ListingType == type.Value);
        return await query.OrderByDescending(l => l.PublishedAt).ToListAsync();
    }

    public async Task<IReadOnlyList<Listing>> GetByStudFarmIdAsync(Guid studFarmId) =>
        await _db.Listings.Where(l => l.StudFarmId == studFarmId)
            .Include(l => l.Stallion).OrderByDescending(l => l.CreatedAt).ToListAsync();

    public async Task<IReadOnlyList<AuctionListing>> GetExpiredAuctionsAsync() =>
        await _db.AuctionListings
            .Where(a => a.EndDateTime <= DateTime.UtcNow && a.Status == ListingStatus.Active)
            .ToListAsync();

    public async Task<Listing> AddAsync(Listing listing)
    {
        _db.Listings.Add(listing);
        await _db.SaveChangesAsync();
        return listing;
    }

    public async Task UpdateAsync(Listing listing)
    {
        _db.Listings.Update(listing);
        await _db.SaveChangesAsync();
    }
}
