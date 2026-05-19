using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class StallionRepository : IStallionRepository
{
    private readonly AppDbContext _db;
    public StallionRepository(AppDbContext db) => _db = db;

    public async Task<Stallion?> GetByIdAsync(Guid id) =>
        await _db.Stallions.Include(s => s.Images).FirstOrDefaultAsync(s => s.Id == id);

    public async Task<IReadOnlyList<Stallion>> GetByStudFarmIdAsync(Guid studFarmId) =>
        await _db.Stallions.Where(s => s.StudFarmId == studFarmId && s.IsActive)
            .Include(s => s.Images).ToListAsync();

    public async Task<IReadOnlyList<Stallion>> GetWithActiveListingsAsync() =>
        await _db.Stallions.Where(s => s.IsActive && s.Listings.Any(l => l.Status == Stallions.Shared.Enums.ListingStatus.Active))
            .Include(s => s.Images).ToListAsync();

    public async Task<Stallion> AddAsync(Stallion stallion)
    {
        _db.Stallions.Add(stallion);
        await _db.SaveChangesAsync();
        return stallion;
    }

    public async Task UpdateAsync(Stallion stallion)
    {
        _db.Stallions.Update(stallion);
        await _db.SaveChangesAsync();
    }
}
