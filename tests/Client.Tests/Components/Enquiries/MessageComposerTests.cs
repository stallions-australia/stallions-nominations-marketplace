using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Stallions.Client.Components.Enquiries;

namespace Stallions.Client.Tests.Components.Enquiries;

public class MessageComposerTests : TestContext
{
    [Fact]
    public void MessageComposer_Send_InvokesCallback()
    {
        string? sentText = null;
        var cut = RenderComponent<MessageComposer>(p => p
            .Add(c => c.OnSend, EventCallback.Factory.Create<string>(this, text => sentText = text)));

        cut.Find("textarea").Input("Hello from buyer!");
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() => sentText.Should().Be("Hello from buyer!"));
    }

    [Fact]
    public void MessageComposer_SendButton_DisabledWhenEmpty()
    {
        var cut = RenderComponent<MessageComposer>(p => p
            .Add(c => c.OnSend, EventCallback.Factory.Create<string>(this, _ => { })));

        cut.Find("button[type='submit']").HasAttribute("disabled").Should().BeTrue();
    }
}
