using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data;

public class ConcurrencyStampTests
{
    private static DbContextOptions<AppDbContext> Options(string name) =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options;

    [Fact]
    public async Task UpdatingAListing_RenewsItsStamp()
    {
        var name = Guid.NewGuid().ToString();
        var listing = new AuctionListing { Status = ListingStatus.Active, EndDateTime = DateTime.UtcNow };
        await using (var db = new AppDbContext(Options(name))) { db.AuctionListings.Add(listing); await db.SaveChangesAsync(); }
        var before = listing.ConcurrencyStamp;

        await using (var db = new AppDbContext(Options(name)))
        {
            var loaded = await db.AuctionListings.SingleAsync();
            loaded.Status = ListingStatus.AwaitingPayment;
            await db.SaveChangesAsync();
            loaded.ConcurrencyStamp.Should().NotBe(before);
        }
    }

    [Fact]
    public async Task TwoContextsUpdatingTheSameListing_SecondOneConflicts()
    {
        var name = Guid.NewGuid().ToString();
        await using (var db = new AppDbContext(Options(name)))
        {
            db.AuctionListings.Add(new AuctionListing { Status = ListingStatus.Active, EndDateTime = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        await using var first = new AppDbContext(Options(name));
        await using var second = new AppDbContext(Options(name));
        var a = await first.AuctionListings.SingleAsync();
        var b = await second.AuctionListings.SingleAsync();

        a.Status = ListingStatus.AwaitingPayment;
        await first.SaveChangesAsync();
        b.Status = ListingStatus.Unsold;

        await second.Invoking(db => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task TwoContextsUpdatingTheSamePurchase_SecondOneConflicts()
    {
        var name = Guid.NewGuid().ToString();
        await using (var db = new AppDbContext(Options(name)))
        {
            db.Purchases.Add(new Purchase { Status = PurchaseStatus.Pending });
            await db.SaveChangesAsync();
        }
        await using var first = new AppDbContext(Options(name));
        await using var second = new AppDbContext(Options(name));
        var a = await first.Purchases.SingleAsync();
        var b = await second.Purchases.SingleAsync();

        a.ChargeAttempts = 1;
        await first.SaveChangesAsync();
        b.ChargeAttempts = 1;

        await second.Invoking(db => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
