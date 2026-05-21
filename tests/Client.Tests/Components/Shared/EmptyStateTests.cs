using Bunit;
using FluentAssertions;
using Stallions.Client.Components.Shared;

namespace Stallions.Client.Tests.Components.Shared;

public class EmptyStateTests : TestContext
{
    [Fact]
    public void EmptyState_RendersMessageAndIcon()
    {
        var cut = RenderComponent<EmptyState>(p => p
            .Add(c => c.Message, "No listings found")
            .Add(c => c.Icon, "🔍"));

        cut.Find(".empty-state-message").TextContent.Should().Be("No listings found");
        cut.Find(".empty-state-icon").TextContent.Trim().Should().Be("🔍");
    }
}
