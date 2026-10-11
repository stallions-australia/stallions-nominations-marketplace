using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Auctions;

public class AuctionCloserCloseTests
{
    private readonly AuctionCloserFixture _f = new();
    private DateTime Ended => _f.Clock.UtcNow.AddMinutes(-1);

    [Fact]
    public async Task NoBids_ClosesUnsold_AndTellsTheStud()
    {
        var listing = _f.Seed(db => AuctionTestData.Auction(db, Ended));

        var run = await _f.CreateSut().RunAsync();

        run.AuctionsClosed.Should().Be(1);
        var closed = await _f.Read().AuctionListings.SingleAsync();
        closed.Status.Should().Be(ListingStatus.Unsold);
        closed.CloseReason.Should().Be(ListingCloseReason.NoBids);
        closed.ClosedAt.Should().Be(_f.Clock.UtcNow);
        _f.Emails.Verify(e => e.StudNoSaleAsync(It.Is<AuctionListing>(l => l.Id == listing.Id), ListingCloseReason.NoBids), Times.Once);
    }

    [Fact]
    public async Task BelowReserve_ClosesUnsold_EveryBidderLosesAndIsTold()
    {
        var (a, b) = _f.Seed(db =>
        {
            var listing = AuctionTestData.Auction(db, Ended, reserve: 20000m);
            var a = AuctionTestData.Buyer(db, "A");
            var b = AuctionTestData.Buyer(db, "B");
            AuctionTestData.Bid(db, listing, a, 9000m, BidStatus.Outbid);
            AuctionTestData.Bid(db, listing, b, 10000m);
            return (a, b);
        });

        await _f.CreateSut().RunAsync();

        using var db = _f.Read();
        (await db.AuctionListings.SingleAsync()).CloseReason.Should().Be(ListingCloseReason.ReserveNotMet);
        (await db.Bids.ToListAsync()).Should().OnlyContain(x => x.Status == BidStatus.Lost);
        (await db.Purchases.CountAsync()).Should().Be(0);
        _f.Emails.Verify(e => e.AuctionEndedWithoutSaleAsync(It.IsAny<AuctionListing>(), It.Is<User>(u => u.Id == a.Id)), Times.Once);
        _f.Emails.Verify(e => e.AuctionEndedWithoutSaleAsync(It.IsAny<AuctionListing>(), It.Is<User>(u => u.Id == b.Id)), Times.Once);
        _f.Emails.Verify(e => e.StudNoSaleAsync(It.IsAny<AuctionListing>(), ListingCloseReason.ReserveNotMet), Times.Once);
    }

    [Fact]
    public async Task ReserveMet_CreatesThePendingSaleRecord_WithTheGstSplitAndBalance()
    {
        var (winner, winningBid) = _f.Seed(db =>
        {
            var listing = AuctionTestData.Auction(db, Ended, reserve: 8000m);
            var w = AuctionTestData.Buyer(db, "Winner");
            AuctionTestData.Card(db, w);
            return (w, AuctionTestData.Bid(db, listing, w, 10000m));
        });
        _f.ChargesDecline(); // keep the sale record Pending so this test looks only at closing

        await _f.CreateSut().RunAsync();

        var purchase = await _f.Read().Purchases.SingleAsync();
        purchase.BuyerUserId.Should().Be(winner.Id);
        purchase.BidId.Should().Be(winningBid.Id);
        purchase.TotalPriceIncGst.Should().Be(10000m);
        purchase.BuyerFeeIncGst.Should().Be(150m);
        purchase.BuyerFeeGst.Should().Be(13.64m);
        purchase.BuyerFeeExGst.Should().Be(136.36m);
        purchase.BalancePayableToStudIncGst.Should().Be(9850m);
    }

