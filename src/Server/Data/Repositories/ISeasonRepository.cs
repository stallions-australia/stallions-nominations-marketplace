using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface ISeasonRepository
{
    Task<Season?> GetByIdAsync(Guid id);
    Task<Season?> GetCurrentOpenSeasonAsync();
    Task<IReadOnlyList<Season>> GetAllAsync();
    Task<Season> AddAsync(Season season);
    Task UpdateAsync(Season season);
}
