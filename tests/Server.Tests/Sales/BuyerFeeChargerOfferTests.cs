using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Sales;
using Stallions.Server.TaxDocuments;
using Stallions.Server.Tests.Auctions;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Sales;

public class BuyerFeeChargerOfferTests
{
    private readonly AuctionCloserFixture _f = new();

    /// <summary>An Active Make an Offer listing with an accepted $9,000 offer and its Pending sale record.</summary>
    private (Guid ListingId, Guid PurchaseId, Guid OtherOfferId) AcceptedOffer(bool withCard = true) => _f.Seed(db =>
    {
        var auction = AuctionTestData.Auction(db, _f.Clock.UtcNow.AddDays(-2), status: ListingStatus.Unsold);
        var listing = new MakeAnOfferListing
        {
            StallionId = auction.StallionId, SeasonId = auction.SeasonId, StudFarmId = auction.StudFarmId,
            ListingType = ListingType.MakeAnOffer, Status = ListingStatus.Active, BuyerFeeIncGst = 150m,
            SourceAuctionListingId = auction.Id, ExpiresAt = _f.Clock.UtcNow.AddDays(30), PublishedAt = _f.Clock.UtcNow
        };
        db.MakeAnOfferListings.Add(listing);
        var buyer = AuctionTestData.Buyer(db, "Offer Buyer");
        if (withCard) AuctionTestData.Card(db, buyer);
        var other = AuctionTestData.Buyer(db, "Other Buyer");
        var offer = new Offer { ListingId = listing.Id, BuyerUserId = buyer.Id, AmountIncGst = 9000m,
            Status = OfferStatus.Accepted, CreatedAt = _f.Clock.UtcNow, ExpiresAt = _f.Clock.UtcNow.AddDays(7) };
        var pending = new Offer { ListingId = listing.Id, BuyerUserId = other.Id, AmountIncGst = 8000m,
            CreatedAt = _f.Clock.UtcNow, ExpiresAt = _f.Clock.UtcNow.AddDays(7) };
        db.Offers.AddRange(offer, pending);
        var purchase = new Purchase
        {
            ListingId = listing.Id, BuyerUserId = buyer.Id, OfferId = offer.Id, TotalPriceIncGst = 9000m,
            BuyerFeeIncGst = 150m, BuyerFeeExGst = 136.36m, BuyerFeeGst = 13.64m, BalancePayableToStudIncGst = 8850m,
            Status = PurchaseStatus.Pending, CreatedAt = _f.Clock.UtcNow
        };
        db.Purchases.Add(purchase);
        db.SaveChanges();
        return (listing.Id, purchase.Id, pending.Id);
    });

    private async Task<Purchase> Sale(Guid id) => await _f.Read().Purchases.SingleAsync(p => p.Id == id);
    private async Task<Listing> Listing(Guid id) => await _f.Read().Listings.SingleAsync(l => l.Id == id);

