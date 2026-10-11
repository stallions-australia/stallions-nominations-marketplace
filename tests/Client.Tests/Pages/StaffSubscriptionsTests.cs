using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Staff;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Admin;
using Stallions.Shared.DTOs.IntroOffers;
using Stallions.Shared.DTOs.Seasons;
using Stallions.Shared.DTOs.Settings;
using Stallions.Shared.DTOs.Stallions;
using Stallions.Shared.DTOs.Subscriptions;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class StaffSubscriptionsTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };
    private readonly Mock<SubscriptionApiService> _subs = new(MockBehavior.Loose, Http());
    private readonly Mock<IntroOfferApiService> _offers = new(MockBehavior.Loose, Http());
    private readonly StudFarmSummaryDto _farm = new() { Id = Guid.NewGuid(), Name = "Arrowfield" };
    private readonly SeasonDto _season = new() { Id = Guid.NewGuid(), Name = "2026 Season", IsOpen = true };

    private SubscriptionDto Pending() => new()
    {
        Id = Guid.NewGuid(), StallionName = "Dundeel", StudFarmName = "Arrowfield", SeasonName = "2026 Season",
        FeeIncGst = 990m, FeeExGst = 900m, GstAmount = 90m, StandardFeeIncGst = 990m, Status = "Pending"
    };

    private IRenderedComponent<StaffSubscriptions> Render(params SubscriptionDto[] subscriptions)
    {
        this.AddTestAuthorization().SetAuthorized("staff@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        _subs.Setup(s => s.GetAllAsync(It.IsAny<Guid?>(), It.IsAny<string?>())).ReturnsAsync(subscriptions.ToList());
        Services.AddSingleton(_subs.Object);
        Services.AddSingleton(_offers.Object);

        var staffApi = new Mock<StaffApiService>(MockBehavior.Loose, Http());
        staffApi.Setup(s => s.GetSeasonsAsync()).ReturnsAsync([_season]);
        staffApi.Setup(s => s.GetStudFarmsAsync()).ReturnsAsync([_farm]);
        staffApi.Setup(s => s.GetStudFarmStallionsAsync(_farm.Id))
            .ReturnsAsync([new StallionSummaryDto { Id = Guid.NewGuid(), Name = "Dundeel", StudFarmId = _farm.Id }]);
        Services.AddSingleton(staffApi.Object);

        var settings = new Mock<PlatformSettingsApiService>(MockBehavior.Loose, Http());
        settings.Setup(s => s.GetAsync()).ReturnsAsync(new PlatformSettingsDto { StandardListingFeeIncGst = 990m });
        Services.AddSingleton(settings.Object);

        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "Staff", Email = "staff@example.com", Role = "Staff", Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));

        return RenderComponent<StaffSubscriptions>();
    }

    private static void Click(IRenderedComponent<StaffSubscriptions> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim().StartsWith(text)).Click();

    [Fact]
    public void Create_ShowsTheIntroOfferThatWillApply()
    {
        _offers.Setup(o => o.PreviewAsync(_farm.Id, _season.Id)).ReturnsAsync(new IntroOfferPreviewDto
            { StandardFeeIncGst = 990m, FeeIncGst = 495m, IntroOfferName = "Launch 2026" });
        var cut = Render();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No subscriptions match"));
        Click(cut, "+ New subscription");

        cut.Find("#new-farm").Change(_farm.Id.ToString());

        cut.WaitForAssertion(() => cut.Find(".intro-offer-preview").TextContent
            .Should().Contain("Intro offer Launch 2026 applies: $990.00 → $495.00"));
    }

    [Fact]
    public void Create_WhenTheOfferWaivesTheFee_SaysSo()
    {
        _offers.Setup(o => o.PreviewAsync(_farm.Id, _season.Id)).ReturnsAsync(new IntroOfferPreviewDto
            { StandardFeeIncGst = 990m, FeeIncGst = 0m, IntroOfferName = "Founding studs" });
        var cut = Render();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No subscriptions match"));
        Click(cut, "+ New subscription");

        cut.Find("#new-farm").Change(_farm.Id.ToString());

        cut.WaitForAssertion(() => cut.Find(".intro-offer-preview").TextContent.Should().Contain("the fee will be waived"));
    }

    [Fact]
    public void IssueTaxInvoice_ConfirmsFirst_ThenIssuesAndShowsTheNumber()
    {
        var sub = Pending();
        _subs.Setup(s => s.IssueInvoiceAsync(sub.Id)).ReturnsAsync(new SubscriptionDto
            { Id = sub.Id, Status = "Pending", TaxInvoiceNumber = "SA-000001", TaxInvoiceStatus = "Unpaid" });
        var cut = Render(sub);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Dundeel"));

        Click(cut, "Issue tax invoice");

        cut.Markup.Should().Contain("Emails Arrowfield an unpaid tax invoice for $990.00");
        _subs.Verify(s => s.IssueInvoiceAsync(It.IsAny<Guid>()), Times.Never);

        Click(cut, "Issue and email");

        _subs.Verify(s => s.IssueInvoiceAsync(sub.Id), Times.Once);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Tax invoice SA-000001 issued and emailed to Arrowfield."));
    }

    [Fact]
    public void AnInvoicedSubscription_LinksItsTaxInvoice_AndCannotBeInvoicedAgain()
    {
        var sub = Pending();
        sub.TaxInvoiceId = Guid.NewGuid();
        sub.TaxInvoiceNumber = "SA-000001";
        sub.TaxInvoiceStatus = "Unpaid";
        sub.IntroOfferName = "Launch 2026";

        var cut = Render(sub);

        cut.WaitForAssertion(() => cut.Find($"a[href='/tax-documents/{sub.TaxInvoiceId}']").TextContent.Should().Be("SA-000001"));
        cut.Markup.Should().Contain("Intro offer: Launch 2026");
        cut.FindAll("button").Should().NotContain(b => b.TextContent.Contains("Issue tax invoice"));
    }
}
