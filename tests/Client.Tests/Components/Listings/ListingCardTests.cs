using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Stallions.Client.Components.Listings;
using Stallions.Shared.DTOs.Listings;

namespace Stallions.Client.Tests.Components.Listings;

public class ListingCardTests : TestContext
{
    private static ListingCardDto FixedPriceCard() => new()
    {
        Id = Guid.NewGuid(),
        ListingType = "FixedPrice",
        StallionName = "Fastnet Rock",
        StudFarmName = "Coolmore Australia",
        PriceIncGst = 8000m,
        QuantityRemaining = 4,
        TotalQuantity = 10
    };

    private static ListingCardDto AuctionCard() => new()
    {
        Id = Guid.NewGuid(),
        ListingType = "Auction",
        StallionName = "Snitzel",
        StudFarmName = "Arrowfield Stud",
        PriceIncGst = 5000m,
        CurrentHighestBidIncGst = 6500m,
        BidCount = 3,
        AuctionClosesAt = DateTime.UtcNow.AddDays(2)
    };

    [Fact]
    public void ListingCard_FixedPrice_ShowsBuyNowBadge()
    {
        this.AddTestAuthorization();
        Services.AddSingleton<NavigationManager>(new FakeNavigationManager(this));

        var cut = RenderComponent<ListingCard>(p => p.Add(c => c.Listing, FixedPriceCard()));

        cut.Find(".badge-buynow").Should().NotBeNull();
        cut.Find(".card-stallion-name").TextContent.Should().Be("Fastnet Rock");
    }

    [Fact]
    public void ListingCard_Auction_ShowsBidCount()
    {
        this.AddTestAuthorization();
        Services.AddSingleton<NavigationManager>(new FakeNavigationManager(this));

        var cut = RenderComponent<ListingCard>(p => p.Add(c => c.Listing, AuctionCard()));

        cut.Find(".badge-auction").Should().NotBeNull();
        cut.Markup.Should().Contain("3 bids");
    }

    [Fact]
    public void ListingCard_Auction_ShowsHighestBidAsPrice()
    {
        this.AddTestAuthorization();
        Services.AddSingleton<NavigationManager>(new FakeNavigationManager(this));

        var cut = RenderComponent<ListingCard>(p => p.Add(c => c.Listing, AuctionCard()));

        // $6,500 is the current highest bid — should be the displayed price
        cut.Markup.Should().Contain("6,500");
    }
}
