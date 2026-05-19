using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class StudFarmRepository : IStudFarmRepository
{
    private readonly AppDbContext _db;
    public StudFarmRepository(AppDbContext db) => _db = db;

    public async Task<StudFarm?> GetByIdAsync(Guid id) =>
        await _db.StudFarms.FindAsync(id);

    public async Task<StudFarm?> GetByUserIdAsync(Guid userId) =>
        await _db.StudFarms.FirstOrDefaultAsync(f => f.UserId == userId);

    public async Task<StudFarm> AddAsync(StudFarm studFarm)
    {
        _db.StudFarms.Add(studFarm);
        await _db.SaveChangesAsync();
        return studFarm;
    }

    public async Task UpdateAsync(StudFarm studFarm)
    {
        _db.StudFarms.Update(studFarm);
        await _db.SaveChangesAsync();
    }
}
