using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Checkout;
using Stallions.Shared.DTOs.Listings;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class CheckoutTests : TestContext
{
    private UserStateService CreateBuyerUserState()
    {
        var userApiMock = new Mock<UserApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        userApiMock.Setup(s => s.GetMeAsync())
            .ReturnsAsync(new UserDto { Id = Guid.NewGuid(), DisplayName = "Test Buyer", Role = "Buyer", Status = "Active" });
        var userState = new UserStateService(userApiMock.Object);
        return userState;
    }

    [Fact]
    public void Checkout_Step1_ShowsMareNameInput()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("buyer@example.com");
        auth.SetRoles("Buyer");

        Services.AddSingleton(CreateBuyerUserState());

        var listingMock = new Mock<ListingApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        listingMock.Setup(s => s.GetByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new AuctionListingDto
            {
                Id = Guid.NewGuid(), StallionName = "Fastnet Rock", StudFarmName = "Coolmore",
                ListingType = "Auction", Status = "Active",
                EndDateTime = DateTime.UtcNow.AddHours(-1)
            });
        Services.AddSingleton(listingMock.Object);
        Services.AddSingleton(new Mock<CheckoutApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") }).Object);

        var cut = RenderComponent<Checkout>(p =>
            p.Add(c => c.ListingId, Guid.NewGuid()));

        cut.WaitForAssertion(() => cut.Find("input[id='mare-name']").Should().NotBeNull());
    }

    [Fact]
    public void Checkout_AfterMareSubmit_ShowsDisclosurePanel()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("buyer@example.com");
        auth.SetRoles("Buyer");

        Services.AddSingleton(CreateBuyerUserState());

        var listingId = Guid.NewGuid();

        var listingMock = new Mock<ListingApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        listingMock.Setup(s => s.GetByIdAsync(listingId))
            .ReturnsAsync(new AuctionListingDto
            {
                Id = listingId, StallionName = "Fastnet Rock",
                ListingType = "Auction", Status = "Active",
                EndDateTime = DateTime.UtcNow.AddHours(-1)
            });
        Services.AddSingleton(listingMock.Object);

        var checkoutMock = new Mock<CheckoutApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        checkoutMock.Setup(s => s.InitiateAsync(listingId, It.IsAny<CheckoutRequest>()))
            .ReturnsAsync(new CheckoutResponse
            {
                PurchaseId = Guid.NewGuid(),
                Disclosure = new CheckoutDisclosureDto
                {
                    TotalPriceIncGst = 10000m,
                    PlatformFeeIncGst = 250m,
                    StudFarmBalanceArrangement = "Stud farm will contact you.",
                    RefundPolicy = "90% refund if arrangement falls through."
                }
            });
        Services.AddSingleton(checkoutMock.Object);

        var cut = RenderComponent<Checkout>(p =>
            p.Add(c => c.ListingId, listingId));

        // Step 1: mare name form visible
        cut.WaitForAssertion(() => cut.Find("input[id='mare-name']").Should().NotBeNull());

        // Fill mare name and submit
        cut.Find("input[id='mare-name']").Change("Brilliant Star");
        cut.Find("form").Submit();

        // Step 2: disclosure panel should appear
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("disclosure-panel"));
    }
}
