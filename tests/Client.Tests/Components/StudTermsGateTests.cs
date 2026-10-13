using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Components.Stud;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Terms;

namespace Stallions.Client.Tests.Components;

public class StudTermsGateTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };
    private readonly Mock<TermsApiService> _terms = new(MockBehavior.Loose, Http());
    private readonly List<string> _errors = [];
    private int _ran;

    private static TermsDocumentDto StudTerms() => new() { Id = Guid.NewGuid(), Audience = "Stud", Version = 2, Body = "Stud listing **terms**" };

    private IRenderedComponent<StudTermsGate> Render()
    {
        Services.AddSingleton(_terms.Object);
        return RenderComponent<StudTermsGate>(p => p.Add(c => c.OnError, (string m) => _errors.Add(m)));
    }

    private Task Require(IRenderedComponent<StudTermsGate> cut) =>
        cut.InvokeAsync(() => cut.Instance.RequireAsync(() => { _ran++; return Task.CompletedTask; }));

    private static void Click(IRenderedComponent<StudTermsGate> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == text).Click();

    [Fact]
    public async Task WhenNothingNeedsAccepting_ItRunsStraightAway()
    {
        _terms.Setup(t => t.GetStudStatusAsync()).ReturnsAsync(new StudTermsStatusDto { Current = StudTerms(), AcceptedVersion = 2 });
        var cut = Render();

        await Require(cut);

        _ran.Should().Be(1);
        cut.FindAll(".stud-terms-body").Should().BeEmpty();
    }

    [Fact]
    public async Task WhenTheStatusIsUnknown_ItRunsStraightAway()
    {
        var cut = Render(); // loose mock returns null

        await Require(cut);

        _ran.Should().Be(1);
    }

    [Fact]
    public async Task WhenAcceptanceIsNeeded_ItShowsTheTerms_AcceptsThem_ThenRuns()
    {
        _terms.Setup(t => t.GetStudStatusAsync()).ReturnsAsync(new StudTermsStatusDto { Current = StudTerms(), NeedsAcceptance = true });
        _terms.Setup(t => t.AcceptStudTermsAsync(2)).ReturnsAsync(new StudTermsStatusDto());
        var cut = Render();

        await Require(cut);

        _ran.Should().Be(0);
        cut.Find(".stud-terms-body").InnerHtml.Should().Contain("<strong>terms</strong>");
        cut.Find(".stud-terms-agree input").Change(true);
        Click(cut, "I accept");

        cut.WaitForAssertion(() => _ran.Should().Be(1));
        cut.FindAll(".stud-terms-body").Should().BeEmpty();
        _terms.Verify(t => t.AcceptStudTermsAsync(2), Times.Once);
    }

    [Fact]
    public async Task Cancelling_NeitherAcceptsNorRuns()
    {
        _terms.Setup(t => t.GetStudStatusAsync()).ReturnsAsync(new StudTermsStatusDto { Current = StudTerms(), NeedsAcceptance = true });
        var cut = Render();

        await Require(cut);
        Click(cut, "Cancel");

        cut.FindAll(".stud-terms-body").Should().BeEmpty();
        _ran.Should().Be(0);
        _terms.Verify(t => t.AcceptStudTermsAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task WhenAcceptingFails_TheErrorIsShownInTheModal_AndNothingRuns()
    {
        _terms.Setup(t => t.GetStudStatusAsync()).ReturnsAsync(new StudTermsStatusDto { Current = StudTerms(), NeedsAcceptance = true });
        _terms.Setup(t => t.AcceptStudTermsAsync(2)).ThrowsAsync(new ApiException(400, "Nope."));
        var cut = Render();

        await Require(cut);
        cut.Find(".stud-terms-agree input").Change(true);
        Click(cut, "I accept");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Nope."));
        cut.FindAll(".stud-terms-body").Should().ContainSingle();
        _ran.Should().Be(0);
    }

    [Fact]
    public async Task WhenTheStatusCannotBeLoaded_ItReportsTheError_AndDoesNotRun()
    {
        _terms.Setup(t => t.GetStudStatusAsync()).ThrowsAsync(new ApiException(500, "Boom."));
        var cut = Render();

        await Require(cut);

        _errors.Should().Equal("Boom.");
        _ran.Should().Be(0);
    }
}
