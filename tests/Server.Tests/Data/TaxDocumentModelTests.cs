using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data;

public class TaxDocumentModelTests
{
    private readonly string _name = Guid.NewGuid().ToString();
    private AppDbContext Db() => DbContextFactory.Create(_name);

    [Fact]
    public void TheModel_NumbersDocumentsFromASequence_AndAllowsOneDocumentPerFee()
    {
        using var db = Db();
        // Sequences are relational annotations: read them from the design-time model.
        var sequence = db.GetService<IDesignTimeModel>().Model.FindSequence("TaxDocumentNumbers");
        sequence.Should().NotBeNull();
        sequence!.StartValue.Should().Be(1);
        sequence.IncrementBy.Should().Be(1);

        var doc = db.Model.FindEntityType(typeof(TaxDocument))!;
        doc.GetIndexes().Should().Contain(i => i.IsUnique && i.Properties.Single().Name == "Number");
        doc.GetIndexes().Should().Contain(i => i.IsUnique && i.Properties.Single().Name == "SubscriptionId"
            && i.GetFilter() == "[Kind] = 'ListingFeeInvoice'");
        doc.GetIndexes().Should().Contain(i => i.IsUnique && i.Properties.Single().Name == "PurchaseId"
            && i.GetFilter() == "[Kind] = 'BuyerFeeInvoice'");
        doc.GetIndexes().Should().Contain(i => i.IsUnique && i.Properties.Single().Name == "OriginalDocumentId"
            && i.GetFilter() == "[Kind] = 'AdjustmentNote'");
        doc.GetIndexes().Should().Contain(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "Kind", "IssuedAt" }));
    }

    [Fact]
    public async Task TwoContextsMarkingTheSameInvoicePaid_SecondOneConflicts()
    {
        using (var db = Db())
        {
            db.TaxDocuments.Add(new TaxDocument
            {
                Number = "SA-000001", Kind = TaxDocumentKind.ListingFeeInvoice, Status = TaxDocumentStatus.Unpaid,
                IssuedAt = DateTime.UtcNow, Description = "Listing fee", AmountIncGst = 990m, AmountExGst = 900m, GstAmount = 90m,
                SupplierName = "Stallions Australia Pty Ltd", SupplierAbn = "12345678901", SupplierAddress = "1 Racecourse Rd",
                RecipientName = "Arrowfield", RecipientEmail = "sales@arrowfield.example"
            });
            await db.SaveChangesAsync();
        }
        using var first = Db();
        using var second = Db();
        var a = await first.TaxDocuments.SingleAsync();
        var b = await second.TaxDocuments.SingleAsync();

        a.Status = TaxDocumentStatus.Paid;
        await first.SaveChangesAsync();
        b.Status = TaxDocumentStatus.Paid;

        await second.Invoking(db => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task ASubscription_KeepsItsIntroOffer_AndStandardFee()
    {
        Guid id;
        using (var db = Db())
        {
            var offer = new IntroOffer { Name = "Launch 2026", Type = IntroOfferType.AmountOff, AmountOffIncGst = 495m,
                EndsAt = DateTime.UtcNow.AddDays(30), CreatedByUserId = Guid.NewGuid() };
            db.IntroOffers.Add(offer);
            var sub = new StallionSeasonSubscription { StallionId = Guid.NewGuid(), SeasonId = Guid.NewGuid(),
                StudFarmId = Guid.NewGuid(), FeeIncGst = 495m, StandardFeeIncGst = 990m, IntroOfferId = offer.Id };
            db.StallionSeasonSubscriptions.Add(sub);
            await db.SaveChangesAsync();
            id = sub.Id;
        }

        using var read = Db();
        var loaded = await read.StallionSeasonSubscriptions.Include(s => s.IntroOffer).SingleAsync(s => s.Id == id);
        loaded.StandardFeeIncGst.Should().Be(990m);
        loaded.IntroOffer!.Name.Should().Be("Launch 2026");
    }

    [Fact]
    public async Task TheSettingsSeed_HasFourteenDayTerms_AndNoBusinessDetailsYet()
    {
        using var db = Db();
        await db.Database.EnsureCreatedAsync();

        var settings = await db.PlatformSettings.SingleAsync();
        settings.InvoicePaymentTermsDays.Should().Be(14);
        settings.BusinessLegalName.Should().BeNull();
        settings.BusinessAbn.Should().BeNull();
        settings.BankBsb.Should().BeNull();
    }

    [Fact]
    public void NominationBindings_AreGone()
    {
        using var db = Db();
        db.Model.GetEntityTypes().Select(t => t.ClrType.Name).Should().NotContain("NominationBinding");
    }
}
