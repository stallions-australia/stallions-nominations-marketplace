using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Sales;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Sales;

public class OfferExpirerTests
{
    private readonly string _name = Guid.NewGuid().ToString();
    private readonly TestClock _clock = new();
    private readonly Mock<IAuctionEmails> _emails = new();
    private readonly Mock<IAuditLogRepository> _audit = new();

    private AppDbContext Db() => DbContextFactory.Create(_name);

    private OfferExpirer Sut()
    {
        var db = Db();
        return new OfferExpirer(new OfferRepository(db), new ListingRepository(db), _audit.Object, _emails.Object,
            new InlineTransactionRunner(), _clock, NullLogger<OfferExpirer>.Instance);
    }

    private (Guid ListingId, Guid OfferId) Seed(DateTime listingExpires, DateTime offerExpires, ListingStatus status = ListingStatus.Active)
    {
        using var db = Db();
        var auction = AuctionTestData.Auction(db, _clock.UtcNow.AddDays(-2), status: ListingStatus.Unsold);
        var listing = new MakeAnOfferListing
        {
            StallionId = auction.StallionId, SeasonId = auction.SeasonId, StudFarmId = auction.StudFarmId,
            ListingType = ListingType.MakeAnOffer, Status = status, BuyerFeeIncGst = 150m,
            SourceAuctionListingId = auction.Id, ExpiresAt = listingExpires
        };
        db.MakeAnOfferListings.Add(listing);
        var buyer = AuctionTestData.Buyer(db, "Offer Buyer");
        var offer = new Offer { ListingId = listing.Id, BuyerUserId = buyer.Id, AmountIncGst = 9000m,
            CreatedAt = _clock.UtcNow.AddDays(-1), ExpiresAt = offerExpires };
        db.Offers.Add(offer);
        db.SaveChanges();
        return (listing.Id, offer.Id);
    }

    [Fact]
    public async Task AnOfferPastItsExpiry_IsExpired_AndTheBuyerToldIt()
    {
        var (_, offerId) = Seed(_clock.UtcNow.AddDays(30), _clock.UtcNow.AddMinutes(-1));

        await Sut().RunAsync();

        (await Db().Offers.SingleAsync(o => o.Id == offerId)).Status.Should().Be(OfferStatus.Expired);
        _emails.Verify(e => e.OfferExpiredAsync(It.IsAny<MakeAnOfferListing>(), It.IsAny<Offer>(), It.IsAny<User>(), false), Times.Once);
    }

    [Fact]
    public async Task AListingPastTheSeasonEnd_IsExpired_WithItsPendingOffers()
    {
        var (listingId, offerId) = Seed(_clock.UtcNow.AddMinutes(-1), _clock.UtcNow.AddDays(5));

        await Sut().RunAsync();

        using var db = Db();
        var listing = await db.Listings.SingleAsync(l => l.Id == listingId);
        listing.Status.Should().Be(ListingStatus.Expired);
        listing.ClosedAt.Should().Be(_clock.UtcNow);
        (await db.Offers.SingleAsync(o => o.Id == offerId)).Status.Should().Be(OfferStatus.Expired);
        _emails.Verify(e => e.OfferExpiredAsync(It.IsAny<MakeAnOfferListing>(), It.IsAny<Offer>(), It.IsAny<User>(), true), Times.Once);
    }

    [Fact]
    public async Task PendingOfferOnAClosedListing_IsExpired()
    {
        var (_, offerId) = Seed(_clock.UtcNow.AddDays(30), _clock.UtcNow.AddDays(5), ListingStatus.Cancelled);

        await Sut().RunAsync();

        (await Db().Offers.SingleAsync(o => o.Id == offerId)).Status.Should().Be(OfferStatus.Expired);
    }

    [Fact]
    public async Task ExpirePendingOffers_ForOneListing_LeavesOthersAlone()
    {
        var (listingId, offerId) = Seed(_clock.UtcNow.AddDays(30), _clock.UtcNow.AddDays(5), ListingStatus.Cancelled);
        var (_, otherOffer) = Seed(_clock.UtcNow.AddDays(30), _clock.UtcNow.AddDays(5));

        await Sut().ExpirePendingOffersAsync(listingId);

        (await Db().Offers.SingleAsync(o => o.Id == offerId)).Status.Should().Be(OfferStatus.Expired);
        (await Db().Offers.SingleAsync(o => o.Id == otherOffer)).Status.Should().Be(OfferStatus.Pending);
    }

    [Fact]
    public async Task AnOfferStillInDate_IsLeftAlone()
    {
        var (_, offerId) = Seed(_clock.UtcNow.AddDays(30), _clock.UtcNow.AddDays(5));

        (await Sut().RunAsync()).Should().Be(0);
        (await Db().Offers.SingleAsync(o => o.Id == offerId)).Status.Should().Be(OfferStatus.Pending);
    }

    [Fact]
    public async Task ExpirePendingOffers_OnAnOpenListing_DoesNothing()
    {
        var (listingId, offerId) = Seed(_clock.UtcNow.AddDays(30), _clock.UtcNow.AddDays(5));

        await Sut().ExpirePendingOffersAsync(listingId);

        (await Db().Offers.SingleAsync(o => o.Id == offerId)).Status.Should().Be(OfferStatus.Pending);
        _emails.Verify(e => e.OfferExpiredAsync(It.IsAny<MakeAnOfferListing>(), It.IsAny<Offer>(), It.IsAny<User>(), It.IsAny<bool>()), Times.Never);
    }
}
