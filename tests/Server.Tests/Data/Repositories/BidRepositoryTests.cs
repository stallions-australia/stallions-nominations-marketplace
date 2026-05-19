using FluentAssertions;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data.Repositories;

public class BidRepositoryTests
{
    [Fact]
    public async Task GetHighestBidAsync_ReturnsActiveBidWithHighestAmount()
    {
        using var db = DbContextFactory.Create(nameof(GetHighestBidAsync_ReturnsActiveBidWithHighestAmount));
        var repo = new BidRepository(db);
        var listingId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        await repo.AddAsync(new Bid { AuctionListingId = listingId, BuyerUserId = buyerId, AmountIncGst = 5000m, Status = BidStatus.Outbid });
        await repo.AddAsync(new Bid { AuctionListingId = listingId, BuyerUserId = buyerId, AmountIncGst = 7500m, Status = BidStatus.Active });

        var highest = await repo.GetHighestBidAsync(listingId);

        highest.Should().NotBeNull();
        highest!.AmountIncGst.Should().Be(7500m);
    }

    [Fact]
    public async Task GetSecondHighestBidAsync_ReturnsHighestOutbidBidExcludingWinner()
    {
        using var db = DbContextFactory.Create(nameof(GetSecondHighestBidAsync_ReturnsHighestOutbidBidExcludingWinner));
        var repo = new BidRepository(db);
        var listingId = Guid.NewGuid();
        var winner = Guid.NewGuid();
        var secondBidder = Guid.NewGuid();
        await repo.AddAsync(new Bid { AuctionListingId = listingId, BuyerUserId = secondBidder, AmountIncGst = 5000m, Status = BidStatus.Outbid });
        await repo.AddAsync(new Bid { AuctionListingId = listingId, BuyerUserId = winner, AmountIncGst = 7500m, Status = BidStatus.Active });

        var second = await repo.GetSecondHighestBidAsync(listingId, winnerBuyerUserId: winner);

        second.Should().NotBeNull();
        second!.AmountIncGst.Should().Be(5000m);
        second.BuyerUserId.Should().Be(secondBidder);
    }
}
