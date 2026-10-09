using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public interface ISubscriptionRepository
{
    Task<StallionSeasonSubscription?> GetByIdAsync(Guid id);
    Task<StallionSeasonSubscription?> GetByStallionAndSeasonAsync(Guid stallionId, Guid seasonId);
    Task<IReadOnlyList<StallionSeasonSubscription>> GetByStudFarmIdAsync(Guid studFarmId);
    Task<IReadOnlyList<StallionSeasonSubscription>> GetAllAsync(Guid? seasonId = null, SubscriptionStatus? status = null);
    Task<StallionSeasonSubscription> AddAsync(StallionSeasonSubscription subscription);
    Task UpdateAsync(StallionSeasonSubscription subscription);

    /// <summary>Saves the open checkout page, writing only those two columns so a concurrent status change is never overwritten.</summary>
    Task SetPendingCheckoutAsync(StallionSeasonSubscription subscription, string url, DateTime expiresAt);

    /// <summary>Stops tracking an entity (e.g. after a failed insert) so it is not saved again.</summary>
    void Detach(StallionSeasonSubscription subscription);
}
