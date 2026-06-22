using Microsoft.EntityFrameworkCore;
using Stallions.Server.Auth;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.Terms;
using Stallions.Shared.DTOs.Users;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _repo;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditLogRepository _auditRepo;
    private readonly ITermsRepository _terms;

    public UserService(IUserRepository repo, ICurrentUserService currentUser,
        IAuditLogRepository auditRepo, ITermsRepository terms)
    {
        _repo = repo;
        _currentUser = currentUser;
        _auditRepo = auditRepo;
        _terms = terms;
    }

    public async Task<User?> GetOrCreateCurrentUserAsync()
    {
        var objectId = _currentUser.ObjectId;
        if (objectId == null) return null;

        var user = await _repo.GetByObjectIdAsync(objectId);
        if (user != null) return user;

        // First login — provision user from B2C claims.
        // B2C tokens carry no role claims; all users start as Buyer (PendingVerification)
        // and are promoted to StudFarmAdmin / Staff by a platform admin via the database.
        var role = UserRole.Buyer;
        var status = UserStatus.PendingVerification;

        user = new User
        {
            ObjectId = objectId,
            Email = _currentUser.Email ?? string.Empty,
            DisplayName = _currentUser.DisplayName ?? _currentUser.Email ?? string.Empty,
            Role = role,
            Status = status,
            VerifiedAt = null   // Buyers start unverified; admin promotes via dashboard
        };

        try
        {
            return await _repo.AddAsync(user);
        }
        catch (DbUpdateException)
        {
            // Concurrent first-login: another request inserted the same user — fetch the row that won
            return await _repo.GetByObjectIdAsync(objectId);
        }
    }

    public async Task<User?> GetCurrentUserReadOnlyAsync()
    {
        var objectId = _currentUser.ObjectId;
        if (objectId is null) return null;
        return await _repo.GetByObjectIdAsync(objectId);
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

    public async Task<ServiceResult<UserDto>> GetByIdAsync(Guid userId)
    {
        var user = await _repo.GetByIdAsync(userId);
        return user == null
            ? ServiceResult<UserDto>.NotFound("User not found.")
            : ServiceResult<UserDto>.Ok(MapToDto(user));
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

    public async Task<ServiceResult> AcceptTermsAsync(AcceptTermsRequest request)
    {
        var user = await GetOrCreateCurrentUserAsync();
        if (user == null) return ServiceResult.Forbidden();

        var current = await _terms.GetCurrentAsync();
        if (current == null)
            return ServiceResult.BadRequest("No Terms & Conditions are currently published.");
        if (request.Version != current.Version)
            return ServiceResult.BadRequest("The Terms & Conditions have changed. Please review the latest version.");

        user.AcceptedTermsVersion = current.Version;
        user.AcceptedTermsAt = DateTime.UtcNow;
        await _repo.UpdateAsync(user);
        await _auditRepo.LogAsync("User", user.Id, "TermsAccepted", user.Id,
            $"{{\"Version\":{current.Version}}}");
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> SuppressBidConfirmationAsync()
    {
        var user = await GetOrCreateCurrentUserAsync();
        if (user == null) return ServiceResult.Forbidden();
        user.SuppressBidConfirmation = true;
        await _repo.UpdateAsync(user);
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
        VerifiedAt = u.VerifiedAt,
        AcceptedTermsVersion = u.AcceptedTermsVersion,
        SuppressBidConfirmation = u.SuppressBidConfirmation
    };
}
