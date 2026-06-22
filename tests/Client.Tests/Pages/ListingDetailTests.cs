using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Listings;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class ListingDetailTests : TestContext
{
    private void RegisterServices(ListingDto listing, string? role = null)
    {
        var listingMock = new Mock<ListingApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        listingMock.Setup(s => s.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
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
    public void ListingDetail_FixedPrice_ShowsPurchaseLink()
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        var listing = new FixedPriceListingDto
        {
            Id = Guid.NewGuid(), StallionName = "Fastnet Rock", StudFarmName = "Coolmore",
            ListingType = "FixedPrice", Status = "Active",
            PriceIncGst = 8000m, QuantityRemaining = 3, Quantity = 10
        };
        RegisterServices(listing, role: "Buyer");

        var cut = RenderComponent<ListingDetail>(p => p.Add(c => c.Id, listing.Id));

        cut.WaitForAssertion(() => cut.Find("a[href*='checkout']").Should().NotBeNull());
    }

    [Fact]
    public void ListingDetail_Auction_Unauthenticated_ShowsSignInPrompt()
    {
        this.AddTestAuthorization(); // anonymous
        var listing = new AuctionListingDto
        {
            Id = Guid.NewGuid(), StallionName = "Snitzel", StudFarmName = "Arrowfield",
            ListingType = "Auction", Status = "Active",
            StartingPrice = 5000m,
            EndDateTime = DateTime.UtcNow.AddDays(3)
        };
        RegisterServices(listing);

        var cut = RenderComponent<ListingDetail>(p => p.Add(c => c.Id, listing.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Sign in to bid"));
    }
}
