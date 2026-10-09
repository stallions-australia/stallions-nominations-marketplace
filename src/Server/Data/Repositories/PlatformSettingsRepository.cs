using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class PlatformSettingsRepository : IPlatformSettingsRepository
{
    private readonly AppDbContext _db;
    public PlatformSettingsRepository(AppDbContext db) => _db = db;

    public async Task<PlatformSettings> GetAsync() =>
        await _db.PlatformSettings.FirstOrDefaultAsync(s => s.Id == PlatformSettings.SingletonId)
        ?? throw new InvalidOperationException("Platform settings row is missing — has the database been migrated?");

    public async Task UpdateAsync(PlatformSettings settings)
    {
        _db.PlatformSettings.Update(settings);
        await _db.SaveChangesAsync();
    }
}
