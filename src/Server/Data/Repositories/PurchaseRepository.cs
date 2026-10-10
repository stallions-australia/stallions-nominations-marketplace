using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class PurchaseRepository : IPurchaseRepository
{
    private readonly AppDbContext _db;
    public PurchaseRepository(AppDbContext db) => _db = db;

    public async Task<Purchase?> GetByIdAsync(Guid id) =>
        await WithListingDetails(_db.Purchases).Include(p => p.Buyer)
            .FirstOrDefaultAsync(p => p.Id == id);

    public async Task<IReadOnlyList<Purchase>> GetByBuyerIdAsync(Guid buyerUserId) =>
        await WithListingDetails(_db.Purchases).Where(p => p.BuyerUserId == buyerUserId)
            .OrderByDescending(p => p.CreatedAt).ToListAsync();

    private static IQueryable<Purchase> WithListingDetails(IQueryable<Purchase> q) =>
        q.Include(p => p.Listing).ThenInclude(l => l.Stallion)
         .Include(p => p.Listing).ThenInclude(l => l.Season)
         .Include(p => p.Listing).ThenInclude(l => l.StudFarm);

    public async Task<IReadOnlyList<Purchase>> GetAllAsync() =>
        await _db.Purchases
            .Include(p => p.Buyer)
            .Include(p => p.Listing)
                .ThenInclude(l => l.Stallion)
            .Include(p => p.Listing)
                .ThenInclude(l => l.StudFarm)
            .Include(p => p.Listing)
                .ThenInclude(l => l.Season)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

    public async Task<Purchase> AddAsync(Purchase purchase)
    {
        _db.Purchases.Add(purchase);
        await _db.SaveChangesAsync();
        return purchase;
    }

    public async Task UpdateAsync(Purchase purchase)
    {
        _db.Purchases.Update(purchase);
        await _db.SaveChangesAsync();
    }

}
