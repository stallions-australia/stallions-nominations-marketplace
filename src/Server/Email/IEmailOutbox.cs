namespace Stallions.Server.Email;

/// <summary>
/// Queues an email in the current database transaction, so it is saved only if the change that
/// caused it is. EmailDispatchService sends it shortly after.
/// </summary>
public interface IEmailOutbox
{
    Task EnqueueAsync(OutgoingEmail email, string? relatedEntityType = null, Guid? relatedEntityId = null);
}
