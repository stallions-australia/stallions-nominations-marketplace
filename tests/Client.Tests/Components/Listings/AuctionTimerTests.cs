using Bunit;
using FluentAssertions;
using Stallions.Client.Components.Listings;

namespace Stallions.Client.Tests.Components.Listings;

public class AuctionTimerTests : TestContext
{
    [Fact]
    public void AuctionTimer_ShowsHoursAndMinutes()
    {
        var closesAt = DateTime.UtcNow.AddHours(5).AddMinutes(30).AddSeconds(30);
        var cut = RenderComponent<AuctionTimer>(p => p.Add(c => c.ClosesAt, closesAt));

        var segments = cut.FindAll(".timer-segment");
        // When < 1 day: two segments (hours + minutes)
        segments.Should().HaveCount(2);
        segments[0].TextContent.Should().Contain("5");   // hours
        segments[1].TextContent.Should().Contain("30");  // minutes
    }

    [Fact]
    public void AuctionTimer_LessThan6Hours_AppliesUrgentClass()
    {
        var closesAt = DateTime.UtcNow.AddHours(3);
        var cut = RenderComponent<AuctionTimer>(p => p.Add(c => c.ClosesAt, closesAt));

        cut.Find(".auction-timer--urgent").Should().NotBeNull();
    }
}
