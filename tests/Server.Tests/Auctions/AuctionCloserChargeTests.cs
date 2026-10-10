using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Payments;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Auctions;

public class AuctionCloserChargeTests
{
    private readonly AuctionCloserFixture _f = new();

    /// <summary>An ended auction with one winning bid of $10,000 by a buyer with a card.</summary>
    private (AuctionListing Listing, User Winner) EndedAuctionWithWinner(bool withCard = true) => _f.Seed(db =>
    {
        var listing = AuctionTestData.Auction(db, _f.Clock.UtcNow.AddMinutes(-1));
        var winner = AuctionTestData.Buyer(db, "Winner");
        if (withCard) AuctionTestData.Card(db, winner);
        AuctionTestData.Bid(db, listing, winner, 10000m);
        return (listing, winner);
    });

    private async Task<Purchase> SaleRecord() => await _f.Read().Purchases.SingleAsync();
    private async Task<AuctionListing> Listing() => await _f.Read().AuctionListings.SingleAsync();

    [Fact]
    public async Task ASuccessfulCharge_CompletesTheSale()
    {
        EndedAuctionWithWinner();

        var run = await _f.CreateSut().RunAsync();

        run.ChargesAttempted.Should().Be(1);
        var sale = await SaleRecord();
        sale.Status.Should().Be(PurchaseStatus.Completed);
        sale.PaymentProvider.Should().Be("Fake");
        sale.PaymentReference.Should().Be("pi_1");
        sale.PaidAt.Should().Be(_f.Clock.UtcNow);
        var listing = await Listing();
        listing.Status.Should().Be(ListingStatus.Sold);
        listing.WinningBidId.Should().Be(sale.BidId);
        _f.Charges.Single().AmountIncGst.Should().Be(150m);
        _f.Charges.Single().IdempotencyKey.Should().Be($"buyer-fee-{sale.Id}-1");
        _f.Emails.Verify(e => e.WonAndChargedAsync(It.IsAny<AuctionListing>(), It.IsAny<Purchase>(), It.IsAny<User>()), Times.Once);
        _f.Emails.Verify(e => e.StudSaleConfirmationAsync(It.IsAny<AuctionListing>(), It.IsAny<Purchase>(), It.IsAny<User>()), Times.Once);
        _f.Audit.Verify(a => a.LogAsync("Purchase", sale.Id, "BuyerFeeCharged", null, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task AFirstFailure_StartsTheGracePeriodFromSettings_AndEmailsTheWinner()
    {
        EndedAuctionWithWinner();
        _f.Settings.ChargeGracePeriodHours = 5;
        _f.ChargesDecline();

        await _f.CreateSut().RunAsync();

        var sale = await SaleRecord();
        sale.Status.Should().Be(PurchaseStatus.Pending);
        sale.ChargeDueBy.Should().Be(_f.Clock.UtcNow.AddHours(5));
        sale.LastChargeFailure.Should().Be("Your card was declined.");
        sale.ChargeAttemptStartedAt.Should().BeNull();
        (await Listing()).Status.Should().Be(ListingStatus.AwaitingPayment);
        _f.Emails.Verify(e => e.PaymentFailedAsync(It.IsAny<AuctionListing>(), It.IsAny<Purchase>(), It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task WithoutAValidCard_ItFailsWithoutCallingTheProvider()
    {
        EndedAuctionWithWinner(withCard: false);

        await _f.CreateSut().RunAsync();

        _f.Charges.Should().BeEmpty();
        (await SaleRecord()).LastChargeFailure.Should().Be("No valid card on file.");
    }

    [Fact]
    public async Task DuringTheGracePeriod_NothingHappensUntilANewCardIsSaved()
    {
        EndedAuctionWithWinner();
        _f.ChargesDecline();
        await _f.CreateSut().RunAsync();

        _f.Clock.Advance(TimeSpan.FromMinutes(30));
        await _f.CreateSut().RunAsync();
        _f.Charges.Should().HaveCount(1, "no retry until a new card is saved or the deadline passes");

        _f.Seed(db =>
        {
            var p = db.Purchases.Single();
            p.RetryRequested = true;
            db.SaveChanges();
            return p;
        });
        _f.ChargesSucceed();
        await _f.CreateSut().RunAsync();

        _f.Charges.Should().HaveCount(2);
        _f.Charges[1].IdempotencyKey.Should().EndWith("-2");
        (await SaleRecord()).Status.Should().Be(PurchaseStatus.Completed);
    }

    [Fact]
    public async Task ARepeatFailureInTheGracePeriod_DoesNotEmailAgain_OrMoveTheDeadline()
    {
        EndedAuctionWithWinner();
        _f.ChargesDecline();
        await _f.CreateSut().RunAsync();
        var deadline = (await SaleRecord()).ChargeDueBy;

        _f.Seed(db => { var p = db.Purchases.Single(); p.RetryRequested = true; db.SaveChanges(); return p; });
        _f.Clock.Advance(TimeSpan.FromMinutes(10));
        await _f.CreateSut().RunAsync();

        (await SaleRecord()).ChargeDueBy.Should().Be(deadline);
        _f.Emails.Verify(e => e.PaymentFailedAsync(It.IsAny<AuctionListing>(), It.IsAny<Purchase>(), It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task AtTheDeadline_AFinalFailure_MeansNoSale()
    {
        EndedAuctionWithWinner();
        _f.ChargesDecline();
        await _f.CreateSut().RunAsync();

        _f.Clock.Advance(TimeSpan.FromHours(2));
        await _f.CreateSut().RunAsync();

        _f.Charges.Should().HaveCount(2);
        (await SaleRecord()).Status.Should().Be(PurchaseStatus.Voided);
        var listing = await Listing();
        listing.Status.Should().Be(ListingStatus.Unsold);
        listing.CloseReason.Should().Be(ListingCloseReason.ChargeFailed);
        _f.Emails.Verify(e => e.PaymentAbandonedAsync(It.IsAny<AuctionListing>(), It.IsAny<Purchase>(), It.IsAny<User>()), Times.Once);
        _f.Emails.Verify(e => e.StudNoSaleAsync(It.IsAny<AuctionListing>(), ListingCloseReason.ChargeFailed), Times.Once);
    }

    [Fact]
    public async Task AtTheDeadline_AFinalSuccess_CompletesTheSale()
    {
        EndedAuctionWithWinner();
        _f.ChargesDecline();
        await _f.CreateSut().RunAsync();

        _f.Clock.Advance(TimeSpan.FromHours(2));
        _f.ChargesSucceed();
        await _f.CreateSut().RunAsync();

        (await SaleRecord()).Status.Should().Be(PurchaseStatus.Completed);
    }

    [Fact]
    public async Task AnInterruptedAttempt_IsRepeatedWithTheSameIdempotencyKey()
    {
        EndedAuctionWithWinner();
        _f.Provider.Setup(p => p.ChargeSavedCardAsync(It.IsAny<ChargeRequest>()))
            .Callback<ChargeRequest>(_f.Charges.Add)
            .ThrowsAsync(new HttpRequestException("provider unreachable"));

        var first = await _f.CreateSut().RunAsync();
        first.ChargesAttempted.Should().Be(0);
        (await SaleRecord()).ChargeAttemptStartedAt.Should().NotBeNull();

        _f.Clock.Advance(TimeSpan.FromMinutes(1));
        await _f.CreateSut().RunAsync();
        _f.Charges.Should().HaveCount(1, "an attempt younger than 2 minutes may still be in flight");

        _f.Clock.Advance(TimeSpan.FromMinutes(2));
        _f.ChargesSucceed();
        await _f.CreateSut().RunAsync();

        _f.Charges.Should().HaveCount(2);
        _f.Charges[1].IdempotencyKey.Should().Be(_f.Charges[0].IdempotencyKey);
        (await SaleRecord()).Status.Should().Be(PurchaseStatus.Completed);
    }
}
