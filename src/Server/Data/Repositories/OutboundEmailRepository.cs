using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class OutboundEmailRepository : IOutboundEmailRepository
{
    private readonly AppDbContext _db;

    public OutboundEmailRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(OutboundEmail email)
    {
        _db.OutboundEmails.Add(email);
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<OutboundEmail>> GetDueAsync(DateTime now, int max) =>
        await _db.OutboundEmails
            .Where(e => e.SentAt == null && e.FailedAt == null && (e.NextAttemptAt == null || e.NextAttemptAt <= now))
            .OrderBy(e => e.CreatedAt)
            .Take(max)
            .ToListAsync();

    public async Task<bool> TryUpdateAsync(OutboundEmail email)
    {
        try
        {
            await UpdateAsync(email);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.Entry(email).State = EntityState.Detached;
            return false;
        }
    }

    public async Task UpdateAsync(OutboundEmail email)
    {
        _db.OutboundEmails.Update(email);
        await _db.SaveChangesAsync();
    }
}
