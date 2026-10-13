using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Content;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data;

public class EditableContentModelTests
{
    private readonly string _name = Guid.NewGuid().ToString();
    private AppDbContext Db() => DbContextFactory.Create(_name);

    [Fact]
    public async Task TheSeed_HasVersion1OfEveryCatalogItem_WithTheDefaultText_AndNoAuthor()
    {
        using var db = Db();
        await db.Database.EnsureCreatedAsync();

        var rows = await db.ContentItems.ToListAsync();
        rows.Should().HaveCount(ContentCatalog.Entries.Count);
        foreach (var entry in ContentCatalog.Entries)
        {
            var row = rows.Single(r => r.Key == entry.Key);
            row.Id.Should().Be(ContentCatalog.SeedId(entry.Key));
            row.Version.Should().Be(1);
            row.Subject.Should().Be(entry.DefaultSubject);
            row.Body.Should().Be(entry.DefaultBody);
            row.CreatedByUserId.Should().BeNull();
            row.CreatedAt.Should().Be(ContentCatalog.SeededAt);
        }
    }

    [Fact]
    public void ContentItems_AreUniquePerKeyAndVersion_AndFitTheirColumns()
    {
        using var db = Db();
        var item = db.Model.FindEntityType(typeof(ContentItem))!;
        item.GetIndexes().Should().Contain(i => i.IsUnique
            && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "Key", "Version" }));
        item.FindProperty("Key")!.GetMaxLength().Should().Be(ContentCatalog.MaxKeyLength);
        item.FindProperty("Subject")!.GetMaxLength().Should().Be(ContentCatalog.MaxSubjectLength);
        item.FindProperty("Body")!.GetMaxLength().Should().Be(ContentCatalog.MaxBodyLength);
    }

    [Fact]
    public void TermsDocuments_AreNumberedPerAudience()
    {
        using var db = Db();
        var terms = db.Model.FindEntityType(typeof(TermsDocument))!;
        terms.GetIndexes().Should().Contain(i => i.IsUnique
            && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "Audience", "Version" }));
        terms.GetIndexes().Should().NotContain(i => i.IsUnique && i.Properties.Count == 1 && i.Properties[0].Name == "Version");
        terms.FindProperty("Audience")!.GetMaxLength().Should().Be(10);
        new TermsDocument().Audience.Should().Be(TermsAudience.Buyer);
    }

    [Fact]
    public async Task AStudFarm_KeepsItsStudTermsAcceptance()
    {
        var farmId = Guid.NewGuid();
        var by = Guid.NewGuid();
        var at = new DateTime(2026, 10, 12, 2, 0, 0, DateTimeKind.Utc);
        using (var db = Db())
        {
            db.StudFarms.Add(new StudFarm { Id = farmId, UserId = Guid.NewGuid(), Name = "Arrowfield",
                AcceptedStudTermsVersion = 2, AcceptedStudTermsAt = at, AcceptedStudTermsByUserId = by });
            await db.SaveChangesAsync();
        }

        using var read = Db();
        var farm = await read.StudFarms.SingleAsync(f => f.Id == farmId);
        farm.AcceptedStudTermsVersion.Should().Be(2);
        farm.AcceptedStudTermsAt.Should().Be(at);
        farm.AcceptedStudTermsByUserId.Should().Be(by);
    }

    [Fact]
    public void Catalog_KeysAndSeedIds_AreUnique()
    {
        ContentCatalog.Entries.Select(e => e.Key).Should().OnlyHaveUniqueItems();
        ContentCatalog.Entries.Select(e => ContentCatalog.SeedId(e.Key)).Should().OnlyHaveUniqueItems();
        ContentCatalog.Entries.Should().HaveCount(25);
        ContentCatalog.SeedId(ContentKeys.Outbid).Should().Be(Guid.Parse("5922d4db-4231-3b59-0187-4497db7b2c1b"));
    }

    [Fact]
    public void DisclosureDefaults_AreTheOldCheckoutWording()
    {
        // Copied from the appsettings.json "Checkout" section this phase removes.
        ContentCatalog.Find(ContentKeys.BuyerFeeExplanation)!.DefaultBody.Should().Be(
            "The buyer fee is paid to Stallions Australia and forms part of the price — it is not added on top. You pay the balance (the price less the buyer fee) directly to the stud, under the stud's own terms. The price arrangement is entirely between you and the stud.");
        ContentCatalog.Find(ContentKeys.StudFarmBalanceArrangement)!.DefaultBody.Should().Be(
            "The stud farm will contact you separately to arrange payment of the remaining balance. The balance amount, payment terms, and any foal guarantee conditions are a matter between you and the stud farm only. Stallions Australia is not party to that arrangement.");
        ContentCatalog.Find(ContentKeys.SavedCardExplanation)!.DefaultBody.Should().Be(
            "Your saved card is charged the buyer fee automatically when you win an auction or a stud accepts your offer. The nomination price is not charged to this card — you pay the balance directly to the stud, under the stud's own terms.");
    }
}
