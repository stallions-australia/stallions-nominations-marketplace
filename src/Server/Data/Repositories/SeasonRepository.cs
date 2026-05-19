using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class SeasonRepository : ISeasonRepository
{
    private readonly AppDbContext _db;
    public SeasonRepository(AppDbContext db) => _db = db;

    public async Task<Season?> GetByIdAsync(Guid id) =>
        await _db.Seasons.FindAsync(id);

    public async Task<Season?> GetCurrentOpenSeasonAsync() =>
        await _db.Seasons.FirstOrDefaultAsync(s => s.IsOpen);

    public async Task<IReadOnlyList<Season>> GetAllAsync() =>
        await _db.Seasons.OrderByDescending(s => s.StartDate).ToListAsync();

    public async Task<Season> AddAsync(Season season)
    {
        _db.Seasons.Add(season);
        await _db.SaveChangesAsync();
        return season;
    }

    public async Task UpdateAsync(Season season)
    {
        _db.Seasons.Update(season);
        await _db.SaveChangesAsync();
    }
}
