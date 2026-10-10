using FluentAssertions;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data.Repositories;

public class AuctionCloseQueriesTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
    private readonly string _name = Guid.NewGuid().ToString();
    private AppDbContext Db() => DbContextFactory.Create(_name);

    [Fact]
    public async Task AuctionsDueToClose_AreActiveAndEndedByTheCutoff_OldestFirst()
    {
        using var seed = Db();
        var later = AuctionTestData.Auction(seed, Now.AddMinutes(-5));
        var earlier = AuctionTestData.Auction(seed, Now.AddMinutes(-10));
        AuctionTestData.Auction(seed, Now.AddMinutes(5));                                   // not ended
        AuctionTestData.Auction(seed, Now.AddMinutes(-20), status: ListingStatus.Sold);     // already closed

        var ids = await new ListingRepository(Db()).GetAuctionIdsDueToCloseAsync(Now, 10);

        ids.Should().Equal(earlier.Id, later.Id);
    }

    [Fact]
    public async Task AuctionWithDetails_LoadsStallionSeasonFarmAndOwner()
    {
        using var seed = Db();
        var listing = AuctionTestData.Auction(seed, Now);

        var loaded = await new ListingRepository(Db()).GetAuctionWithDetailsAsync(listing.Id);

        loaded!.Stallion.Name.Should().Be("Snitzel");
        loaded.Season.Name.Should().Be("2026 Season");
        loaded.StudFarm.User.Email.Should().Be("owner@arrowfield.example");
    }

    [Fact]
    public async Task BidsWithBuyers_AreHighestFirst()
    {
        using var seed = Db();
        var listing = AuctionTestData.Auction(seed, Now);
        var a = AuctionTestData.Buyer(seed, "A");
        var b = AuctionTestData.Buyer(seed, "B");
        AuctionTestData.Bid(seed, listing, a, 5000m, BidStatus.Outbid);
        AuctionTestData.Bid(seed, listing, b, 6000m);

        var bids = await new BidRepository(Db()).GetByAuctionWithBuyersAsync(listing.Id);

        bids.Select(x => x.AmountIncGst).Should().Equal(6000m, 5000m);
        bids[0].Buyer.Email.Should().Be("b@example.com");
    }

    private Guid AddPurchase(Action<Purchase> configure)
    {
        using var seed = Db();
        var listing = AuctionTestData.Auction(seed, Now, status: ListingStatus.AwaitingPayment);
        var buyer = AuctionTestData.Buyer(seed);
        var purchase = new Purchase
        {
            ListingId = listing.Id, BuyerUserId = buyer.Id, BidId = Guid.NewGuid(),
            Status = PurchaseStatus.Pending, CreatedAt = Now
        };
        configure(purchase);
        seed.Purchases.Add(purchase);
        seed.SaveChanges();
        return purchase.Id;
    }

    [Fact]
    public async Task DueForCharge_CoversFirstAttemptRetryDeadlineAndInterrupted()
    {
        var first = AddPurchase(_ => { });
        var retry = AddPurchase(p => { p.ChargeAttempts = 1; p.ChargeDueBy = Now.AddHours(1); p.RetryRequested = true; });
        var deadline = AddPurchase(p => { p.ChargeAttempts = 1; p.ChargeDueBy = Now.AddMinutes(-1); });
        var interrupted = AddPurchase(p => { p.ChargeAttempts = 1; p.ChargeAttemptStartedAt = Now.AddMinutes(-3); });
        AddPurchase(p => { p.ChargeAttempts = 1; p.ChargeDueBy = Now.AddHours(1); });             // waiting
        AddPurchase(p => { p.ChargeAttempts = 1; p.ChargeAttemptStartedAt = Now.AddSeconds(-30); }); // in flight
        AddPurchase(p => p.Status = PurchaseStatus.Completed);
        AddPurchase(p => { p.ChargeAttempts = 1; p.ChargeAttemptStartedAt = Now.AddMinutes(-90); p.ChargeNeedsAttention = true; }); // flagged for Staff

        var ids = await new PurchaseRepository(Db()).GetIdsDueForChargeAsync(Now, Now.AddMinutes(-2), 50);

        ids.Should().BeEquivalentTo(new[] { first, retry, deadline, interrupted });
    }

    [Fact]
    public async Task AwaitingCardRetry_IsThePendingFailedChargesOfThatBuyer()
    {
        var failed = AddPurchase(p => { p.ChargeAttempts = 1; p.ChargeDueBy = Now.AddHours(1); });
        var buyerId = Db().Purchases.Single(p => p.Id == failed).BuyerUserId;

        var found = await new PurchaseRepository(Db()).GetAwaitingCardRetryAsync(buyerId);

        found.Select(p => p.Id).Should().Equal(failed);
    }

    [Fact]
    public async Task ForCharge_LoadsTheListingDetailsAndTheBuyer()
    {
        var id = AddPurchase(_ => { });

        var p = await new PurchaseRepository(Db()).GetForChargeAsync(id);

        p!.Buyer.Should().NotBeNull();
        p.Listing.Should().BeOfType<AuctionListing>();
        p.Listing.Stallion.Name.Should().Be("Snitzel");
        p.Listing.StudFarm.User.Should().NotBeNull();
    }
}
