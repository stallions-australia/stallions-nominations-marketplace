using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Listings;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class ListingServiceTests
{
    private readonly Mock<IListingRepository> _listingRepoMock = new();
    private readonly Mock<ISeasonRepository> _seasonRepoMock = new();
    private readonly Mock<IStallionRepository> _stallionRepoMock = new();
    private readonly Mock<IStudFarmRepository> _farmRepoMock = new();
    private readonly Mock<IUserService> _usersMock = new();

    private ListingService CreateSut() => new(
        _listingRepoMock.Object, _seasonRepoMock.Object,
        _stallionRepoMock.Object, _farmRepoMock.Object, _usersMock.Object);

    private static User FarmUser() => new() { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };
    private static StudFarm FarmFor(User u) => new() { Id = Guid.NewGuid(), UserId = u.Id };

    [Fact]
    public async Task CreateAuctionListing_WhenSeasonNotOpen_ReturnsBadRequest()
    {
        var caller = FarmUser(); var farm = FarmFor(caller);
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        var stallion = new Stallion { Id = Guid.NewGuid(), StudFarmId = farm.Id, IsActive = true };
        _stallionRepoMock.Setup(r => r.GetByIdAsync(stallion.Id)).ReturnsAsync(stallion);
        var closedSeason = new Season { Id = Guid.NewGuid(), IsOpen = false };
        _seasonRepoMock.Setup(r => r.GetByIdAsync(closedSeason.Id)).ReturnsAsync(closedSeason);

        var result = await CreateSut().CreateAuctionListingAsync(new CreateAuctionListingRequest
        {
            StallionId = stallion.Id, SeasonId = closedSeason.Id,
            StartingPrice = 5000m, EndDateTime = DateTime.UtcNow.AddDays(7),
            TermsAndConditions = "Standard terms."
        });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task UpdateListing_NeverUpdatesPlatformFeePercent()
    {
        var caller = FarmUser(); var farm = FarmFor(caller);
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        var listing = new AuctionListing
        {
            Id = Guid.NewGuid(), StudFarmId = farm.Id, Status = ListingStatus.Draft,
            PlatformFeePercent = 2.5m, StartingPrice = 8000m
        };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        await CreateSut().UpdateListingAsync(listing.Id, new UpdateListingRequest { StartingPrice = 9000m });

        _listingRepoMock.Verify(r => r.UpdateAsync(It.Is<Listing>(l => l.PlatformFeePercent == 2.5m)), Times.Once);
    }

    [Fact]
    public async Task UpdateListing_WhenCancelled_ReturnsBadRequest()
    {
        var caller = FarmUser(); var farm = FarmFor(caller);
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        var listing = new AuctionListing
        {
            Id = Guid.NewGuid(), StudFarmId = farm.Id, Status = ListingStatus.Cancelled,
            StartingPrice = 8000m
        };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        var result = await CreateSut().UpdateListingAsync(listing.Id, new UpdateListingRequest { Description = "Updated" });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task UpdateListingAsync_AllowsDescriptionEdit_OnActiveAuctionListing()
    {
        // Active listing (PublishedAt set) — description should be editable
        var caller = FarmUser(); var farm = FarmFor(caller);
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        var listing = new AuctionListing
        {
            Id = Guid.NewGuid(), StudFarmId = farm.Id, Status = ListingStatus.Active,
            PublishedAt = DateTime.UtcNow.AddDays(-1),
            StartingPrice = 8000m,
            Description = "Old description"
        };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        var result = await CreateSut().UpdateListingAsync(listing.Id, new UpdateListingRequest { Description = "New description" });

        result.Succeeded.Should().BeTrue();
        _listingRepoMock.Verify(r => r.UpdateAsync(It.Is<Listing>(l => l.Description == "New description")), Times.Once);
    }

    [Fact]
    public async Task UpdateListingAsync_BlocksTermsEdit_AfterPublish()
    {
        // Draft listing but PublishedAt is set (was previously published, then unpublished)
        var caller = FarmUser(); var farm = FarmFor(caller);
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        var listing = new AuctionListing
        {
            Id = Guid.NewGuid(), StudFarmId = farm.Id, Status = ListingStatus.Draft,
            PublishedAt = DateTime.UtcNow.AddDays(-1),   // was published — T&C now locked
            StartingPrice = 8000m,
            TermsAndConditions = "Original T&C"
        };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        var result = await CreateSut().UpdateListingAsync(listing.Id, new UpdateListingRequest { TermsAndConditions = "New T&C" });

        // Call succeeds (not an error) but T&C is silently ignored
        result.Succeeded.Should().BeTrue();
        _listingRepoMock.Verify(r => r.UpdateAsync(It.Is<Listing>(l => l.TermsAndConditions == "Original T&C")), Times.Once);
    }

    [Fact]
    public async Task UnpublishListingAsync_SetsStatusToDraft_DoesNotClearPublishedAt()
    {
        var caller = FarmUser(); var farm = FarmFor(caller);
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        var publishedAt = DateTime.UtcNow.AddDays(-1);
        var listing = new AuctionListing
        {
            Id = Guid.NewGuid(), StudFarmId = farm.Id, Status = ListingStatus.Active,
            PublishedAt = publishedAt,
            StartingPrice = 8000m
        };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        var result = await CreateSut().UnpublishListingAsync(listing.Id);

        result.Succeeded.Should().BeTrue();
        _listingRepoMock.Verify(r => r.UpdateAsync(It.Is<Listing>(l =>
            l.Status == ListingStatus.Draft && l.PublishedAt == publishedAt)), Times.Once);
    }

    [Fact]
    public async Task CloseByStudFarmAsync_SetsCancelledAndClosedAt()
    {
        var caller = FarmUser(); var farm = FarmFor(caller);
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        var listing = new AuctionListing
        {
            Id = Guid.NewGuid(), StudFarmId = farm.Id, Status = ListingStatus.Active,
            StartingPrice = 8000m
        };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        var result = await CreateSut().CloseByStudFarmAsync(listing.Id);

        result.Succeeded.Should().BeTrue();
        _listingRepoMock.Verify(r => r.UpdateAsync(It.Is<Listing>(l =>
            l.Status == ListingStatus.Cancelled && l.ClosedAt != null)), Times.Once);
    }

    [Fact]
    public async Task PublishListing_WhenNoFeeSet_ReturnsBadRequest()
    {
        var caller = FarmUser(); var farm = FarmFor(caller);
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        var listing = new AuctionListing
        {
            Id = Guid.NewGuid(), StudFarmId = farm.Id, Status = ListingStatus.Draft,
            PlatformFeePercent = null, StartingPrice = 8000m
        };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        var result = await CreateSut().PublishListingAsync(listing.Id);

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }
}
