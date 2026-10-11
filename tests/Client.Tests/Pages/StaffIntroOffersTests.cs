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
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class StaffIntroOffersTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };
    private readonly Mock<IntroOfferApiService> _api = new(MockBehavior.Loose, Http());

    private static IntroOfferDto Launch(int timesApplied = 0) => new()
    {
        Id = Guid.NewGuid(), Name = "Launch 2026", Type = "AmountOff", AmountOffIncGst = 495m,
        EndsOn = new DateOnly(2026, 12, 31), State = "Active", TimesApplied = timesApplied
    };

    private IRenderedComponent<StaffIntroOffers> Render(params IntroOfferDto[] offers)
    {
        this.AddTestAuthorization().SetAuthorized("staff@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.Setup(a => a.GetAllAsync()).ReturnsAsync(offers.ToList());
        _api.Setup(a => a.CreateAsync(It.IsAny<SaveIntroOfferRequest>())).ReturnsAsync(new IntroOfferDto());
        Services.AddSingleton(_api.Object);

        var staffApi = new Mock<StaffApiService>(MockBehavior.Loose, Http());
        staffApi.Setup(s => s.GetStudFarmsAsync()).ReturnsAsync([new StudFarmSummaryDto { Id = Guid.NewGuid(), Name = "Arrowfield" }]);
        staffApi.Setup(s => s.GetSeasonsAsync()).ReturnsAsync([new SeasonDto { Id = Guid.NewGuid(), Name = "2026 Season", IsOpen = true }]);
        Services.AddSingleton(staffApi.Object);

        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "Staff", Email = "staff@example.com", Role = "Staff", Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));

        return RenderComponent<StaffIntroOffers>();
    }

    private static void Click(IRenderedComponent<StaffIntroOffers> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == text).Click();

    [Fact]
    public void Lists_OffersWithTheirTermsScopeStateAndUse()
    {
        var retired = new IntroOfferDto
        {
            Id = Guid.NewGuid(), Name = "Founding studs", Type = "Waiver", StudFarmName = "Arrowfield", SeasonName = "2026 Season",
            EndsOn = new DateOnly(2026, 11, 30), State = "Retired", IsRetired = true, TimesApplied = 3
        };

        var cut = Render(Launch(), retired);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Launch 2026"));
        cut.Markup.Should().Contain("$495.00 off").And.Contain("Fee waived").And.Contain("All studs · Any season")
            .And.Contain("Arrowfield · 2026 Season").And.Contain("31 Dec 2026").And.Contain("Retired");
        cut.FindAll("button").Count(b => b.TextContent.Trim() == "Edit").Should().Be(1);
    }

    [Fact]
    public void Create_AmountOffWithoutAnAmount_ShowsAnError_AndDoesNotSave()
    {
        var cut = Render();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No intro offers yet."));
        Click(cut, "+ New intro offer");

        cut.Find("#offer-name").Change("Launch");
        Click(cut, "Save intro offer");

        cut.Markup.Should().Contain("Enter the dollars off the listing fee (more than $0).");
        _api.Verify(a => a.CreateAsync(It.IsAny<SaveIntroOfferRequest>()), Times.Never);
    }

    [Fact]
    public void Create_AWaiver_SendsTheRequest()
    {
        var cut = Render();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No intro offers yet."));
        Click(cut, "+ New intro offer");

        cut.Find("#offer-name").Change("Founding studs");
        cut.Find("#offer-type").Change("Waiver");
        cut.Find("#offer-ends").Change("2026-12-31");
        Click(cut, "Save intro offer");

        _api.Verify(a => a.CreateAsync(It.Is<SaveIntroOfferRequest>(r => r.Name == "Founding studs" && r.Type == "Waiver"
            && r.AmountOffIncGst == null && r.StudFarmId == null && r.SeasonId == null
            && r.EndsOn == new DateOnly(2026, 12, 31))), Times.Once);
    }

    [Fact]
    public void Edit_AnAppliedOffer_LocksItsTerms()
    {
        var cut = Render(Launch(timesApplied: 2));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Launch 2026"));

        Click(cut, "Edit");

        cut.Markup.Should().Contain("only the name and end date can change");
        cut.Find("#offer-type").HasAttribute("disabled").Should().BeTrue();
        cut.Find("#offer-amount").HasAttribute("disabled").Should().BeTrue();
        cut.Find("#offer-name").HasAttribute("disabled").Should().BeFalse();
        cut.Find("#offer-ends").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Retire_AsksFirst_ThenRetires()
    {
        var offer = Launch();
        _api.Setup(a => a.RetireAsync(offer.Id)).ReturnsAsync(offer);
        var cut = Render(offer);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Launch 2026"));

        Click(cut, "Retire");

        cut.Markup.Should().Contain("Retire Launch 2026?");
        _api.Verify(a => a.RetireAsync(It.IsAny<Guid>()), Times.Never);

        Click(cut, "Retire offer");

        _api.Verify(a => a.RetireAsync(offer.Id), Times.Once);
    }
}
