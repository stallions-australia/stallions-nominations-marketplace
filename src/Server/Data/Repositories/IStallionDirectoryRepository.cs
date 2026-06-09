using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IStallionDirectoryRepository
{
    Task<StallionDirectory?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<StallionDirectory>> GetAllAsync(bool includeInactive = false, Guid? studDirectoryId = null);
    Task<IReadOnlyList<StallionDirectory>> GetByStudDirectoryIdAsync(Guid studDirectoryId, bool activeOnly = true);
    Task<StallionDirectory> AddAsync(StallionDirectory entry);
    Task UpdateAsync(StallionDirectory entry);
}
