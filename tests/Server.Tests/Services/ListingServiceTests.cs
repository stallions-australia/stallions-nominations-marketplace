using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Infrastructure;
using Stallions.Server.Sales;
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
    private readonly Mock<IPlatformSettingsRepository> _settingsRepoMock = new();
    private readonly Mock<ISubscriptionService> _subscriptionsMock = new();
    private readonly Mock<IAuditLogRepository> _auditMock = new();
    private readonly Mock<IOfferExpirer> _offerExpirerMock = new();

    public ListingServiceTests()
    {
        // Default: the stallion has a paid listing fee, so publish tests not about the guard pass it.
        _subscriptionsMock.Setup(s => s.HasActiveSubscriptionAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(true);
        // Default: no bids on any auction.
        _listingRepoMock.Setup(r => r.GetBidAggregatesAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(new Dictionary<Guid, (int Count, decimal? Highest)>());
        _settingsRepoMock.Setup(r => r.GetAsync()).ReturnsAsync(new PlatformSettings
        {
            BuyerFeeIncGst = 150m, StandardListingFeeIncGst = 990m, MinimumBidIncrement = 25m,
            ChargeGracePeriodHours = 2, OfferExpiryDays = 7
        });
    }

    private ListingService CreateSut() => new(
        _listingRepoMock.Object, _seasonRepoMock.Object,
        _stallionRepoMock.Object, _farmRepoMock.Object, _usersMock.Object,
        _settingsRepoMock.Object, _subscriptionsMock.Object, _auditMock.Object, _offerExpirerMock.Object);

    private void SetBuyerFeeSetting(decimal fee) =>
        _settingsRepoMock.Setup(r => r.GetAsync()).ReturnsAsync(new PlatformSettings
        {
            BuyerFeeIncGst = fee, StandardListingFeeIncGst = 990m, MinimumBidIncrement = 25m,
            ChargeGracePeriodHours = 2, OfferExpiryDays = 7
        });

    private (User caller, StudFarm farm) SignedInFarm()
    {
        var caller = FarmUser(); var farm = FarmFor(caller);
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        return (caller, farm);
    }

    private static AuctionListing DraftAuction(Guid farmId, decimal? buyerFee = null) => new()
    {
        Id = Guid.NewGuid(), StudFarmId = farmId, Status = ListingStatus.Draft,
        BuyerFeeIncGst = buyerFee,
        EndDateTime = DateTime.UtcNow.AddDays(7)
    };

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
            EndDateTime = DateTime.UtcNow.AddDays(7),
            TermsAndConditions = "Standard terms."
        });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task UpdateListing_NeverChangesBuyerFee()
    {
        var (_, farm) = SignedInFarm();
        var listing = DraftAuction(farm.Id, buyerFee: 150m);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        await CreateSut().UpdateListingAsync(listing.Id, new UpdateListingRequest { ReservePrice = 9000m });

        _listingRepoMock.Verify(r => r.UpdateAsync(It.Is<Listing>(l => l.BuyerFeeIncGst == 150m)), Times.Once);
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
            PublishedAt = publishedAt, EndDateTime = DateTime.UtcNow.AddDays(1),
        };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        var result = await CreateSut().UnpublishListingAsync(listing.Id);

        result.Succeeded.Should().BeTrue();
        _listingRepoMock.Verify(r => r.UpdateAsync(It.Is<Listing>(l =>
            l.Status == ListingStatus.Draft && l.PublishedAt == publishedAt)), Times.Once);
    }

    [Theory]
    [InlineData(ListingStatus.AwaitingPayment)]
    [InlineData(ListingStatus.Unsold)]
    public async Task CloseByStudFarmAsync_AfterTheAuctionClosed_IsRejected(ListingStatus status)
    {
        var caller = FarmUser(); var farm = FarmFor(caller);
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        var listing = new AuctionListing { Id = Guid.NewGuid(), StudFarmId = farm.Id, Status = status };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        var result = await CreateSut().CloseByStudFarmAsync(listing.Id);

        result.Succeeded.Should().BeFalse();
        _listingRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Listing>()), Times.Never);
    }

    [Fact]
    public async Task CloseByStudFarmAsync_AfterTheEndTime_IsRejected()
    {
        var (_, farm) = SignedInFarm();
        var listing = new AuctionListing
        {
            Id = Guid.NewGuid(), StudFarmId = farm.Id, Status = ListingStatus.Active,
            EndDateTime = DateTime.UtcNow.AddMinutes(-1)
        };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        var result = await CreateSut().CloseByStudFarmAsync(listing.Id);

        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Be("This auction has ended and is being closed.");
        _listingRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Listing>()), Times.Never);
    }

    [Fact]
    public async Task UnpublishListingAsync_AfterTheEndTime_IsRejected()
    {
        var (_, farm) = SignedInFarm();
        var listing = new AuctionListing
        {
            Id = Guid.NewGuid(), StudFarmId = farm.Id, Status = ListingStatus.Active,
            EndDateTime = DateTime.UtcNow.AddMinutes(-1)
        };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        var result = await CreateSut().UnpublishListingAsync(listing.Id);

        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Be("This auction has ended and is being closed.");
        _listingRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Listing>()), Times.Never);
    }

    [Fact]
    public async Task CancelListingAsync_AfterTheEndTime_IsRejected()
    {
        var listing = new AuctionListing
        {
            Id = Guid.NewGuid(), Status = ListingStatus.Active,
            EndDateTime = DateTime.UtcNow.AddMinutes(-1)
        };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        var result = await CreateSut().CancelListingAsync(listing.Id);

        result.HttpStatusCode.Should().Be(400);
        _listingRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Listing>()), Times.Never);
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
            EndDateTime = DateTime.UtcNow.AddDays(1),
        };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        var result = await CreateSut().CloseByStudFarmAsync(listing.Id);

        result.Succeeded.Should().BeTrue();
        _listingRepoMock.Verify(r => r.UpdateAsync(It.Is<Listing>(l =>
            l.Status == ListingStatus.Cancelled && l.ClosedAt != null)), Times.Once);
    }

    [Fact]
    public async Task PublishListing_FirstPublish_SnapshotsBuyerFeeFromSettings()
    {
        var (_, farm) = SignedInFarm();
        var listing = DraftAuction(farm.Id);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        SetBuyerFeeSetting(150m);

        var result = await CreateSut().PublishListingAsync(listing.Id);

        result.Succeeded.Should().BeTrue();
        listing.Status.Should().Be(ListingStatus.Active);
        listing.BuyerFeeIncGst.Should().Be(150m);
    }

    [Fact]
    public async Task PublishListing_Republish_KeepsOriginalBuyerFee()
    {
        // Published at $150, unpublished, then Staff raised the setting to $200.
        var (_, farm) = SignedInFarm();
        var listing = DraftAuction(farm.Id, buyerFee: 150m);
        listing.PublishedAt = DateTime.UtcNow.AddDays(-2);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        SetBuyerFeeSetting(200m);

        var result = await CreateSut().PublishListingAsync(listing.Id);

        result.Succeeded.Should().BeTrue();
        listing.BuyerFeeIncGst.Should().Be(150m);
    }

    [Fact]
    public async Task GetById_AfterSettingsChange_StillShowsSnapshottedBuyerFee()
    {
        var listing = DraftAuction(Guid.NewGuid(), buyerFee: 150m);
        listing.Status = ListingStatus.Active;
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        SetBuyerFeeSetting(200m);

        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync((User?)null); // anonymous

        var result = await CreateSut().GetByIdAsync(listing.Id);

        // Visible to buyers (isStaff: false) and unaffected by the new setting.
        result.Value!.BuyerFeeIncGst.Should().Be(150m);
    }

    [Fact]
    public async Task PublishListing_WithoutActiveSubscription_ReturnsBadRequestNamingTheSeason()
    {
        var (_, farm) = SignedInFarm();
        var listing = DraftAuction(farm.Id);
        listing.StallionId = Guid.NewGuid();
        listing.SeasonId = Guid.NewGuid();
        listing.Season = new Season { Id = listing.SeasonId, Name = "2026 Season" };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        _subscriptionsMock.Setup(s => s.HasActiveSubscriptionAsync(listing.StallionId, listing.SeasonId))
            .ReturnsAsync(false);

        var result = await CreateSut().PublishListingAsync(listing.Id);

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Be("This stallion has no paid listing fee for 2026 Season. Contact Stallions Australia.");
        listing.Status.Should().Be(ListingStatus.Draft);
        listing.BuyerFeeIncGst.Should().BeNull();
        _listingRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Listing>()), Times.Never);
    }

    [Fact]
    public async Task PublishListing_WithActiveSubscription_Publishes()
    {
        var (_, farm) = SignedInFarm();
        var listing = DraftAuction(farm.Id);
        listing.StallionId = Guid.NewGuid();
        listing.SeasonId = Guid.NewGuid();
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        _subscriptionsMock.Setup(s => s.HasActiveSubscriptionAsync(listing.StallionId, listing.SeasonId))
            .ReturnsAsync(true);

        var result = await CreateSut().PublishListingAsync(listing.Id);

        result.Succeeded.Should().BeTrue();
        listing.Status.Should().Be(ListingStatus.Active);
    }

    // ── Auction rules: increment from settings, hidden reserve ─────────────

    [Fact]
    public async Task CreateAuctionListing_TakesMinimumBidIncrementFromSettings()
    {
        var (_, farm) = SignedInFarm();
        var stallion = new Stallion { Id = Guid.NewGuid(), StudFarmId = farm.Id, IsActive = true };
        _stallionRepoMock.Setup(r => r.GetByIdAsync(stallion.Id)).ReturnsAsync(stallion);
        var season = new Season { Id = Guid.NewGuid(), IsOpen = true };
        _seasonRepoMock.Setup(r => r.GetByIdAsync(season.Id)).ReturnsAsync(season);
        _settingsRepoMock.Setup(r => r.GetAsync()).ReturnsAsync(new PlatformSettings
        {
            BuyerFeeIncGst = 150m, StandardListingFeeIncGst = 990m, MinimumBidIncrement = 50m,
            ChargeGracePeriodHours = 2, OfferExpiryDays = 7
        });
        AuctionListing? added = null;
        _listingRepoMock.Setup(r => r.AddAsync(It.IsAny<Listing>()))
            .ReturnsAsync((Listing l) => { added = (AuctionListing)l; return l; });

        var result = await CreateSut().CreateAuctionListingAsync(new CreateAuctionListingRequest
        {
            StallionId = stallion.Id, SeasonId = season.Id, ReservePrice = 20000m,
            EndDateTime = DateTime.UtcNow.AddDays(7), TermsAndConditions = "Standard terms."
        });

        result.Succeeded.Should().BeTrue(result.Error);
        added!.MinimumBidIncrement.Should().Be(50m);
        ((AuctionListingDto)result.Value!).MinimumBidIncrement.Should().Be(50m);
    }

    private AuctionListing ActiveAuctionWithReserve(Guid farmId, decimal? reserve, decimal? highestBid)
    {
        var listing = DraftAuction(farmId, buyerFee: 150m);
        listing.Status = ListingStatus.Active;
        listing.ReservePrice = reserve;
        listing.IsNoReserve = reserve == null;
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        var aggregates = new Dictionary<Guid, (int Count, decimal? Highest)>();
        if (highestBid.HasValue) aggregates[listing.Id] = (2, highestBid);
        _listingRepoMock.Setup(r => r.GetBidAggregatesAsync(It.Is<IEnumerable<Guid>>(ids => ids.Contains(listing.Id))))
            .ReturnsAsync(aggregates);
        return listing;
    }

    private void SignInAs(UserRole role, StudFarm? farm = null)
    {
        var user = new User { Id = farm?.UserId ?? Guid.NewGuid(), Role = role, Status = UserStatus.Active };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(user);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(user.Id)).ReturnsAsync(farm);
    }

    [Fact]
    public async Task GetById_HidesReserveAmountFromBuyer_ButReportsReserveMet()
    {
        var listing = ActiveAuctionWithReserve(Guid.NewGuid(), reserve: 20000m, highestBid: 21000m);
        SignInAs(UserRole.Buyer);

        var dto = (AuctionListingDto)(await CreateSut().GetByIdAsync(listing.Id)).Value!;

        dto.ReservePrice.Should().BeNull();
        dto.ReserveMet.Should().BeTrue();
        dto.CurrentHighestBidIncGst.Should().Be(21000m);
    }

    [Fact]
    public async Task GetById_HidesReserveAmountFromAnonymousVisitor()
    {
        var listing = ActiveAuctionWithReserve(Guid.NewGuid(), reserve: 20000m, highestBid: null);
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync((User?)null);

        var dto = (AuctionListingDto)(await CreateSut().GetByIdAsync(listing.Id)).Value!;

        dto.ReservePrice.Should().BeNull();
    }

    [Fact]
    public async Task GetById_HidesReserveAmountFromAnotherFarmsAdmin()
    {
        var listing = ActiveAuctionWithReserve(Guid.NewGuid(), reserve: 20000m, highestBid: null);
        SignInAs(UserRole.StudFarmAdmin, new StudFarm { Id = Guid.NewGuid(), UserId = Guid.NewGuid() });

        var dto = (AuctionListingDto)(await CreateSut().GetByIdAsync(listing.Id)).Value!;

        dto.ReservePrice.Should().BeNull();
    }

    [Fact]
    public async Task GetById_ShowsReserveAmountToOwningStudAdmin()
    {
        var farm = new StudFarm { Id = Guid.NewGuid(), UserId = Guid.NewGuid() };
        var listing = ActiveAuctionWithReserve(farm.Id, reserve: 20000m, highestBid: null);
        SignInAs(UserRole.StudFarmAdmin, farm);

        var dto = (AuctionListingDto)(await CreateSut().GetByIdAsync(listing.Id)).Value!;

        dto.ReservePrice.Should().Be(20000m);
    }

    [Fact]
    public async Task GetById_ShowsReserveAmountToStaff()
    {
        var listing = ActiveAuctionWithReserve(Guid.NewGuid(), reserve: 20000m, highestBid: null);
        SignInAs(UserRole.Staff);

        var dto = (AuctionListingDto)(await CreateSut().GetByIdAsync(listing.Id)).Value!;

        dto.ReservePrice.Should().Be(20000m);
    }

    [Theory]
    [InlineData(20000, null, null)]      // no bids yet → unknown
    [InlineData(null, 5000, null)]       // no reserve → not applicable
    [InlineData(20000, 19975, false)]    // below reserve
    [InlineData(20000, 20000, true)]     // at reserve
    [InlineData(20000, 25000, true)]     // above reserve
    public async Task GetById_ReserveMet_ForEachCase(int? reserve, int? highestBid, bool? expected)
    {
        var listing = ActiveAuctionWithReserve(Guid.NewGuid(), reserve, highestBid);
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync((User?)null);

        var dto = (AuctionListingDto)(await CreateSut().GetByIdAsync(listing.Id)).Value!;

        dto.ReserveMet.Should().Be(expected);
        dto.IsNoReserve.Should().Be(reserve == null);
    }

    [Fact]
    public async Task GetById_ReserveNotSetAndNotFlagged_IsTreatedAsNoReserve()
    {
        var listing = ActiveAuctionWithReserve(Guid.NewGuid(), reserve: null, highestBid: 5000m);
        listing.IsNoReserve = false; // stud left the reserve blank without ticking "No reserve"
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync((User?)null);

        var dto = (AuctionListingDto)(await CreateSut().GetByIdAsync(listing.Id)).Value!;

        dto.IsNoReserve.Should().BeTrue();
        dto.ReserveMet.Should().BeNull();
    }

    [Fact]
    public async Task Relist_NewListingHasNoBuyerFeeUntilPublished()
    {
        var (_, farm) = SignedInFarm();
        var expired = DraftAuction(farm.Id, buyerFee: 150m);
        expired.Status = ListingStatus.Expired;
        _listingRepoMock.Setup(r => r.GetByIdAsync(expired.Id)).ReturnsAsync(expired);
        Listing? added = null;
        _listingRepoMock.Setup(r => r.AddAsync(It.IsAny<Listing>()))
            .ReturnsAsync((Listing l) => { added = l; return l; });

        var result = await CreateSut().RelistAsync(expired.Id);

        result.Succeeded.Should().BeTrue();
        added!.BuyerFeeIncGst.Should().BeNull();
        added.Status.Should().Be(ListingStatus.Draft);
    }

    private static AuctionListing UnsoldAuction(Guid farmId, DateOnly seasonEnd) => new()
    {
        Id = Guid.NewGuid(), StudFarmId = farmId, StallionId = Guid.NewGuid(), SeasonId = Guid.NewGuid(),
        Status = ListingStatus.Unsold, CloseReason = ListingCloseReason.ReserveNotMet,
        EndDateTime = DateTime.UtcNow.AddDays(-1), BuyerFeeIncGst = 150m, PublishedAt = DateTime.UtcNow.AddDays(-8),
        TermsAndConditions = "Stud terms", Description = "Lovely type",
        Season = new Season { Name = "2026 Season", EndDate = seasonEnd }
    };

    private AuctionListing ConvertibleAuction(Guid farmId)
    {
        var auction = UnsoldAuction(farmId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60)));
        _listingRepoMock.Setup(r => r.GetByIdAsync(auction.Id)).ReturnsAsync(auction);
        _subscriptionsMock.Setup(s => s.HasActiveSubscriptionAsync(auction.StallionId, auction.SeasonId)).ReturnsAsync(true);
        _listingRepoMock.Setup(r => r.AddAsync(It.IsAny<Listing>())).ReturnsAsync((Listing l) => l);
        SetBuyerFeeSetting(175m);
        return auction;
    }

    [Fact]
    public async Task Convert_CreatesALiveMakeAnOfferListing_FromTheUnsoldLot()
    {
        var (_, farm) = SignedInFarm();
        var auction = ConvertibleAuction(farm.Id);
        Listing? added = null;
        _listingRepoMock.Setup(r => r.AddAsync(It.IsAny<Listing>())).Callback<Listing>(l => added = l).ReturnsAsync((Listing l) => l);

        var result = await CreateSut().ConvertToMakeAnOfferAsync(auction.Id);

        result.HttpStatusCode.Should().Be(201);
        var mao = added.Should().BeOfType<MakeAnOfferListing>().Subject;
        mao.SourceAuctionListingId.Should().Be(auction.Id);
        mao.Status.Should().Be(ListingStatus.Active);
        mao.ListingType.Should().Be(ListingType.MakeAnOffer);
        mao.BuyerFeeIncGst.Should().Be(175m, "the fee is snapshotted from current settings, like a publish");
        mao.PublishedAt.Should().NotBeNull();
        mao.TermsAndConditions.Should().Be("Stud terms");
        mao.Description.Should().Be("Lovely type");
        mao.ExpiresAt.Should().Be(SydneyTime.EndOfDayUtc(auction.Season.EndDate));
        result.Value.Should().BeOfType<MakeAnOfferListingDto>();
        _auditMock.Verify(a => a.LogAsync("Listing", mao.Id, "ConvertedToMakeAnOffer", It.IsAny<Guid?>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task Convert_SomeoneElsesLot_IsForbidden()
    {
        SignedInFarm();
        var auction = ConvertibleAuction(Guid.NewGuid());

        (await CreateSut().ConvertToMakeAnOfferAsync(auction.Id)).HttpStatusCode.Should().Be(403);
    }

    [Theory]
    [InlineData(ListingStatus.Active)]
    [InlineData(ListingStatus.Sold)]
    [InlineData(ListingStatus.Cancelled)]
    public async Task Convert_OnlyUnsoldLots(ListingStatus status)
    {
        var (_, farm) = SignedInFarm();
        var auction = ConvertibleAuction(farm.Id);
        auction.Status = status;

        (await CreateSut().ConvertToMakeAnOfferAsync(auction.Id)).HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Convert_AfterTheSeasonEnded_IsRejected()
    {
        var (_, farm) = SignedInFarm();
        var auction = ConvertibleAuction(farm.Id);
        auction.Season.EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3));

        var result = await CreateSut().ConvertToMakeAnOfferAsync(auction.Id);

        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Contain("season has ended");
    }

    [Fact]
    public async Task Convert_WithoutAPaidListingFee_IsRejected()
    {
        var (_, farm) = SignedInFarm();
        var auction = ConvertibleAuction(farm.Id);
        _subscriptionsMock.Setup(s => s.HasActiveSubscriptionAsync(auction.StallionId, auction.SeasonId)).ReturnsAsync(false);

        (await CreateSut().ConvertToMakeAnOfferAsync(auction.Id)).HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Convert_Twice_IsRejected()
    {
        var (_, farm) = SignedInFarm();
        var auction = ConvertibleAuction(farm.Id);
        _listingRepoMock.Setup(r => r.GetActiveMakeAnOfferForAuctionAsync(auction.Id))
            .ReturnsAsync(new MakeAnOfferListing { Id = Guid.NewGuid(), Status = ListingStatus.Active });

        var result = await CreateSut().ConvertToMakeAnOfferAsync(auction.Id);

        result.HttpStatusCode.Should().Be(400);
        _listingRepoMock.Verify(r => r.AddAsync(It.IsAny<Listing>()), Times.Never);
    }

    [Fact]
    public async Task Unpublish_AMakeAnOfferListing_IsRejected()
    {
        var (_, farm) = SignedInFarm();
        var mao = new MakeAnOfferListing { Id = Guid.NewGuid(), StudFarmId = farm.Id, Status = ListingStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddDays(10) };
        _listingRepoMock.Setup(r => r.GetByIdAsync(mao.Id)).ReturnsAsync(mao);

        var result = await CreateSut().UnpublishListingAsync(mao.Id);

        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Contain("close it instead");
    }

    [Fact]
    public async Task GetById_MapsAMakeAnOfferListing_WithoutAnyPrice()
    {
        var mao = new MakeAnOfferListing
        {
            Id = Guid.NewGuid(), Status = ListingStatus.Active, ListingType = ListingType.MakeAnOffer,
            ExpiresAt = new DateTime(2027, 1, 31, 12, 59, 59, DateTimeKind.Utc), BuyerFeeIncGst = 150m,
            Stallion = new Stallion { Name = "Snitzel" }, Season = new Season { Name = "2026 Season" }, StudFarm = new StudFarm { Name = "Arrowfield" }
        };
        _listingRepoMock.Setup(r => r.GetByIdAsync(mao.Id)).ReturnsAsync(mao);

        var dto = (await CreateSut().GetByIdAsync(mao.Id)).Value.Should().BeOfType<MakeAnOfferListingDto>().Subject;

        dto.ListingType.Should().Be("MakeAnOffer");
        dto.ExpiresAt.Should().Be(mao.ExpiresAt);
        dto.BuyerFeeIncGst.Should().Be(150m);
    }

    [Fact]
    public async Task ListingCards_IncludeMakeAnOfferListings()
    {
        var mao = new MakeAnOfferListing
        {
            Id = Guid.NewGuid(), Status = ListingStatus.Active, ListingType = ListingType.MakeAnOffer,
            ExpiresAt = DateTime.UtcNow.AddDays(30), Stallion = new Stallion { Name = "Snitzel" },
            StudFarm = new StudFarm { Name = "Arrowfield" }, Season = new Season { Name = "2026 Season" }
        };
        _listingRepoMock.Setup(r => r.GetActiveAsync(null, null, ListingType.MakeAnOffer)).ReturnsAsync(new List<Listing> { mao });

        var cards = (await CreateSut().GetListingCardsAsync(null, null, "MakeAnOffer")).Value!;

        cards.Single().ListingType.Should().Be("MakeAnOffer");
        cards.Single().OffersUntil.Should().Be(mao.ExpiresAt);
        cards.Single().CurrentHighestBidIncGst.Should().BeNull();
    }

    [Fact]
    public async Task ClosingAMakeAnOfferListing_ExpiresItsPendingOffers()
    {
        var (_, farm) = SignedInFarm();
        var mao = new MakeAnOfferListing { Id = Guid.NewGuid(), StudFarmId = farm.Id, Status = ListingStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddDays(10) };
        _listingRepoMock.Setup(r => r.GetByIdAsync(mao.Id)).ReturnsAsync(mao);

        (await CreateSut().CloseByStudFarmAsync(mao.Id)).Succeeded.Should().BeTrue();

        mao.Status.Should().Be(ListingStatus.Cancelled);
        _offerExpirerMock.Verify(x => x.ExpirePendingOffersAsync(mao.Id), Times.Once);
    }

    [Fact]
    public async Task ClosingAnAuction_DoesNotTouchOffers()
    {
        var (_, farm) = SignedInFarm();
        var auction = DraftAuction(farm.Id);
        auction.Status = ListingStatus.Active;
        _listingRepoMock.Setup(r => r.GetByIdAsync(auction.Id)).ReturnsAsync(auction);

        await CreateSut().CloseByStudFarmAsync(auction.Id);

        _offerExpirerMock.Verify(x => x.ExpirePendingOffersAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task CloseByStudFarmAsync_WhenTheListingChangedUnderneath_IsAConflict()
    {
        var (_, farm) = SignedInFarm();
        var mao = new MakeAnOfferListing { Id = Guid.NewGuid(), StudFarmId = farm.Id, Status = ListingStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddDays(10) };
        _listingRepoMock.Setup(r => r.GetByIdAsync(mao.Id)).ReturnsAsync(mao);
        _listingRepoMock.Setup(r => r.UpdateAsync(It.IsAny<Listing>())).ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException());

        var result = await CreateSut().CloseByStudFarmAsync(mao.Id);

        result.HttpStatusCode.Should().Be(409);
        _offerExpirerMock.Verify(x => x.ExpirePendingOffersAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task CancelListingAsync_WhenTheListingChangedUnderneath_IsAConflict()
    {
        var mao = new MakeAnOfferListing { Id = Guid.NewGuid(), Status = ListingStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddDays(10) };
        _listingRepoMock.Setup(r => r.GetByIdAsync(mao.Id)).ReturnsAsync(mao);
        _listingRepoMock.Setup(r => r.UpdateAsync(It.IsAny<Listing>())).ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException());

        var result = await CreateSut().CancelListingAsync(mao.Id);

        result.HttpStatusCode.Should().Be(409);
    }
}
