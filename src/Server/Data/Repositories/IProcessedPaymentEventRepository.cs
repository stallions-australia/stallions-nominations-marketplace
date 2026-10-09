using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IProcessedPaymentEventRepository
{
    /// <summary>
    /// Claims an event id by inserting its row. Returns false if the id was already claimed
    /// (including by a concurrent request), unless the existing claim is incomplete and stale,
    /// in which case it is re-claimed. This saves the context, so it must be called before any
    /// other tracked changes in the request.
    /// </summary>
    Task<bool> TryClaimAsync(ProcessedPaymentEvent processed);

    /// <summary>Marks a claimed event as fully processed.</summary>
    Task MarkCompletedAsync(string eventId);

    /// <summary>Deletes the claim if present, so a provider retry is processed again.</summary>
    Task ReleaseAsync(string eventId);
}
