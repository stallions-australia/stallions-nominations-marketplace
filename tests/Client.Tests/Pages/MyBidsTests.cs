using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Bids;

namespace Stallions.Client.Tests.Pages;

public class MyBidsTests : TestContext
{
    private IRenderedComponent<MyBids> Render(params BidDto[] bids)
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        var api = new Mock<BidApiService>(MockBehavior.Loose, new HttpClient { BaseAddress = new Uri("https://localhost/") });
        api.Setup(a => a.GetMyBidsAsync()).ReturnsAsync(bids.ToList());
        Services.AddSingleton(api.Object);
        return RenderComponent<MyBids>();
    }

    [Fact]
    public void ShowsLeadingWonAndLost_WithTheStallion()
    {
        var cut = Render(
            new BidDto { Id = Guid.NewGuid(), StallionName = "Snitzel", Status = "Active", AuctionOpen = true, AmountIncGst = 5000m },
            new BidDto { Id = Guid.NewGuid(), StallionName = "Zoustar", Status = "Won", AmountIncGst = 6000m },
            new BidDto { Id = Guid.NewGuid(), StallionName = "I Am Invincible", Status = "Lost", AmountIncGst = 7000m });

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Leading"));
        cut.Markup.Should().Contain("Snitzel").And.Contain("Won").And.Contain("Lost").And.Contain("Sale record");
    }

    [Fact]
    public void BidAgain_OnlyWhileTheAuctionIsOpen()
    {
        var cut = Render(
            new BidDto { Id = Guid.NewGuid(), Status = "Outbid", AuctionOpen = true },
            new BidDto { Id = Guid.NewGuid(), Status = "Outbid", AuctionOpen = false });

        cut.WaitForAssertion(() => cut.FindAll("a").Count(a => a.TextContent.Contains("Bid again")).Should().Be(1));
    }
}
