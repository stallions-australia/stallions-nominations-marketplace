using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class ProcessedPaymentEventRepository : IProcessedPaymentEventRepository
{
    /// <summary>An incomplete claim older than this is treated as abandoned (e.g. a crash mid-processing).</summary>
    public static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _db;
    public ProcessedPaymentEventRepository(AppDbContext db) => _db = db;

    public async Task<ClaimResult> ClaimAsync(ProcessedPaymentEvent processed)
    {
        // Cheap path for the common duplicate delivery.
        var existing = await _db.ProcessedPaymentEvents.FirstOrDefaultAsync(p => p.EventId == processed.EventId);
        if (existing is not null)
        {
            if (existing.CompletedAt is not null) return ClaimResult.AlreadyCompleted;
            if (existing.ProcessedAt > DateTime.UtcNow - ClaimTimeout) return ClaimResult.InProgress;

            existing.ProcessedAt = DateTime.UtcNow; // stale incomplete claim: take it over
            await _db.SaveChangesAsync();
            return ClaimResult.Claimed;
        }

        _db.ProcessedPaymentEvents.Add(processed);
        try
        {
            await _db.SaveChangesAsync();
            return ClaimResult.Claimed;
        }
        catch (DbUpdateException)
        {
            _db.Entry(processed).State = EntityState.Detached;
            // Only a duplicate means "someone else claimed it"; anything else is a real failure.
            var row = await _db.ProcessedPaymentEvents.AsNoTracking().FirstOrDefaultAsync(p => p.EventId == processed.EventId);
            if (row is null) throw;
            return row.CompletedAt is not null ? ClaimResult.AlreadyCompleted : ClaimResult.InProgress;
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
        // A completed claim must survive: a commit that succeeded but reported a transient error
        // still did the work, and its record is what stops a duplicate.
        if (existing is null || existing.CompletedAt is not null) return;
        _db.ProcessedPaymentEvents.Remove(existing);
        await _db.SaveChangesAsync();
    }
}
