using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface ISavedCardRepository
{
    Task<SavedCard?> GetByUserIdAsync(Guid userId);
    Task<SavedCard> AddAsync(SavedCard card);
    Task UpdateAsync(SavedCard card);
}
