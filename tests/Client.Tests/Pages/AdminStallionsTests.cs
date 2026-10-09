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
    private readonly StallionSummaryDto _zoustar = new() { Id = Guid.NewGuid(), Name = "Zoustar", IsActive = true };
    private readonly SeasonDto _season = new() { Id = Guid.NewGuid(), Name = "2026 Season", IsOpen = true };
    private readonly List<StallionSummaryDto> _stallions = [];

    public AdminStallionsTests() => _stallions.Add(_snitzel);

    private IRenderedComponent<AdminStallions> Render(params SubscriptionDto[] subscriptions)
    {
        _subs.Setup(s => s.GetMineAsync()).ReturnsAsync(subscriptions.ToList());
        return RenderAt(null);
    }

    // The caller sets up GetMineAsync (often as a sequence) before rendering.
    private IRenderedComponent<AdminStallions> RenderAt(string? query)
    {
        this.AddTestAuthorization().SetAuthorized("stud@example.com");
        var admin = new Mock<AdminApiService>(MockBehavior.Loose, Http());
        admin.Setup(a => a.GetMyStallionsAsync()).ReturnsAsync(_stallions);
        admin.Setup(a => a.GetAuthorizedStallionsAsync()).ReturnsAsync(new AuthorizedStallionsDto { IsLinked = false });
        admin.Setup(a => a.GetSeasonsAsync()).ReturnsAsync([_season]);
        Services.AddSingleton(admin.Object);
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

    // WaitForAssertion only re-checks after a render; a pending API call doesn't render.
    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++) await Task.Delay(10);
        condition().Should().BeTrue("the condition should hold within 5 s");
    }

    private SubscriptionDto Subscription(string status, decimal fee, StallionSummaryDto? stallion = null) => new()
        { StallionId = (stallion ?? _snitzel).Id, SeasonId = _season.Id, Status = status, FeeIncGst = fee };

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

        var cut = RenderAt($"payment=success&stallion={_snitzel.Id}");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Paid")
            .And.NotContain("Activate for").And.NotContain("Confirming your listing-fee payment"), TimeSpan.FromSeconds(5));
        _subs.Verify(s => s.GetMineAsync(), Times.Exactly(3));
    }

    [Fact]
    public void ReturningFromPayment_AlreadyPaid_ShowsNoBannerAndDoesNotPoll()
    {
        _subs.Setup(s => s.GetMineAsync()).ReturnsAsync([Subscription("Paid", 990m)]);

        var cut = RenderAt($"payment=success&stallion={_snitzel.Id}");

        cut.Markup.Should().NotContain("Confirming your listing-fee payment").And.NotContain("still being confirmed");
        _subs.Verify(s => s.GetMineAsync(), Times.Once);
    }

    [Fact]
    public async Task ReturningFromPayment_WaitsForTheNamedStallion_NotAnother()
    {
        _stallions.Add(_zoustar);
        var calls = 0;
        _subs.Setup(s => s.GetMineAsync()).ReturnsAsync(() => ++calls switch
        {
            1 => [Subscription("Pending", 990m)],
            2 => [Subscription("Pending", 990m), Subscription("Paid", 990m, _zoustar)],
            _ => [Subscription("Paid", 990m), Subscription("Paid", 990m, _zoustar)]
        });

        var cut = RenderAt($"payment=success&stallion={_snitzel.Id}");

        await Until(() => calls >= 3);
        cut.WaitForAssertion(() => cut.Markup.Should().NotContain("Confirming your listing-fee payment"));
        calls.Should().Be(3, "polling continues past another stallion's payment and stops at Snitzel's");
        cut.Markup.Should().NotContain("Activate for");
    }

    [Fact]
    public void ReturningCancelled_ShowsTheNotice()
    {
        var cut = RenderAt($"payment=cancelled&stallion={_snitzel.Id}");

        cut.Markup.Should().Contain("Payment not completed");
    }

    [Fact]
    public void ReturningFromPayment_StripsTheQuery()
    {
        _subs.Setup(s => s.GetMineAsync()).ReturnsAsync([Subscription("Paid", 990m)]);

        RenderAt($"payment=success&stallion={_snitzel.Id}");

        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/admin/stallions");
    }

    [Fact]
    public async Task LeavingThePageMidPoll_ThrowsNothing()
    {
        var slow = new TaskCompletionSource<List<SubscriptionDto>>();
        var calls = 0;
        _subs.Setup(s => s.GetMineAsync())
            .Returns(() => ++calls == 1 ? Task.FromResult(new List<SubscriptionDto> { Subscription("Pending", 990m) }) : slow.Task);
        RenderAt($"payment=success&stallion={_snitzel.Id}");
        await Until(() => calls == 2);

        DisposeComponents();
        slow.SetResult([Subscription("Pending", 990m)]);
        await Task.Delay(100);

        Renderer.UnhandledException.IsCompleted.Should().BeFalse();
        calls.Should().Be(2, "polling stops once the page is gone");
    }
}
