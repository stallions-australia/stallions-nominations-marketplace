using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Bids;
using Stallions.Shared.DTOs.Checkout;
using Stallions.Shared.DTOs.Listings;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class ListingDetailTests : TestContext
{
    private void RegisterServices(ListingDto listing, string? role = null, List<PublicBidDto>? history = null)
    {
        var listingMock = new Mock<ListingApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        listingMock.Setup(s => s.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        listingMock.Setup(s => s.GetBidHistoryAsync(listing.Id)).ReturnsAsync(history ?? []);
        listingMock.Setup(s => s.GetBuyerFeeDisclosureAsync()).ReturnsAsync(new BuyerFeeDisclosureDto
        {
            BuyerFeeExplanation = "CONFIGURED: the buyer fee forms part of the price.",
            BalanceArrangement = "CONFIGURED: pay the stud directly."
        });
        Services.AddSingleton(listingMock.Object);

        var bidMock = new Mock<BidApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        Services.AddSingleton(bidMock.Object);

        var userApiMock = new Mock<UserApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        if (role is not null)
        {
            userApiMock.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            {
                Id = Guid.NewGuid(), DisplayName = "Test User",
                Email = "test@example.com", Role = role, Status = "Active"
            });
        }
        else
        {
            userApiMock.Setup(s => s.GetMeAsync()).ReturnsAsync((UserDto?)null);
        }

        Services.AddSingleton(userApiMock.Object);

        var termsMock = new Mock<TermsApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        termsMock.Setup(s => s.GetCurrentAsync()).ReturnsAsync((Stallions.Shared.DTOs.Terms.TermsDocumentDto?)null);
        Services.AddSingleton(termsMock.Object);

        var userState = new UserStateService(userApiMock.Object);
        userState.LoadAsync().GetAwaiter().GetResult();
        Services.AddSingleton(userState);
    }

    [Fact]
    public void ListingDetail_Auction_Unauthenticated_ShowsSignInPrompt()
    {
        this.AddTestAuthorization(); // anonymous
        var listing = new AuctionListingDto
        {
            Id = Guid.NewGuid(), StallionName = "Snitzel", StudFarmName = "Arrowfield",
            ListingType = "Auction", Status = "Active",
            EndDateTime = DateTime.UtcNow.AddDays(3)
        };
        RegisterServices(listing);

        var cut = RenderComponent<ListingDetail>(p => p.Add(c => c.Id, listing.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Sign in to bid"));
    }

    private static AuctionListingDto ActiveAuction() => new()
    {
        Id = Guid.NewGuid(), StallionName = "Snitzel", StudFarmName = "Arrowfield",
        ListingType = "Auction", Status = "Active",
        BuyerFeeIncGst = 150m, MinimumBidIncrement = 25m,
        EndDateTime = DateTime.UtcNow.AddDays(3)
    };

    [Fact]
    public void ListingDetail_Buyer_NeverRendersTheReserveAmount()
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        // Even if a reserve amount somehow reached the client, the buyer page must not show it.
        var listing = ActiveAuction();
        listing.ReservePrice = 23456m;
        listing.ReserveMet = false;
        listing.CurrentHighestBidIncGst = 20000m;
        RegisterServices(listing, role: "Buyer");

        var cut = RenderComponent<ListingDetail>(p => p.Add(c => c.Id, listing.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Reserve not met"));
        cut.Markup.Should().NotContain("23,456").And.NotContain("23456");
    }

    [Fact]
    public void ListingDetail_ShowsReserveMetAndNoReserveLabels()
    {
        this.AddTestAuthorization();
        var met = ActiveAuction();
        met.ReserveMet = true;
        met.CurrentHighestBidIncGst = 30000m;
        RegisterServices(met);

        var cut = RenderComponent<ListingDetail>(p => p.Add(c => c.Id, met.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Reserve met"));
    }

    [Fact]
    public void ListingDetail_NoReserveAuction_IsLabelled()
    {
        this.AddTestAuthorization();
        var listing = ActiveAuction();
        listing.IsNoReserve = true;
        RegisterServices(listing);

        var cut = RenderComponent<ListingDetail>(p => p.Add(c => c.Id, listing.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No reserve"));
        cut.Markup.Should().NotContain("Reserve met").And.NotContain("Reserve not met");
    }

    [Fact]
    public void ListingDetail_ShowsBuyerFeeWithConfiguredExplanation_AndNoStartingPrice()
    {
        this.AddTestAuthorization();
        var listing = ActiveAuction();
        RegisterServices(listing);

        var cut = RenderComponent<ListingDetail>(p => p.Add(c => c.Id, listing.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("$150"));
        cut.Markup.Should().Contain("paid to Stallions Australia");
        cut.Markup.Should().Contain("CONFIGURED: the buyer fee forms part of the price.");
        cut.Markup.Should().Contain("No bids yet");
    }

    [Fact]
    public void ListingDetail_ShowsAnonymisedBidHistory()
    {
        this.AddTestAuthorization();
        var listing = ActiveAuction();
        listing.CurrentHighestBidIncGst = 1500m;
        RegisterServices(listing, history:
        [
            new PublicBidDto { Bidder = "Bidder 1", AmountIncGst = 1500m, PlacedAt = DateTime.UtcNow },
            new PublicBidDto { Bidder = "Bidder 2", AmountIncGst = 1200m, PlacedAt = DateTime.UtcNow.AddMinutes(-5) }
        ]);

        var cut = RenderComponent<ListingDetail>(p => p.Add(c => c.Id, listing.Id));

        cut.WaitForAssertion(() => cut.FindAll(".bid-history-row").Should().HaveCount(2));
        cut.Markup.Should().Contain("Bidder 1").And.Contain("Bidder 2").And.Contain("$1,200");
    }
}
