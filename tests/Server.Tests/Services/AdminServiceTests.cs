using FluentAssertions;
using Moq;
using Stallions.Server.Auth;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Admin;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class AdminServiceTests
{
    private readonly Mock<IListingRepository> _listingRepoMock = new();
    private readonly Mock<IPurchaseRepository> _purchaseRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IStudFarmRepository> _studFarmRepoMock = new();
    private readonly Mock<IAuditLogRepository> _auditRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<IUserService> _userServiceMock = new();

    private AdminService CreateSut() => new(
        _listingRepoMock.Object,
        _purchaseRepoMock.Object,
        _userRepoMock.Object,
        _studFarmRepoMock.Object,
        _auditRepoMock.Object,
        _currentUserMock.Object,
        _userServiceMock.Object);

    [Fact]
    public async Task SetListingFee_WhenListingExists_UpdatesFeeAndWritesAuditLog()
    {
        var listing = new AuctionListing
        {
            Id = Guid.NewGuid(), Status = ListingStatus.Draft, PlatformFeePercent = null
        };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        _currentUserMock.Setup(u => u.EntraObjectId).Returns("staff-oid");
        _userServiceMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(new User { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active });

        var result = await CreateSut().SetListingFeeAsync(listing.Id, new SetListingFeeRequest { PlatformFeePercent = 2.5m });

        result.Succeeded.Should().BeTrue();
        listing.PlatformFeePercent.Should().Be(2.5m);
        _auditRepoMock.Verify(r => r.LogAsync(
            "Listing",
            listing.Id,
            "SetListingFee",
            It.IsAny<Guid?>(),
            It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task SetListingFee_WhenListingNotFound_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _listingRepoMock.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Listing?)null);
        _userServiceMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(new User { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active });

        var result = await CreateSut().SetListingFeeAsync(id, new SetListingFeeRequest { PlatformFeePercent = 2m });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task SetListingFee_WhenFeeOutOfRange_ReturnsBadRequest()
    {
        var listing = new AuctionListing { Id = Guid.NewGuid(), Status = ListingStatus.Draft };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        _userServiceMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(new User { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active });

        var result = await CreateSut().SetListingFeeAsync(listing.Id, new SetListingFeeRequest { PlatformFeePercent = 101m });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task GetAllStudFarmsAsync_ReturnsMappedDtos()
    {
        var user = new User { Id = Guid.NewGuid(), DisplayName = "Alice", Email = "alice@test.com",
            EntraObjectId = "oid1", Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };
        var farm = new StudFarm
        {
            Id = Guid.NewGuid(), Name = "Alpha Stud", ABN = "123", ContactEmail = "farm@test.com",
            UserId = user.Id, User = user, IsActive = true, CreatedAt = DateTime.UtcNow
        };
        _studFarmRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<StudFarm> { farm });

        var result = await CreateSut().GetAllStudFarmsAsync();

        result.Succeeded.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value![0].Name.Should().Be("Alpha Stud");
        result.Value![0].LinkedUserDisplayName.Should().Be("Alice");
        result.Value![0].LinkedUserEmail.Should().Be("alice@test.com");
    }

    [Fact]
    public async Task CreateStudFarmAsync_WhenUserNotFound_ReturnsNotFound()
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

        var result = await CreateSut().CreateStudFarmAsync(new CreateStudFarmRequest
        {
            UserId = Guid.NewGuid(), Name = "New Farm"
        });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task CreateStudFarmAsync_WhenUserIsNotStudFarmAdmin_ReturnsBadRequest()
    {
        var user = new User { Id = Guid.NewGuid(), EntraObjectId = "oid2", Role = UserRole.Buyer,
            Status = UserStatus.Active, DisplayName = "Bob", Email = "bob@test.com" };
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

        var result = await CreateSut().CreateStudFarmAsync(new CreateStudFarmRequest
        {
            UserId = user.Id, Name = "New Farm"
        });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CreateStudFarmAsync_WhenUserAlreadyHasFarm_ReturnsBadRequest()
    {
        var user = new User { Id = Guid.NewGuid(), EntraObjectId = "oid3", Role = UserRole.StudFarmAdmin,
            Status = UserStatus.Active, DisplayName = "Carol", Email = "carol@test.com" };
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
        _studFarmRepoMock.Setup(r => r.GetByUserIdAsync(user.Id))
            .ReturnsAsync(new StudFarm { Id = Guid.NewGuid(), UserId = user.Id, Name = "Existing" });

        var result = await CreateSut().CreateStudFarmAsync(new CreateStudFarmRequest
        {
            UserId = user.Id, Name = "New Farm"
        });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CreateStudFarmAsync_WhenValid_CreatesFarmAndAuditLogs()
    {
        var user = new User
        {
            Id = Guid.NewGuid(), DisplayName = "Alice", Email = "alice@test.com",
            EntraObjectId = "oid4", Role = UserRole.StudFarmAdmin, Status = UserStatus.Active
        };
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
        _studFarmRepoMock.Setup(r => r.GetByUserIdAsync(user.Id)).ReturnsAsync((StudFarm?)null);
        _studFarmRepoMock.Setup(r => r.AddAsync(It.IsAny<StudFarm>()))
            .ReturnsAsync((StudFarm f) => f);
        _userServiceMock.Setup(u => u.GetOrCreateCurrentUserAsync())
            .ReturnsAsync(new User { Id = Guid.NewGuid(), EntraObjectId = "oid-staff",
                Role = UserRole.Staff, Status = UserStatus.Active, DisplayName = "Staff", Email = "staff@test.com" });

        var result = await CreateSut().CreateStudFarmAsync(new CreateStudFarmRequest
        {
            UserId = user.Id, Name = "Alpha Stud", ABN = "123456789"
        });

        result.Succeeded.Should().BeTrue();
        result.Value!.Name.Should().Be("Alpha Stud");
        _auditRepoMock.Verify(r => r.LogAsync("StudFarm", It.IsAny<Guid>(), "CreateStudFarm",
            It.IsAny<Guid?>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ForceListingStatusAsync_WhenListingNotFound_ReturnsNotFound()
    {
        _listingRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Listing?)null);
        _userServiceMock.Setup(u => u.GetOrCreateCurrentUserAsync())
            .ReturnsAsync(new User { Id = Guid.NewGuid(), EntraObjectId = "oid-staff2",
                Role = UserRole.Staff, Status = UserStatus.Active, DisplayName = "Staff", Email = "staff@test.com" });

        var result = await CreateSut().ForceListingStatusAsync(Guid.NewGuid(),
            new ForceListingStatusRequest { Status = "Cancelled" });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ForceListingStatusAsync_WhenInvalidStatus_ReturnsBadRequest()
    {
        var listing = new AuctionListing { Id = Guid.NewGuid(), Status = ListingStatus.Draft };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        _userServiceMock.Setup(u => u.GetOrCreateCurrentUserAsync())
            .ReturnsAsync(new User { Id = Guid.NewGuid(), EntraObjectId = "oid-staff3",
                Role = UserRole.Staff, Status = UserStatus.Active, DisplayName = "Staff", Email = "staff@test.com" });

        var result = await CreateSut().ForceListingStatusAsync(listing.Id,
            new ForceListingStatusRequest { Status = "NotAStatus" });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ForceListingStatusAsync_WhenValid_SetsStatusAndAuditLogs()
    {
        var listing = new AuctionListing { Id = Guid.NewGuid(), Status = ListingStatus.Draft };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        _userServiceMock.Setup(u => u.GetOrCreateCurrentUserAsync())
            .ReturnsAsync(new User { Id = Guid.NewGuid(), EntraObjectId = "oid-staff4",
                Role = UserRole.Staff, Status = UserStatus.Active, DisplayName = "Staff", Email = "staff@test.com" });

        var result = await CreateSut().ForceListingStatusAsync(listing.Id,
            new ForceListingStatusRequest { Status = "Cancelled", Reason = "Quality issue" });

        result.Succeeded.Should().BeTrue();
        listing.Status.Should().Be(ListingStatus.Cancelled);
        _auditRepoMock.Verify(r => r.LogAsync("Listing", listing.Id, "ForceListingStatus",
            It.IsAny<Guid?>(), It.Is<string?>(s => s!.Contains("Quality issue"))), Times.Once);
    }
}
