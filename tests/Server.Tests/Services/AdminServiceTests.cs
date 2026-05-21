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
    private readonly Mock<IAuditLogRepository> _auditRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<IUserService> _userServiceMock = new();

    private AdminService CreateSut() => new(
        _listingRepoMock.Object,
        _purchaseRepoMock.Object,
        _userRepoMock.Object,
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
}
