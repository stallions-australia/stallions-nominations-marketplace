using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Staff;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Admin;
using Stallions.Shared.DTOs.Terms;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class StaffStudFarmsTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };

    [Fact]
    public void EachFarm_ShowsWhichStudTermsVersionItAccepted()
    {
        this.AddTestAuthorization().SetAuthorized("staff@example.com");
        // Midday UTC, so the local calendar date is 12 Oct in any time zone from UTC-12 to UTC+11.
        var at = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
        var staffApi = new Mock<StaffApiService>(MockBehavior.Loose, Http());
        staffApi.Setup(s => s.GetStudFarmsAsync()).ReturnsAsync(
        [
            new StudFarmSummaryDto { Id = Guid.NewGuid(), Name = "Arrowfield", AcceptedStudTermsVersion = 2, AcceptedStudTermsAt = at },
            new StudFarmSummaryDto { Id = Guid.NewGuid(), Name = "Coolmore", AcceptedStudTermsVersion = 1, AcceptedStudTermsAt = at },
            new StudFarmSummaryDto { Id = Guid.NewGuid(), Name = "Darley" }
        ]);
        Services.AddSingleton(staffApi.Object);
        var terms = new Mock<TermsApiService>(MockBehavior.Loose, Http());
        terms.Setup(t => t.GetStudStatusAsync()).ReturnsAsync(new StudTermsStatusDto
            { Current = new TermsDocumentDto { Audience = "Stud", Version = 2, Body = "Stud terms" } });
        Services.AddSingleton(terms.Object);
        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "Staff", Email = "staff@example.com", Role = "Staff", Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));

        var cut = RenderComponent<StaffStudFarms>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Arrowfield"));
        cut.Find("thead").TextContent.Should().Contain("Stud terms");
        var rows = cut.FindAll("tbody tr");
        rows[0].TextContent.Should().Contain("v2 · 12 Oct 2026").And.NotContain("not current");
        rows[1].TextContent.Should().Contain("v1 · 12 Oct 2026 (not current)");
        rows[2].TextContent.Should().Contain("Not accepted");
    }
}
