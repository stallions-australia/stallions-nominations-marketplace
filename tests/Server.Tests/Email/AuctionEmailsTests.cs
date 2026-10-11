using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Options;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Email;

public class AuctionEmailsTests
{
    private readonly List<OutgoingEmail> _queued = new();
    private readonly Mock<IUserRepository> _users = new();

    private AuctionEmails CreateSut()
    {
        var outbox = new Mock<IEmailOutbox>();
        outbox.Setup(o => o.EnqueueAsync(It.IsAny<OutgoingEmail>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .Callback<OutgoingEmail, string?, Guid?>((e, _, _) => _queued.Add(e))
            .Returns(Task.CompletedTask);
        return new AuctionEmails(outbox.Object, _users.Object,
            Microsoft.Extensions.Options.Options.Create(new DisclosureOptions
            {
                BuyerFeeExplanation = "CONFIGURED fee wording.",
                StudFarmBalanceArrangement = "CONFIGURED balance wording."
            }),
            Microsoft.Extensions.Options.Options.Create(new EmailOptions { PublicBaseUrl = "https://app.example/" }),
            NullLogger<AuctionEmails>.Instance);
    }

    private static readonly User Owner = new() { Email = "owner@arrowfield.example", DisplayName = "Owner" };

    private static AuctionListing Listing(string? contactEmail = "sales@arrowfield.example") => new()
    {
        Id = Guid.NewGuid(),
        EndDateTime = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc),
        Stallion = new Stallion { Name = "Snitzel" },
        Season = new Season { Name = "2026 Season" },
        StudFarm = new StudFarm { Name = "Arrowfield", ContactEmail = contactEmail, ContactPhone = "02 6545 9888", User = Owner, UserId = Owner.Id }
    };

    private static readonly User Winner = new() { Email = "winner@example.com", DisplayName = "Jane Buyer" };

    private static Purchase Sale() => new()
    {
        TotalPriceIncGst = 10000m, BuyerFeeIncGst = 150m, BuyerFeeExGst = 136.36m, BuyerFeeGst = 13.64m,
        BalancePayableToStudIncGst = 9850m, PaymentReference = "pi_123",
        ChargeDueBy = new DateTime(2026, 10, 10, 11, 0, 0, DateTimeKind.Utc),
        LastChargeFailure = "Your card was declined."
    };

    [Fact]
    public async Task WonAndCharged_ShowsPriceFeeBalanceAndTheConfiguredWording()
    {
        await CreateSut().WonAndChargedAsync(Listing(), Sale(), Winner);

        var email = _queued.Single();
        email.ToAddress.Should().Be("winner@example.com");
        email.Template.Should().Be("WonAndCharged");
        email.Subject.Should().Contain("Snitzel");
        email.TextBody.Should().Contain("$10,000.00").And.Contain("$150.00").And.Contain("$9,850.00")
            .And.Contain("CONFIGURED fee wording.").And.Contain("CONFIGURED balance wording.")
            .And.Contain("https://app.example/my-purchases");
        email.HtmlBody.Should().Contain("$9,850.00");
    }

    [Fact]
    public async Task StudSaleConfirmation_GoesToTheFarmContact_AndNamesTheBuyer()
    {
        await CreateSut().StudSaleConfirmationAsync(Listing(), Sale(), Winner);

        var email = _queued.Single();
        email.ToAddress.Should().Be("sales@arrowfield.example");
        email.TextBody.Should().Contain("Jane Buyer").And.Contain("winner@example.com").And.Contain("$9,850.00");
    }

    [Fact]
    public async Task StudEmails_FallBackToTheOwnersEmail()
    {
        await CreateSut().StudNoSaleAsync(Listing(contactEmail: null), ListingCloseReason.ReserveNotMet);

        var email = _queued.Single();
        email.ToAddress.Should().Be("owner@arrowfield.example");
        email.TextBody.Should().Contain("below your reserve");
    }

    [Fact]
    public async Task PaymentFailed_GivesTheDeadlineInSydneyTime_AndTheCardLink()
    {
        await CreateSut().PaymentFailedAsync(Listing(), Sale(), Winner);

        var email = _queued.Single();
        email.Template.Should().Be("PaymentFailed");
        // 11:00 UTC on 10 Oct 2026 is 10:00 pm in Sydney (AEDT, UTC+11).
        email.TextBody.Should().Contain("10 Oct 2026, 10:00 pm").And.Contain("Your card was declined.")
            .And.Contain("https://app.example/account/card");
    }

    [Fact]
    public async Task Outbid_LooksUpThePreviousBidder()
    {
        var bidder = new User { Id = Guid.NewGuid(), Email = "outbid@example.com" };
        _users.Setup(u => u.GetByIdAsync(bidder.Id)).ReturnsAsync(bidder);

        await CreateSut().OutbidAsync(Listing(), bidder.Id, 12500m);

        var email = _queued.Single();
        email.ToAddress.Should().Be("outbid@example.com");
        email.TextBody.Should().Contain("$12,500.00");
    }

    [Fact]
    public async Task ValuesAreHtmlEncoded()
    {
        var listing = Listing();
        listing.Stallion.Name = "<b>Snitzel</b>";

        await CreateSut().AuctionLostAsync(listing, Winner);

        _queued.Single().HtmlBody.Should().Contain("&lt;b&gt;Snitzel&lt;/b&gt;").And.NotContain("<b>Snitzel");
    }

    private static MakeAnOfferListing OfferListing() => new()
    {
        Id = Guid.NewGuid(),
        Stallion = new Stallion { Name = "Snitzel" },
        Season = new Season { Name = "2026 Season" },
        StudFarm = new StudFarm { Name = "Arrowfield", ContactEmail = "sales@arrowfield.example" }
    };

    private static Purchase OfferSale() => new()
    {
        OfferId = Guid.NewGuid(), TotalPriceIncGst = 9000m, BuyerFeeIncGst = 150m, BuyerFeeExGst = 136.36m,
        BuyerFeeGst = 13.64m, BalancePayableToStudIncGst = 8850m
    };

    [Fact]
    public async Task AnAcceptedOfferCharged_UsesOfferWording_AndShowsTheSplit()
    {
        await CreateSut().WonAndChargedAsync(OfferListing(), OfferSale(), new User { Email = "buyer@example.com" });

        var email = _queued.Single();
        email.Template.Should().Be("OfferAcceptedAndCharged");
        email.Subject.Should().Contain("offer").And.Contain("accepted");
        email.TextBody.Should().Contain("Price (your accepted offer): $9,000.00").And.Contain("$8,850.00")
            .And.Contain("CONFIGURED fee wording.").And.NotContain("winning bid");
    }

    [Fact]
    public async Task StudNoSale_ForAnOfferListing_SaysTheListingStaysOpen()
    {
        await CreateSut().StudNoSaleAsync(OfferListing(), ListingCloseReason.ChargeFailed);

        _queued.Single().TextBody.Should().Contain("stays open").And.NotContain("auction");
    }

    [Fact]
    public async Task StudNoSale_ForAnAuction_LinksToMakeAnOffer()
    {
        await CreateSut().StudNoSaleAsync(Listing(), ListingCloseReason.ReserveNotMet);

        _queued.Single().TextBody.Should().Contain("Offer it as Make an Offer");
    }
}
