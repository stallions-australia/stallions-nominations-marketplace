using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Staff;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Settings;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class StaffSettingsTests : TestContext
{
    private readonly Mock<PlatformSettingsApiService> _settingsMock =
        new(MockBehavior.Loose, new HttpClient { BaseAddress = new Uri("https://localhost/") });

    private IRenderedComponent<StaffSettings> Render()
    {
        this.AddTestAuthorization().SetAuthorized("staff@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        _settingsMock.Setup(s => s.GetAsync()).ReturnsAsync(new PlatformSettingsDto
        {
            BuyerFeeIncGst = 150m, StandardListingFeeIncGst = 990m, MinimumBidIncrement = 25m,
            ChargeGracePeriodHours = 2, OfferExpiryDays = 7
        });
        Services.AddSingleton(_settingsMock.Object);

        var userApiMock = new Mock<UserApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        userApiMock.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
        {
            Id = Guid.NewGuid(), DisplayName = "Staff", Email = "staff@example.com",
            Role = "Staff", Status = "Active"
        });
        Services.AddSingleton(new UserStateService(userApiMock.Object));

        return RenderComponent<StaffSettings>();
    }

    [Fact]
    public void Save_AsksForConfirmation_BeforeUpdating()
    {
        var cut = Render();
        cut.WaitForAssertion(() => cut.Find("#buyer-fee"));

        cut.Find("#buyer-fee").Change("200");
        cut.FindAll("button").Single(b => b.TextContent.Contains("Save settings")).Click();

        cut.Markup.Should().Contain("Confirm settings change");
        _settingsMock.Verify(s => s.UpdateAsync(It.IsAny<UpdatePlatformSettingsRequest>()), Times.Never);

        cut.FindAll("button").Single(b => b.TextContent.Contains("Confirm and save")).Click();

        _settingsMock.Verify(s => s.UpdateAsync(It.Is<UpdatePlatformSettingsRequest>(r => r.BuyerFeeIncGst == 200m)),
            Times.Once);
    }

    [Fact]
    public void Save_WithNonPositiveValue_ShowsErrorAndDoesNotConfirm()
    {
        var cut = Render();
        cut.WaitForAssertion(() => cut.Find("#offer-expiry"));

        cut.Find("#offer-expiry").Change("0");
        cut.FindAll("button").Single(b => b.TextContent.Contains("Save settings")).Click();

        cut.Markup.Should().Contain("Offer expiry must be greater than zero.");
        cut.Markup.Should().NotContain("Confirm settings change");
    }
}
