using Microsoft.AspNetCore.Authorization;
using Moq;
using Stallions.Server.Auth;
using Stallions.Server.Data.Entities;
using Stallions.Server.Services;
using Stallions.Shared.Enums;
using System.Security.Claims;

namespace Stallions.Server.Tests.Auth;

public class DbRoleHandlerTests
{
    private readonly Mock<IUserService> _userService = new();

    private DbRoleHandler CreateHandler() => new DbRoleHandler(_userService.Object);

    private static AuthorizationHandlerContext CreateContext(
        bool isAuthenticated,
        DbRoleRequirement requirement)
    {
        var identity = new ClaimsIdentity(isAuthenticated ? "Test" : null);
        var principal = new ClaimsPrincipal(identity);
        return new AuthorizationHandlerContext(
            new[] { requirement },
            principal,
            null);
    }

    [Fact]
    public async Task Unauthenticated_DoesNotSucceedOrFail()
    {
        var req = new DbRoleRequirement(UserRole.Staff);
        var ctx = CreateContext(isAuthenticated: false, req);

        await CreateHandler().HandleAsync(ctx);

        Assert.False(ctx.HasSucceeded);
        Assert.False(ctx.HasFailed);
    }

    [Fact]
    public async Task UserNotFound_DoesNotSucceed()
    {
        _userService.Setup(s => s.GetCurrentUserReadOnlyAsync()).ReturnsAsync((User?)null);
        var req = new DbRoleRequirement(UserRole.Staff);
        var ctx = CreateContext(isAuthenticated: true, req);

        await CreateHandler().HandleAsync(ctx);

        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public async Task ActiveUser_WithMatchingRole_Succeeds()
    {
        var user = new User { Role = UserRole.Staff, Status = UserStatus.Active };
        _userService.Setup(s => s.GetCurrentUserReadOnlyAsync()).ReturnsAsync(user);
        var req = new DbRoleRequirement(UserRole.Staff);
        var ctx = CreateContext(isAuthenticated: true, req);

        await CreateHandler().HandleAsync(ctx);

        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task SuspendedUser_WithMatchingRole_ExplicitlyFails()
    {
        var user = new User { Role = UserRole.Staff, Status = UserStatus.Suspended };
        _userService.Setup(s => s.GetCurrentUserReadOnlyAsync()).ReturnsAsync(user);
        var req = new DbRoleRequirement(UserRole.Staff);
        var ctx = CreateContext(isAuthenticated: true, req);

        await CreateHandler().HandleAsync(ctx);

        Assert.True(ctx.HasFailed);
        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public async Task ActiveUser_WithWrongRole_DoesNotSucceed()
    {
        var user = new User { Role = UserRole.Buyer, Status = UserStatus.Active };
        _userService.Setup(s => s.GetCurrentUserReadOnlyAsync()).ReturnsAsync(user);
        var req = new DbRoleRequirement(UserRole.Staff);
        var ctx = CreateContext(isAuthenticated: true, req);

        await CreateHandler().HandleAsync(ctx);

        Assert.False(ctx.HasSucceeded);
    }
}