    [Fact]
    public async Task ReserveMet_WinnerWins_OtherBiddersLose_AndAreTold()
    {
        var (loser, winner) = _f.Seed(db =>
        {
            var listing = AuctionTestData.Auction(db, Ended); // no reserve
            var l = AuctionTestData.Buyer(db, "Loser");
            var w = AuctionTestData.Buyer(db, "Winner");
            AuctionTestData.Card(db, w);
            AuctionTestData.Bid(db, listing, w, 5000m, BidStatus.Outbid);
            AuctionTestData.Bid(db, listing, l, 5500m, BidStatus.Outbid);
            AuctionTestData.Bid(db, listing, w, 6000m);
            return (l, w);
        });
        _f.ChargesDecline();

        await _f.CreateSut().RunAsync();

        using var db = _f.Read();
        var bids = await db.Bids.ToListAsync();
        bids.Single(x => x.AmountIncGst == 6000m).Status.Should().Be(BidStatus.Won);
        bids.Single(x => x.AmountIncGst == 5500m).Status.Should().Be(BidStatus.Lost);
        bids.Single(x => x.AmountIncGst == 5000m).Status.Should().Be(BidStatus.Outbid, "the winner's own earlier bid didn't lose");
        (await db.AuctionListings.SingleAsync()).Status.Should().Be(ListingStatus.AwaitingPayment);
        _f.Emails.Verify(e => e.AuctionLostAsync(It.IsAny<AuctionListing>(), It.Is<User>(u => u.Id == loser.Id)), Times.Once);
        _f.Emails.Verify(e => e.AuctionLostAsync(It.IsAny<AuctionListing>(), It.Is<User>(u => u.Id == winner.Id)), Times.Never);
    }

