using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public class SubscriptionRepository : ISubscriptionRepository
{
    private readonly AppDbContext _db;
    public SubscriptionRepository(AppDbContext db) => _db = db;

    private IQueryable<StallionSeasonSubscription> WithDetails() =>
        _db.StallionSeasonSubscriptions
            .Include(s => s.Stallion)
            .Include(s => s.Season)
            .Include(s => s.StudFarm);

    public async Task<StallionSeasonSubscription?> GetByIdAsync(Guid id) =>
        await WithDetails().FirstOrDefaultAsync(s => s.Id == id);

    public async Task<StallionSeasonSubscription?> GetByStallionAndSeasonAsync(Guid stallionId, Guid seasonId) =>
        await _db.StallionSeasonSubscriptions
            .FirstOrDefaultAsync(s => s.StallionId == stallionId && s.SeasonId == seasonId);

    public async Task<IReadOnlyList<StallionSeasonSubscription>> GetByStudFarmIdAsync(Guid studFarmId) =>
        await WithDetails()
            .Where(s => s.StudFarmId == studFarmId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

    public async Task<IReadOnlyList<StallionSeasonSubscription>> GetAllAsync(
        Guid? seasonId = null, SubscriptionStatus? status = null)
    {
        var query = WithDetails();
        if (seasonId.HasValue) query = query.Where(s => s.SeasonId == seasonId.Value);
        if (status.HasValue)   query = query.Where(s => s.Status == status.Value);
        return await query.OrderByDescending(s => s.CreatedAt).ToListAsync();
    }

    public async Task<StallionSeasonSubscription> AddAsync(StallionSeasonSubscription subscription)
    {
        _db.StallionSeasonSubscriptions.Add(subscription);
        await _db.SaveChangesAsync();
        return subscription;
    }

    public async Task UpdateAsync(StallionSeasonSubscription subscription)
    {
        _db.StallionSeasonSubscriptions.Update(subscription);
        await _db.SaveChangesAsync();
    }

    public async Task SetPendingCheckoutAsync(StallionSeasonSubscription subscription, string url, DateTime expiresAt)
    {
        var entry = _db.Entry(subscription);
        if (entry.State == EntityState.Detached) _db.StallionSeasonSubscriptions.Attach(subscription);
        // Only these two columns are written; everything else keeps whatever the database holds.
        // (Clearing IsModified restores original values, so do it before assigning the new ones.)
        foreach (var property in entry.Properties)
            property.IsModified = false;
        subscription.PendingCheckoutUrl = url;
        subscription.PendingCheckoutExpiresAt = expiresAt;
        entry.Property(s => s.PendingCheckoutUrl).IsModified = true;
        entry.Property(s => s.PendingCheckoutExpiresAt).IsModified = true;
        await _db.SaveChangesAsync();
    }

    public void Detach(StallionSeasonSubscription subscription) =>
        _db.Entry(subscription).State = EntityState.Detached;
}
