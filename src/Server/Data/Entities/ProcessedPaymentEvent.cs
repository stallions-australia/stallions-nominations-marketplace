namespace Stallions.Server.Data.Entities;

/// <summary>Idempotency record: a provider event id is processed at most once.</summary>
public class ProcessedPaymentEvent
{
    public string EventId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
