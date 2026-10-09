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
}
