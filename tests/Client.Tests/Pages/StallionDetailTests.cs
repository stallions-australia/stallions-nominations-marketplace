using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Listings;
using Stallions.Shared.DTOs.Stallions;

namespace Stallions.Client.Tests.Pages;

public class StallionDetailTests : TestContext
{
    [Fact]
    public void StallionDetail_ShowsStallionName()
    {
        this.AddTestAuthorization();
        var stallionId = Guid.NewGuid();

        var stallionMock = new Mock<StallionApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        stallionMock.Setup(s => s.GetByIdAsync(stallionId))
            .ReturnsAsync(new StallionDto { Id = stallionId, Name = "Fastnet Rock" });
        Services.AddSingleton(stallionMock.Object);

        var listingMock = new Mock<ListingApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        listingMock.Setup(s => s.GetListingsAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>()))
            .ReturnsAsync(new List<ListingCardDto>());
        Services.AddSingleton(listingMock.Object);

        var cut = RenderComponent<StallionDetail>(p => p.Add(c => c.Id, stallionId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Fastnet Rock"));
    }
}
