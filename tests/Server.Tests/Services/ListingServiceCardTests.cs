using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Listings;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class ListingServiceCardTests
{
    private readonly Mock<IListingRepository> _mockListingRepo = new();
    private readonly Mock<ISeasonRepository> _mockSeasonRepo = new();
    private readonly Mock<IStallionRepository> _mockStallionRepo = new();
    private readonly Mock<IStudFarmRepository> _mockFarmRepo = new();
    private readonly Mock<IUserService> _mockUsers = new();

    private ListingService CreateSut() => new(
        _mockListingRepo.Object,
        _mockSeasonRepo.Object,
        _mockStallionRepo.Object,
        _mockFarmRepo.Object,
        _mockUsers.Object);

    [Fact]
    public async Task GetListingCardsAsync_FixedPrice_PopulatesStudFarmNameAndQuantity()
    {
        var studFarm = new StudFarm { Id = Guid.NewGuid(), Name = "Coolmore Australia" };
        var stallion = new Stallion
        {
            Id = Guid.NewGuid(), Name = "Fastnet Rock",
            Images = new List<StallionImage>()
        };
        var listing = new FixedPriceListing
        {
            Id = Guid.NewGuid(),
            StudFarm = studFarm, StudFarmId = studFarm.Id,
            Stallion = stallion, StallionId = stallion.Id,
            Season = new Season { Name = "2025 Season" },
            PriceIncGst = 8000m, Quantity = 5, QuantityRemaining = 4,
            Status = ListingStatus.Active, ListingType = ListingType.FixedPrice
        };

        _mockListingRepo
            .Setup(r => r.GetActiveAsync(null, null, null))
            .ReturnsAsync(new List<Listing> { listing });

        var result = await CreateSut().GetListingCardsAsync(null, null, null);

        result.Succeeded.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        var card = result.Value![0];
        card.StudFarmName.Should().Be("Coolmore Australia");
        card.QuantityRemaining.Should().Be(4);
        card.PriceIncGst.Should().Be(8000m);
        card.ListingType.Should().Be("FixedPrice");
    }

    [Fact]
    public async Task GetListingCardsAsync_Auction_IncludesBidCountAndHighestBid()
    {
        var studFarm = new StudFarm { Id = Guid.NewGuid(), Name = "Arrowfield Stud" };
        var stallion = new Stallion
        {
            Id = Guid.NewGuid(), Name = "Snitzel",
            Images = new List<StallionImage>()
        };
        var auctionId = Guid.NewGuid();
        var listing = new AuctionListing
        {
            Id = auctionId,
            StudFarm = studFarm, StudFarmId = studFarm.Id,
            Stallion = stallion, StallionId = stallion.Id,
            Season = new Season { Name = "2025 Season" },
            StartingPrice = 5000m, ReservePrice = 8000m, IsNoReserve = false,
            MinimumBidIncrement = 25m,
            EndDateTime = DateTime.UtcNow.AddDays(3),
            Status = ListingStatus.Active, ListingType = ListingType.Auction
        };

        _mockListingRepo
            .Setup(r => r.GetActiveAsync(null, null, null))
            .ReturnsAsync(new List<Listing> { listing });

        _mockListingRepo
            .Setup(r => r.GetBidAggregatesAsync(It.Is<IEnumerable<Guid>>(ids => ids.Contains(auctionId))))
            .ReturnsAsync(new Dictionary<Guid, (int Count, decimal? Highest)>
            {
                { auctionId, (3, 7500m) }
            });

        var result = await CreateSut().GetListingCardsAsync(null, null, null);

        result.Succeeded.Should().BeTrue();
        var card = result.Value![0];
        card.BidCount.Should().Be(3);
        card.CurrentHighestBidIncGst.Should().Be(7500m);
        card.ReserveMet.Should().BeFalse(); // 7500 < 8000 reserve
        card.AuctionClosesAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(3), TimeSpan.FromSeconds(5));
    }
}
