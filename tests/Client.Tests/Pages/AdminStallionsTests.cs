using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Admin;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Directory;
using Stallions.Shared.DTOs.Seasons;
using Stallions.Shared.DTOs.Settings;
using Stallions.Shared.DTOs.Stallions;
using Stallions.Shared.DTOs.Subscriptions;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class AdminStallionsTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };
    private readonly Mock<SubscriptionApiService> _subs = new(MockBehavior.Loose, Http());
    private readonly StallionSummaryDto _snitzel = new() { Id = Guid.NewGuid(), Name = "Snitzel", IsActive = true };
    private readonly SeasonDto _season = new() { Id = Guid.NewGuid(), Name = "2026 Season", IsOpen = true };

    private IRenderedComponent<AdminStallions> Render(params SubscriptionDto[] subscriptions) => Render(null, subscriptions);

    private IRenderedComponent<AdminStallions> Render(string? query, params SubscriptionDto[] subscriptions)
    {
        this.AddTestAuthorization().SetAuthorized("stud@example.com");
        var admin = new Mock<AdminApiService>(MockBehavior.Loose, Http());
        admin.Setup(a => a.GetMyStallionsAsync()).ReturnsAsync([_snitzel]);
        admin.Setup(a => a.GetAuthorizedStallionsAsync()).ReturnsAsync(new AuthorizedStallionsDto { IsLinked = false });
        admin.Setup(a => a.GetSeasonsAsync()).ReturnsAsync([_season]);
        Services.AddSingleton(admin.Object);

        if (query is null) _subs.Setup(s => s.GetMineAsync()).ReturnsAsync(subscriptions.ToList());
        Services.AddSingleton(_subs.Object);

        var settings = new Mock<PlatformSettingsApiService>(MockBehavior.Loose, Http());
        settings.Setup(s => s.GetAsync()).ReturnsAsync(new PlatformSettingsDto { StandardListingFeeIncGst = 990m });
        Services.AddSingleton(settings.Object);

        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "S", Email = "s@x", Role = "StudFarmAdmin", Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));

        if (query != null) Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/stallions?{query}");
        var cut = RenderComponent<AdminStallions>(p => p.Add(c => c.PollInterval, TimeSpan.FromMilliseconds(10)));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Snitzel"));
        return cut;
    }

    private SubscriptionDto Subscription(string status, decimal fee) => new()
        { StallionId = _snitzel.Id, SeasonId = _season.Id, Status = status, FeeIncGst = fee };

    [Fact]
    public void NoSubscription_OffersActivationAtTheStandardFee()
    {
        var cut = Render();

        cut.Markup.Should().Contain("Activate for 2026 Season — $990");
    }

    [Fact]
    public void PendingSubscription_OffersActivationAtItsOwnFee()
    {
        var cut = Render(Subscription("Pending", 495m));

        cut.Markup.Should().Contain("Activate for 2026 Season — $495");
    }

    [Fact]
    public void PaidSubscription_ShowsPaidAndNoActivateButton()
    {
        var cut = Render(Subscription("Paid", 990m));

        cut.Markup.Should().Contain("Paid").And.NotContain("Activate for");
    }

    [Fact]
    public void Activate_SendsTheBrowserToThePaymentPage()
    {
        _subs.Setup(s => s.ActivateAsync(_snitzel.Id)).ReturnsAsync("https://checkout.example/pay");
        var cut = Render();

        cut.FindAll("button").Single(b => b.TextContent.Contains("Activate for")).Click();

        cut.WaitForAssertion(() =>
            Services.GetRequiredService<NavigationManager>().Uri.Should().Be("https://checkout.example/pay"));
    }

    [Fact]
    public void ReturningFromPayment_PollsUntilTheFeeIsPaid()
    {
        _subs.SetupSequence(s => s.GetMineAsync())
            .ReturnsAsync([Subscription("Pending", 990m)])
            .ReturnsAsync([Subscription("Pending", 990m)])
            .ReturnsAsync([Subscription("Paid", 990m)]);

        var cut = Render("payment=success");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Paid")
            .And.NotContain("Activate for").And.NotContain("Confirming your listing-fee payment"), TimeSpan.FromSeconds(5));
        _subs.Verify(s => s.GetMineAsync(), Times.Exactly(3));
    }
}
