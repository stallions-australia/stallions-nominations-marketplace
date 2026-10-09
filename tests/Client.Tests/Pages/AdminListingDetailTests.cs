using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Admin;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Listings;
using Stallions.Shared.DTOs.Subscriptions;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class AdminListingDetailTests : TestContext
{
    private readonly AuctionListingDto _draft = new()
    {
        Id = Guid.NewGuid(), StallionId = Guid.NewGuid(), SeasonId = Guid.NewGuid(),
        StallionName = "Snitzel", SeasonName = "2026 Season",
        ListingType = "Auction", Status = "Draft",
        ReservePrice = 20000m, MinimumBidIncrement = 25m,
        EndDateTime = DateTime.UtcNow.AddDays(7)
    };

    private void Render(List<SubscriptionDto> subscriptions, out IRenderedComponent<AdminListingDetail> cut)
    {
        this.AddTestAuthorization().SetAuthorized("stud@example.com");
        var http = new HttpClient { BaseAddress = new Uri("https://localhost/") };

        var adminMock = new Mock<AdminApiService>(MockBehavior.Loose, http);
        adminMock.Setup(s => s.GetListingAsync(_draft.Id)).ReturnsAsync(_draft);
        Services.AddSingleton(adminMock.Object);

        var subsMock = new Mock<SubscriptionApiService>(MockBehavior.Loose, http);
        subsMock.Setup(s => s.GetMineAsync()).ReturnsAsync(subscriptions);
        Services.AddSingleton(subsMock.Object);

        var userApiMock = new Mock<UserApiService>(MockBehavior.Loose, http);
        userApiMock.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
        {
            Id = Guid.NewGuid(), DisplayName = "Stud", Email = "stud@example.com",
            Role = "StudFarmAdmin", Status = "Active"
        });
        var userState = new UserStateService(userApiMock.Object);
        Services.AddSingleton(userState);

        cut = RenderComponent<AdminListingDetail>(p => p.Add(c => c.Id, _draft.Id));
    }

    private SubscriptionDto Subscription(string status) => new()
    {
        Id = Guid.NewGuid(), StallionId = _draft.StallionId, SeasonId = _draft.SeasonId, Status = status
    };

    private static AngleSharp.Dom.IElement PublishButton(IRenderedComponent<AdminListingDetail> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Contains("Publish"));

    [Fact]
    public void PublishIsDisabled_WhenStallionHasNoSubscription()
    {
        Render([], out var cut);

        cut.WaitForAssertion(() => PublishButton(cut).HasAttribute("disabled").Should().BeTrue());
        cut.Markup.Should().Contain("Listing fee required");
    }

    [Fact]
    public void PublishIsDisabled_WhenSubscriptionIsPending()
    {
        Render([Subscription("Pending")], out var cut);

        cut.WaitForAssertion(() => PublishButton(cut).HasAttribute("disabled").Should().BeTrue());
    }

    [Theory]
    [InlineData("Paid")]
    [InlineData("Waived")]
    public void PublishIsEnabled_WhenSubscriptionIsActive(string status)
    {
        Render([Subscription(status)], out var cut);

        cut.WaitForAssertion(() => PublishButton(cut).HasAttribute("disabled").Should().BeFalse());
        cut.Markup.Should().NotContain("Listing fee required");
    }

    [Fact]
    public void OwningStudSeesTheReserveAmount()
    {
        Render([Subscription("Paid")], out var cut);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("$20,000"));
    }
}
