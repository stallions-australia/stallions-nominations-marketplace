using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Staff;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.ChangeLog;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class StaffChangeLogTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };
    private readonly Mock<ChangeLogApiService> _api = new(MockBehavior.Loose, Http());

    private static ChangeLogEntryDto Business() => new()
    {
        Id = 1, When = "12 Oct 2026, 2:00 pm", Who = "Sam Staff", Area = ChangeLogAreas.BusinessDetails, AreaLabel = "Business details",
        Item = "Business details",
        Changes = [new ChangeLogChangeDto { Field = "Bank account number", OldValue = "•••678", NewValue = "•••321" }]
    };

    private static ChangeLogEntryDto Wording() => new()
    {
        Id = 2, When = "12 Oct 2026, 1:00 pm", Who = "Sam Staff", Area = ChangeLogAreas.Wording, AreaLabel = "Wording",
        Item = "Outbid — version 2", Link = "/staff/wording?key=Email.Outbid&version=2",
        Changes =
        [
            new ChangeLogChangeDto { Field = "Subject", OldValue = "You've been outbid", NewValue = "Outbid" },
            new ChangeLogChangeDto { Field = "Message", IsLongText = true }
        ]
    };

    private IRenderedComponent<StaffChangeLog> Render(int total = 2)
    {
        this.AddTestAuthorization().SetAuthorized("staff@example.com");
        _api.Setup(a => a.GetAsync(It.IsAny<ChangeLogFilter>())).ReturnsAsync((ChangeLogFilter f) => new ChangeLogPageDto
        {
            Entries = [Business(), Wording()], Page = f.Page, PageSize = 50, Total = total
        });
        Services.AddSingleton(_api.Object);
        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "Staff", Email = "staff@example.com", Role = "Staff", Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));
        return RenderComponent<StaffChangeLog>();
    }

    [Fact]
    public void Lists_TheChanges_WithOldAndNewValues_AndMaskedAccountNumbers()
    {
        var cut = Render();

        cut.WaitForAssertion(() => cut.FindAll(".change-log-table tbody tr").Should().HaveCount(2));
        var first = cut.FindAll(".change-log-table tbody tr")[0].TextContent;
        first.Should().Contain("12 Oct 2026, 2:00 pm").And.Contain("Sam Staff").And.Contain("Business details")
            .And.Contain("Bank account number:").And.Contain("•••678 → •••321");
    }

    [Fact]
    public void LongText_ShowsChanged_WithALinkToTheVersion()
    {
        var cut = Render();

        cut.WaitForAssertion(() => cut.Find("a[href='/staff/wording?key=Email.Outbid&version=2']").TextContent.Should().Be("View version"));
        var row = cut.FindAll(".change-log-table tbody tr")[1].TextContent;
        row.Should().Contain("Subject:").And.Contain("You've been outbid → Outbid").And.Contain("Message:").And.Contain("changed");
    }

    [Fact]
    public void Filters_AreSentToTheApi()
    {
        var cut = Render();
        cut.WaitForAssertion(() => cut.FindAll(".change-log-table tbody tr").Should().HaveCount(2));

        cut.Find("#cl-area").Change("Wording");
        cut.Find("#cl-from").Change("2026-10-01");
        cut.Find("#cl-to").Change("2026-10-31");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Apply").Click();

        cut.WaitForAssertion(() => _api.Verify(a => a.GetAsync(It.Is<ChangeLogFilter>(f =>
            f.Area == "Wording" && f.From == new DateOnly(2026, 10, 1) && f.To == new DateOnly(2026, 10, 31) && f.Page == 1)), Times.Once));
    }

    [Fact]
    public void Next_RequestsTheNextPage()
    {
        var cut = Render(total: 120);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Page 1 of 3"));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Next").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Page 2 of 3"));
        _api.Verify(a => a.GetAsync(It.Is<ChangeLogFilter>(f => f.Page == 2)), Times.Once);
    }

    [Fact]
    public void AnApiError_IsShown()
    {
        var cut = Render();
        _api.Setup(a => a.GetAsync(It.IsAny<ChangeLogFilter>())).ThrowsAsync(new ApiException(500, "Couldn't load the change log."));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Apply").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Couldn't load the change log."));
    }
}
