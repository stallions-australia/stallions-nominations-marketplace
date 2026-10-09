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
    private readonly Mock<ITermsRepository> _termsRepoMock = new();

    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private readonly Mock<ICardService> _cardsMock = new();

    public BidServiceTests()
    {
        _cardsMock.Setup(c => c.HasValidCardAsync(It.IsAny<Guid>())).ReturnsAsync(true);
    }

    private BidService CreateSut() =>
        new(_bidRepoMock.Object, _listingRepoMock.Object, _usersMock.Object, CreateInMemoryDb(),
            _termsRepoMock.Object, _cardsMock.Object);

    [Fact]
    public async Task PlaceBid_WithoutAValidSavedCard_ReturnsBadRequest()
    {
        var buyer = ActiveBuyer();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        _cardsMock.Setup(c => c.HasValidCardAsync(buyer.Id)).ReturnsAsync(false);
        var auction = OpenAuction();
        _listingRepoMock.Setup(r => r.GetAuctionByIdAsync(auction.Id)).ReturnsAsync(auction);

        var result = await CreateSut().PlaceBidAsync(auction.Id, new PlaceBidRequest { AmountIncGst = 5000m });

        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Be("Save a card before bidding.");
        _bidRepoMock.Verify(r => r.AddAsync(It.IsAny<Bid>()), Times.Never);
    }

    private static User ActiveBuyer() => new()
        { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.Active };

    private static AuctionListing OpenAuction(decimal? buyerFee = 150m, decimal increment = 25m) => new()
    {
        Id = Guid.NewGuid(), Status = ListingStatus.Active,
        BuyerFeeIncGst = buyerFee, MinimumBidIncrement = increment,
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
    public async Task PlaceBid_FirstBidBelowBuyerFee_ReturnsBadRequest()
    {
        // A sale must never be worth less than the buyer fee, which comes out of the price.
        var buyer = ActiveBuyer();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var auction = OpenAuction(buyerFee: 150m);
        _listingRepoMock.Setup(r => r.GetAuctionByIdAsync(auction.Id)).ReturnsAsync(auction);
        _bidRepoMock.Setup(r => r.GetHighestBidAsync(auction.Id)).ReturnsAsync((Bid?)null);

        var result = await CreateSut().PlaceBidAsync(auction.Id, new PlaceBidRequest { AmountIncGst = 149.99m });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Contain("$150.00").And.Contain("buyer fee");
        _bidRepoMock.Verify(r => r.AddAsync(It.IsAny<Bid>()), Times.Never);
    }

    [Fact]
    public async Task PlaceBid_FirstBidEqualToBuyerFee_IsAccepted()
    {
        var buyer = ActiveBuyer();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var auction = OpenAuction(buyerFee: 150m);
        _listingRepoMock.Setup(r => r.GetAuctionByIdAsync(auction.Id)).ReturnsAsync(auction);
        _bidRepoMock.Setup(r => r.GetHighestBidAsync(auction.Id)).ReturnsAsync((Bid?)null);
        _bidRepoMock.Setup(r => r.AddAsync(It.IsAny<Bid>())).ReturnsAsync((Bid b) => b);

        var result = await CreateSut().PlaceBidAsync(auction.Id, new PlaceBidRequest { AmountIncGst = 150m });

        result.Succeeded.Should().BeTrue(result.Error);
    }

    [Fact]
    public async Task PlaceBid_WhenListingHasNoBuyerFee_ReturnsBadRequest()
    {
        var buyer = ActiveBuyer();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var auction = OpenAuction(buyerFee: null);
        _listingRepoMock.Setup(r => r.GetAuctionByIdAsync(auction.Id)).ReturnsAsync(auction);

        var result = await CreateSut().PlaceBidAsync(auction.Id, new PlaceBidRequest { AmountIncGst = 5000m });

        result.HttpStatusCode.Should().Be(400);
        _bidRepoMock.Verify(r => r.AddAsync(It.IsAny<Bid>()), Times.Never);
    }

    [Fact]
    public async Task PlaceBid_AtHighestPlusIncrement_IsAccepted()
    {
        var buyer = ActiveBuyer();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var auction = OpenAuction(increment: 50m);
        _listingRepoMock.Setup(r => r.GetAuctionByIdAsync(auction.Id)).ReturnsAsync(auction);
        _bidRepoMock.Setup(r => r.GetHighestBidAsync(auction.Id))
            .ReturnsAsync(new Bid { Id = Guid.NewGuid(), AmountIncGst = 2000m, Status = BidStatus.Active });
        _bidRepoMock.Setup(r => r.AddAsync(It.IsAny<Bid>())).ReturnsAsync((Bid b) => b);

        var result = await CreateSut().PlaceBidAsync(auction.Id, new PlaceBidRequest { AmountIncGst = 2050m });

        result.Succeeded.Should().BeTrue(result.Error);
    }

    [Fact]
    public async Task PlaceBid_WhenAmountBelowCurrentPlusIncrement_ReturnsBadRequest()
    {
        var buyer = ActiveBuyer();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var auction = OpenAuction(increment: 25m);
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
        var auction = OpenAuction(increment: 25m);
        _listingRepoMock.Setup(r => r.GetAuctionByIdAsync(auction.Id)).ReturnsAsync(auction);
        var previous = new Bid { Id = Guid.NewGuid(), AmountIncGst = 2000m, Status = BidStatus.Active };
        _bidRepoMock.Setup(r => r.GetHighestBidAsync(auction.Id)).ReturnsAsync(previous);
        _bidRepoMock.Setup(r => r.AddAsync(It.IsAny<Bid>())).ReturnsAsync((Bid b) => b);

        var result = await CreateSut().PlaceBidAsync(auction.Id, new PlaceBidRequest { AmountIncGst = 2025m });

        result.Succeeded.Should().BeTrue();
        _bidRepoMock.Verify(r => r.UpdateAsync(It.Is<Bid>(b =>
            b.Id == previous.Id && b.Status == BidStatus.Outbid)), Times.Once);
    }

    [Fact]
    public async Task GetPublicHistory_AnonymisesBiddersInOrderOfFirstBid_HighestFirst()
    {
        var auctionId = Guid.NewGuid();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddHours(-3);
        _bidRepoMock.Setup(r => r.GetByAuctionListingIdAsync(auctionId)).ReturnsAsync(new List<Bid>
        {
            new() { Id = Guid.NewGuid(), AuctionListingId = auctionId, BuyerUserId = bob,   AmountIncGst = 1200m, PlacedAt = t0.AddMinutes(10) },
            new() { Id = Guid.NewGuid(), AuctionListingId = auctionId, BuyerUserId = alice, AmountIncGst = 1000m, PlacedAt = t0 },
            new() { Id = Guid.NewGuid(), AuctionListingId = auctionId, BuyerUserId = alice, AmountIncGst = 1500m, PlacedAt = t0.AddMinutes(20) },
        });

        var result = await CreateSut().GetPublicHistoryAsync(auctionId);

        result.Succeeded.Should().BeTrue();
        result.Value!.Select(b => (b.AmountIncGst, b.Bidder)).Should().Equal(
            (1500m, "Bidder 1"),
            (1200m, "Bidder 2"),
            (1000m, "Bidder 1"));
        // No buyer identity of any kind leaves the server on the public history.
        typeof(PublicBidDto).GetProperties().Select(p => p.Name)
            .Should().NotContain(n => n.Contains("User") || n.Contains("Buyer"));
    }

    [Fact]
    public async Task PlaceBid_WhenTermsPublishedAndBuyerHasNotAccepted_ReturnsBadRequest()
    {
        var buyer = ActiveBuyer();                       // AcceptedTermsVersion = null
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var auction = OpenAuction();
        _listingRepoMock.Setup(r => r.GetAuctionByIdAsync(auction.Id)).ReturnsAsync(auction);
        _termsRepoMock.Setup(t => t.GetCurrentAsync())
            .ReturnsAsync(new TermsDocument { Version = 2, Body = "x" });

        var result = await CreateSut().PlaceBidAsync(auction.Id, new PlaceBidRequest { AmountIncGst = 1000m });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Contain("Terms");
    }

    [Fact]
    public async Task PlaceBid_WhenBuyerAcceptedCurrentTerms_Succeeds()
    {
        var buyer = ActiveBuyer();
        buyer.AcceptedTermsVersion = 2;
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var auction = OpenAuction();
        _listingRepoMock.Setup(r => r.GetAuctionByIdAsync(auction.Id)).ReturnsAsync(auction);
        _bidRepoMock.Setup(r => r.GetHighestBidAsync(auction.Id)).ReturnsAsync((Bid?)null);
        _bidRepoMock.Setup(r => r.AddAsync(It.IsAny<Bid>())).ReturnsAsync((Bid b) => b);
        _termsRepoMock.Setup(t => t.GetCurrentAsync())
            .ReturnsAsync(new TermsDocument { Version = 2, Body = "x" });

        var result = await CreateSut().PlaceBidAsync(auction.Id, new PlaceBidRequest { AmountIncGst = 1000m });

        result.Succeeded.Should().BeTrue();
    }
}
