using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IOfferRepository
{
    Task AddAsync(Offer offer);
    Task UpdateAsync(Offer offer);
    /// <summary>With the buyer and the listing's Stallion, Season, StudFarm and owner.</summary>
    Task<Offer?> GetByIdAsync(Guid id);
    Task<Offer?> GetPendingAsync(Guid listingId, Guid buyerUserId);
    /// <summary>The buyer's most recent offer on the listing (any status).</summary>
    Task<Offer?> GetLatestForBuyerAsync(Guid listingId, Guid buyerUserId);
    /// <summary>The buyer's offers, newest first, with listing details.</summary>
    Task<IReadOnlyList<Offer>> GetByBuyerAsync(Guid buyerUserId);
    /// <summary>All offers on the listing, newest first, with buyers.</summary>
    Task<IReadOnlyList<Offer>> GetForListingAsync(Guid listingId);
    /// <summary>Pending offers past their expiry, or whose listing is no longer Active.</summary>
    Task<IReadOnlyList<Guid>> GetIdsToExpireAsync(DateTime now, int max);
    Task<IReadOnlyList<Guid>> GetPendingIdsForListingAsync(Guid listingId);
}
