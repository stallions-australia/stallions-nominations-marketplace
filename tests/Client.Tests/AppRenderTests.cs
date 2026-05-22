using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Listings;

namespace Stallions.Client.Tests;

public class AppRenderTests : TestContext
{
    [Fact]
    public void App_RendersWithoutThrowing()
    {
        // Add fake auth so AuthorizeRouteView doesn't throw
        this.AddTestAuthorization();

        // Register services required by the Home page (default route /)
        var listingMock = new Mock<ListingApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        listingMock.Setup(s => s.GetListingsAsync(null, null, null))
            .ReturnsAsync(new List<ListingCardDto>());
        Services.AddSingleton(listingMock.Object);

        // Act — will throw if component graph is broken
        var act = () => RenderComponent<App>();

        act.Should().NotThrow();
    }
}
