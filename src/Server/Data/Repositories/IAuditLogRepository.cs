using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IAuditLogRepository
{
    Task LogAsync(string entityType, Guid entityId, string action, Guid? userId, string? details = null);
    Task<IReadOnlyList<AuditLog>> GetByEntityAsync(string entityType, Guid entityId);

    /// <summary>Distinct ids of the entities of this type that have an audit row with this action.</summary>
    Task<IReadOnlyList<Guid>> GetEntityIdsWithActionAsync(string entityType, string action);
}
