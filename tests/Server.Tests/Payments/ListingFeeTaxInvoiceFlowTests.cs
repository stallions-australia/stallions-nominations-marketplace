using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Payments;
using Stallions.Server.TaxDocuments;
using Stallions.Server.Tests.Helpers;
using Stallions.Server.Tests.TaxDocuments;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Payments;

/// <summary>The card webhook with the real TaxDocumentService, end to end on the in-memory provider.</summary>
public class ListingFeeTaxInvoiceFlowTests : IDisposable
{
    private readonly AppDbContext _db = DbContextFactory.Create(Guid.NewGuid().ToString());
    private readonly Mock<IPaymentProvider> _provider = new();
    private readonly Mock<ITaxDocumentEmails> _emails = new();
    private readonly TestClock _clock = new() { Now = DateTimeOffset.UtcNow };
    private readonly FakeTaxDocumentNumberSource _numbers = new();
    private readonly User _owner = new() { ObjectId = "o", Email = "owner@arrowfield.example", DisplayName = "Owner", Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };

    public ListingFeeTaxInvoiceFlowTests()
    {
        _db.Database.EnsureCreated();
        TaxTestData.CompleteBusinessDetails(_db);
        _provider.SetupGet(p => p.Name).Returns("Fake");
        _db.Users.Add(_owner);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private TaxDocumentService TaxDocuments() => TaxDocumentServiceFactory.Create(_db, _clock, _numbers, _emails);

    private PaymentEventProcessor Processor() => new(
        new ProcessedPaymentEventRepository(_db), new SavedCardRepository(_db), new UserRepository(_db),
        new SubscriptionRepository(_db), new PurchaseRepository(_db), _provider.Object, new AuditLogRepository(_db),
        TaxDocuments(), new InlineTransactionRunner(), NullLogger<PaymentEventProcessor>.Instance);

    private async Task<StallionSeasonSubscription> PendingSubscriptionAsync()
    {
        var farm = new StudFarm { UserId = _owner.Id, Name = "Arrowfield", ContactEmail = "sales@arrowfield.example" };
        var stallion = new Stallion { Name = "Dundeel", StudFarmId = farm.Id };
        var season = new Season { Name = "2026 Season" };
        _db.AddRange(farm, stallion, season);
        var sub = new StallionSeasonSubscription
        {
            StallionId = stallion.Id, SeasonId = season.Id, StudFarmId = farm.Id, FeeIncGst = 990m, FeeExGst = 900m,
            GstAmount = 90m, StandardFeeIncGst = 990m, Status = SubscriptionStatus.Pending, CreatedByUserId = _owner.Id
        };
        _db.StallionSeasonSubscriptions.Add(sub);
        await _db.SaveChangesAsync();
        return sub;
    }

    private static ListingFeePaidEvent Paid(Guid subscriptionId) => new("evt_1", subscriptionId, 99000, "aud", "pi_1");

    [Fact]
    public async Task CardPayment_IssuesAPaidTaxInvoiceToTheStud()
    {
        var sub = await PendingSubscriptionAsync();

        (await Processor().ProcessAsync(Paid(sub.Id))).Should().Be(PaymentEventOutcome.Processed);

        var doc = await _db.TaxDocuments.SingleAsync();
        (doc.Kind, doc.Status, doc.SubscriptionId).Should().Be((TaxDocumentKind.ListingFeeInvoice, TaxDocumentStatus.Paid, sub.Id));
        doc.Description.Should().Be("Listing fee — Dundeel, 2026 Season");
        doc.RecipientEmail.Should().Be("sales@arrowfield.example");
        _emails.Verify(e => e.ListingFeeTaxInvoiceAsync(It.IsAny<TaxDocument>()), Times.Once);
    }

    [Fact]
    public async Task CardPayment_ForAnInvoicedSubscription_MarksThatInvoicePaid_AndSendsAReceipt()
    {
        var sub = await PendingSubscriptionAsync();
        await TaxDocuments().IssueListingFeeInvoiceAsync((await new SubscriptionRepository(_db).GetByIdAsync(sub.Id))!,
            paid: false, userId: null);

        await Processor().ProcessAsync(Paid(sub.Id));

        var doc = await _db.TaxDocuments.SingleAsync();
        doc.Status.Should().Be(TaxDocumentStatus.Paid);
        _emails.Verify(e => e.ListingFeeReceiptAsync(It.IsAny<TaxDocument>()), Times.Once);
        _emails.Verify(e => e.ListingFeeTaxInvoiceAsync(It.IsAny<TaxDocument>()), Times.Never);
    }

    [Fact]
    public async Task ListingFeePaid_WithoutBusinessDetails_StillRecordsThePayment_AndAuditsPending()
    {
        TaxTestData.ClearBusinessDetails(_db);
        var sub = await PendingSubscriptionAsync();

        var outcome = await Processor().ProcessAsync(Paid(sub.Id));

        outcome.Should().Be(PaymentEventOutcome.Processed);
        (await _db.StallionSeasonSubscriptions.SingleAsync()).Status.Should().Be(SubscriptionStatus.Paid);
        (await _db.TaxDocuments.AnyAsync()).Should().BeFalse();
        (await _db.AuditLogs.SingleAsync(a => a.Action == TaxDocumentService.PendingAction)).EntityId.Should().Be(sub.Id);
    }
}
