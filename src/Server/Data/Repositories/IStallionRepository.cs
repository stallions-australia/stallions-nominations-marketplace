using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IStallionRepository
{
    Task<Stallion?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<Stallion>> GetByStudFarmIdAsync(Guid studFarmId);
    Task<IReadOnlyList<Stallion>> GetWithActiveListingsAsync();
    Task<Stallion> AddAsync(Stallion stallion);
    Task UpdateAsync(Stallion stallion);
}
