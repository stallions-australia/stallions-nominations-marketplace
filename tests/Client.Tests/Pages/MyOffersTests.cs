using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Offers;

namespace Stallions.Client.Tests.Pages;

public class MyOffersTests : TestContext
{
    [Fact]
    public void ListsOffersWithStatus_AndLinksAcceptedOnesToTheSaleRecord()
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        var api = new Mock<OfferApiService>(MockBehavior.Loose, new HttpClient { BaseAddress = new Uri("https://localhost/") });
        api.Setup(a => a.GetMineAsync()).ReturnsAsync(new List<OfferDto>
        {
            new() { Id = Guid.NewGuid(), StallionName = "Dundeel (NZ)", AmountIncGst = 9000m, Status = "Pending", ExpiresAt = DateTime.UtcNow.AddDays(5), ListingOpen = true },
            new() { Id = Guid.NewGuid(), StallionName = "Maurice (JPN)", AmountIncGst = 8000m, Status = "Accepted", SaleStatus = "Completed", PurchaseId = Guid.NewGuid() },
            new() { Id = Guid.NewGuid(), StallionName = "Snitzel", AmountIncGst = 7000m, Status = "Declined" }
        });
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<MyOffers>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Dundeel (NZ)"));
        cut.Markup.Should().Contain("With the stud").And.Contain("Accepted — paid").And.Contain("Declined");
        cut.Find("a[href='/my-purchases']").Should().NotBeNull();
        cut.FindAll("button").Count(b => b.TextContent.Contains("Withdraw")).Should().Be(1);
    }

    [Fact]
    public void Withdraw_WhenTheNetworkFails_ShowsAnError()
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        var offer = new OfferDto { Id = Guid.NewGuid(), StallionName = "Dundeel (NZ)", AmountIncGst = 9000m, Status = "Pending", ExpiresAt = DateTime.UtcNow.AddDays(5), ListingOpen = true };
        var api = new Mock<OfferApiService>(MockBehavior.Loose, new HttpClient { BaseAddress = new Uri("https://localhost/") });
        api.Setup(a => a.GetMineAsync()).ReturnsAsync(new List<OfferDto> { offer });
        api.Setup(a => a.WithdrawAsync(offer.Id)).ThrowsAsync(new HttpRequestException("offline"));
        Services.AddSingleton(api.Object);
        var cut = RenderComponent<MyOffers>();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Withdraw")));

        cut.FindAll("button").Single(b => b.TextContent.Contains("Withdraw")).Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("couldn't reach the server"));
    }
}
