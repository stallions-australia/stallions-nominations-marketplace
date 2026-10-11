using FluentAssertions;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data.Repositories;

public class MakeAnOfferQueriesTests
{
    private static readonly DateTime Now = new(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
    private readonly string _name = Guid.NewGuid().ToString();
    private AppDbContext Db() => DbContextFactory.Create(_name);

    private MakeAnOfferListing Seed(ListingStatus status, DateTime expiresAt)
    {
        using var db = Db();
        var auction = AuctionTestData.Auction(db, Now.AddDays(-2), status: ListingStatus.Unsold);
        var mao = new MakeAnOfferListing
        {
            StallionId = auction.StallionId, SeasonId = auction.SeasonId, StudFarmId = auction.StudFarmId,
            ListingType = ListingType.MakeAnOffer, Status = status, SourceAuctionListingId = auction.Id, ExpiresAt = expiresAt
        };
        db.MakeAnOfferListings.Add(mao);
        db.SaveChanges();
        return mao;
    }

    [Fact]
    public async Task ActiveMakeAnOfferForAnAuction_IgnoresClosedOnes()
    {
        var closed = Seed(ListingStatus.Cancelled, Now.AddDays(10));

        (await new ListingRepository(Db()).GetActiveMakeAnOfferForAuctionAsync(closed.SourceAuctionListingId))
            .Should().BeNull();
    }

    [Fact]
    public async Task ExpiredIds_AreActiveListingsPastTheirExpiry()
    {
        var due = Seed(ListingStatus.Active, Now.AddMinutes(-1));
        Seed(ListingStatus.Active, Now.AddDays(1));
        Seed(ListingStatus.Cancelled, Now.AddDays(-1));

        (await new ListingRepository(Db()).GetMakeAnOfferIdsExpiredAsync(Now, 50)).Should().Equal(due.Id);
    }

    [Fact]
    public async Task WithDetails_LoadsStallionSeasonFarmAndOwner()
    {
        var mao = Seed(ListingStatus.Active, Now.AddDays(1));

        var loaded = await new ListingRepository(Db()).GetMakeAnOfferWithDetailsAsync(mao.Id);

        loaded!.Stallion.Name.Should().Be("Snitzel");
        loaded.Season.Name.Should().Be("2026 Season");
        loaded.StudFarm.User.Should().NotBeNull();
    }
}
