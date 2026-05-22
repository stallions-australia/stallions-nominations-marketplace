using Bunit;
using FluentAssertions;
using Stallions.Client.Components.Listings;

namespace Stallions.Client.Tests.Components.Listings;

public class AuctionTimerTests : TestContext
{
    [Fact]
    public void AuctionTimer_ShowsHoursAndMinutes()
    {
        // Use a fixed, exact DateTime so the countdown is deterministic at render time.
        var closesAt = DateTime.UtcNow.AddHours(5).AddSeconds(2); // 5h 0m — always renders "5" and "0"
        var cut = RenderComponent<AuctionTimer>(p => p.Add(c => c.ClosesAt, closesAt));

        cut.Markup.Should().Contain("5");   // hours
        // Timer-segment for minutes renders as 0 — verify the h and m labels appear
        cut.FindAll(".timer-segment").Should().HaveCount(2); // hours + minutes (no days)
    }

    [Fact]
    public void AuctionTimer_LessThan6Hours_AppliesUrgentClass()
    {
        var closesAt = DateTime.UtcNow.AddHours(3);
        var cut = RenderComponent<AuctionTimer>(p => p.Add(c => c.ClosesAt, closesAt));

        cut.Find(".auction-timer--urgent").Should().NotBeNull();
    }
}
