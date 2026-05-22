using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Listings;

namespace Stallions.Client.Tests.Pages;

public class HomeTests : TestContext
{
    private Mock<ListingApiService> SetupService(List<ListingCardDto> cards)
    {
        var mock = new Mock<ListingApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        mock.Setup(s => s.GetListingsAsync(null, null, null)).ReturnsAsync(cards);
        Services.AddSingleton(mock.Object);
        return mock;
    }

    [Fact]
    public async Task Home_WhenListingsLoaded_RendersCards()
    {
        this.AddTestAuthorization();
        var cards = new List<ListingCardDto>
        {
            new() { Id = Guid.NewGuid(), StallionName = "Fastnet Rock", ListingType = "FixedPrice", PriceIncGst = 8000m }
        };
        SetupService(cards);

        var cut = RenderComponent<Home>();
        await Task.Delay(50); // allow async OnInitialized to complete
        cut.Render();

        cut.Markup.Should().Contain("Fastnet Rock");
    }

    [Fact]
    public async Task Home_WhenNoListings_RendersEmptyState()
    {
        this.AddTestAuthorization();
        SetupService(new List<ListingCardDto>());

        var cut = RenderComponent<Home>();
        await Task.Delay(50);
        cut.Render();

        cut.Find(".empty-state").Should().NotBeNull();
    }
}
