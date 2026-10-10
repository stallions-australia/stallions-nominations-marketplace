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
        var (_, winner) = EndedAuctionWithWinner();
        _f.ChargesDecline();
        await _f.CreateSut().RunAsync();

        _f.Clock.Advance(TimeSpan.FromMinutes(30));
        await _f.CreateSut().RunAsync();
        _f.Charges.Should().HaveCount(1, "no retry until a new card is saved or the deadline passes");

        _f.Seed(db =>
        {
            var p = db.Purchases.Single();
            p.RetryRequested = true;
            db.SavedCards.Single(c => c.UserId == winner.Id).ProviderPaymentMethodId = "pm_new";
            db.SaveChanges();
            return p;
        });
        _f.ChargesSucceed();
        await _f.CreateSut().RunAsync();

        _f.Charges.Should().HaveCount(2);
        _f.Charges[1].IdempotencyKey.Should().EndWith("-2");
        _f.Charges[1].PaymentMethodId.Should().Be("pm_new", "attempt 2 is a new attempt and uses the newly saved card");
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
        _f.Emails.Verify(e => e.WonAndChargedAsync(It.IsAny<AuctionListing>(), It.IsAny<Purchase>(), It.IsAny<User>()), Times.Once);
        _f.Emails.Verify(e => e.StudSaleConfirmationAsync(It.IsAny<AuctionListing>(), It.IsAny<Purchase>(), It.IsAny<User>()), Times.Once);
        _f.Emails.Verify(e => e.PaymentFailedAsync(It.IsAny<AuctionListing>(), It.IsAny<Purchase>(), It.IsAny<User>()), Times.Never);
    }

    private void ProviderThrows() =>
        _f.Provider.Setup(p => p.ChargeSavedCardAsync(It.IsAny<ChargeRequest>()))
            .Callback<ChargeRequest>(_f.Charges.Add)
            .ThrowsAsync(new HttpRequestException("provider unreachable"));

    [Fact]
    public async Task AnInterruptedAttempt_IsRepeatedWithTheOriginalCard_EvenIfTheCardWasReplaced()
    {
        var (_, winner) = EndedAuctionWithWinner();
        ProviderThrows();
        await _f.CreateSut().RunAsync();

        _f.Seed(db =>
        {
            db.SavedCards.Single(c => c.UserId == winner.Id).ProviderPaymentMethodId = "pm_replaced";
            db.SaveChanges();
            return 0;
        });
        _f.Clock.Advance(TimeSpan.FromMinutes(3));
        _f.ChargesSucceed();
        await _f.CreateSut().RunAsync();

        _f.Charges.Should().HaveCount(2);
        _f.Charges[1].IdempotencyKey.Should().Be(_f.Charges[0].IdempotencyKey);
        _f.Charges[1].PaymentMethodId.Should().Be("pm_good");
        (await SaleRecord()).Status.Should().Be(PurchaseStatus.Completed);
    }

    [Fact]
    public async Task AnInterruptedAttempt_IsRepeatedWithTheOriginalCard_EvenIfTheCardExpired()
    {
        var (_, winner) = EndedAuctionWithWinner();
        ProviderThrows();
        await _f.CreateSut().RunAsync();

        _f.Seed(db =>
        {
            db.SavedCards.Single(c => c.UserId == winner.Id).ExpYear = 2000;
            db.SaveChanges();
            return 0;
        });
        _f.Clock.Advance(TimeSpan.FromMinutes(3));
        _f.ChargesSucceed();
        await _f.CreateSut().RunAsync();

        _f.Charges.Should().HaveCount(2);
        _f.Charges[1].PaymentMethodId.Should().Be("pm_good");
        var sale = await SaleRecord();
        sale.Status.Should().Be(PurchaseStatus.Completed);
        sale.LastChargeFailure.Should().BeNull();
    }

    [Fact]
    public async Task AStuckAttempt_IsFlaggedForStaff_AndTheProviderIsNotCalledAgain()
    {
        EndedAuctionWithWinner();
        ProviderThrows();
        await _f.CreateSut().RunAsync();

        var started = (await SaleRecord()).ChargeAttemptStartedAt;
        _f.Clock.Advance(TimeSpan.FromMinutes(3));
        await _f.CreateSut().RunAsync();
        _f.Charges.Should().HaveCount(2);

        _f.Clock.Advance(TimeSpan.FromMinutes(61));
        await _f.CreateSut().RunAsync();
        await _f.CreateSut().RunAsync();

        _f.Charges.Should().HaveCount(2, "a stuck attempt is never sent again automatically");
        var sale = await SaleRecord();
        sale.ChargeNeedsAttention.Should().BeTrue();
        sale.ChargeAttemptStartedAt.Should().Be(started, "repeats never move the start of the attempt");
        sale.Status.Should().Be(PurchaseStatus.Pending);
        _f.Audit.Verify(a => a.LogAsync("Purchase", sale.Id, "BuyerFeeChargeStuck", null, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ASuccessThatArrivesAfterTheSaleRecordChanged_IsAuditedForStaff()
    {
        EndedAuctionWithWinner();
        _f.Provider.Setup(p => p.ChargeSavedCardAsync(It.IsAny<ChargeRequest>()))
            .Callback<ChargeRequest>(r =>
            {
                _f.Charges.Add(r);
                _f.Seed(db => { db.Purchases.Single().Status = PurchaseStatus.Voided; db.SaveChanges(); return 0; });
            })
            .ReturnsAsync(ChargeResult.Success("pi_late"));

        var run = await _f.CreateSut().RunAsync();

        run.ChargesAttempted.Should().Be(0);
        var sale = await SaleRecord();
        sale.Status.Should().Be(PurchaseStatus.Voided);
        _f.Audit.Verify(a => a.LogAsync("Purchase", sale.Id, "BuyerFeeChargeUnmatched", null,
            It.Is<string?>(d => d != null && d.Contains("pi_late"))), Times.Once);
    }

    [Fact]
    public async Task TheChargeMetadata_NamesThePurchaseAndTheListing()
    {
        var (listing, _) = EndedAuctionWithWinner();

        await _f.CreateSut().RunAsync();

        var sale = await SaleRecord();
        _f.Charges.Single().Metadata.Should().Contain("purchaseId", sale.Id.ToString())
            .And.Contain("listingId", listing.Id.ToString())
            .And.Contain("kind", "buyer-fee");
    }

    [Fact]
    public async Task ARepeatIsNotMade_WhileTheLastSendIsRecent_EvenIfTheAttemptIsOld()
    {
        EndedAuctionWithWinner();
        ProviderThrows();
        await _f.CreateSut().RunAsync();

        _f.Clock.Advance(TimeSpan.FromMinutes(3));
        await _f.CreateSut().RunAsync();
        _f.Charges.Should().HaveCount(2);

        _f.Clock.Advance(TimeSpan.FromMinutes(1));
        await _f.CreateSut().RunAsync();

        _f.Charges.Should().HaveCount(2, "the previous send was only a minute ago and may still be in flight");
    }

    [Fact]
    public async Task ADuplicateConfirmationOfTheRecordedCharge_IsNotAnAlert()
    {
        EndedAuctionWithWinner();
        _f.Provider.Setup(p => p.ChargeSavedCardAsync(It.IsAny<ChargeRequest>()))
            .Callback<ChargeRequest>(r =>
            {
                _f.Charges.Add(r);
                _f.Seed(db =>
                {
                    var p = db.Purchases.Single();
                    p.Status = PurchaseStatus.Completed;
                    p.PaymentReference = "pi_dup";
                    db.SaveChanges();
                    return 0;
                });
            })
            .ReturnsAsync(ChargeResult.Success("pi_dup"));

        await _f.CreateSut().RunAsync();

        _f.Audit.Verify(a => a.LogAsync("Purchase", It.IsAny<Guid>(), "BuyerFeeChargeUnmatched", null, It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ARepeatThatDeclines_KeepsTheRetryRequest_SoTheNewCardIsUsedNext()
    {
        var (_, winner) = EndedAuctionWithWinner();
        ProviderThrows();
        await _f.CreateSut().RunAsync();

        _f.Seed(db => { db.Purchases.Single().RetryRequested = true; db.SaveChanges(); return 0; });
        _f.Clock.Advance(TimeSpan.FromMinutes(3));
        _f.ChargesDecline();
        await _f.CreateSut().RunAsync();
        (await SaleRecord()).RetryRequested.Should().BeTrue();

        _f.Seed(db =>
        {
            db.SavedCards.Single(c => c.UserId == winner.Id).ProviderPaymentMethodId = "pm_new";
            db.SaveChanges();
            return 0;
        });
        _f.ChargesSucceed();
        await _f.CreateSut().RunAsync();

        _f.Charges.Should().HaveCount(3);
        _f.Charges[2].IdempotencyKey.Should().EndWith("-2");
        _f.Charges[2].PaymentMethodId.Should().Be("pm_new");
        (await SaleRecord()).Status.Should().Be(PurchaseStatus.Completed);
    }

    [Fact]
    public async Task AResultForAFlaggedAttempt_ClearsTheFlag_AndAFinalDeclineVoidsTheSale()
    {
        EndedAuctionWithWinner();
        _f.ChargesDecline();
        await _f.CreateSut().RunAsync();

        _f.Clock.Advance(TimeSpan.FromHours(2));
        _f.Provider.Setup(p => p.ChargeSavedCardAsync(It.IsAny<ChargeRequest>()))
            .Callback<ChargeRequest>(r =>
            {
                _f.Charges.Add(r);
                _f.Seed(db => { db.Purchases.Single().ChargeNeedsAttention = true; db.SaveChanges(); return 0; });
            })
            .ReturnsAsync(ChargeResult.Declined("card_declined", "Your card was declined."));
        await _f.CreateSut().RunAsync();

        var sale = await SaleRecord();
        sale.Status.Should().Be(PurchaseStatus.Voided);
        sale.ChargeNeedsAttention.Should().BeFalse();
    }

    [Fact]
    public async Task AResultForAFlaggedAttempt_ClearsTheFlag_AndASuccessCompletesTheSale()
    {
        EndedAuctionWithWinner();
        _f.Provider.Setup(p => p.ChargeSavedCardAsync(It.IsAny<ChargeRequest>()))
            .Callback<ChargeRequest>(r =>
            {
                _f.Charges.Add(r);
                _f.Seed(db => { db.Purchases.Single().ChargeNeedsAttention = true; db.SaveChanges(); return 0; });
            })
            .ReturnsAsync(ChargeResult.Success("pi_ok"));

        await _f.CreateSut().RunAsync();

        var sale = await SaleRecord();
        sale.Status.Should().Be(PurchaseStatus.Completed);
        sale.ChargeNeedsAttention.Should().BeFalse();
    }
}
