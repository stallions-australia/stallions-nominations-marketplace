using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Stallions.Client.Components.Shared;

namespace Stallions.Client.Tests.Components.Shared;

public class ModalTests : TestContext
{
    [Fact]
    public void Modal_WhenIsOpen_RendersChildContent()
    {
        var cut = RenderComponent<Modal>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Title, "Test Modal")
            .Add(c => c.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<p class='modal-test-content'>Hello</p>"))));

        cut.Find(".modal-test-content").Should().NotBeNull();
        cut.Find(".modal-title").TextContent.Should().Be("Test Modal");
    }

    [Fact]
    public void Modal_WhenClosed_DoesNotRenderContent()
    {
        var cut = RenderComponent<Modal>(p => p
            .Add(c => c.IsOpen, false)
            .Add(c => c.Title, "Test")
            .Add(c => c.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<p class='modal-test-content'>Hidden</p>"))));

        cut.FindAll(".modal-test-content").Should().BeEmpty();
    }

    [Fact]
    public void Modal_CloseButton_InvokesCallback()
    {
        var closed = false;
        var cut = RenderComponent<Modal>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Title, "Test")
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closed = true))
            .Add(c => c.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<p>content</p>"))));

        cut.Find("button.modal-close").Click();

        closed.Should().BeTrue();
    }
}
