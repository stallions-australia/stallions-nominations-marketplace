using Microsoft.EntityFrameworkCore;
using Stallions.Server.Auth;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.Users;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _repo;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditLogRepository _auditRepo;

    public UserService(IUserRepository repo, ICurrentUserService currentUser, IAuditLogRepository auditRepo)
    {
        _repo = repo;
        _currentUser = currentUser;
        _auditRepo = auditRepo;
    }

    public async Task<User?> GetOrCreateCurrentUserAsync()
    {
        var entraOid = _currentUser.EntraObjectId;
        if (entraOid == null) return null;

        var user = await _repo.GetByEntraObjectIdAsync(entraOid);
        if (user != null) return user;

        // First login — provision user from Entra claims
        // Priority-order role selection: Staff > StudFarmAdmin > Buyer
        var role = _currentUser.Roles.Contains("Staff") ? UserRole.Staff
                 : _currentUser.Roles.Contains("StudFarmAdmin") ? UserRole.StudFarmAdmin
                 : UserRole.Buyer;
        var status = role == UserRole.Buyer ? UserStatus.PendingVerification : UserStatus.Active;

        user = new User
        {
            EntraObjectId = entraOid,
            Email = _currentUser.Email ?? string.Empty,
            DisplayName = _currentUser.DisplayName ?? _currentUser.Email ?? string.Empty,
            Role = role,
            Status = status,
            VerifiedAt = role != UserRole.Buyer ? DateTime.UtcNow : null
        };

        try
        {
            return await _repo.AddAsync(user);
        }
        catch (DbUpdateException)
        {
            // Concurrent first-login: another request inserted the same user — fetch the row that won
            return await _repo.GetByEntraObjectIdAsync(entraOid);
        }
    }

    public async Task<ServiceResult<UserDto>> GetCurrentUserAsync()
    {
        if (!_currentUser.IsAuthenticated)
            return ServiceResult<UserDto>.Forbidden();
        var user = await GetOrCreateCurrentUserAsync();
        return user == null
            ? ServiceResult<UserDto>.Forbidden()
            : ServiceResult<UserDto>.Ok(MapToDto(user));
    }

    public async Task<ServiceResult<UserDto>> UpdateCurrentUserAsync(UpdateProfileRequest request)
    {
        var user = await GetOrCreateCurrentUserAsync();
        if (user == null) return ServiceResult<UserDto>.Forbidden();
        if (user.Status == UserStatus.Suspended)
            return ServiceResult<UserDto>.Forbidden("Account is suspended.");
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return ServiceResult<UserDto>.BadRequest("Display name is required.");
        user.DisplayName = request.DisplayName.Trim();
        await _repo.UpdateAsync(user);
        return ServiceResult<UserDto>.Ok(MapToDto(user));
    }

    public async Task<ServiceResult<IReadOnlyList<UserDto>>> GetAllAsync(UserRole? role = null, UserStatus? status = null)
    {
        var users = await _repo.GetAllAsync(role, status);
        return ServiceResult<IReadOnlyList<UserDto>>.Ok(users.Select(MapToDto).ToList());
    }

    public async Task<ServiceResult> VerifyUserAsync(Guid id)
    {
        var caller = await GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult.Forbidden();
        var target = await _repo.GetByIdAsync(id);
        if (target == null) return ServiceResult.NotFound("User not found.");
        if (target.Status != UserStatus.PendingVerification)
            return ServiceResult.BadRequest("User is not pending verification.");
        target.Status = UserStatus.Active;
        target.VerifiedAt = DateTime.UtcNow;
        target.VerifiedByUserId = caller.Id;
        await _repo.UpdateAsync(target);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> SuspendUserAsync(Guid id)
    {
        var caller = await GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult.Forbidden();
        var target = await _repo.GetByIdAsync(id);
        if (target == null) return ServiceResult.NotFound("User not found.");
        if (target.Id == caller.Id) return ServiceResult.BadRequest("Cannot suspend your own account.");
        target.Status = UserStatus.Suspended;
        await _repo.UpdateAsync(target);
        await _auditRepo.LogAsync(
            entityType: "User",
            entityId: id,
            action: "Suspend",
            userId: caller.Id,
            details: $"User {target.Email} suspended by {caller.Email}");
        return ServiceResult.Ok();
    }

    private static UserDto MapToDto(User u) => new()
    {
        Id = u.Id,
        DisplayName = u.DisplayName,
        Email = u.Email,
        Role = u.Role.ToString(),
        Status = u.Status.ToString(),
        CreatedAt = u.CreatedAt,
        VerifiedAt = u.VerifiedAt
    };
}
