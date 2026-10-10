using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IOutboundEmailRepository
{
    Task AddAsync(OutboundEmail email);

    /// <summary>Unsent, not given up, and due (NextAttemptAt empty or passed), oldest first.</summary>
    Task<IReadOnlyList<OutboundEmail>> GetDueAsync(DateTime now, int max);

    /// <summary>Saves; false (and the row is let go) if another instance changed it first.</summary>
    Task<bool> TryUpdateAsync(OutboundEmail email);

    Task UpdateAsync(OutboundEmail email);
}
