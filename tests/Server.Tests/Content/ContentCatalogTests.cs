// tests/Server.Tests/Content/ContentCatalogTests.cs
using FluentAssertions;
using Stallions.Server.Content;

namespace Stallions.Server.Tests.Content;

public class ContentCatalogTests
{
    [Fact]
    public void TheCatalog_HasTheAgreedKeys_InOrder() =>
        ContentCatalog.Entries.Select(e => e.Key).Should().Equal(
            "Disclosure.BuyerFeeExplanation", "Disclosure.StudFarmBalanceArrangement", "Disclosure.SavedCardExplanation",
            "Email.Outbid", "Email.AuctionLost", "Email.AuctionEndedWithoutSale", "Email.WonAndCharged",
            "Email.OfferAcceptedAndCharged", "Email.PaymentFailed", "Email.PaymentFailedOffer", "Email.PaymentAbandoned",
            "Email.PaymentAbandonedOffer", "Email.OfferDeclined", "Email.OfferExpired", "Email.OfferExpiredListingClosed",
            "Email.StudSaleConfirmation", "Email.StudSaleConfirmationOffer", "Email.StudNoSaleAuction",
            "Email.StudNoSaleMakeAnOffer", "Email.OfferReceived",
            "Email.ListingFeeTaxInvoice", "Email.ListingFeeInvoiceDue", "Email.ListingFeeReceipt",
            "Email.BuyerFeeTaxInvoice", "Email.AdjustmentNote");

    [Fact]
    public void EveryDefault_PassesItsOwnValidation()
    {
        foreach (var entry in ContentCatalog.Entries)
            ContentTemplate.Validate(entry, entry.DefaultSubject, entry.DefaultBody).Should().BeEmpty(entry.Key);
    }

    [Fact]
    public void Emails_HaveASubject_AndDisclosures_HaveNoSubjectOrPlaceholders_AndAreOneParagraph()
    {
        foreach (var entry in ContentCatalog.Entries)
        {
            if (entry.Group == ContentGroups.Disclosures)
            {
                entry.HasSubject.Should().BeFalse(entry.Key);
                entry.DefaultSubject.Should().BeNull(entry.Key);
                entry.Placeholders.Should().BeEmpty(entry.Key);
                entry.SingleParagraph.Should().BeTrue(entry.Key);
            }
            else
            {
                entry.Key.Should().StartWith("Email.");
                entry.HasSubject.Should().BeTrue(entry.Key);
                entry.DefaultSubject.Should().NotBeNullOrWhiteSpace(entry.Key);
            }
        }
    }

    [Fact]
    public void RequiredPlaceholders_AreAllowed_AndNamesAreUniquePerItem()
    {
        foreach (var entry in ContentCatalog.Entries)
        {
            entry.Placeholders.Select(p => p.Name).Should().OnlyHaveUniqueItems(entry.Key);
            entry.Required.All(entry.Allows).Should().BeTrue(entry.Key);
        }
    }

    [Fact]
    public void EveryItem_IsLabelled_Grouped_AndFitsItsColumns()
    {
        var groups = new[] { ContentGroups.Disclosures, ContentGroups.BuyerEmails, ContentGroups.StudEmails, ContentGroups.TaxDocumentEmails };
        foreach (var entry in ContentCatalog.Entries)
        {
            groups.Should().Contain(entry.Group, entry.Key);
            entry.Label.Should().NotBeNullOrWhiteSpace(entry.Key);
            entry.Description.Should().NotBeNullOrWhiteSpace(entry.Key);
            entry.Key.Length.Should().BeLessThanOrEqualTo(ContentCatalog.MaxKeyLength);
            entry.Placeholders.All(p => p.Description.Length > 0 && p.Sample.Length > 0).Should().BeTrue(entry.Key);
        }
    }

    [Fact]
    public void Find_AnUnknownKey_IsNull()
    {
        ContentCatalog.Find("Email.NoSuchThing").Should().BeNull();
        ContentCatalog.Find(null).Should().BeNull();
        ContentCatalog.Find(ContentKeys.Outbid)!.Label.Should().Be("Outbid");
    }
}
