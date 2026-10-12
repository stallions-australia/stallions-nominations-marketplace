using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Staff;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Terms;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class StaffTermsTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };
    private readonly Mock<TermsApiService> _api = new(MockBehavior.Loose, Http());

    private static TermsDocumentDto Doc(string audience, int version, string body) => new()
    {
        Id = Guid.NewGuid(), Audience = audience, Version = version, Body = body,
        CreatedAt = new DateTime(2026, 10, 12, 3, 0, 0, DateTimeKind.Utc), CreatedByName = "Sam Staff"
    };

    private IRenderedComponent<StaffTerms> Render(string? query = null)
    {
        this.AddTestAuthorization().SetAuthorized("staff@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.Setup(a => a.GetHistoryAsync("Buyer")).ReturnsAsync([Doc("Buyer", 2, "<script>x</script> Buyer **v2**"), Doc("Buyer", 1, "Buyer v1")]);
        _api.Setup(a => a.GetHistoryAsync("Stud")).ReturnsAsync([Doc("Stud", 1, "Stud **terms** v1")]);
        Services.AddSingleton(_api.Object);

        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "Staff", Email = "staff@example.com", Role = "Staff", Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));

        if (query != null) Services.GetRequiredService<NavigationManager>().NavigateTo($"/staff/terms?{query}");
        return RenderComponent<StaffTerms>();
    }

    private static void Click(IRenderedComponent<StaffTerms> cut, string text) =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == text).Click();

    [Fact]
    public void BuyerTerms_AreShownFirst_WithTheirVersionHistory()
    {
        var cut = Render();

        cut.WaitForAssertion(() => cut.Find(".terms-current").TextContent.Should().Contain("2"));
        cut.FindAll(".terms-history tbody tr").Should().HaveCount(2);
        cut.Find(".terms-history").TextContent.Should().Contain("Sam Staff");
        cut.Find(".terms-preview-box").InnerHtml.Should().Contain("<strong>v2</strong>").And.NotContain("<script");
    }

    [Fact]
    public void TheStudSwitch_ShowsTheStudTerms_AndPublishesStudTerms()
    {
        var cut = Render();
        cut.WaitForAssertion(() => cut.FindAll(".terms-history tbody tr").Should().HaveCount(2));
        _api.Setup(a => a.PublishAsync(It.IsAny<string>(), "Stud")).ReturnsAsync(Doc("Stud", 2, "New stud terms"));

        Click(cut, "Stud listing terms");
        cut.WaitForAssertion(() => cut.FindAll(".terms-history tbody tr").Should().HaveCount(1));
        cut.Find("textarea").Input("New stud terms");
        Click(cut, "Publish new version");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("New version published."));
        _api.Verify(a => a.PublishAsync("New stud terms", "Stud"), Times.Once);
        cut.FindAll(".terms-history tbody tr").Should().HaveCount(2);
    }

    [Fact]
    public void View_ShowsAReadOnlyVersion_RenderedSafely()
    {
        var cut = Render();
        cut.WaitForAssertion(() => cut.FindAll(".terms-history tbody tr").Should().HaveCount(2));

        Click(cut, "View");

        cut.Find(".terms-version-view").InnerHtml.Should().Contain("<strong>v2</strong>").And.NotContain("<script");
    }

    [Fact]
    public void AChangeLogLink_OpensThatStudVersion()
    {
        var cut = Render("audience=Stud&version=1");

        cut.WaitForAssertion(() => cut.Find(".terms-version-view").InnerHtml.Should().Contain("<strong>terms</strong>"));
    }
}
