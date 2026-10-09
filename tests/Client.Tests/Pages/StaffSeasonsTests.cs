using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Staff;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Seasons;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class StaffSeasonsTests : TestContext
{
    private readonly Mock<StaffApiService> _staffMock =
        new(MockBehavior.Loose, new HttpClient { BaseAddress = new Uri("https://localhost/") });

    private readonly SeasonDto _open2025 = new()
    {
        Id = Guid.NewGuid(), Name = "2025 Season", IsOpen = true,
        StartDate = new DateOnly(2025, 8, 1), EndDate = new DateOnly(2026, 1, 31)
    };

    private readonly SeasonDto _closed2026 = new()
    {
        Id = Guid.NewGuid(), Name = "2026 Season", IsOpen = false,
        StartDate = new DateOnly(2026, 8, 1), EndDate = new DateOnly(2027, 1, 31)
    };

    private IRenderedComponent<StaffSeasons> Render(params SeasonDto[] seasons)
    {
        this.AddTestAuthorization().SetAuthorized("staff@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        _staffMock.Setup(s => s.GetSeasonsAsync()).ReturnsAsync(seasons.ToList());
        Services.AddSingleton(_staffMock.Object);

        var userApiMock = new Mock<UserApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        userApiMock.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
        {
            Id = Guid.NewGuid(), DisplayName = "Staff", Email = "staff@example.com",
            Role = "Staff", Status = "Active"
        });
        Services.AddSingleton(new UserStateService(userApiMock.Object));

        var cut = RenderComponent<StaffSeasons>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain(seasons[0].Name));
        return cut;
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<StaffSeasons> cut, string text) =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == text);

    [Fact]
    public void ListsSeasonsWithOpenStatus()
    {
        var cut = Render(_open2025, _closed2026);

        cut.FindAll(".season-row").Should().HaveCount(2);
        cut.Markup.Should().Contain("Open").And.Contain("Closed");
    }

    [Fact]
    public void Close_AsksForConfirmation_BeforeCallingTheApi()
    {
        var cut = Render(_open2025);

        Button(cut, "Close").Click();

        cut.Markup.Should().Contain("Existing listings, auctions and listing fees are not affected");
        _staffMock.Verify(s => s.CloseSeasonAsync(It.IsAny<Guid>()), Times.Never);

        Button(cut, "Close season").Click();

        _staffMock.Verify(s => s.CloseSeasonAsync(_open2025.Id), Times.Once);
    }

    [Fact]
    public void Open_WhileAnotherSeasonIsOpen_TellsStaffToCloseItFirst()
    {
        var cut = Render(_open2025, _closed2026);

        Button(cut, "Open").Click();

        cut.Markup.Should().Contain("Close 2025 Season first");
        cut.FindAll("button").Should().NotContain(b => b.TextContent.Trim() == "Open season");
        _staffMock.Verify(s => s.OpenSeasonAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public void Open_WhenNoneOpen_AsksForConfirmation_ThenOpens()
    {
        var cut = Render(_closed2026);

        Button(cut, "Open").Click();
        cut.Markup.Should().Contain("Studs will be able to create listings in 2026 Season");
        _staffMock.Verify(s => s.OpenSeasonAsync(It.IsAny<Guid>()), Times.Never);

        Button(cut, "Open season").Click();

        _staffMock.Verify(s => s.OpenSeasonAsync(_closed2026.Id), Times.Once);
    }

    [Fact]
    public void Create_WithEndBeforeStart_ShowsErrorAndDoesNotSave()
    {
        var cut = Render(_open2025);

        Button(cut, "+ New season").Click();
        cut.Find("#season-name").Change("2026 Season");
        cut.Find("#season-start").Change("2026-08-01");
        cut.Find("#season-end").Change("2026-07-31");
        Button(cut, "Save").Click();

        cut.Markup.Should().Contain("End date must be after the start date.");
        _staffMock.Verify(s => s.CreateSeasonAsync(It.IsAny<CreateSeasonRequest>()), Times.Never);
    }

    [Fact]
    public void Create_WithValidDates_CallsTheApi()
    {
        var cut = Render(_open2025);

        Button(cut, "+ New season").Click();
        cut.Find("#season-name").Change("2026 Season");
        cut.Find("#season-start").Change("2026-08-01");
        cut.Find("#season-end").Change("2027-01-31");
        Button(cut, "Save").Click();

        _staffMock.Verify(s => s.CreateSeasonAsync(It.Is<CreateSeasonRequest>(r =>
            r.Name == "2026 Season" && r.StartDate == new DateOnly(2026, 8, 1) && r.EndDate == new DateOnly(2027, 1, 31))),
            Times.Once);
    }
}
