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

    private static ListingCardDto AuctionCard() => new()
    {
        Id = Guid.NewGuid(),
        ListingType = "Auction",
        StallionName = "Snitzel",
        StudFarmName = "Arrowfield Stud",
        CurrentHighestBidIncGst = 6500m,
        BidCount = 3,
        AuctionClosesAt = DateTime.UtcNow.AddDays(2)
    };

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

    [Fact]
    public void AMakeAnOfferCard_ShowsTheBadgeAndButton_AndNoBidInfo()
    {
        this.AddTestAuthorization();
        Services.AddSingleton<NavigationManager>(new FakeNavigationManager(this));

        var cut = RenderComponent<ListingCard>(p => p.Add(c => c.Listing, new ListingCardDto
        {
            Id = Guid.NewGuid(), ListingType = "MakeAnOffer", StallionName = "Dundeel (NZ)",
            StudFarmName = "Arrowfield", OffersUntil = DateTime.UtcNow.AddDays(30)
        }));

        cut.Markup.Should().Contain("Make an Offer").And.Contain("Make an offer").And.Contain("Open to offers")
            .And.NotContain("No bids yet").And.NotContain("Place a bid");
    }
}
