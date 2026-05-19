using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id);
    Task<User?> GetByEntraObjectIdAsync(string entraObjectId);
    Task<IReadOnlyList<User>> GetAllAsync(UserRole? role = null, UserStatus? status = null);
    Task<User> AddAsync(User user);
    Task UpdateAsync(User user);
}
