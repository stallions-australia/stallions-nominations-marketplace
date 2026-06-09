using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class StallionDirectoryRepository : IStallionDirectoryRepository
{
    private readonly AppDbContext _db;
    public StallionDirectoryRepository(AppDbContext db) => _db = db;

    public async Task<StallionDirectory?> GetByIdAsync(Guid id) =>
        await _db.StallionDirectories
            .Include(d => d.StudDirectory)
            .FirstOrDefaultAsync(d => d.Id == id);

    public async Task<IReadOnlyList<StallionDirectory>> GetAllAsync(
        bool includeInactive = false, Guid? studDirectoryId = null) =>
        await _db.StallionDirectories
            .Include(d => d.StudDirectory)
            .Where(d => includeInactive || d.IsActive)
            .Where(d => studDirectoryId == null || d.StudDirectoryId == studDirectoryId)
            .OrderBy(d => d.Name)
            .ToListAsync();

    public async Task<IReadOnlyList<StallionDirectory>> GetByStudDirectoryIdAsync(
        Guid studDirectoryId, bool activeOnly = true) =>
        await _db.StallionDirectories
            .Include(d => d.StudDirectory)
            .Where(d => d.StudDirectoryId == studDirectoryId)
            .Where(d => !activeOnly || d.IsActive)
            .OrderBy(d => d.Name)
            .ToListAsync();

    public async Task<StallionDirectory> AddAsync(StallionDirectory entry)
    {
        _db.StallionDirectories.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    public async Task UpdateAsync(StallionDirectory entry)
    {
        entry.UpdatedAt = DateTime.UtcNow;
        _db.StallionDirectories.Update(entry);
        await _db.SaveChangesAsync();
    }
}
