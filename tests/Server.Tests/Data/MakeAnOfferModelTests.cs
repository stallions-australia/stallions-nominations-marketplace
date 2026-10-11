using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data;

public class MakeAnOfferModelTests
{
    private readonly string _name = Guid.NewGuid().ToString();
    private AppDbContext Db() => DbContextFactory.Create(_name);

    [Fact]
    public async Task AMakeAnOfferListing_RoundTripsAsItsOwnType_WithItsSourceAuction()
    {
        Guid id;
        using (var db = Db())
        {
            var auction = AuctionTestData.Auction(db, DateTime.UtcNow.AddDays(-1), status: ListingStatus.Unsold);
            var mao = new MakeAnOfferListing
            {
                StallionId = auction.StallionId, SeasonId = auction.SeasonId, StudFarmId = auction.StudFarmId,
                ListingType = ListingType.MakeAnOffer, Status = ListingStatus.Active,
                SourceAuctionListingId = auction.Id, ExpiresAt = DateTime.UtcNow.AddDays(30), BuyerFeeIncGst = 150m
            };
            db.MakeAnOfferListings.Add(mao);
            await db.SaveChangesAsync();
            id = mao.Id;
        }

        using var read = Db();
        var loaded = await read.Listings.SingleAsync(l => l.Id == id);
        loaded.Should().BeOfType<MakeAnOfferListing>()
            .Which.SourceAuctionListingId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task TwoContextsUpdatingTheSameOffer_SecondOneConflicts()
    {
        using (var db = Db())
        {
            db.Offers.Add(new Offer { ListingId = Guid.NewGuid(), BuyerUserId = Guid.NewGuid(), AmountIncGst = 5000m,
                CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(7) });
            await db.SaveChangesAsync();
        }
        using var first = Db();
        using var second = Db();
        var a = await first.Offers.SingleAsync();
        var b = await second.Offers.SingleAsync();

        a.Status = OfferStatus.Accepted;
        await first.SaveChangesAsync();
        b.Status = OfferStatus.Declined;

        await second.Invoking(db => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public void TheModel_AllowsOnePendingOfferPerBuyerPerListing_AndOneLiveSaleRecordPerOffer()
    {
        using var db = Db();
        var offer = db.Model.FindEntityType(typeof(Offer))!;
        offer.GetIndexes().Should().Contain(i => i.IsUnique
            && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "ListingId", "BuyerUserId" })
            && i.GetFilter() == "[Status] = 'Pending'");

        var purchase = db.Model.FindEntityType(typeof(Purchase))!;
        purchase.GetIndexes().Should().Contain(i => i.IsUnique
            && i.Properties.Single().Name == "OfferId"
            && i.GetFilter() == "[OfferId] IS NOT NULL AND [Status] <> 'Voided'");
    }
}
