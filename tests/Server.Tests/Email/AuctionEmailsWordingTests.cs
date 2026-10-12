using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Content;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Email;

/// <summary>
/// The wording side of the auction emails: with the seeded defaults they are unchanged; Staff edits change only the
/// subject and message, never the generated amounts, disclosure, contact details or links.
/// The fixtures below are the ones DefaultEmailGolden was captured with — don't change them.
/// </summary>
public class AuctionEmailsWordingTests : IDisposable
{
    private static readonly User Winner = new() { Email = "winner@example.com", DisplayName = "Jane Buyer" };
    private static readonly User OutbidBidder = new() { Id = Guid.Parse("77777777-7777-7777-7777-777777777777"), Email = "outbid@example.com" };

    private readonly AppDbContext _db = DbContextFactory.Create(Guid.NewGuid().ToString());
    private readonly List<OutgoingEmail> _queued = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITaxDocumentRepository> _taxDocuments = new();
    private readonly CapturingLogger<ContentService> _contentLog = new();

    public AuctionEmailsWordingTests()
    {
        _db.Database.EnsureCreated(); // seeds version 1 (today's wording) of every item
        _users.Setup(u => u.GetByIdAsync(OutbidBidder.Id)).ReturnsAsync(OutbidBidder);
        _taxDocuments.Setup(t => t.GetBuyerFeeInvoiceAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new TaxDocument { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), Number = "SA-000124" });
    }

    public void Dispose() => _db.Dispose();

    private AuctionEmails CreateSut()
    {
        var outbox = new Mock<IEmailOutbox>();
        outbox.Setup(o => o.EnqueueAsync(It.IsAny<OutgoingEmail>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .Callback<OutgoingEmail, string?, Guid?>((e, _, _) => _queued.Add(e))
            .Returns(Task.CompletedTask);
        return new AuctionEmails(outbox.Object, _users.Object, _taxDocuments.Object,
            ContentTestData.Service(_db, log: _contentLog),
            Microsoft.Extensions.Options.Options.Create(new EmailOptions { PublicBaseUrl = "https://app.example/" }),
            NullLogger<AuctionEmails>.Instance);
    }

    private static AuctionListing Listing() => new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        EndDateTime = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc),
        Stallion = new Stallion { Name = "Snitzel" }, Season = new Season { Name = "2026 Season" },
        StudFarm = new StudFarm { Name = "Arrowfield", ContactEmail = "sales@arrowfield.example", ContactPhone = "02 6545 9888" }
    };

    private static MakeAnOfferListing OfferListing() => new()
    {
        Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Stallion = new Stallion { Name = "Snitzel" }, Season = new Season { Name = "2026 Season" },
        StudFarm = new StudFarm { Name = "Arrowfield", ContactEmail = "sales@arrowfield.example", ContactPhone = "02 6545 9888" }
    };

    private static Purchase Sale() => new()
    {
        Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
        TotalPriceIncGst = 10000m, BuyerFeeIncGst = 150m, BuyerFeeExGst = 136.36m, BuyerFeeGst = 13.64m,
        BalancePayableToStudIncGst = 9850m, ChargeDueBy = new DateTime(2026, 10, 10, 11, 0, 0, DateTimeKind.Utc),
        LastChargeFailure = "Your card was declined."
    };

    private static Purchase OfferSale()
    {
        var sale = Sale();
        sale.OfferId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        sale.TotalPriceIncGst = 9000m;
        sale.BalancePayableToStudIncGst = 8850m;
        return sale;
    }

    private static Offer TheOffer() => new() { AmountIncGst = 9000m, ExpiresAt = new DateTime(2026, 10, 18, 1, 0, 0, DateTimeKind.Utc) };

    /// <summary>Each content key and the email call that uses it.</summary>
    private static readonly IReadOnlyDictionary<string, Func<AuctionEmails, Task>> Calls = new Dictionary<string, Func<AuctionEmails, Task>>
    {
        [ContentKeys.Outbid] = s => s.OutbidAsync(Listing(), OutbidBidder.Id, 12500m),
        [ContentKeys.AuctionLost] = s => s.AuctionLostAsync(Listing(), Winner),
        [ContentKeys.AuctionEndedWithoutSale] = s => s.AuctionEndedWithoutSaleAsync(Listing(), Winner),
        [ContentKeys.WonAndCharged] = s => s.WonAndChargedAsync(Listing(), Sale(), Winner),
        [ContentKeys.OfferAcceptedAndCharged] = s => s.WonAndChargedAsync(OfferListing(), OfferSale(), Winner),
        [ContentKeys.PaymentFailed] = s => s.PaymentFailedAsync(Listing(), Sale(), Winner),
        [ContentKeys.PaymentFailedOffer] = s => s.PaymentFailedAsync(OfferListing(), OfferSale(), Winner),
        [ContentKeys.PaymentAbandoned] = s => s.PaymentAbandonedAsync(Listing(), Sale(), Winner),
        [ContentKeys.PaymentAbandonedOffer] = s => s.PaymentAbandonedAsync(OfferListing(), OfferSale(), Winner),
        [ContentKeys.StudSaleConfirmation] = s => s.StudSaleConfirmationAsync(Listing(), Sale(), Winner),
        [ContentKeys.StudSaleConfirmationOffer] = s => s.StudSaleConfirmationAsync(OfferListing(), OfferSale(), Winner),
        [ContentKeys.StudNoSaleAuction] = s => s.StudNoSaleAsync(Listing(), ListingCloseReason.ReserveNotMet),
        [ContentKeys.StudNoSaleMakeAnOffer] = s => s.StudNoSaleAsync(OfferListing(), ListingCloseReason.ChargeFailed),
        [ContentKeys.OfferReceived] = s => s.OfferReceivedAsync(OfferListing(), TheOffer(), Winner),
        [ContentKeys.OfferDeclined] = s => s.OfferDeclinedAsync(OfferListing(), TheOffer(), Winner),
        [ContentKeys.OfferExpired] = s => s.OfferExpiredAsync(OfferListing(), TheOffer(), Winner, listingClosed: false),
        [ContentKeys.OfferExpiredListingClosed] = s => s.OfferExpiredAsync(OfferListing(), TheOffer(), Winner, listingClosed: true)
    };

    public static IEnumerable<object[]> EmailKeys() => Calls.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(EmailKeys))]
    public async Task DefaultWording_MatchesTodaysEmailsExactly(string key)
    {
        await Calls[key](CreateSut());

        var email = _queued.Single();
        var expected = DefaultEmailGolden.Emails[key];
        email.Subject.Should().Be(expected.Subject);
        email.TextBody.Should().Be(expected.Text);
        email.HtmlBody.Should().Be(expected.Html);
        _contentLog.Warnings.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(EmailKeys))]
    public async Task EveryEmail_SuppliesExactlyTheCatalogsPlaceholders(string key)
    {
        var entry = ContentCatalog.Find(key)!;
        var everything = string.Join(" ", entry.Placeholders.Select(p => $"[{p.Name}={{{p.Name}}}]"));
        ContentTestData.SetWording(_db, key, "S " + everything, everything);

        await Calls[key](CreateSut());

        _contentLog.Warnings.Should().BeEmpty("the email code must supply every placeholder its catalog entry lists, and no others");
        var text = _queued.Single().TextBody;
        foreach (var p in entry.Placeholders)
            text.Should().Contain($"[{p.Name}=");
    }

    [Fact]
    public void EveryAuctionEmailKey_IsCovered() =>
        Calls.Keys.Should().BeEquivalentTo(ContentCatalog.Entries
            .Where(e => e.Group is ContentGroups.BuyerEmails or ContentGroups.StudEmails).Select(e => e.Key));

    [Fact]
    public async Task WonAndCharged_WithEditedWording_KeepsTheAmountsTableDisclosureAndLinks()
    {
        ContentTestData.SetWording(_db, ContentKeys.WonAndCharged, "Congratulations — {StallionName} is yours",
            "Well done!\n\nYou bought the {StallionName} nomination for {Amount}.");

        await Calls[ContentKeys.WonAndCharged](CreateSut());

        var email = _queued.Single();
        email.Subject.Should().Be("Congratulations — Snitzel is yours");
        email.TextBody.Should().StartWith("Well done!\n\nYou bought the Snitzel nomination for $10,000.00.\n\nPrice (your winning bid): $10,000.00\n");
        email.TextBody.Should()
            .Contain("Buyer fee paid to Stallions Australia: $150.00\nBalance you pay the stud directly: $9,850.00\nAll amounts include GST.")
            .And.Contain("The buyer fee of $150.00 has been charged to your saved card.")
            .And.Contain(ContentCatalog.Find(ContentKeys.BuyerFeeExplanation)!.DefaultBody)
            .And.Contain(ContentCatalog.Find(ContentKeys.StudFarmBalanceArrangement)!.DefaultBody)
            .And.Contain("Stud contact: Arrowfield, sales@arrowfield.example, 02 6545 9888")
            .And.Contain("Your tax invoice SA-000124 is available here: https://app.example/tax-documents/33333333-3333-3333-3333-333333333333")
            .And.EndWith("View your sale record: https://app.example/my-purchases\n\nStallions Australia");
        email.HtmlBody.Should().Contain("<p>Well done!</p><p>You bought the Snitzel nomination for $10,000.00.</p><table");
    }

    [Fact]
    public async Task EditedDisclosure_IsWhatTheWinEmailShows()
    {
        ContentTestData.SetWording(_db, ContentKeys.BuyerFeeExplanation, null, "EDITED fee wording.");
        ContentTestData.SetWording(_db, ContentKeys.StudFarmBalanceArrangement, null, "EDITED balance wording.");

        await Calls[ContentKeys.OfferAcceptedAndCharged](CreateSut());

        _queued.Single().TextBody.Should()
            .Contain("The buyer fee of $150.00 has been charged to your saved card.\n\nEDITED fee wording.\n\nEDITED balance wording.")
            .And.NotContain("it is not added on top");
    }

    [Fact]
    public async Task EditedWording_WithMarkup_IsEncoded()
    {
        ContentTestData.SetWording(_db, ContentKeys.AuctionLost, "Closed: {StallionName}",
            "<a href=\"https://evil.example\">Claim your prize</a> {StallionName}");

        await Calls[ContentKeys.AuctionLost](CreateSut());

        var email = _queued.Single();
        email.HtmlBody.Should().Contain("<p>&lt;a href=&quot;https://evil.example&quot;&gt;Claim your prize&lt;/a&gt; Snitzel</p>")
            .And.NotContain("<a href=\"https://evil.example\"");
    }

    [Fact]
    public async Task Subject_WithLineBreaksInAValue_StaysOneLine()
    {
        var listing = Listing();
        listing.Stallion = new Stallion { Name = "Snitzel\r\nBcc: x@y" };

        await CreateSut().AuctionLostAsync(listing, Winner);

        var subject = _queued.Single().Subject;
        subject.Should().NotContain("\r").And.NotContain("\n");
        subject.Should().StartWith("Auction closed: Snitzel").And.Contain("Bcc: x@y");
    }

    [Fact]
    public async Task EditedWording_ALineBreak_IsABrInHtml_AndStaysInText()
    {
        ContentTestData.SetWording(_db, ContentKeys.OfferDeclined, "Declined: {StallionName}",
            "Sorry.\nYour offer of {Amount} was declined.");

        await Calls[ContentKeys.OfferDeclined](CreateSut());

        var email = _queued.Single();
        email.HtmlBody.Should().Contain("<p>Sorry.<br>Your offer of $9,000.00 was declined.</p><p><a href=");
        email.TextBody.Should().StartWith("Sorry.\nYour offer of $9,000.00 was declined.\n\nView the listing: ");
    }
}
