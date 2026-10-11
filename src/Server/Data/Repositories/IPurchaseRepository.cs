using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IPurchaseRepository
{
    Task<Purchase?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<Purchase>> GetByBuyerIdAsync(Guid buyerUserId);
    /// <summary>The buyer's most recent sale record on the listing, if any.</summary>
    Task<Purchase?> GetByListingAndBuyerAsync(Guid listingId, Guid buyerUserId);
    Task<IReadOnlyList<Purchase>> GetAllAsync();
    /// <summary>
    /// Pending sale records due a charge attempt: never attempted, a retry requested after a new
    /// card, the grace period over, or an attempt interrupted (started at or before interruptedBefore).
    /// </summary>
    Task<IReadOnlyList<Guid>> GetIdsDueForChargeAsync(DateTime now, DateTime interruptedBefore, int max);
    /// <summary>With the buyer and the listing's Stallion, Season, StudFarm and owner.</summary>
    Task<Purchase?> GetForChargeAsync(Guid id);
    /// <summary>The buyer's Pending sale records whose charge has failed (in the grace period).</summary>
    Task<IReadOnlyList<Purchase>> GetAwaitingCardRetryAsync(Guid buyerUserId);
    Task<Purchase> AddAsync(Purchase purchase);
    Task UpdateAsync(Purchase purchase);
}