    [Fact]
    public async Task OfferSale_Paid_CompletesTheSaleRecord_AndLeavesTheListingOpen()
    {
        var (listingId, purchaseId, otherOfferId) = AcceptedOffer();

        var outcome = await _f.CreateCharger().ChargeAsync(purchaseId);

        outcome.Should().Be(ChargeOutcome.Paid);
        (await Sale(purchaseId)).Status.Should().Be(PurchaseStatus.Completed);
        (await Listing(listingId)).Status.Should().Be(ListingStatus.Active);
        (await _f.Read().Offers.SingleAsync(o => o.Id == otherOfferId)).Status.Should().Be(OfferStatus.Pending);
        _f.Emails.Verify(e => e.WonAndChargedAsync(It.Is<Listing>(l => l.Id == listingId), It.IsAny<Purchase>(), It.IsAny<User>()), Times.Once);
        _f.Emails.Verify(e => e.StudSaleConfirmationAsync(It.Is<Listing>(l => l.Id == listingId), It.IsAny<Purchase>(), It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task OfferSale_FirstFailure_StartsTheGracePeriod()
    {
        var (_, purchaseId, _) = AcceptedOffer();
        _f.ChargesDecline();

        var outcome = await _f.CreateCharger().ChargeAsync(purchaseId);

        outcome.Should().Be(ChargeOutcome.PaymentFailed);
        (await Sale(purchaseId)).ChargeDueBy.Should().Be(_f.Clock.UtcNow.AddHours(2));
    }

    [Fact]
    public async Task OfferSale_FinalFailure_LeavesTheListingOpen()
    {
        var (listingId, purchaseId, otherOfferId) = AcceptedOffer();
        _f.ChargesDecline();
        await _f.CreateCharger().ChargeAsync(purchaseId);
        _f.Clock.Advance(TimeSpan.FromHours(2));

        var outcome = await _f.CreateCharger().ChargeAsync(purchaseId);

        outcome.Should().Be(ChargeOutcome.NoSale);
        (await Sale(purchaseId)).Status.Should().Be(PurchaseStatus.Voided);
        var listing = await Listing(listingId);
        listing.Status.Should().Be(ListingStatus.Active);
        listing.CloseReason.Should().BeNull();
        (await _f.Read().Offers.SingleAsync(o => o.Id == otherOfferId)).Status.Should().Be(OfferStatus.Pending);
        _f.Emails.Verify(e => e.StudNoSaleAsync(It.Is<Listing>(l => l.Id == listingId), ListingCloseReason.ChargeFailed), Times.Once);
    }

    [Fact]
    public async Task NotDue_IsSkipped()
    {
        var (_, purchaseId, _) = AcceptedOffer();
        await _f.CreateCharger().ChargeAsync(purchaseId);

        (await _f.CreateCharger().ChargeAsync(purchaseId)).Should().Be(ChargeOutcome.Skipped);
    }

    [Fact]
    public async Task Paid_IssuesTheBuyerFeeInvoice_OnceTheSaleIsCompleted_BeforeTheWinEmail()
    {
        var (_, purchaseId, _) = AcceptedOffer();
        var calls = new List<string>();
        PurchaseStatus? statusAtIssue = null;
        _f.TaxDocuments.Setup(t => t.IssueBuyerFeeInvoiceAsync(It.IsAny<Purchase>()))
            .Callback<Purchase>(p => { statusAtIssue = p.Status; calls.Add("invoice"); })
            .ReturnsAsync(new TaxDocumentIssueResult(TaxDocumentIssueStatus.Issued, new TaxDocument()));
        _f.Emails.Setup(e => e.WonAndChargedAsync(It.IsAny<Listing>(), It.IsAny<Purchase>(), It.IsAny<User>()))
            .Callback(() => calls.Add("win email"))
            .Returns(Task.CompletedTask);

        (await _f.CreateCharger().ChargeAsync(purchaseId)).Should().Be(ChargeOutcome.Paid);

        statusAtIssue.Should().Be(PurchaseStatus.Completed);
        calls.Should().Equal("invoice", "win email");
        _f.TaxDocuments.Verify(t => t.IssueBuyerFeeInvoiceAsync(It.Is<Purchase>(p => p.Id == purchaseId)), Times.Once);
        _f.TaxDocuments.Verify(t => t.RecordPendingAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<TaxDocumentKind>()), Times.Never);
    }

    [Fact]
    public async Task Paid_WithoutBusinessDetails_StillCompletesTheSale_AndRecordsPending()
    {
        var (_, purchaseId, _) = AcceptedOffer();
        _f.TaxDocuments.Setup(t => t.IssueBuyerFeeInvoiceAsync(It.IsAny<Purchase>())).ReturnsAsync(TaxDocumentIssueResult.Missing);

        (await _f.CreateCharger().ChargeAsync(purchaseId)).Should().Be(ChargeOutcome.Paid);

        (await Sale(purchaseId)).Status.Should().Be(PurchaseStatus.Completed);
        _f.TaxDocuments.Verify(t => t.RecordPendingAsync("Purchase", purchaseId, TaxDocumentKind.BuyerFeeInvoice), Times.Once);
        _f.Emails.Verify(e => e.WonAndChargedAsync(It.IsAny<Listing>(), It.IsAny<Purchase>(), It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task Declined_IssuesNoInvoice()
    {
        var (_, purchaseId, _) = AcceptedOffer();
        _f.ChargesDecline();

        (await _f.CreateCharger().ChargeAsync(purchaseId)).Should().Be(ChargeOutcome.PaymentFailed);

        _f.TaxDocuments.Verify(t => t.IssueBuyerFeeInvoiceAsync(It.IsAny<Purchase>()), Times.Never);
    }

    [Fact]
    public async Task FinalFailure_IssuesNoInvoice()
    {
        var (_, purchaseId, _) = AcceptedOffer();
        _f.ChargesDecline();
        await _f.CreateCharger().ChargeAsync(purchaseId);
        _f.Clock.Advance(TimeSpan.FromHours(2));

        (await _f.CreateCharger().ChargeAsync(purchaseId)).Should().Be(ChargeOutcome.NoSale);

        _f.TaxDocuments.Verify(t => t.IssueBuyerFeeInvoiceAsync(It.IsAny<Purchase>()), Times.Never);
    }
}
