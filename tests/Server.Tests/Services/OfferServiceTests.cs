using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Sales;
using Stallions.Server.Services;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.DTOs.Offers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class OfferServiceTests
{
    private readonly string _name = Guid.NewGuid().ToString();
    private readonly TestClock _clock = new();
    private readonly Mock<IUserService> _users = new();
    private readonly Mock<IBuyerFeeCharger> _charger = new();
    private readonly Mock<IAuctionEmails> _emails = new();
    private readonly Mock<IAuditLogRepository> _audit = new();
    private readonly Mock<ITermsRepository> _terms = new();
    private readonly Mock<ICardService> _cards = new();
    private readonly Mock<IPlatformSettingsRepository> _settings = new();

    public OfferServiceTests()
    {
        _cards.Setup(c => c.HasValidCardAsync(It.IsAny<Guid>())).ReturnsAsync(true);
        _settings.Setup(s => s.GetAsync()).ReturnsAsync(new PlatformSettings { BuyerFeeIncGst = 150m, OfferExpiryDays = 7 });
        _charger.Setup(c => c.ChargeAsync(It.IsAny<Guid>())).ReturnsAsync(ChargeOutcome.Paid);
    }

    private AppDbContext Db() => DbContextFactory.Create(_name);

    private OfferService Sut(AppDbContext? db = null, ITransactionRunner? runner = null)
    {
        db ??= Db();
        return new OfferService(new OfferRepository(db), new ListingRepository(db), new PurchaseRepository(db),
            new StudFarmRepository(db), _users.Object, _settings.Object, _audit.Object, _emails.Object, _charger.Object,
            runner ?? new InlineTransactionRunner(), _terms.Object, _cards.Object, _clock, NullLogger<OfferService>.Instance);
    }

    private void SignIn(User user) => _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(user);

    private sealed class ThrowingRunner(Exception toThrow) : ITransactionRunner
    {
        public Task<T> RunAsync<T>(Func<Task<T>> work) => throw toThrow;
    }

    private sealed class CountingRunner : ITransactionRunner
    {
        public int Runs { get; private set; }
        public Task<T> RunAsync<T>(Func<Task<T>> work) { Runs++; return work(); }
    }

    [Fact]
    public async Task Make_ExpiryIsCappedAtTheListingsOwnExpiry()
    {
        var (listing, _, buyer) = Setup(expiresAt: _clock.UtcNow.AddDays(2));

        var offer = await MakeOffer(listing, buyer);

        offer.ExpiresAt.Should().Be(listing.ExpiresAt);
    }

    [Theory]
    [InlineData(2601)]
    [InlineData(2627)]
    public async Task Make_AUniqueIndexViolation_IsAConflict(int sqlNumber)
    {
        var (listing, _, buyer) = Setup();
        SignIn(buyer);
        var runner = new ThrowingRunner(new DbUpdateException("dup", SqlExceptionFactory.WithNumber(sqlNumber)));

        var r = await Sut(runner: runner).MakeAsync(listing.Id, new MakeOfferRequest { AmountIncGst = 9000m });

        r.HttpStatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Make_AnyOtherSaveFailure_IsNotReportedAsADuplicate()
    {
        var (listing, _, buyer) = Setup();
        SignIn(buyer);
        var runner = new ThrowingRunner(new DbUpdateException("boom", SqlExceptionFactory.WithNumber(547)));

        var act = () => Sut(runner: runner).MakeAsync(listing.Id, new MakeOfferRequest { AmountIncGst = 9000m });

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Decline_AndWithdraw_RunInsideATransaction()
    {
        var (listing, owner, buyer) = Setup();
        var declined = await MakeOffer(listing, buyer);
        var counting = new CountingRunner();
        SignIn(owner);
        (await Sut(runner: counting).DeclineAsync(declined.Id)).Succeeded.Should().BeTrue();
        counting.Runs.Should().Be(1);

        var second = await MakeOffer(listing, buyer, 9500m);
        SignIn(buyer);
        (await Sut(runner: counting).WithdrawAsync(second.Id)).Succeeded.Should().BeTrue();
        counting.Runs.Should().Be(2);
    }

    /// <summary>An Active Make an Offer listing, its stud owner, and a verified buyer.</summary>
    private (MakeAnOfferListing Listing, User Owner, User Buyer) Setup(DateTime? expiresAt = null)
    {
        using var db = Db();
        var auction = AuctionTestData.Auction(db, _clock.UtcNow.AddDays(-2), status: ListingStatus.Unsold);
        var listing = new MakeAnOfferListing
        {
            StallionId = auction.StallionId, SeasonId = auction.SeasonId, StudFarmId = auction.StudFarmId,
            ListingType = ListingType.MakeAnOffer, Status = ListingStatus.Active, BuyerFeeIncGst = 150m,
            SourceAuctionListingId = auction.Id, ExpiresAt = expiresAt ?? _clock.UtcNow.AddDays(30), PublishedAt = _clock.UtcNow
        };
        db.MakeAnOfferListings.Add(listing);
        db.SaveChanges();
        var owner = db.Users.Single(u => u.Id == db.StudFarms.Single(f => f.Id == auction.StudFarmId).UserId);
        var buyer = AuctionTestData.Buyer(db, "Offer Buyer");
        return (listing, owner, buyer);
    }

    private async Task<OfferDto> MakeOffer(MakeAnOfferListing listing, User buyer, decimal amount = 9000m)
    {
        SignIn(buyer);
        var r = await Sut().MakeAsync(listing.Id, new MakeOfferRequest { AmountIncGst = amount });
        r.HttpStatusCode.Should().Be(201, r.Error);
        return r.Value!;
    }

    [Fact]
    public async Task Make_CreatesAPendingOffer_ExpiringPerSettings_AndTellsTheStud()
    {
        var (listing, _, buyer) = Setup();

        var offer = await MakeOffer(listing, buyer);

        offer.Status.Should().Be("Pending");
        offer.ExpiresAt.Should().Be(_clock.UtcNow.AddDays(7));
        _emails.Verify(e => e.OfferReceivedAsync(It.Is<MakeAnOfferListing>(l => l.Id == listing.Id), It.IsAny<Offer>(),
            It.Is<User>(u => u.Id == buyer.Id)), Times.Once);
        _audit.Verify(a => a.LogAsync("Offer", offer.Id, "OfferMade", buyer.Id, It.IsAny<string?>()), Times.Once);
    }

    [Theory]
    [InlineData("150.00", 201)]
    [InlineData("149.99", 400)]
    [InlineData("150.001", 400)]
    [InlineData("10000000.01", 400)]
    public async Task Make_AmountRules(string amount, int expected)
    {
        var (listing, _, buyer) = Setup();
        SignIn(buyer);

        var r = await Sut().MakeAsync(listing.Id, new MakeOfferRequest { AmountIncGst = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture) });

        r.HttpStatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task Make_WithoutACard_IsRejected()
    {
        var (listing, _, buyer) = Setup();
        _cards.Setup(c => c.HasValidCardAsync(buyer.Id)).ReturnsAsync(false);
        SignIn(buyer);

        var r = await Sut().MakeAsync(listing.Id, new MakeOfferRequest { AmountIncGst = 9000m });

        r.Error.Should().Be("Save a card before making an offer.");
    }

    [Fact]
    public async Task Make_ASecondPendingOffer_Conflicts_ButAfterWithdrawingItIsAllowed()
    {
        var (listing, _, buyer) = Setup();
        var first = await MakeOffer(listing, buyer);

        (await Sut().MakeAsync(listing.Id, new MakeOfferRequest { AmountIncGst = 9500m })).HttpStatusCode.Should().Be(409);

        (await Sut().WithdrawAsync(first.Id)).Succeeded.Should().BeTrue();
        (await Sut().MakeAsync(listing.Id, new MakeOfferRequest { AmountIncGst = 9500m })).HttpStatusCode.Should().Be(201);
    }

    [Fact]
    public async Task Make_OnAListingPastItsExpiry_IsRejected()
    {
        var (listing, _, buyer) = Setup(expiresAt: _clock.UtcNow.AddMinutes(-1));
        SignIn(buyer);

        var r = await Sut().MakeAsync(listing.Id, new MakeOfferRequest { AmountIncGst = 9000m });

        r.Error.Should().Be("This listing is no longer taking offers.");
    }

    [Fact]
    public async Task Withdraw_SomeoneElsesOffer_IsForbidden()
    {
        var (listing, _, buyer) = Setup();
        var offer = await MakeOffer(listing, buyer);
        using (var db = Db()) SignIn(AuctionTestData.Buyer(db, "Someone Else"));

        (await Sut().WithdrawAsync(offer.Id)).HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task TheOwningStud_SeesOffersWithTheBuyer_AnotherStudCannot()
    {
        var (listing, owner, buyer) = Setup();
        await MakeOffer(listing, buyer);

        SignIn(owner);
        var mine = (await Sut().GetForListingAsync(listing.Id)).Value!;
        mine.Single().BuyerEmail.Should().Be(buyer.Email);

        SignIn(new User { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active });
        (await Sut().GetForListingAsync(listing.Id)).HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GetMine_ShowsOnlyTheBuyersOwnOffers()
    {
        var (listing, _, buyer) = Setup();
        await MakeOffer(listing, buyer);
        User other;
        using (var db = Db()) other = AuctionTestData.Buyer(db, "Other");
        await MakeOffer(listing, other, 8000m);

        SignIn(buyer);
        var mine = (await Sut().GetMineAsync()).Value!;

        mine.Should().ContainSingle().Which.AmountIncGst.Should().Be(9000m);
    }

    [Fact]
    public async Task Decline_SetsDeclined_AndEmailsTheBuyer()
    {
        var (listing, owner, buyer) = Setup();
        var offer = await MakeOffer(listing, buyer);
        SignIn(owner);

        (await Sut().DeclineAsync(offer.Id)).Succeeded.Should().BeTrue();

        (await Db().Offers.SingleAsync()).Status.Should().Be(OfferStatus.Declined);
        _emails.Verify(e => e.OfferDeclinedAsync(It.IsAny<MakeAnOfferListing>(), It.IsAny<Offer>(), It.Is<User>(u => u.Id == buyer.Id)), Times.Once);
    }

    [Fact]
    public async Task Accept_CreatesThePendingSaleRecord_AndChargesStraightAway()
    {
        var (listing, owner, buyer) = Setup();
        var offer = await MakeOffer(listing, buyer);
        SignIn(owner);

        var r = await Sut().AcceptAsync(offer.Id);

        r.Succeeded.Should().BeTrue(r.Error);
        r.Value!.Charge.Should().Be("Paid");
        var sale = await Db().Purchases.SingleAsync();
        sale.OfferId.Should().Be(offer.Id);
        sale.BidId.Should().BeNull();
        sale.TotalPriceIncGst.Should().Be(9000m);
        sale.BuyerFeeIncGst.Should().Be(150m);
        sale.BuyerFeeGst.Should().Be(13.64m);
        sale.BuyerFeeExGst.Should().Be(136.36m);
        sale.BalancePayableToStudIncGst.Should().Be(8850m);
        (await Db().Offers.SingleAsync()).Status.Should().Be(OfferStatus.Accepted);
        (await Db().Listings.SingleAsync(l => l.Id == listing.Id)).Status.Should().Be(ListingStatus.Active);
        _charger.Verify(c => c.ChargeAsync(sale.Id), Times.Once);
    }

    [Fact]
    public async Task Accept_WhenTheProviderIsUnreachable_ReportsProcessing()
    {
        var (listing, owner, buyer) = Setup();
        var offer = await MakeOffer(listing, buyer);
        _charger.Setup(c => c.ChargeAsync(It.IsAny<Guid>())).ThrowsAsync(new HttpRequestException("down"));
        SignIn(owner);

        var r = await Sut().AcceptAsync(offer.Id);

        r.Succeeded.Should().BeTrue();
        r.Value!.Charge.Should().Be("Processing");
    }

    [Fact]
    public async Task Accept_AnExpiredOffer_IsRejected_AndNothingIsCharged()
    {
        var (listing, owner, buyer) = Setup();
        var offer = await MakeOffer(listing, buyer);
        _clock.Advance(TimeSpan.FromDays(8));
        SignIn(owner);

        (await Sut().AcceptAsync(offer.Id)).HttpStatusCode.Should().Be(400);
        (await Db().Purchases.CountAsync()).Should().Be(0);
        _charger.Verify(c => c.ChargeAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Accept_Several_IsAllowed()
    {
        var (listing, owner, buyer) = Setup();
        var a = await MakeOffer(listing, buyer);
        User other;
        using (var db = Db()) other = AuctionTestData.Buyer(db, "Other");
        var b = await MakeOffer(listing, other, 8000m);
        SignIn(owner);

        (await Sut().AcceptAsync(a.Id)).Succeeded.Should().BeTrue();
        (await Sut().AcceptAsync(b.Id)).Succeeded.Should().BeTrue();

        (await Db().Purchases.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Accept_TwiceConcurrently_CreatesOneSaleRecord()
    {
        var (listing, owner, buyer) = Setup();
        var offer = await MakeOffer(listing, buyer);
        SignIn(owner);
        // The second request read the offer (still Pending) before the first accepted it.
        using var staleDb = Db();
        await staleDb.Offers.SingleAsync(o => o.Id == offer.Id);

        (await Sut().AcceptAsync(offer.Id)).Succeeded.Should().BeTrue();
        var second = await Sut(staleDb).AcceptAsync(offer.Id);

        second.HttpStatusCode.Should().Be(409);
        (await Db().Purchases.CountAsync()).Should().Be(1);
        _charger.Verify(c => c.ChargeAsync(It.IsAny<Guid>()), Times.Once);
    }

    [Fact]
    public async Task AnotherStud_CannotAcceptOrDecline()
    {
        var (listing, _, buyer) = Setup();
        var offer = await MakeOffer(listing, buyer);
        User stranger;
        using (var db = Db())
        {
            stranger = new User { ObjectId = "x", Role = UserRole.StudFarmAdmin, Status = UserStatus.Active, Email = "s@x" };
            db.Users.Add(stranger);
            db.StudFarms.Add(new StudFarm { Name = "Other Stud", UserId = stranger.Id });
            db.SaveChanges();
        }
        SignIn(stranger);

        (await Sut().AcceptAsync(offer.Id)).HttpStatusCode.Should().Be(403);
        (await Sut().DeclineAsync(offer.Id)).HttpStatusCode.Should().Be(403);
    }
}
