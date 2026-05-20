using FluentAssertions;
using Moq;
using Stallions.Server.Auth;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Users;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class UserServiceTests
{
    private readonly Mock<IUserRepository> _repoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<IAuditLogRepository> _auditRepoMock = new();
    private UserService CreateSut() => new(_repoMock.Object, _currentUserMock.Object, _auditRepoMock.Object);

    [Fact]
    public async Task GetCurrentUser_WhenUserExists_ReturnsDto()
    {
        var entraOid = Guid.NewGuid().ToString();
        var user = new User { EntraObjectId = entraOid, DisplayName = "Jane", Email = "jane@example.com",
            Role = UserRole.Buyer, Status = UserStatus.Active };
        _currentUserMock.Setup(s => s.EntraObjectId).Returns(entraOid);
        _currentUserMock.Setup(s => s.IsAuthenticated).Returns(true);
        _repoMock.Setup(r => r.GetByEntraObjectIdAsync(entraOid)).ReturnsAsync(user);

        var result = await CreateSut().GetCurrentUserAsync();

        result.Succeeded.Should().BeTrue();
        result.Value!.Email.Should().Be("jane@example.com");
    }

    [Fact]
    public async Task GetCurrentUser_WhenFirstLogin_CreatesNewBuyerUser()
    {
        var entraOid = Guid.NewGuid().ToString();
        _currentUserMock.Setup(s => s.EntraObjectId).Returns(entraOid);
        _currentUserMock.Setup(s => s.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(s => s.Email).Returns("new@example.com");
        _currentUserMock.Setup(s => s.DisplayName).Returns("New User");
        // NOTE: Roles is IReadOnlyList<string>, not string? EntraRole
        _currentUserMock.Setup(s => s.Roles).Returns(new List<string> { "Buyer" }.AsReadOnly());
        _repoMock.Setup(r => r.GetByEntraObjectIdAsync(entraOid)).ReturnsAsync((User?)null);
        _repoMock.Setup(r => r.AddAsync(It.IsAny<User>()))
            .ReturnsAsync((User u) => u);

        var result = await CreateSut().GetCurrentUserAsync();

        result.Succeeded.Should().BeTrue();
        _repoMock.Verify(r => r.AddAsync(It.Is<User>(u =>
            u.Role == UserRole.Buyer && u.Status == UserStatus.PendingVerification)), Times.Once);
    }

    [Fact]
    public async Task VerifyUser_WhenUserIsPendingVerification_SetsStatusActive()
    {
        var staffId = Guid.NewGuid().ToString();
        var staff = new User { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active };
        var buyer = new User { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.PendingVerification };
        _currentUserMock.Setup(s => s.EntraObjectId).Returns(staffId);
        _currentUserMock.Setup(s => s.IsAuthenticated).Returns(true);
        _repoMock.Setup(r => r.GetByEntraObjectIdAsync(staffId)).ReturnsAsync(staff);
        _repoMock.Setup(r => r.GetByIdAsync(buyer.Id)).ReturnsAsync(buyer);

        var result = await CreateSut().VerifyUserAsync(buyer.Id);

        result.Succeeded.Should().BeTrue();
        _repoMock.Verify(r => r.UpdateAsync(It.Is<User>(u =>
            u.Id == buyer.Id &&
            u.Status == UserStatus.Active &&
            u.VerifiedAt.HasValue &&
            u.VerifiedByUserId == staff.Id)), Times.Once);
    }

    [Fact]
    public async Task SuspendUser_WhenUserIsActive_SetsSuspended()
    {
        var staffId = Guid.NewGuid().ToString();
        var staff = new User { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active };
        var buyer = new User { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.Active };
        _currentUserMock.Setup(s => s.EntraObjectId).Returns(staffId);
        _currentUserMock.Setup(s => s.IsAuthenticated).Returns(true);
        _repoMock.Setup(r => r.GetByEntraObjectIdAsync(staffId)).ReturnsAsync(staff);
        _repoMock.Setup(r => r.GetByIdAsync(buyer.Id)).ReturnsAsync(buyer);
        _auditRepoMock.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut().SuspendUserAsync(buyer.Id);

        result.Succeeded.Should().BeTrue();
        _repoMock.Verify(r => r.UpdateAsync(It.Is<User>(u =>
            u.Id == buyer.Id && u.Status == UserStatus.Suspended)), Times.Once);
    }
}
