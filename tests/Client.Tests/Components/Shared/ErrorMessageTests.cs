using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Stallions.Client.Components.Shared;

namespace Stallions.Client.Tests.Components.Shared;

public class ErrorMessageTests : TestContext
{
    [Fact]
    public void ErrorMessage_NullMessage_RendersNothing()
    {
        var cut = RenderComponent<ErrorMessage>(p => p.Add(c => c.Message, (string?)null));

        cut.FindAll(".error-message").Should().BeEmpty();
    }

    [Fact]
    public void ErrorMessage_WithMessage_RendersAlert()
    {
        var cut = RenderComponent<ErrorMessage>(p => p.Add(c => c.Message, "Something went wrong"));

        cut.Find("[role=alert]").Should().NotBeNull();
        cut.Find("[role=alert]").TextContent.Should().Contain("Something went wrong");
    }

    [Fact]
    public void ErrorMessage_WithRetry_ShowsRetryButton()
    {
        var retried = false;
        var cut = RenderComponent<ErrorMessage>(p => p
            .Add(c => c.Message, "Failed to load")
            .Add(c => c.OnRetry, EventCallback.Factory.Create(this, () => retried = true)));

        cut.Find("button").Click();

        retried.Should().BeTrue();
    }

    [Fact]
    public void ErrorMessage_WithoutRetry_NoRetryButton()
    {
        var cut = RenderComponent<ErrorMessage>(p => p.Add(c => c.Message, "An error occurred"));

        cut.FindAll("button").Should().BeEmpty();
    }
}
