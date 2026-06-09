using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class StudDirectoryRepository : IStudDirectoryRepository
{
    private readonly AppDbContext _db;
    public StudDirectoryRepository(AppDbContext db) => _db = db;

    public async Task<StudDirectory?> GetByIdAsync(Guid id) =>
        await _db.StudDirectories
            .Include(d => d.Stallions)
            .FirstOrDefaultAsync(d => d.Id == id);

    public async Task<IReadOnlyList<StudDirectory>> GetAllAsync(bool includeInactive = false) =>
        await _db.StudDirectories
            .Include(d => d.Stallions)
            .Where(d => includeInactive || d.IsActive)
            .OrderBy(d => d.Name)
            .ToListAsync();

    public async Task<StudDirectory> AddAsync(StudDirectory entry)
    {
        _db.StudDirectories.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    public async Task UpdateAsync(StudDirectory entry)
    {
        entry.UpdatedAt = DateTime.UtcNow;
        _db.StudDirectories.Update(entry);
        await _db.SaveChangesAsync();
    }
}
