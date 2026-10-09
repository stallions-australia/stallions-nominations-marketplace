using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class ProcessedPaymentEventRepository : IProcessedPaymentEventRepository
{
    /// <summary>An incomplete claim older than this is treated as abandoned (e.g. a crash mid-processing).</summary>
    public static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _db;
    public ProcessedPaymentEventRepository(AppDbContext db) => _db = db;

    public async Task<bool> TryClaimAsync(ProcessedPaymentEvent processed)
    {
        // Cheap path for the common duplicate delivery.
        var existing = await _db.ProcessedPaymentEvents.FirstOrDefaultAsync(p => p.EventId == processed.EventId);
        if (existing is not null)
        {
            if (existing.CompletedAt is not null || existing.ProcessedAt > DateTime.UtcNow - ClaimTimeout)
                return false;

            existing.ProcessedAt = DateTime.UtcNow; // stale incomplete claim: take it over
            await _db.SaveChangesAsync();
            return true;
        }

        _db.ProcessedPaymentEvents.Add(processed);
        try
        {
            await _db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            _db.Entry(processed).State = EntityState.Detached;
            // Only a duplicate means "someone else claimed it"; anything else is a real failure.
            if (await _db.ProcessedPaymentEvents.AsNoTracking().AnyAsync(p => p.EventId == processed.EventId))
                return false;
            throw;
        }
    }

    public async Task MarkCompletedAsync(string eventId)
    {
        var existing = await _db.ProcessedPaymentEvents.FirstOrDefaultAsync(p => p.EventId == eventId);
        if (existing is null) return;
        existing.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task ReleaseAsync(string eventId)
    {
        _db.ChangeTracker.Clear(); // a failed earlier write must not block the delete
        var existing = await _db.ProcessedPaymentEvents.FirstOrDefaultAsync(p => p.EventId == eventId);
        if (existing is null) return;
        _db.ProcessedPaymentEvents.Remove(existing);
        await _db.SaveChangesAsync();
    }
}
