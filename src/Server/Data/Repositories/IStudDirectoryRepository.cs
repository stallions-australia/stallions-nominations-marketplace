using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IStudDirectoryRepository
{
    Task<StudDirectory?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<StudDirectory>> GetAllAsync(bool includeInactive = false);
    Task<StudDirectory> AddAsync(StudDirectory entry);
    Task UpdateAsync(StudDirectory entry);
}
