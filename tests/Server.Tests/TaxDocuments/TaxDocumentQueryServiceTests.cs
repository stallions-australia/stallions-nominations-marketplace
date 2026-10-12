using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Content;
using Stallions.Server.Services;
using Stallions.Server.TaxDocuments;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared;
using Stallions.Shared.DTOs.TaxDocuments;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.TaxDocuments;

public class TaxDocumentQueryServiceTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 11, 2, 0, 0, DateTimeKind.Utc); // 1 pm, 11 Oct in Sydney

    private readonly AppDbContext _db = DbContextFactory.Create(Guid.NewGuid().ToString());
    private readonly Mock<IUserService> _users = new();
    private readonly TestClock _clock = new() { Now = new DateTimeOffset(T0) };
    private readonly User _staff = NewUser(UserRole.Staff, "staff");
    private readonly User _buyerA = NewUser(UserRole.Buyer, "jane");
    private readonly User _buyerB = NewUser(UserRole.Buyer, "bob");
    private readonly User _studA = NewUser(UserRole.StudFarmAdmin, "arrowfield");
    private readonly User _studB = NewUser(UserRole.StudFarmAdmin, "coolmore");
    private readonly StudFarm _farmA;
    private readonly StudFarm _farmB;

    public TaxDocumentQueryServiceTests()
    {
        _db.Database.EnsureCreated();
        _farmA = new StudFarm { UserId = _studA.Id, Name = "Arrowfield", ContactEmail = "sales@arrowfield.example" };
        _farmB = new StudFarm { UserId = _studB.Id, Name = "Coolmore" };
        _db.AddRange(_staff, _buyerA, _buyerB, _studA, _studB, _farmA, _farmB);
        _db.SaveChanges();
        ContentTestData.SetWording(_db, ContentKeys.BuyerFeeExplanation, null, "CONFIGURED fee wording.");
    }

    public void Dispose() => _db.Dispose();

    private static User NewUser(UserRole role, string name) => new()
    {
        Id = Guid.NewGuid(), ObjectId = name, Email = $"{name}@example.com", DisplayName = name, Role = role, Status = UserStatus.Active
    };

    private void SignIn(User user) => _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(user);

    private TaxDocumentService TaxDocuments() => TaxDocumentServiceFactory.Create(_db, _clock, new FakeTaxDocumentNumberSource(), new Mock<ITaxDocumentEmails>());

    private TaxDocumentQueryService CreateSut(ITaxDocumentService? taxDocuments = null) => new(new TaxDocumentRepository(_db), taxDocuments ?? TaxDocuments(),
        new SubscriptionRepository(_db), new PurchaseRepository(_db), new StudFarmRepository(_db), new AuditLogRepository(_db),
        _users.Object, new InlineTransactionRunner(),
        ContentTestData.Service(_db),
        NullLogger<TaxDocumentQueryService>.Instance);

    private TaxDocument AddDoc(string number, TaxDocumentKind kind, Guid? buyer = null, Guid? farm = null,
        TaxDocumentStatus status = TaxDocumentStatus.Paid, decimal amount = 990m, DateTime? issuedAt = null,
        TaxDocument? original = null, string recipient = "Arrowfield")
    {
        var split = GstBreakdown.FromIncGst(amount);
        var d = new TaxDocument
        {
            Number = number, Kind = kind, Status = status, IssuedAt = issuedAt ?? T0,
            PaidAt = status == TaxDocumentStatus.Paid ? issuedAt ?? T0 : null,
            DueAt = status == TaxDocumentStatus.Unpaid ? (issuedAt ?? T0).AddDays(14) : null,
            Description = kind == TaxDocumentKind.BuyerFeeInvoice ? "Buyer fee — nomination to Dundeel, 2026 Season (auction)" : "Listing fee — Dundeel, 2026 Season",
            AmountIncGst = split.IncGst, AmountExGst = split.ExGst, GstAmount = split.Gst,
            SupplierName = "Stallions Australia Pty Ltd", SupplierAbn = "12345678901", SupplierAddress = "1 Racecourse Road",
            RecipientName = recipient, RecipientEmail = "r@example.com", RecipientAbn = farm != null ? "98765432109" : null,
            BuyerUserId = buyer, StudFarmId = farm, OriginalDocumentId = original?.Id,
            Reason = original != null ? "Buyer fee refunded" : null
        };
        _db.TaxDocuments.Add(d);
        _db.SaveChanges();
        return d;
    }

    // ── One document ────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_TheBuyersOwnInvoice_IsShown_WithTheConfiguredFeeNote()
    {
        var doc = AddDoc("SA-000001", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerA.Id, amount: 150m, recipient: "jane");
        SignIn(_buyerA);

        var r = await CreateSut().GetAsync(doc.Id);

        r.Succeeded.Should().BeTrue(r.Error);
        var dto = r.Value!;
        (dto.Number, dto.Kind, dto.Status).Should().Be(("SA-000001", "BuyerFeeInvoice", "Paid"));
        dto.SupplierAbn.Should().Be("12 345 678 901");
        dto.IssuedOn.Should().Be(new DateOnly(2026, 10, 11));
        dto.FeeNote.Should().Be("CONFIGURED fee wording.");
        (dto.AmountIncGst, dto.AmountExGst, dto.GstAmount).Should().Be((150m, 136.36m, 13.64m));
    }

    [Fact]
    public async Task Get_AnotherBuyersDocument_IsNotFound()
    {
        var doc = AddDoc("SA-000001", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerA.Id, amount: 150m);
        SignIn(_buyerB);

        (await CreateSut().GetAsync(doc.Id)).HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Get_TheStudsOwnInvoice_IsShown_WithItsAbnGrouped()
    {
        var doc = AddDoc("SA-000001", TaxDocumentKind.ListingFeeInvoice, farm: _farmA.Id, status: TaxDocumentStatus.Unpaid);
        SignIn(_studA);

        var dto = (await CreateSut().GetAsync(doc.Id)).Value!;

        dto.RecipientAbn.Should().Be("98 765 432 109");
        dto.DueOn.Should().Be(new DateOnly(2026, 10, 25));
        dto.FeeNote.Should().BeNull();
    }

    [Fact]
    public async Task Get_AnotherStudsDocument_IsNotFound()
    {
        var doc = AddDoc("SA-000001", TaxDocumentKind.ListingFeeInvoice, farm: _farmA.Id);
        SignIn(_studB);

        (await CreateSut().GetAsync(doc.Id)).HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Get_AStudCannotSeeTheBuyerFeeInvoiceForItsSale()
    {
        var doc = AddDoc("SA-000001", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerA.Id, amount: 150m);
        SignIn(_studA);

        (await CreateSut().GetAsync(doc.Id)).HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Get_StaffSeeEveryDocument_AndAnUnknownIdIsNotFound()
    {
        var buyerDoc = AddDoc("SA-000001", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerA.Id, amount: 150m);
        var studDoc = AddDoc("SA-000002", TaxDocumentKind.ListingFeeInvoice, farm: _farmA.Id);
        SignIn(_staff);

        (await CreateSut().GetAsync(buyerDoc.Id)).Succeeded.Should().BeTrue();
        (await CreateSut().GetAsync(studDoc.Id)).Succeeded.Should().BeTrue();
        (await CreateSut().GetAsync(Guid.NewGuid())).HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Get_AReversedInvoice_NamesItsNote_AndTheNoteNamesTheOriginal()
    {
        var invoice = AddDoc("SA-000001", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerA.Id, amount: 150m);
        var note = AddDoc("SA-000002", TaxDocumentKind.AdjustmentNote, buyer: _buyerA.Id, amount: 150m,
            issuedAt: T0.AddDays(1), original: invoice);
        SignIn(_buyerA);

        (await CreateSut().GetAsync(invoice.Id)).Value!.ReversedByNumber.Should().Be("SA-000002");
        var noteDto = (await CreateSut().GetAsync(note.Id)).Value!;
        (noteDto.OriginalNumber, noteDto.OriginalIssuedOn, noteDto.Reason).Should().Be(
            ("SA-000001", new DateOnly(2026, 10, 11), "Buyer fee refunded"));
    }

    // ── Lists ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Mine_AndStud_ListOnlyTheCallersOwnDocuments()
    {
        AddDoc("SA-000001", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerA.Id, amount: 150m);
        AddDoc("SA-000002", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerB.Id, amount: 150m);
        AddDoc("SA-000003", TaxDocumentKind.ListingFeeInvoice, farm: _farmA.Id);
        AddDoc("SA-000004", TaxDocumentKind.ListingFeeInvoice, farm: _farmB.Id);

        SignIn(_buyerA);
        (await CreateSut().GetMineAsync()).Value!.Select(d => d.Number).Should().Equal("SA-000001");
        SignIn(_studA);
        (await CreateSut().GetForStudAsync()).Value!.Select(d => d.Number).Should().Equal("SA-000003");
        (await CreateSut().GetMineAsync()).HttpStatusCode.Should().Be(403);
    }

    // ── Staff register ──────────────────────────────────────────────────────

    [Fact]
    public async Task Register_SubtractsAdjustmentNotes_AndShowsUnpaidSeparately()
    {
        AddDoc("SA-000001", TaxDocumentKind.ListingFeeInvoice, farm: _farmA.Id);                                   // paid 990
        AddDoc("SA-000002", TaxDocumentKind.ListingFeeInvoice, farm: _farmA.Id, status: TaxDocumentStatus.Unpaid); // owed 990
        var waived = AddDoc("SA-000003", TaxDocumentKind.ListingFeeInvoice, farm: _farmB.Id,
            status: TaxDocumentStatus.Unpaid, amount: 495m);                                                        // reversed
        AddDoc("SA-000004", TaxDocumentKind.AdjustmentNote, farm: _farmB.Id, amount: 495m, original: waived);
        var buyerFee = AddDoc("SA-000005", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerA.Id, amount: 150m);
        AddDoc("SA-000006", TaxDocumentKind.AdjustmentNote, buyer: _buyerA.Id, amount: 150m, original: buyerFee);
        SignIn(_staff);

        var register = (await CreateSut().GetRegisterAsync(new TaxDocumentFilter())).Value!;

        var t = register.Totals;
        (t.InvoiceCount, t.InvoicesIncGst, t.InvoicesExGst, t.InvoicesGst).Should().Be((4, 2625m, 2386.36m, 238.64m));
        (t.AdjustmentCount, t.AdjustmentsIncGst, t.AdjustmentsExGst, t.AdjustmentsGst).Should().Be((2, 645m, 586.36m, 58.64m));
        (t.NetIncGst, t.NetExGst, t.NetGst).Should().Be((1980m, 1800m, 180m));
        (t.UnpaidCount, t.UnpaidIncGst).Should().Be((1, 990m));
        register.Documents.Should().HaveCount(6);
        register.Documents.Single(d => d.Number == "SA-000003").ReversedByNumber.Should().Be("SA-000004");
        register.Documents.Single(d => d.Number == "SA-000006").OriginalNumber.Should().Be("SA-000005");
    }

    [Fact]
    public async Task Register_FiltersByKindStatusAndSydneyDate()
    {
        AddDoc("SA-000001", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerA.Id, amount: 150m,
            issuedAt: new DateTime(2026, 10, 10, 13, 30, 0, DateTimeKind.Utc));   // 12:30 am, 11 Oct in Sydney
        AddDoc("SA-000002", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerA.Id, amount: 150m,
            issuedAt: new DateTime(2026, 10, 10, 12, 30, 0, DateTimeKind.Utc));   // 11:30 pm, 10 Oct in Sydney
        AddDoc("SA-000003", TaxDocumentKind.ListingFeeInvoice, farm: _farmA.Id, status: TaxDocumentStatus.Unpaid);
        SignIn(_staff);

        var oct11 = new TaxDocumentFilter { From = new DateOnly(2026, 10, 11), To = new DateOnly(2026, 10, 11), Kind = "buyerfeeinvoice" };
        (await CreateSut().GetRegisterAsync(oct11)).Value!.Documents.Select(d => d.Number).Should().Equal("SA-000001");
        (await CreateSut().GetRegisterAsync(new TaxDocumentFilter { Status = "Unpaid" })).Value!.Documents
            .Select(d => d.Number).Should().Equal("SA-000003");
    }

    [Fact]
    public async Task Register_RejectsAnUnknownKind_OrADateRangeBackwards()
    {
        SignIn(_staff);

        (await CreateSut().GetRegisterAsync(new TaxDocumentFilter { Kind = "Receipt" })).HttpStatusCode.Should().Be(400);
        (await CreateSut().GetRegisterAsync(new TaxDocumentFilter { From = new DateOnly(2026, 10, 12), To = new DateOnly(2026, 10, 11) }))
            .HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Register_Export_AndIssueMissing_AreStaffOnly()
    {
        SignIn(_buyerA);

        (await CreateSut().GetRegisterAsync(new TaxDocumentFilter())).HttpStatusCode.Should().Be(403);
        (await CreateSut().ExportCsvAsync(new TaxDocumentFilter())).HttpStatusCode.Should().Be(403);
        (await CreateSut().IssueMissingAsync()).HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Export_EscapesFormulaText_AndSignsAdjustmentNotes()
    {
        var invoice = AddDoc("SA-000001", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerA.Id, amount: 150m, recipient: "=SUM(A1:A9)");
        AddDoc("SA-000002", TaxDocumentKind.AdjustmentNote, buyer: _buyerA.Id, amount: 150m, original: invoice, recipient: "@evil");
        SignIn(_staff);

        var csv = (await CreateSut().ExportCsvAsync(new TaxDocumentFilter())).Value!;

        csv.Should().Contain(",'=SUM(A1:A9),").And.Contain(",'@evil,").And.Contain(",-150.00,-136.36,-13.64,SA-000001,");
    }

    // ── Pending documents ───────────────────────────────────────────────────

    private async Task<(Guid SubscriptionId, Guid PurchaseId)> FeesRecordedWithoutDocumentsAsync()
    {
        var stallion = new Stallion { Name = "Dundeel", StudFarmId = _farmA.Id };
        var season = new Season { Name = "2026 Season" };
        _db.AddRange(stallion, season);
        var sub = new StallionSeasonSubscription
        {
            StallionId = stallion.Id, SeasonId = season.Id, StudFarmId = _farmA.Id, FeeIncGst = 990m, FeeExGst = 900m,
            GstAmount = 90m, StandardFeeIncGst = 990m, Status = SubscriptionStatus.Paid,
            PaymentMethod = SubscriptionPaymentMethod.Card, PaidAt = T0, CreatedByUserId = _studA.Id
        };
        _db.StallionSeasonSubscriptions.Add(sub);
        var listing = AuctionTestData.Auction(_db, T0.AddDays(-1), status: ListingStatus.Sold);
        var purchase = new Purchase
        {
            ListingId = listing.Id, BuyerUserId = _buyerA.Id, BidId = Guid.NewGuid(), TotalPriceIncGst = 20000m,
            BuyerFeeIncGst = 150m, BuyerFeeExGst = 136.36m, BuyerFeeGst = 13.64m, BalancePayableToStudIncGst = 19850m,
            Status = PurchaseStatus.Completed, PaidAt = T0
        };
        _db.Purchases.Add(purchase);
        await _db.SaveChangesAsync();
        var audit = new AuditLogRepository(_db);
        await audit.LogAsync("StallionSeasonSubscription", sub.Id, TaxDocumentService.PendingAction, null, "{}");
        await audit.LogAsync("Purchase", purchase.Id, TaxDocumentService.PendingAction, null, "{}");
        return (sub.Id, purchase.Id);
    }

    [Fact]
    public async Task PendingFees_AreCounted_AndIssueMissingIssuesThem_OnceTheBusinessDetailsAreComplete()
    {
        var (subscriptionId, purchaseId) = await FeesRecordedWithoutDocumentsAsync();
        SignIn(_staff);

        (await CreateSut().GetRegisterAsync(new TaxDocumentFilter())).Value!.PendingCount.Should().Be(2);
        (await CreateSut().IssueMissingAsync()).Error.Should().Be("Complete the business details in Settings first.");

        TaxTestData.CompleteBusinessDetails(_db);
        var result = (await CreateSut().IssueMissingAsync()).Value!;

        (result.Issued, result.Remaining).Should().Be((2, 0));
        (await _db.TaxDocuments.SingleAsync(d => d.SubscriptionId == subscriptionId)).Status.Should().Be(TaxDocumentStatus.Paid);
        (await _db.TaxDocuments.SingleAsync(d => d.PurchaseId == purchaseId)).Kind.Should().Be(TaxDocumentKind.BuyerFeeInvoice);
        (await CreateSut().GetRegisterAsync(new TaxDocumentFilter())).Value!.PendingCount.Should().Be(0);
        (await _db.AuditLogs.CountAsync(a => a.Action == "IssueMissingTaxDocuments" && a.UserId == _staff.Id)).Should().Be(1);
    }

    [Fact]
    public async Task IssueMissing_WhenOneItemThrows_IssuesTheRest_AndStillAudits()
    {
        var (subscriptionId, purchaseId) = await FeesRecordedWithoutDocumentsAsync();
        TaxTestData.CompleteBusinessDetails(_db);
        SignIn(_staff);
        var real = TaxDocuments();
        var failing = new Mock<ITaxDocumentService>();
        failing.Setup(t => t.CanIssueAsync(It.IsAny<bool>())).ReturnsAsync(true);
        failing.Setup(t => t.IssueListingFeeInvoiceAsync(It.IsAny<StallionSeasonSubscription>(), It.IsAny<bool>(),
                It.IsAny<Guid?>(), It.IsAny<DateTime?>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        failing.Setup(t => t.IssueBuyerFeeInvoiceAsync(It.IsAny<Purchase>()))
            .Returns((Purchase p) => real.IssueBuyerFeeInvoiceAsync(p));

        var result = (await CreateSut(failing.Object).IssueMissingAsync()).Value!;

        (result.Issued, result.Remaining).Should().Be((1, 1));
        (await _db.TaxDocuments.AnyAsync(d => d.SubscriptionId == subscriptionId)).Should().BeFalse();
        (await _db.TaxDocuments.SingleAsync(d => d.PurchaseId == purchaseId)).Kind.Should().Be(TaxDocumentKind.BuyerFeeInvoice);
        (await _db.AuditLogs.CountAsync(a => a.Action == "IssueMissingTaxDocuments" && a.UserId == _staff.Id)).Should().Be(1);
    }

    [Fact]
    public async Task PendingFees_ThatAlreadyHaveADocument_AreNotCounted()
    {
        var (subscriptionId, _) = await FeesRecordedWithoutDocumentsAsync();
        var existing = AddDoc("SA-000009", TaxDocumentKind.ListingFeeInvoice, farm: _farmA.Id);
        existing.SubscriptionId = subscriptionId;
        await _db.SaveChangesAsync();
        SignIn(_staff);

        (await CreateSut().GetRegisterAsync(new TaxDocumentFilter())).Value!.PendingCount.Should().Be(1);
    }
}
