namespace Stallions.Server.Data.Entities;

public class AuditLog
{
    public long Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string? Details { get; set; }

    // Navigation properties
    public User? User { get; set; }
}
