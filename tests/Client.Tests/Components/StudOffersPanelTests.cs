using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Components.Offers;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Offers;

namespace Stallions.Client.Tests.Components;

public class StudOffersPanelTests : TestContext
{
    private readonly Mock<OfferApiService> _api = new(MockBehavior.Loose, new HttpClient { BaseAddress = new Uri("https://localhost/") });
    private readonly Guid _listingId = Guid.NewGuid();
    private readonly StudOfferDto _pending = new()
    {
        Id = Guid.NewGuid(), BuyerName = "Jane Buyer", BuyerEmail = "jane@example.com", AmountIncGst = 9000m,
        Status = "Pending", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(7)
    };

    private IRenderedComponent<StudOffersPanel> Render(params StudOfferDto[] offers)
    {
        _api.Setup(a => a.GetForListingAsync(_listingId)).ReturnsAsync(offers.ToList());
        Services.AddSingleton(_api.Object);
        return RenderComponent<StudOffersPanel>(p => p.Add(c => c.ListingId, _listingId).Add(c => c.BuyerFeeIncGst, 150m));
    }

    [Fact]
    public void ShowsPendingOffers_WithBuyerAndAmount()
    {
        var cut = Render(_pending);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Jane Buyer"));
        cut.Markup.Should().Contain("jane@example.com").And.Contain("$9,000");
    }

    [Fact]
    public void Accept_AsksForConfirmation_ThenReportsThePayment()
    {
        _api.Setup(a => a.AcceptAsync(_pending.Id)).ReturnsAsync(new AcceptOfferResultDto { OfferId = _pending.Id, Charge = "Paid", SaleStatus = "Completed" });
        var cut = Render(_pending);
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Accept"));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Accept").Click();
        cut.Markup.Should().Contain("charges the buyer the $150 buyer fee");
        cut.FindAll("button").Single(b => b.TextContent.Contains("Yes, accept")).Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Accepted — buyer fee paid"));
        _api.Verify(a => a.AcceptAsync(_pending.Id), Times.Once);
    }

    [Fact]
    public void Accept_WhenThePaymentFails_ShowsTheDeadline()
    {
        _api.Setup(a => a.AcceptAsync(_pending.Id)).ReturnsAsync(new AcceptOfferResultDto
        {
            OfferId = _pending.Id, Charge = "PaymentFailed", SaleStatus = "Pending", ChargeDueBy = DateTime.UtcNow.AddHours(2)
        });
        var cut = Render(_pending);
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Accept"));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Accept").Click();
        cut.FindAll("button").Single(b => b.TextContent.Contains("Yes, accept")).Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("payment failed").And.Contain("the buyer has until"));
    }

    [Fact]
    public void Accept_WhenTheChargeWasSkipped_SaysItIsBeingProcessed()
    {
        _api.Setup(a => a.AcceptAsync(_pending.Id)).ReturnsAsync(new AcceptOfferResultDto { OfferId = _pending.Id, Charge = "Skipped", SaleStatus = "Pending" });
        var cut = Render(_pending);
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Accept"));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Accept").Click();
        cut.FindAll("button").Single(b => b.TextContent.Contains("Yes, accept")).Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("the payment is being processed"));
    }

    [Fact]
    public void Decline_DeclinesTheOffer()
    {
        var cut = Render(_pending);
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Decline"));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Decline").Click();

        _api.Verify(a => a.DeclineAsync(_pending.Id), Times.Once);
    }

    [Fact]
    public void Accept_WhenTheNetworkFails_ShowsAnError_AndReloadsTheTrueState()
    {
        _api.Setup(a => a.AcceptAsync(_pending.Id)).ThrowsAsync(new HttpRequestException("offline"));
        var cut = Render(_pending);
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Accept"));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Accept").Click();
        cut.FindAll("button").Single(b => b.TextContent.Contains("Yes, accept")).Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("couldn't reach the server"));
        _api.Verify(a => a.GetForListingAsync(_listingId), Times.Exactly(2));
    }
}
