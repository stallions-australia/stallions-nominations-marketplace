// tests/Server.Tests/TaxDocuments/TaxDocumentServiceTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.TaxDocuments;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.TaxDocuments;

public class TaxDocumentServiceTests : IDisposable
{
    private readonly string _name = Guid.NewGuid().ToString();
    private readonly AppDbContext _db;
    private readonly TestClock _clock = new() { Now = new DateTimeOffset(2026, 10, 11, 2, 0, 0, TimeSpan.Zero) };
    private readonly FakeTaxDocumentNumberSource _numbers = new();
    private readonly Mock<ITaxDocumentEmails> _emails = new();
    private readonly User _owner = new() { ObjectId = "o", Email = "owner@arrowfield.example", DisplayName = "Owner", Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };
    private readonly User _buyer = new() { ObjectId = "b", Email = "jane@example.com", DisplayName = "Jane Buyer", Role = UserRole.Buyer, Status = UserStatus.Active };
    private readonly StudFarm _farm;
    private readonly Season _season = new() { Name = "2026 Season", IsOpen = true };

    public TaxDocumentServiceTests()
    {
        _db = DbContextFactory.Create(_name);
        _db.Database.EnsureCreated();
        TaxTestData.CompleteBusinessDetails(_db);
        _farm = new StudFarm { UserId = _owner.Id, Name = "Arrowfield", ABN = "98 765 432 109", Address = "Scone NSW",
            ContactEmail = "sales@arrowfield.example" };
        _db.AddRange(_owner, _buyer, _farm, _season);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private TaxDocumentService CreateSut(AppDbContext? db = null, ITaxDocumentRepository? repo = null)
    {
        db ??= _db;
        return TaxDocumentServiceFactory.Create(db, _clock, _numbers, _emails, repo);
    }

    /// <summary>A subscription for a new stallion "Dundeel", loaded with Stallion, Season and StudFarm.</summary>
    private async Task<StallionSeasonSubscription> SubscriptionAsync(decimal fee = 990m, IntroOffer? offer = null)
    {
        var stallion = new Stallion { Name = "Dundeel", StudFarmId = _farm.Id };
        _db.Stallions.Add(stallion);
        var split = GstBreakdown.FromIncGst(fee);
        var sub = new StallionSeasonSubscription
        {
            StallionId = stallion.Id, SeasonId = _season.Id, StudFarmId = _farm.Id, FeeIncGst = split.IncGst,
            FeeExGst = split.ExGst, GstAmount = split.Gst, StandardFeeIncGst = 990m, IntroOfferId = offer?.Id,
            CreatedByUserId = _owner.Id
        };
        _db.StallionSeasonSubscriptions.Add(sub);
        await _db.SaveChangesAsync();
        return (await new SubscriptionRepository(_db).GetByIdAsync(sub.Id))!;
    }

    /// <summary>A completed sale of a Snitzel nomination to Jane Buyer, loaded as the charger loads it.</summary>
    private async Task<Purchase> SaleAsync(bool fromOffer = false)
    {
        var listing = AuctionTestData.Auction(_db, _clock.UtcNow.AddDays(-1), status: ListingStatus.Sold);
        var purchase = new Purchase
        {
            ListingId = listing.Id, BuyerUserId = _buyer.Id, BidId = fromOffer ? null : Guid.NewGuid(),
            OfferId = fromOffer ? Guid.NewGuid() : null, TotalPriceIncGst = 20000m, BuyerFeeIncGst = 150m,
            BuyerFeeExGst = 136.36m, BuyerFeeGst = 13.64m, BalancePayableToStudIncGst = 19850m,
            Status = PurchaseStatus.Completed, PaidAt = _clock.UtcNow, PaymentReference = "pi_1"
        };
        _db.Purchases.Add(purchase);
        await _db.SaveChangesAsync();
        return (await new PurchaseRepository(_db).GetForChargeAsync(purchase.Id))!;
    }

    /// <summary>Its first look misses the other instance's document; its insert then hits the unique index.</summary>
    private sealed class RacingRepository(AppDbContext db) : TaxDocumentRepository(db)
    {
        private bool _looked;

        public override async Task<TaxDocument?> GetListingFeeInvoiceAsync(Guid subscriptionId)
        {
            if (!_looked) { _looked = true; return null; }
            return await base.GetListingFeeInvoiceAsync(subscriptionId);
        }

        public override Task<bool> TryAddAsync(TaxDocument document) => Task.FromResult(false);
    }

    [Fact]
    public async Task ListingFeeInvoice_Paid_HasTheNumberGstSplitAndSnapshots()
    {
        var sub = await SubscriptionAsync();

        var r = await CreateSut().IssueListingFeeInvoiceAsync(sub, paid: true, userId: _owner.Id, paidAt: _clock.UtcNow);

        r.Status.Should().Be(TaxDocumentIssueStatus.Issued);
        var doc = await _db.TaxDocuments.SingleAsync();
        doc.Number.Should().Be("SA-000001");
        doc.Kind.Should().Be(TaxDocumentKind.ListingFeeInvoice);
        doc.Status.Should().Be(TaxDocumentStatus.Paid);
        doc.IssuedAt.Should().Be(_clock.UtcNow);
        doc.PaidAt.Should().Be(_clock.UtcNow);
        doc.DueAt.Should().BeNull();
        (doc.AmountIncGst, doc.AmountExGst, doc.GstAmount).Should().Be((990m, 900m, 90m));
        doc.Description.Should().Be("Listing fee — Dundeel, 2026 Season");
        (doc.SupplierName, doc.SupplierAbn, doc.SupplierAddress, doc.SupplierEmail).Should().Be(
            ("Stallions Australia Pty Ltd", "12345678901", "1 Racecourse Road, Scone NSW 2337", "accounts@stallions.example"));
        (doc.RecipientName, doc.RecipientEmail, doc.RecipientAbn, doc.RecipientAddress).Should().Be(
            ("Arrowfield", "sales@arrowfield.example", "98 765 432 109", "Scone NSW"));
        doc.BankBsb.Should().BeNull();
        doc.StudFarmId.Should().Be(_farm.Id);
        doc.SubscriptionId.Should().Be(sub.Id);
        doc.BuyerUserId.Should().BeNull();
        doc.StandardAmountIncGst.Should().BeNull();
        _emails.Verify(e => e.ListingFeeTaxInvoiceAsync(It.Is<TaxDocument>(d => d.Id == doc.Id)), Times.Once);
        var audit = await _db.AuditLogs.SingleAsync(a => a.EntityId == doc.Id);
        (audit.EntityType, audit.Action, audit.UserId).Should().Be(("TaxDocument", "TaxDocumentIssued", _owner.Id));
    }

    [Fact]
    public async Task ListingFeeInvoice_Unpaid_IsDueAfterTheTerms_WithTheBankDetails()
    {
        var sub = await SubscriptionAsync();

        await CreateSut().IssueListingFeeInvoiceAsync(sub, paid: false, userId: null);

        var doc = await _db.TaxDocuments.SingleAsync();
        doc.Status.Should().Be(TaxDocumentStatus.Unpaid);
        doc.PaidAt.Should().BeNull();
        doc.DueAt.Should().Be(_clock.UtcNow.AddDays(14));
        (doc.BankAccountName, doc.BankBsb, doc.BankAccountNumber).Should().Be(("Stallions Australia Pty Ltd", "062000", "12345678"));
        _emails.Verify(e => e.ListingFeeInvoiceDueAsync(It.Is<TaxDocument>(d => d.Id == doc.Id)), Times.Once);
        _emails.Verify(e => e.ListingFeeTaxInvoiceAsync(It.IsAny<TaxDocument>()), Times.Never);
    }

    [Fact]
    public async Task ListingFeeInvoice_WithAnIntroOffer_ShowsTheStandardFeeOfferAndDiscount()
    {
        var offer = new IntroOffer { Name = "Launch 2026", Type = IntroOfferType.AmountOff, AmountOffIncGst = 495m,
            EndsAt = _clock.UtcNow.AddDays(30), CreatedByUserId = _owner.Id };
        _db.IntroOffers.Add(offer);
        await _db.SaveChangesAsync();
        var sub = await SubscriptionAsync(fee: 495m, offer: offer);

        await CreateSut().IssueListingFeeInvoiceAsync(sub, paid: true, userId: null);

        var doc = await _db.TaxDocuments.SingleAsync();
        (doc.StandardAmountIncGst, doc.IntroOfferName, doc.DiscountIncGst).Should().Be((990m, "Launch 2026", 495m));
        (doc.AmountIncGst, doc.AmountExGst, doc.GstAmount).Should().Be((495m, 450m, 45m));
    }

    [Fact]
    public async Task Numbers_AreSequentialAndZeroPadded()
    {
        _numbers.StartAt(123);

        var first = (await CreateSut().IssueListingFeeInvoiceAsync(await SubscriptionAsync(), true, null)).Document!;
        var second = (await CreateSut().IssueListingFeeInvoiceAsync(await SubscriptionAsync(), true, null)).Document!;

        first.Number.Should().Be("SA-000123");
        second.Number.Should().Be("SA-000124");
        TaxDocumentService.FormatNumber(1_234_567).Should().Be("SA-1234567");
    }

    [Fact]
    public async Task IssuingTwice_ReturnsTheSameDocument_AndSendsOneEmail()
    {
        var sub = await SubscriptionAsync();
        var first = await CreateSut().IssueListingFeeInvoiceAsync(sub, paid: true, userId: null);

        var second = await CreateSut().IssueListingFeeInvoiceAsync(sub, paid: true, userId: null);

        second.Status.Should().Be(TaxDocumentIssueStatus.Existing);
        second.Document!.Id.Should().Be(first.Document!.Id);
        (await _db.TaxDocuments.CountAsync()).Should().Be(1);
        _emails.Verify(e => e.ListingFeeTaxInvoiceAsync(It.IsAny<TaxDocument>()), Times.Once);
    }

    [Fact]
    public async Task Issue_LosingTheInsertRace_ReturnsTheWinnersDocument_AndSendsNoEmail()
    {
        var sub = await SubscriptionAsync();
        var winner = (await CreateSut().IssueListingFeeInvoiceAsync(sub, paid: true, userId: null)).Document!;
        _emails.Invocations.Clear();

        var r = await CreateSut(repo: new RacingRepository(_db)).IssueListingFeeInvoiceAsync(sub, paid: true, userId: null);

        r.Status.Should().Be(TaxDocumentIssueStatus.Existing);
        r.Document!.Id.Should().Be(winner.Id);
        (await _db.TaxDocuments.CountAsync()).Should().Be(1);
        _emails.Verify(e => e.ListingFeeTaxInvoiceAsync(It.IsAny<TaxDocument>()), Times.Never);
        (await _db.AuditLogs.CountAsync(a => a.Action == "TaxDocumentIssued")).Should().Be(1);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("abn")]
    [InlineData("address")]
    public async Task MissingBusinessDetails_IssueNothing(string missing)
    {
        var settings = _db.PlatformSettings.Single();
        if (missing == "name") settings.BusinessLegalName = " ";
        if (missing == "abn") settings.BusinessAbn = null;
        if (missing == "address") settings.BusinessAddress = null;
        await _db.SaveChangesAsync();

        var r = await CreateSut().IssueListingFeeInvoiceAsync(await SubscriptionAsync(), paid: true, userId: null);

        r.Status.Should().Be(TaxDocumentIssueStatus.BusinessDetailsMissing);
        (await _db.TaxDocuments.AnyAsync()).Should().BeFalse();
        _emails.Verify(e => e.ListingFeeTaxInvoiceAsync(It.IsAny<TaxDocument>()), Times.Never);
        (await CreateSut().CanIssueAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task AnUnpaidInvoice_NeedsTheBankDetails_ButAPaidOneDoesNot()
    {
        TaxTestData.CompleteBusinessDetails(_db, withBank: false);

        (await CreateSut().IssueListingFeeInvoiceAsync(await SubscriptionAsync(), paid: false, userId: null)).Status
            .Should().Be(TaxDocumentIssueStatus.BusinessDetailsMissing);
        (await CreateSut().IssueListingFeeInvoiceAsync(await SubscriptionAsync(), paid: true, userId: null)).Status
            .Should().Be(TaxDocumentIssueStatus.Issued);
        (await CreateSut().CanIssueAsync(unpaidInvoice: true)).Should().BeFalse();
        (await CreateSut().CanIssueAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task AZeroFee_NeedsNoDocument()
    {
        var r = await CreateSut().IssueListingFeeInvoiceAsync(await SubscriptionAsync(fee: 0m), paid: true, userId: null);

        r.Status.Should().Be(TaxDocumentIssueStatus.NotNeeded);
        (await _db.TaxDocuments.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task MarkPaid_MarksTheUnpaidInvoicePaid_AndSendsAReceipt()
    {
        var sub = await SubscriptionAsync();
        await CreateSut().IssueListingFeeInvoiceAsync(sub, paid: false, userId: null);
        _clock.Advance(TimeSpan.FromDays(3));

        var r = await CreateSut().MarkListingFeeInvoicePaidAsync(sub, _clock.UtcNow, _owner.Id);

        r.Status.Should().Be(TaxDocumentIssueStatus.MarkedPaid);
        var doc = await _db.TaxDocuments.SingleAsync();
        doc.Status.Should().Be(TaxDocumentStatus.Paid);
        doc.PaidAt.Should().Be(_clock.UtcNow);
        _emails.Verify(e => e.ListingFeeReceiptAsync(It.Is<TaxDocument>(d => d.Id == doc.Id)), Times.Once);
        (await _db.AuditLogs.CountAsync(a => a.EntityId == doc.Id && a.Action == "TaxInvoicePaid")).Should().Be(1);
    }

    [Fact]
    public async Task MarkPaid_WhenAnotherInstancePaidItFirst_IsIdempotent()
    {
        var sub = await SubscriptionAsync();
        await CreateSut().IssueListingFeeInvoiceAsync(sub, paid: false, userId: null);
        using var other = DbContextFactory.Create(_name);
        await other.TaxDocuments.SingleAsync();                                  // the second instance read it while Unpaid
        await CreateSut().MarkListingFeeInvoicePaidAsync(sub, _clock.UtcNow, null); // the first instance pays it

        var r = await CreateSut(other).MarkListingFeeInvoicePaidAsync(sub, _clock.UtcNow, null);

        r.Status.Should().Be(TaxDocumentIssueStatus.Existing);
        r.Document!.Status.Should().Be(TaxDocumentStatus.Paid);
        _emails.Verify(e => e.ListingFeeReceiptAsync(It.IsAny<TaxDocument>()), Times.Once);
    }

    [Fact]
    public async Task MarkPaid_WithNoInvoice_DoesNothing()
    {
        (await CreateSut().MarkListingFeeInvoicePaidAsync(await SubscriptionAsync(), _clock.UtcNow, null)).Status
            .Should().Be(TaxDocumentIssueStatus.NotNeeded);
    }

    [Fact]
    public async Task Settle_WithNoInvoice_IssuesAPaidOne_AndWithAnUnpaidOne_MarksItPaid()
    {
        var fresh = await SubscriptionAsync();
        (await CreateSut().SettleListingFeeAsync(fresh, _clock.UtcNow, null)).Status.Should().Be(TaxDocumentIssueStatus.Issued);
        _emails.Verify(e => e.ListingFeeTaxInvoiceAsync(It.IsAny<TaxDocument>()), Times.Once);

        var invoiced = await SubscriptionAsync();
        await CreateSut().IssueListingFeeInvoiceAsync(invoiced, paid: false, userId: null);
        (await CreateSut().SettleListingFeeAsync(invoiced, _clock.UtcNow, null)).Status.Should().Be(TaxDocumentIssueStatus.MarkedPaid);
        _emails.Verify(e => e.ListingFeeReceiptAsync(It.IsAny<TaxDocument>()), Times.Once);
        (await _db.TaxDocuments.CountAsync()).Should().Be(2);
    }

    /// <summary>Just before its insert, another instance (Staff's "Issue tax invoice") inserts its document first.</summary>
    private sealed class BeatenToTheInsertRepository(AppDbContext db, Func<Task> otherInstanceInserts) : TaxDocumentRepository(db)
    {
        public override async Task<bool> TryAddAsync(TaxDocument document)
        {
            await otherInstanceInserts();
            return false; // the unique index rejects ours
        }
    }

    [Fact]
    public async Task Settle_WhenStaffIssuesAnUnpaidInvoiceDuringTheInsert_MarksThatInvoicePaid_AndSendsAReceipt()
    {
        var sub = await SubscriptionAsync();
        using var staff = DbContextFactory.Create(_name);
        var staffSub = (await new SubscriptionRepository(staff).GetByIdAsync(sub.Id))!;
        var repo = new BeatenToTheInsertRepository(_db, async () =>
            await CreateSut(staff).IssueListingFeeInvoiceAsync(staffSub, paid: false, userId: _owner.Id));
        _emails.Invocations.Clear();

        var r = await CreateSut(repo: repo).SettleListingFeeAsync(sub, _clock.UtcNow, null);

        r.Status.Should().Be(TaxDocumentIssueStatus.MarkedPaid);
        using var fresh = DbContextFactory.Create(_name);
        var doc = await fresh.TaxDocuments.SingleAsync();
        doc.Status.Should().Be(TaxDocumentStatus.Paid);
        doc.PaidAt.Should().Be(_clock.UtcNow);
        _emails.Verify(e => e.ListingFeeReceiptAsync(It.Is<TaxDocument>(d => d.Id == doc.Id)), Times.Once);
    }

    [Theory]
    [InlineData(false, "Buyer fee — nomination to Snitzel, 2026 Season (auction)")]
    [InlineData(true, "Buyer fee — nomination to Snitzel, 2026 Season (accepted offer)")]
    public async Task BuyerFeeInvoice_IsAPaidInvoiceToTheBuyer(bool fromOffer, string description)
    {
        var sale = await SaleAsync(fromOffer);

        var r = await CreateSut().IssueBuyerFeeInvoiceAsync(sale);

        r.Status.Should().Be(TaxDocumentIssueStatus.Issued);
        var doc = await _db.TaxDocuments.SingleAsync();
        doc.Kind.Should().Be(TaxDocumentKind.BuyerFeeInvoice);
        doc.Status.Should().Be(TaxDocumentStatus.Paid);
        doc.Description.Should().Be(description);
        (doc.AmountIncGst, doc.AmountExGst, doc.GstAmount).Should().Be((150m, 136.36m, 13.64m));
        (doc.RecipientName, doc.RecipientEmail).Should().Be(("Jane Buyer", "jane@example.com"));
        doc.BuyerUserId.Should().Be(_buyer.Id);
        doc.StudFarmId.Should().BeNull();
        doc.PurchaseId.Should().Be(sale.Id);
        _emails.Verify(e => e.BuyerFeeTaxInvoiceAsync(It.Is<TaxDocument>(d => d.Id == doc.Id)), Times.Once);
    }

    [Fact]
    public async Task AdjustmentNote_ReversesTheWholeInvoice_AndLinksIt()
    {
        var sale = await SaleAsync();
        var invoice = (await CreateSut().IssueBuyerFeeInvoiceAsync(sale)).Document!;
        _clock.Advance(TimeSpan.FromDays(1));

        var r = await CreateSut().IssueAdjustmentNoteAsync(invoice, "Buyer fee refunded — duplicate charge", _owner.Id);

        r.Status.Should().Be(TaxDocumentIssueStatus.Issued);
        var note = r.Document!;
        note.Number.Should().Be("SA-000002");
        note.Kind.Should().Be(TaxDocumentKind.AdjustmentNote);
        note.Status.Should().Be(TaxDocumentStatus.Paid);
        (note.AmountIncGst, note.AmountExGst, note.GstAmount).Should().Be((150m, 136.36m, 13.64m));
        note.OriginalDocumentId.Should().Be(invoice.Id);
        note.PurchaseId.Should().Be(sale.Id);
        note.BuyerUserId.Should().Be(_buyer.Id);
        (note.RecipientName, note.RecipientEmail).Should().Be(("Jane Buyer", "jane@example.com"));
        note.Reason.Should().Be("Buyer fee refunded — duplicate charge");
        note.IssuedAt.Should().Be(_clock.UtcNow);
        _emails.Verify(e => e.AdjustmentNoteAsync(It.Is<TaxDocument>(d => d.Id == note.Id),
            It.Is<TaxDocument>(d => d.Id == invoice.Id)), Times.Once);
    }

    [Fact]
    public async Task AdjustmentNote_Twice_ReturnsTheExistingNote()
    {
        var invoice = (await CreateSut().IssueBuyerFeeInvoiceAsync(await SaleAsync())).Document!;
        var first = await CreateSut().IssueAdjustmentNoteAsync(invoice, "Buyer fee refunded", null);

        var second = await CreateSut().IssueAdjustmentNoteAsync(invoice, "Buyer fee refunded", null);

        second.Status.Should().Be(TaxDocumentIssueStatus.Existing);
        second.Document!.Id.Should().Be(first.Document!.Id);
        _emails.Verify(e => e.AdjustmentNoteAsync(It.IsAny<TaxDocument>(), It.IsAny<TaxDocument>()), Times.Once);
    }

    [Fact]
    public async Task RecordPending_AuditsTaxDocumentPending()
    {
        var id = Guid.NewGuid();

        await CreateSut().RecordPendingAsync("Purchase", id, TaxDocumentKind.BuyerFeeInvoice);

        var audit = await _db.AuditLogs.SingleAsync(a => a.EntityId == id);
        (audit.EntityType, audit.Action).Should().Be(("Purchase", TaxDocumentService.PendingAction));
        audit.Details.Should().Contain("BuyerFeeInvoice");
    }

    [Fact]
    public async Task TheStudsInvoice_GoesToTheOwnersEmail_WhenTheFarmHasNoContactEmail()
    {
        _farm.ContactEmail = null;
        await _db.SaveChangesAsync();

        await CreateSut().IssueListingFeeInvoiceAsync(await SubscriptionAsync(), paid: true, userId: null);

        (await _db.TaxDocuments.SingleAsync()).RecipientEmail.Should().Be("owner@arrowfield.example");
    }
}
