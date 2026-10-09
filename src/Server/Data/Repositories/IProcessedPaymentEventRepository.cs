using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

/// <summary>Result of claiming a provider event id.</summary>
public enum ClaimResult
{
    /// <summary>The caller owns the event and should process it.</summary>
    Claimed,
    /// <summary>The event was already fully processed.</summary>
    AlreadyCompleted,
    /// <summary>Another delivery is processing it right now (a fresh incomplete claim).</summary>
    InProgress
}

public interface IProcessedPaymentEventRepository
{
    /// <summary>
    /// Claims an event id by inserting its row. Returns Claimed when inserted, or when an existing
    /// incomplete claim is older than the claim timeout (abandoned) and has been taken over;
    /// AlreadyCompleted when the event was fully processed; InProgress for a fresh incomplete claim
    /// (including a concurrent request). This saves the context, so it must be called before any
    /// other tracked changes in the request.
    /// </summary>
    Task<ClaimResult> ClaimAsync(ProcessedPaymentEvent processed);

    /// <summary>Marks a claimed event as fully processed.</summary>
    Task MarkCompletedAsync(string eventId);

    /// <summary>Deletes the claim if present, so a provider retry is processed again.</summary>
    Task ReleaseAsync(string eventId);
}
