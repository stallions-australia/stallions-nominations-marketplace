namespace Stallions.Server.Data.Entities;

/// <summary>
/// An email waiting to be sent (or sent). Added in the same transaction as the change that
/// caused it, then sent by EmailDispatchService.
/// </summary>
public class OutboundEmail : IHasConcurrencyStamp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ToAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public string TextBody { get; set; } = string.Empty;
    /// <summary>Which email this is, e.g. "WonAndCharged" — for support and tests.</summary>
    public string Template { get; set; } = string.Empty;
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public int Attempts { get; set; }
    /// <summary>Not before this time. Also used as a short lease while one instance is sending.</summary>
    public DateTime? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    /// <summary>Set when the dispatcher gives up.</summary>
    public DateTime? FailedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
