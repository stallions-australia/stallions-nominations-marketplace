using Stallions.Server.Data.Entities;
using Stallions.Shared.DTOs.Users;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public interface IUserService
{
    Task<ServiceResult<UserDto>> GetCurrentUserAsync();
    Task<ServiceResult<UserDto>> UpdateCurrentUserAsync(UpdateProfileRequest request);
    Task<ServiceResult<IReadOnlyList<UserDto>>> GetAllAsync(UserRole? role = null, UserStatus? status = null);
    Task<ServiceResult<UserDto>> GetByIdAsync(Guid userId);
    Task<ServiceResult> VerifyUserAsync(Guid id);
    Task<ServiceResult> SuspendUserAsync(Guid id);
    Task<User?> GetOrCreateCurrentUserAsync();
}
