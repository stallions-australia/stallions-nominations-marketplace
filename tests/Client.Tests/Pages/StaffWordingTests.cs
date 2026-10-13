using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Staff;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Content;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class StaffWordingTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };
    private readonly Mock<ContentApiService> _api = new(MockBehavior.Loose, Http());

    private static ContentItemDetailDto Outbid(int version = 1, string body = "Another buyer has bid {HighBid} on {StallionName}.") => new()
    {
        Key = "Email.Outbid", Group = "Buyer emails", Label = "Outbid",
        Description = "To a bidder when another buyer places a higher bid.",
        HasSubject = true, Subject = "You've been outbid on {StallionName}", Body = body, Version = version,
        ChangedAt = new DateTime(2026, 10, 12, 3, 0, 0, DateTimeKind.Utc), ChangedByName = version == 1 ? null : "Sam Staff",
        IsDefault = version == 1, MaxSubjectLength = 200, MaxBodyLength = 4000,
        Placeholders =
        [
            new ContentPlaceholderDto { Name = "StallionName", Description = "The stallion's name", Sample = "Snitzel", Required = true },
            new ContentPlaceholderDto { Name = "HighBid", Description = "The new highest bid (inc. GST)", Sample = "$12,500.00", Required = true }
        ]
    };

    private static List<ContentVersionDto> Versions(int current = 1) =>
        Enumerable.Range(1, current).Reverse().Select(v => new ContentVersionDto
        {
            Version = v, Subject = "You've been outbid on {StallionName}",
            Body = v == 1 ? "Default body {HighBid} {StallionName}" : $"Edited body {v} {{HighBid}} {{StallionName}}",
            CreatedAt = new DateTime(2026, 10, 12, 3, 0, 0, DateTimeKind.Utc), CreatedByName = v == 1 ? null : "Sam Staff",
            IsCurrent = v == current
        }).ToList();

    private IRenderedComponent<StaffWording> Render(string? query = null, ContentItemDetailDto? detail = null,
        List<ContentVersionDto>? versions = null, string role = "Staff")
    {
        this.AddTestAuthorization().SetAuthorized("staff@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.Setup(a => a.GetAsync("Email.Outbid")).ReturnsAsync(detail ?? Outbid());
        _api.Setup(a => a.GetHistoryAsync("Email.Outbid")).ReturnsAsync(versions ?? Versions());
        _api.Setup(a => a.PreviewAsync("Email.Outbid", It.IsAny<SaveContentRequest>())).ReturnsAsync(new ContentPreviewDto
        {
            Subject = "You've been outbid on Snitzel", Paragraphs = ["Another buyer has bid $12,500.00 on Snitzel."]
        });
        Services.AddSingleton(_api.Object);

        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "Staff", Email = "staff@example.com", Role = role, Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));

        if (query != null) Services.GetRequiredService<NavigationManager>().NavigateTo($"/staff/wording?{query}");
        return RenderComponent<StaffWording>(p => p.Add(c => c.PreviewDelay, TimeSpan.Zero));
    }

    private static void Click(IRenderedComponent<StaffWording> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == text).Click();

    private static string BodyValue(IRenderedComponent<StaffWording> cut)
    {
        var body = cut.Find("#wording-body");
        return body.GetAttribute("value") ?? body.TextContent;
    }

    [Fact]
    public void List_GroupsTheItems_WithTheirVersionAndWhoChangedThem()
    {
        _api.Setup(a => a.GetAllAsync()).ReturnsAsync(
        [
            new ContentItemSummaryDto { Key = "Disclosure.BuyerFeeExplanation", Group = "Disclosures", Label = "Buyer fee explanation",
                Description = "Shown before bidding.", CurrentVersion = 1, ChangedAt = new DateTime(2026, 10, 12, 3, 0, 0, DateTimeKind.Utc) },
            new ContentItemSummaryDto { Key = "Email.Outbid", Group = "Buyer emails", Label = "Outbid", Description = "To a bidder.",
                HasSubject = true, CurrentVersion = 3, ChangedAt = new DateTime(2026, 10, 13, 3, 0, 0, DateTimeKind.Utc), ChangedByName = "Sam Staff" }
        ]);

        var cut = Render();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Buyer fee explanation"));
        cut.FindAll("h2.wording-group").Select(h => h.TextContent).Should().Equal("Disclosures", "Buyer emails");
        cut.Markup.Should().Contain("v3").And.Contain("by Sam Staff").And.Contain("by System default");
        cut.Find("a[href='/staff/wording?key=Email.Outbid']").TextContent.Should().Be("Edit");
    }

    [Fact]
    public void Editor_ShowsTheText_ThePlaceholders_AndThePreviewFromTheServer()
    {
        var cut = Render("key=Email.Outbid");

        cut.WaitForAssertion(() => cut.Find("#wording-subject").GetAttribute("value").Should().Be("You've been outbid on {StallionName}"));
        BodyValue(cut).Should().Be("Another buyer has bid {HighBid} on {StallionName}.");
        cut.Find(".wording-placeholders").TextContent.Should().Contain("{StallionName}").And.Contain("The new highest bid (inc. GST)")
            .And.Contain("Required").And.Contain("$12,500.00");
        cut.WaitForAssertion(() => cut.Find(".wording-preview").TextContent.Should()
            .Contain("You've been outbid on Snitzel").And.Contain("Another buyer has bid $12,500.00 on Snitzel."));
    }

    [Fact]
    public void Typing_RefreshesThePreview_AndShowsValidationErrorsInline()
    {
        var cut = Render("key=Email.Outbid");
        cut.WaitForAssertion(() => cut.Find("#wording-body"));
        _api.Setup(a => a.PreviewAsync("Email.Outbid", It.Is<SaveContentRequest>(r => r.Body.Contains("{Foo}"))))
            .ReturnsAsync(new ContentPreviewDto { Errors = ["{Foo} isn't available in this email.", "The message must include {HighBid}."] });

        cut.Find("#wording-body").Input("Bid {Foo} on {StallionName}");

        cut.WaitForAssertion(() => cut.FindAll(".wording-errors li").Select(li => li.TextContent).Should()
            .Equal("{Foo} isn't available in this email.", "The message must include {HighBid}."));
        _api.Verify(a => a.PreviewAsync("Email.Outbid", It.Is<SaveContentRequest>(r =>
            r.Body == "Bid {Foo} on {StallionName}" && r.Subject == "You've been outbid on {StallionName}")), Times.Once);
    }

    [Fact]
    public void Save_SendsTheSubjectAndMessage_AndShowsTheNewVersion()
    {
        var cut = Render("key=Email.Outbid");
        cut.WaitForAssertion(() => cut.Find("#wording-body"));
        _api.Setup(a => a.SaveAsync("Email.Outbid", It.IsAny<SaveContentRequest>())).ReturnsAsync(Outbid(2, "{HighBid} on {StallionName}."));
        _api.Setup(a => a.GetHistoryAsync("Email.Outbid")).ReturnsAsync(Versions(2));

        cut.Find("#wording-subject").Input("Outbid: {StallionName}");
        cut.Find("#wording-body").Input("{HighBid} on {StallionName}.");
        Click(cut, "Save as new version");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Saved as version 2."));
        _api.Verify(a => a.SaveAsync("Email.Outbid", It.Is<SaveContentRequest>(r =>
            r.Subject == "Outbid: {StallionName}" && r.Body == "{HighBid} on {StallionName}.")), Times.Once);
        cut.FindAll(".wording-versions tbody tr").Should().HaveCount(2);
    }

    [Fact]
    public void Save_AServerRejection_ShowsEachErrorOnItsOwnLine()
    {
        var cut = Render("key=Email.Outbid");
        cut.WaitForAssertion(() => cut.Find("#wording-body"));
        _api.Setup(a => a.SaveAsync("Email.Outbid", It.IsAny<SaveContentRequest>()))
            .ThrowsAsync(new ApiException(400, "The subject can't be empty.\nThe message must include {HighBid}."));

        Click(cut, "Save as new version");

        cut.WaitForAssertion(() => cut.FindAll(".wording-errors li").Select(li => li.TextContent).Should()
            .Equal("The subject can't be empty.", "The message must include {HighBid}."));
    }

    [Fact]
    public void Reset_AsksFirst_ThenResets()
    {
        var cut = Render("key=Email.Outbid", Outbid(2, "Edited {HighBid} {StallionName}."), Versions(2));
        cut.WaitForAssertion(() => cut.Find("#wording-body"));
        _api.Setup(a => a.ResetAsync("Email.Outbid")).ReturnsAsync(Outbid(3));

        Click(cut, "Reset to default");

        cut.Markup.Should().Contain("Replace the current wording with the default?");
        _api.Verify(a => a.ResetAsync(It.IsAny<string>()), Times.Never);
        Click(cut, "Yes, reset");
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Reset to the default wording as version 3."));
        _api.Verify(a => a.ResetAsync("Email.Outbid"), Times.Once);
    }

    [Fact]
    public void Restore_AsksFirst_ThenRestoresThatVersion()
    {
        var cut = Render("key=Email.Outbid", Outbid(2, "Edited {HighBid} {StallionName}."), Versions(2));
        cut.WaitForAssertion(() => cut.FindAll(".wording-versions tbody tr").Should().HaveCount(2));
        _api.Setup(a => a.RestoreAsync("Email.Outbid", 1)).ReturnsAsync(Outbid(3));

        Click(cut, "Restore");

        cut.Markup.Should().Contain("Restore version 1?");
        Click(cut, "Yes, restore");
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Version 1 restored as version 3."));
        _api.Verify(a => a.RestoreAsync("Email.Outbid", 1), Times.Once);
    }

    [Fact]
    public void AVersionLink_OpensThatVersion()
    {
        var cut = Render("key=Email.Outbid&version=1", Outbid(2, "Edited {HighBid} {StallionName}."), Versions(2));

        cut.WaitForAssertion(() => cut.Find(".wording-version-text").TextContent.Should().Contain("Default body {HighBid} {StallionName}"));
    }

    [Fact]
    public void AnyoneButStaff_IsSentAway()
    {
        var cut = Render(role: "Buyer");

        cut.WaitForAssertion(() => new Uri(Services.GetRequiredService<NavigationManager>().Uri).AbsolutePath.Should().Be("/"));
        _api.Verify(a => a.GetAllAsync(), Times.Never);
        cut.Markup.Should().NotContain("wording-group");
    }

    [Fact]
    public void ASlowEarlierPreview_DoesNotOverwriteALaterOne()
    {
        var cut = Render("key=Email.Outbid");
        cut.WaitForAssertion(() => cut.Find(".wording-preview").TextContent.Should().Contain("Snitzel"));
        var slow = new TaskCompletionSource<ContentPreviewDto>();
        _api.Setup(a => a.PreviewAsync("Email.Outbid", It.Is<SaveContentRequest>(r => r.Body == "first {HighBid} {StallionName}")))
            .Returns(slow.Task);
        _api.Setup(a => a.PreviewAsync("Email.Outbid", It.Is<SaveContentRequest>(r => r.Body == "second {HighBid} {StallionName}")))
            .ReturnsAsync(new ContentPreviewDto { Subject = "S", Paragraphs = ["Second result"] });

        cut.Find("#wording-body").Input("first {HighBid} {StallionName}");
        cut.Find("#wording-body").Input("second {HighBid} {StallionName}");
        cut.WaitForAssertion(() => cut.Find(".wording-preview").TextContent.Should().Contain("Second result"));

        slow.SetResult(new ContentPreviewDto { Subject = "S", Paragraphs = ["First result"] });

        cut.WaitForAssertion(() => cut.Find(".wording-preview").TextContent.Should().Contain("Second result").And.NotContain("First result"));
    }

    [Fact]
    public void ANetworkFailure_OnSave_ShowsAFriendlyError()
    {
        var cut = Render("key=Email.Outbid");
        cut.WaitForAssertion(() => cut.Find("#wording-body"));
        _api.Setup(a => a.SaveAsync("Email.Outbid", It.IsAny<SaveContentRequest>())).ThrowsAsync(new HttpRequestException("down"));

        Click(cut, "Save as new version");

        cut.WaitForAssertion(() => cut.FindAll(".wording-errors li").Select(li => li.TextContent).Should()
            .Equal("Couldn't save. Check your connection and try again."));
    }

    [Fact]
    public void ANetworkFailure_OnPreview_ShowsAFriendlyError()
    {
        var cut = Render("key=Email.Outbid");
        cut.WaitForAssertion(() => cut.Find("#wording-body"));
        _api.Setup(a => a.PreviewAsync("Email.Outbid", It.Is<SaveContentRequest>(r => r.Body == "x {HighBid} {StallionName}")))
            .ThrowsAsync(new HttpRequestException("down"));

        cut.Find("#wording-body").Input("x {HighBid} {StallionName}");

        cut.WaitForAssertion(() => cut.FindAll(".wording-errors li").Select(li => li.TextContent).Should()
            .Equal("Couldn't refresh the preview. Check your connection and try again."));
    }

    [Fact]
    public void ChangingOnlyTheVersionQuery_OpensThatVersion()
    {
        var cut = Render("key=Email.Outbid", Outbid(2, "Edited {HighBid} {StallionName}."), Versions(2));
        cut.WaitForAssertion(() => cut.FindAll(".wording-versions tbody tr").Should().HaveCount(2));
        cut.FindAll(".wording-version-text").Should().BeEmpty();

        Services.GetRequiredService<NavigationManager>().NavigateTo("/staff/wording?key=Email.Outbid&version=1");

        cut.WaitForAssertion(() => cut.Find(".wording-version-text").TextContent.Should().Contain("Default body"));
    }
}
