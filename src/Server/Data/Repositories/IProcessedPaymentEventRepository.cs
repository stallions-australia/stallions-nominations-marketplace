using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IProcessedPaymentEventRepository
{
    /// <summary>
    /// Claims an event id by inserting its row. Returns false if the id was already claimed
    /// (including by a concurrent request).
    /// </summary>
    Task<bool> TryClaimAsync(ProcessedPaymentEvent processed);

    /// <summary>Deletes the claim if present, so a provider retry is processed again.</summary>
    Task ReleaseAsync(string eventId);
}
