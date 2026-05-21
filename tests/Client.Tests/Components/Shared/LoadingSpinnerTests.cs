using Bunit;
using FluentAssertions;
using Stallions.Client.Components.Shared;

namespace Stallions.Client.Tests.Components.Shared;

public class LoadingSpinnerTests : TestContext
{
    [Fact]
    public void LoadingSpinner_Default_RendersSpinnerRing()
    {
        var cut = RenderComponent<LoadingSpinner>();

        cut.Find(".spinner-ring").Should().NotBeNull();
    }

    [Fact]
    public void LoadingSpinner_Large_AppliesLargeClass()
    {
        var cut = RenderComponent<LoadingSpinner>(p => p.Add(c => c.Large, true));

        cut.Find(".spinner--large").Should().NotBeNull();
    }

    [Fact]
    public void LoadingSpinner_NotLarge_DoesNotApplyLargeClass()
    {
        var cut = RenderComponent<LoadingSpinner>(p => p.Add(c => c.Large, false));

        cut.FindAll(".spinner--large").Should().BeEmpty();
    }
}
