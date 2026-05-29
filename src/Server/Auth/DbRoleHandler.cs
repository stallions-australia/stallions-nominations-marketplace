using Microsoft.AspNetCore.Authorization;
using Stallions.Server.Services;
using Stallions.Shared.Enums;

namespace Stallions.Server.Auth;

/// <summary>
/// Represents a requirement that the authenticated user must have a specific role
/// (or one of several roles) in the application database, with Active status.
/// </summary>
public class DbRoleRequirement : IAuthorizationRequirement
{
    public IReadOnlyList<UserRole> AllowedRoles { get; }

    public DbRoleRequirement(params UserRole[] roles) =>
        AllowedRoles = roles;
}

/// <summary>
/// Resolves the current user from the database and checks their role and status.
/// Called for every request that hits a policy-protected endpoint.
/// </summary>
public class DbRoleHandler : AuthorizationHandler<DbRoleRequirement>
{
    private readonly IUserService _users;

    public DbRoleHandler(IUserService users) => _users = users;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        DbRoleRequirement requirement)
    {
        // The JWT must already be authenticated before this handler runs.
        if (context.User.Identity?.IsAuthenticated != true)
            return;

        var user = await _users.GetOrCreateCurrentUserAsync();

        if (user is null)
            return;

        if (user.Status == UserStatus.Active &&
            requirement.AllowedRoles.Contains(user.Role))
        {
            context.Succeed(requirement);
        }
    }
}
