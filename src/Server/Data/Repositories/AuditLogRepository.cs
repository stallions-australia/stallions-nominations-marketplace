using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly AppDbContext _db;
    public AuditLogRepository(AppDbContext db) => _db = db;

    public async Task LogAsync(string entityType, Guid entityId, string action, Guid? userId, string? details = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            UserId = userId,
            Details = details,
            OccurredAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<AuditLog>> GetByEntityAsync(string entityType, Guid entityId) =>
        await _db.AuditLogs
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .OrderByDescending(a => a.OccurredAt)
            .ToListAsync();

    public async Task<IReadOnlyList<Guid>> GetEntityIdsWithActionAsync(string entityType, string action) =>
        await _db.AuditLogs
            .Where(a => a.EntityType == entityType && a.Action == action)
            .Select(a => a.EntityId)
            .Distinct()
            .ToListAsync();

    public async Task<(IReadOnlyList<AuditLog> Rows, int Total)> GetPageAsync(IReadOnlyCollection<string> actions,
        DateTime? fromUtc, DateTime? toUtcExclusive, int skip, int take)
    {
        var query = _db.AuditLogs.AsNoTracking().Where(a => actions.Contains(a.Action));
        if (fromUtc is { } start) query = query.Where(a => a.OccurredAt >= start);
        if (toUtcExclusive is { } end) query = query.Where(a => a.OccurredAt < end);

        var total = await query.CountAsync();
        var rows = await query
            .Include(a => a.User)
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Skip(skip).Take(take)
            .ToListAsync();
        return (rows, total);
    }
}