    [Fact]
    public async Task WithoutABuyerFeeSnapshot_ClosesUnsoldAsChargeFailed()
    {
        _f.Seed(db =>
        {
            var listing = AuctionTestData.Auction(db, Ended, buyerFee: null);
            AuctionTestData.Bid(db, listing, AuctionTestData.Buyer(db), 5000m);
            return listing;
        });

        await _f.CreateSut().RunAsync();

        using var db = _f.Read();
        (await db.AuctionListings.SingleAsync()).CloseReason.Should().Be(ListingCloseReason.ChargeFailed);
        (await db.Purchases.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AnAuctionEndedLessThan30SecondsAgo_IsLeftForTheNextRun()
    {
        _f.Seed(db => AuctionTestData.Auction(db, _f.Clock.UtcNow.AddSeconds(-10)));

        (await _f.CreateSut().RunAsync()).AuctionsClosed.Should().Be(0);
        (await _f.Read().AuctionListings.SingleAsync()).Status.Should().Be(ListingStatus.Active);
    }

    [Fact]
    public async Task TwoInstancesClosingTheSameAuction_OnlyOneSaleRecordIsCreated()
    {
        var winningBid = _f.Seed(db =>
        {
            var listing = AuctionTestData.Auction(db, Ended);
            var w = AuctionTestData.Buyer(db, "Winner");
            AuctionTestData.Card(db, w);
            return AuctionTestData.Bid(db, listing, w, 6000m);
        });
        _f.ChargesDecline();
        // The second instance found the auction due and read it and its bids (still Active) before the
        // first one closed it: replay that by giving it the earlier reads and its stale context.
        using var staleDb = _f.Read();
        var real = new ListingRepository(staleDb);
        var dueIds = await real.GetAuctionIdsDueToCloseAsync(_f.Clock.UtcNow, 50);
        await real.GetAuctionWithDetailsAsync(dueIds.Single());
        await new BidRepository(staleDb).GetByAuctionWithBuyersAsync(dueIds.Single());
        var stale = new Mock<IListingRepository>();
        stale.Setup(r => r.GetAuctionIdsDueToCloseAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(dueIds);
        stale.Setup(r => r.GetAuctionWithDetailsAsync(It.IsAny<Guid>())).Returns<Guid>(real.GetAuctionWithDetailsAsync);
        stale.Setup(r => r.UpdateAsync(It.IsAny<Listing>())).Returns<Listing>(real.UpdateAsync);

        await _f.CreateSut().RunAsync();
        var second = await _f.CreateSut(staleDb, stale.Object).RunAsync();

        second.AuctionsClosed.Should().Be(0);
        using var db = _f.Read();
        (await db.Purchases.CountAsync()).Should().Be(1);
        (await db.AuctionListings.SingleAsync()).Status.Should().Be(ListingStatus.AwaitingPayment);
        (await db.Bids.SingleAsync(b => b.Id == winningBid.Id)).Status.Should().Be(BidStatus.Won);
        _f.Audit.Verify(a => a.LogAsync("Listing", It.IsAny<Guid>(), "AuctionClosed", It.IsAny<Guid?>(), It.IsAny<string?>()), Times.Once);
        _f.Emails.Verify(e => e.AuctionLostAsync(It.IsAny<AuctionListing>(), It.IsAny<User>()), Times.Never);
        _f.Emails.Verify(e => e.AuctionEndedWithoutSaleAsync(It.IsAny<AuctionListing>(), It.IsAny<User>()), Times.Never);
        _f.Emails.Verify(e => e.StudNoSaleAsync(It.IsAny<AuctionListing>(), It.IsAny<ListingCloseReason>()), Times.Never);
    }

    [Fact]
    public async Task EqualHighestBids_TheEarlierBidWins()
    {
        var (early, late) = _f.Seed(db =>
        {
            var listing = AuctionTestData.Auction(db, Ended);
            var a = AuctionTestData.Buyer(db, "Late");
            var b = AuctionTestData.Buyer(db, "Early");
            var lateBid = AuctionTestData.Bid(db, listing, a, 7000m);
            var earlyBid = AuctionTestData.Bid(db, listing, b, 7000m);
            lateBid.PlacedAt = _f.Clock.UtcNow.AddMinutes(-10);
            earlyBid.PlacedAt = _f.Clock.UtcNow.AddMinutes(-20);
            db.SaveChanges();
            return (earlyBid, lateBid);
        });
        _f.ChargesDecline();

        await _f.CreateSut().RunAsync();

        using var db = _f.Read();
        (await db.Bids.SingleAsync(b => b.Id == early.Id)).Status.Should().Be(BidStatus.Won);
        (await db.Bids.SingleAsync(b => b.Id == late.Id)).Status.Should().Be(BidStatus.Lost);
        (await db.Purchases.SingleAsync()).BidId.Should().Be(early.Id);
    }

    [Fact]
    public async Task WinnersOtherActiveBids_BecomeOutbid_NotLeftActive()
    {
        _f.Seed(db =>
        {
            var listing = AuctionTestData.Auction(db, Ended);
            var w = AuctionTestData.Buyer(db, "Winner");
            var l = AuctionTestData.Buyer(db, "Loser");
            AuctionTestData.Card(db, w);
            AuctionTestData.Bid(db, listing, w, 4000m, BidStatus.Outbid);
            AuctionTestData.Bid(db, listing, l, 5000m);
            AuctionTestData.Bid(db, listing, w, 6000m);
            AuctionTestData.Bid(db, listing, w, 5500m); // only possible via a race
            return listing;
        });
        _f.ChargesDecline();

        await _f.CreateSut().RunAsync();

        using var db = _f.Read();
        var bids = await db.Bids.ToListAsync();
        bids.Single(x => x.AmountIncGst == 6000m).Status.Should().Be(BidStatus.Won);
        bids.Single(x => x.AmountIncGst == 5500m).Status.Should().Be(BidStatus.Outbid);
        bids.Single(x => x.AmountIncGst == 5000m).Status.Should().Be(BidStatus.Lost);
        bids.Single(x => x.AmountIncGst == 4000m).Status.Should().Be(BidStatus.Outbid);
    }
}
