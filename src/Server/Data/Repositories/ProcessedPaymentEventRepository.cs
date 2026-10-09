using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class ProcessedPaymentEventRepository : IProcessedPaymentEventRepository
{
    private readonly AppDbContext _db;
    public ProcessedPaymentEventRepository(AppDbContext db) => _db = db;

    public async Task<bool> TryClaimAsync(ProcessedPaymentEvent processed)
    {
        // Cheap path for the common duplicate delivery.
        if (await _db.ProcessedPaymentEvents.AnyAsync(p => p.EventId == processed.EventId))
            return false;

        _db.ProcessedPaymentEvents.Add(processed);
        try
        {
            await _db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            // Concurrent claim won the race; keep the scoped context usable.
            _db.Entry(processed).State = EntityState.Detached;
            return false;
        }
    }

    public async Task ReleaseAsync(string eventId)
    {
        var existing = await _db.ProcessedPaymentEvents.FirstOrDefaultAsync(p => p.EventId == eventId);
        if (existing is null) return;
        _db.ProcessedPaymentEvents.Remove(existing);
        await _db.SaveChangesAsync();
    }
}
