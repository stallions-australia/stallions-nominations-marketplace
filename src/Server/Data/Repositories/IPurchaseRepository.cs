using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IPurchaseRepository
{
    Task<Purchase?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<Purchase>> GetByBuyerIdAsync(Guid buyerUserId);
    Task<IReadOnlyList<Purchase>> GetAllAsync();
    Task<Purchase> AddAsync(Purchase purchase);
    Task UpdateAsync(Purchase purchase);
}
