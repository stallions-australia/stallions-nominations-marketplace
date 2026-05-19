using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IStudFarmRepository
{
    Task<StudFarm?> GetByIdAsync(Guid id);
    Task<StudFarm?> GetByUserIdAsync(Guid userId);
    Task<StudFarm> AddAsync(StudFarm studFarm);
    Task UpdateAsync(StudFarm studFarm);
}
