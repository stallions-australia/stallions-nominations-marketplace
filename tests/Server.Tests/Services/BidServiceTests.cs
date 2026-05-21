using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Bids;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class BidServiceTests
{
    private readonly Mock<IBidRepository> _bidRepoMock = new();
    private readonly Mock<IListingRepository> _listingRepoMock = new();
    private readonly Mock<IUserService> _usersMock = new();

    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private BidService CreateSut() => new(_bidRepoMock.Object, _listingRepoMock.Object, _usersMock.Object, CreateInMemoryDb());

    private static User ActiveBuyer() => new()
        { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.Active };

    private static AuctionListing OpenAuction(decimal startingPrice = 1000m, decimal increment = 25m) => new()
    {
        Id = Guid.NewGuid(), Status = ListingStatus.Active,
        StartingPrice = startingPrice, MinimumBidIncrement = increment,
        EndDateTime = DateTime.UtcNow.AddDays(3)
    };

    [Fact]
    public async Task PlaceBid_WhenBuyerNotVerified_ReturnsForbidden()
    {
        var unverified = new User { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.PendingVerification };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(unverified);
        var auction = OpenAuction();
        _listingRepoMock.Setup(r => r.GetAuctionByIdAsync(auction.Id)).ReturnsAsync(auction);

        var result = await CreateSut().PlaceBidAsync(auction.Id, new PlaceBidRequest { AmountIncGst = 1000m });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task PlaceBid_WhenAmountBelowStartingPrice_ReturnsBadRequest()
    {
        var buyer = ActiveBuyer();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var auction = OpenAuction(startingPrice: 1000m);
        _listingRepoMock.Setup(r => r.GetAuctionByIdAsync(auction.Id)).ReturnsAsync(auction);
        _bidRepoMock.Setup(r => r.GetHighestBidAsync(auction.Id)).ReturnsAsync((Bid?)null);

        var result = await CreateSut().PlaceBidAsync(auction.Id, new PlaceBidRequest { AmountIncGst = 999m });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task PlaceBid_WhenAmountBelowCurrentPlusIncrement_ReturnsBadRequest()
    {
        var buyer = ActiveBuyer();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var auction = OpenAuction(startingPrice: 1000m, increment: 25m);
        _listingRepoMock.Setup(r => r.GetAuctionByIdAsync(auction.Id)).ReturnsAsync(auction);
        var highest = new Bid { Id = Guid.NewGuid(), AmountIncGst = 2000m, Status = BidStatus.Active };
        _bidRepoMock.Setup(r => r.GetHighestBidAsync(auction.Id)).ReturnsAsync(highest);

        // 2000 + 25 = 2025 minimum; submitting 2024 must fail
        var result = await CreateSut().PlaceBidAsync(auction.Id, new PlaceBidRequest { AmountIncGst = 2024m });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task PlaceBid_WhenValid_MarksPreviousHighestBidAsOutbid()
    {
        var buyer = ActiveBuyer();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var auction = OpenAuction(startingPrice: 1000m, increment: 25m);
        _listingRepoMock.Setup(r => r.GetAuctionByIdAsync(auction.Id)).ReturnsAsync(auction);
        var previous = new Bid { Id = Guid.NewGuid(), AmountIncGst = 2000m, Status = BidStatus.Active };
        _bidRepoMock.Setup(r => r.GetHighestBidAsync(auction.Id)).ReturnsAsync(previous);
        _bidRepoMock.Setup(r => r.AddAsync(It.IsAny<Bid>())).ReturnsAsync((Bid b) => b);

        var result = await CreateSut().PlaceBidAsync(auction.Id, new PlaceBidRequest { AmountIncGst = 2025m });

        result.Succeeded.Should().BeTrue();
        _bidRepoMock.Verify(r => r.UpdateAsync(It.Is<Bid>(b =>
            b.Id == previous.Id && b.Status == BidStatus.Outbid)), Times.Once);
    }
}
