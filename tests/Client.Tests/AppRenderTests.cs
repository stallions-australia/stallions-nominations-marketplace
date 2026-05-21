using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stallions.Client;

namespace Stallions.Client.Tests;

public class AppRenderTests : TestContext
{
    [Fact]
    public void App_RendersWithoutThrowing()
    {
        // Add fake auth so AuthorizeRouteView doesn't throw
        this.AddTestAuthorization();

        // Act — will throw if component graph is broken
        var act = () => RenderComponent<App>();

        act.Should().NotThrow();
    }
}
