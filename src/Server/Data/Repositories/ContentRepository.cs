using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class ContentRepository : IContentRepository
{
    private readonly AppDbContext _db;
    public ContentRepository(AppDbContext db) => _db = db;

    private IQueryable<ContentItem> Read() => _db.ContentItems.AsNoTracking().Include(c => c.CreatedBy);

    public Task<ContentItem?> GetCurrentAsync(string key) =>
        Read().Where(c => c.Key == key).OrderByDescending(c => c.Version).FirstOrDefaultAsync();

    public async Task<IReadOnlyList<ContentItem>> GetCurrentForAllAsync() =>
        await Read()
            .Where(c => c.Version == _db.ContentItems.Where(x => x.Key == c.Key).Max(x => x.Version))
            .ToListAsync();

    public async Task<IReadOnlyList<ContentItem>> GetHistoryAsync(string key) =>
        await Read().Where(c => c.Key == key).OrderByDescending(c => c.Version).ToListAsync();

    public Task<ContentItem?> GetVersionAsync(string key, int version) =>
        Read().FirstOrDefaultAsync(c => c.Key == key && c.Version == version);

    public virtual async Task<bool> TryAddAsync(ContentItem item)
    {
        _db.ContentItems.Add(item);
        try
        {
            await _db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // Inside the caller's transaction EF rolled back to its savepoint; forget the rejected row.
            _db.Entry(item).State = EntityState.Detached;
            return false;
        }
    }
}
