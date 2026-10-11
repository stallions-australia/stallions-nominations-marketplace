using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Server.TaxDocuments;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.DTOs.Checkout;
using Stallions.Shared;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class PurchaseServiceTests
{
    private readonly Mock<IPurchaseRepository> _purchases = new();
    private readonly Mock<IAuditLogRepository> _audit = new();
    private readonly Mock<IUserService> _users = new();

    private readonly Mock<IListingRepository> _listings = new();
    private readonly Mock<IBidRepository> _bids = new();
    private readonly Mock<ITaxDocumentRepository> _taxDocumentRepo = new();
    private readonly Mock<ITaxDocumentService> _taxDocuments = new();

    private PurchaseService CreateSut() =>
        new(_purchases.Object, _audit.Object, _users.Object, _listings.Object, _bids.Object,
            _taxDocumentRepo.Object, _taxDocuments.Object, new InlineTransactionRunner());

    private (User Buyer, AuctionListing Listing) BuyerAndAuction(ListingStatus status, ListingCloseReason? reason = null)
    {
        var buyer = Buyer();
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var listing = new AuctionListing { Id = Guid.NewGuid(), Status = status, CloseReason = reason };
        _listings.Setup(r => r.GetAuctionByIdAsync(listing.Id)).ReturnsAsync(listing);
        return (buyer, listing);
    }

    [Theory]
    [InlineData(PurchaseStatus.Completed, false, AuctionOutcomes.Won)]
    [InlineData(PurchaseStatus.Pending, false, AuctionOutcomes.PaymentPending)]
    [InlineData(PurchaseStatus.Pending, true, AuctionOutcomes.PaymentFailed)]
    [InlineData(PurchaseStatus.Voided, true, AuctionOutcomes.NoSale)]
    public async Task MyAuctionResult_ForTheWinner_FollowsTheSaleRecord(PurchaseStatus status, bool failed, string expected)
    {
        var (buyer, listing) = BuyerAndAuction(ListingStatus.AwaitingPayment);
        var dueBy = failed ? DateTime.UtcNow.AddHours(1) : (DateTime?)null;
        var sale = new Purchase { Id = Guid.NewGuid(), Status = status, ChargeDueBy = dueBy };
        _purchases.Setup(r => r.GetByListingAndBuyerAsync(listing.Id, buyer.Id)).ReturnsAsync(sale);

        var result = (await CreateSut().GetMyAuctionResultAsync(listing.Id)).Value!;

        result.Outcome.Should().Be(expected);
        result.PurchaseId.Should().Be(sale.Id);
        result.ChargeDueBy.Should().Be(dueBy);
    }

    [Theory]
    [InlineData(ListingStatus.Sold, null, AuctionOutcomes.Lost)]
    [InlineData(ListingStatus.Unsold, ListingCloseReason.ReserveNotMet, AuctionOutcomes.EndedWithoutSale)]
    [InlineData(ListingStatus.Unsold, ListingCloseReason.ChargeFailed, AuctionOutcomes.Lost)]
    public async Task MyAuctionResult_ForAnotherBidder(ListingStatus status, ListingCloseReason? reason, string expected)
    {
        var (buyer, listing) = BuyerAndAuction(status, reason);
        _bids.Setup(r => r.GetByAuctionListingIdAsync(listing.Id))
            .ReturnsAsync(new List<Bid> { new() { BuyerUserId = buyer.Id, AmountIncGst = 5000m } });

        (await CreateSut().GetMyAuctionResultAsync(listing.Id)).Value!.Outcome.Should().Be(expected);
    }

    [Fact]
    public async Task MyAuctionResult_WhileOpen_OrWithoutBids_IsNone()
    {
        var (_, open) = BuyerAndAuction(ListingStatus.Active);
        _bids.Setup(r => r.GetByAuctionListingIdAsync(It.IsAny<Guid>())).ReturnsAsync(new List<Bid>());

        (await CreateSut().GetMyAuctionResultAsync(open.Id)).Value!.Outcome.Should().Be(AuctionOutcomes.None);
    }

    private static User Buyer() => new() { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.Active };

    private (User Staff, Purchase Sale) StaffAndCompletedSale()
    {
        var staff = new User { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active };
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(staff);
        var sale = new Purchase
        {
            Id = Guid.NewGuid(), Status = PurchaseStatus.Completed, TotalPriceIncGst = 10000m, BuyerFeeIncGst = 150m,
            BalancePayableToStudIncGst = 9850m, PaymentProvider = "Stripe", PaymentReference = "pi_1"
        };
        _purchases.Setup(r => r.GetByIdAsync(sale.Id)).ReturnsAsync(sale);
        return (staff, sale);
    }

    [Fact]
    public async Task Refund_WithATaxInvoice_IssuesAnAdjustmentNote_AndSaysSo()
    {
        var (staff, sale) = StaffAndCompletedSale();
        var invoice = new TaxDocument { Id = Guid.NewGuid(), Number = "SA-000124", Kind = TaxDocumentKind.BuyerFeeInvoice };
        _taxDocumentRepo.Setup(r => r.GetBuyerFeeInvoiceAsync(sale.Id)).ReturnsAsync(invoice);
        var note = new TaxDocument { Id = Guid.NewGuid(), Number = "SA-000130", Kind = TaxDocumentKind.AdjustmentNote };
        _taxDocuments.Setup(t => t.IssueAdjustmentNoteAsync(invoice, "Buyer fee refunded — Duplicate charge", staff.Id))
            .ReturnsAsync(new TaxDocumentIssueResult(TaxDocumentIssueStatus.Issued, note));

        var r = await CreateSut().RefundAsync(sale.Id, new RefundBuyerFeeRequest { Note = "  Duplicate charge " });

        r.Succeeded.Should().BeTrue(r.Error);
        r.Value!.AdjustmentNoteNumber.Should().Be("SA-000130");
        r.Value.AdjustmentNoteId.Should().Be(note.Id);
        r.Value.Message.Should().Be("Refund recorded. Adjustment note SA-000130 has been emailed to the buyer.");
        sale.Status.Should().Be(PurchaseStatus.Refunded);
        _audit.Verify(a => a.LogAsync("Purchase", sale.Id, "PurchaseRefunded", staff.Id,
            It.Is<string?>(d => d != null && d.Contains("SA-000130"))), Times.Once);
    }

    [Fact]
    public async Task Refund_WhenTheNoteWasAlreadyIssued_DoesNotSayItWasJustEmailed()
    {
        var (_, sale) = StaffAndCompletedSale();
        var invoice = new TaxDocument { Id = Guid.NewGuid(), Number = "SA-000124", Kind = TaxDocumentKind.BuyerFeeInvoice };
        _taxDocumentRepo.Setup(r => r.GetBuyerFeeInvoiceAsync(sale.Id)).ReturnsAsync(invoice);
        var note = new TaxDocument { Id = Guid.NewGuid(), Number = "SA-000130", Kind = TaxDocumentKind.AdjustmentNote };
        _taxDocuments.Setup(t => t.IssueAdjustmentNoteAsync(invoice, It.IsAny<string>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new TaxDocumentIssueResult(TaxDocumentIssueStatus.Existing, note));

        var r = await CreateSut().RefundAsync(sale.Id, new RefundBuyerFeeRequest());

        r.Value!.Message.Should().Be("Refund recorded. Adjustment note SA-000130 was already issued.");
    }

    [Fact]
    public async Task Refund_LosingAConcurrentRefund_IsAConflict()
    {
        var (_, sale) = StaffAndCompletedSale();
        _purchases.Setup(p => p.UpdateAsync(sale))
            .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("stamp changed"));

        var r = await CreateSut().RefundAsync(sale.Id, new RefundBuyerFeeRequest());

        r.HttpStatusCode.Should().Be(409);
        r.Error.Should().Be("This sale was just refunded — refresh the page.");
        _audit.Verify(a => a.LogAsync("Purchase", sale.Id, "PurchaseRefunded", It.IsAny<Guid?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Refund_WithoutBusinessDetails_IsRejected_AndNothingChanges()
    {
        var (_, sale) = StaffAndCompletedSale();
        _taxDocumentRepo.Setup(r => r.GetBuyerFeeInvoiceAsync(sale.Id)).ReturnsAsync(new TaxDocument { Number = "SA-000124" });
        _taxDocuments.Setup(t => t.IssueAdjustmentNoteAsync(It.IsAny<TaxDocument>(), It.IsAny<string>(), It.IsAny<Guid?>()))
            .ReturnsAsync(TaxDocumentIssueResult.Missing);

        var r = await CreateSut().RefundAsync(sale.Id, new RefundBuyerFeeRequest());

        r.HttpStatusCode.Should().Be(400);
        r.Error.Should().Be("Complete the business details in Settings first.");
        sale.Status.Should().Be(PurchaseStatus.Completed);
        _purchases.Verify(p => p.UpdateAsync(It.IsAny<Purchase>()), Times.Never);
    }

    [Fact]
    public async Task Refund_OfASaleWithNoTaxInvoice_IsRecordedWithoutANote()
    {
        var (_, sale) = StaffAndCompletedSale();

        var r = await CreateSut().RefundAsync(sale.Id, new RefundBuyerFeeRequest { Note = "Goodwill" });

        r.Value!.AdjustmentNoteNumber.Should().BeNull();
        r.Value.Message.Should().Be("Refund recorded. This sale has no tax invoice, so no adjustment note was issued.");
        sale.Status.Should().Be(PurchaseStatus.Refunded);
        _taxDocuments.Verify(t => t.IssueAdjustmentNoteAsync(It.IsAny<TaxDocument>(), It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
    }

    [Fact]
    public async Task Refund_WithoutANote_UsesThePlainReason()
    {
        var (staff, sale) = StaffAndCompletedSale();
        var invoice = new TaxDocument { Number = "SA-000124" };
        _taxDocumentRepo.Setup(r => r.GetBuyerFeeInvoiceAsync(sale.Id)).ReturnsAsync(invoice);
        _taxDocuments.Setup(t => t.IssueAdjustmentNoteAsync(invoice, "Buyer fee refunded", staff.Id))
            .ReturnsAsync(new TaxDocumentIssueResult(TaxDocumentIssueStatus.Issued, new TaxDocument { Number = "SA-000130" }));

        (await CreateSut().RefundAsync(sale.Id, new RefundBuyerFeeRequest { Note = "   " })).Succeeded.Should().BeTrue();

        _taxDocuments.Verify(t => t.IssueAdjustmentNoteAsync(invoice, "Buyer fee refunded", staff.Id), Times.Once);
    }

    [Fact]
    public async Task Refund_WithANoteOver500Characters_IsRejected()
    {
        var (_, sale) = StaffAndCompletedSale();

        var r = await CreateSut().RefundAsync(sale.Id, new RefundBuyerFeeRequest { Note = new string('x', 501) });

        r.Error.Should().Be("The note can be at most 500 characters.");
    }

    [Fact]
    public async Task Refund_RefundsTheFullBuyerFee()
    {
        var staff = new User { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active };
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(staff);
        var purchase = new Purchase
        {
            Id = Guid.NewGuid(), Status = PurchaseStatus.Completed,
            TotalPriceIncGst = 10000m, BuyerFeeIncGst = 150m, BalancePayableToStudIncGst = 9850m
        };
        _purchases.Setup(r => r.GetByIdAsync(purchase.Id)).ReturnsAsync(purchase);

        var result = await CreateSut().RefundAsync(purchase.Id, new RefundBuyerFeeRequest());

        result.Succeeded.Should().BeTrue();
        purchase.RefundAmount.Should().Be(150m);
        purchase.Status.Should().Be(PurchaseStatus.Refunded);
    }

    [Fact]
    public async Task Refund_AuditsTheRefundAmountAndPaymentReference()
    {
        _users.Setup(u => u.GetOrCreateCurrentUserAsync())
            .ReturnsAsync(new User { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active });
        var purchase = new Purchase
        {
            Id = Guid.NewGuid(), Status = PurchaseStatus.Completed, BuyerFeeIncGst = 150m,
            PaymentProvider = "Stripe", PaymentReference = "pi_refund_me"
        };
        _purchases.Setup(r => r.GetByIdAsync(purchase.Id)).ReturnsAsync(purchase);

        await CreateSut().RefundAsync(purchase.Id, new RefundBuyerFeeRequest());

        _audit.Verify(a => a.LogAsync("Purchase", purchase.Id, "PurchaseRefunded", It.IsAny<Guid?>(),
            It.Is<string?>(d => d != null && d.Contains("pi_refund_me") && d.Contains("Stripe"))), Times.Once);
    }

    [Fact]
    public async Task Refund_ByABuyer_IsForbidden()
    {
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(Buyer());

        var result = await CreateSut().RefundAsync(Guid.NewGuid(), new RefundBuyerFeeRequest());

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GetPurchaseById_ForAnotherBuyersSaleRecord_IsForbidden()
    {
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(Buyer());
        var purchase = new Purchase { Id = Guid.NewGuid(), BuyerUserId = Guid.NewGuid() };
        _purchases.Setup(r => r.GetByIdAsync(purchase.Id)).ReturnsAsync(purchase);

        var result = await CreateSut().GetPurchaseByIdAsync(purchase.Id);

        result.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GetPurchases_MapsTheChargeState()
    {
        var buyer = Buyer();
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var dueBy = new DateTime(2026, 10, 10, 4, 0, 0, DateTimeKind.Utc);
        _purchases.Setup(r => r.GetByBuyerIdAsync(buyer.Id)).ReturnsAsync(new List<Purchase>
        {
            new()
            {
                BuyerUserId = buyer.Id, Status = PurchaseStatus.Pending, ChargeDueBy = dueBy,
                LastChargeFailure = "Your card was declined.",
                Listing = new AuctionListing
                {
                    Stallion = new Stallion { Name = "Snitzel" },
                    Season = new Season { Name = "2026 Season" },
                    StudFarm = new StudFarm { Name = "Arrowfield" }
                }
            }
        });

        var result = await CreateSut().GetPurchasesAsync();

        var dto = result.Value!.Single();
        dto.StallionName.Should().Be("Snitzel");
        dto.SeasonName.Should().Be("2026 Season");
        dto.StudFarmName.Should().Be("Arrowfield");
        dto.ChargeDueBy.Should().Be(dueBy);
        dto.LastChargeFailure.Should().Be("Your card was declined.");
    }

    [Fact]
    public async Task GetPurchases_SaysWhetherTheSaleCameFromAnOffer()
    {
        var buyer = Buyer();
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        _purchases.Setup(r => r.GetByBuyerIdAsync(buyer.Id)).ReturnsAsync(new List<Purchase>
        {
            new() { BuyerUserId = buyer.Id, OfferId = Guid.NewGuid() },
            new() { BuyerUserId = buyer.Id, BidId = Guid.NewGuid() }
        });

        var dtos = (await CreateSut().GetPurchasesAsync()).Value!;

        dtos.Select(d => d.Source).Should().Equal("Offer", "Auction");
    }
}
