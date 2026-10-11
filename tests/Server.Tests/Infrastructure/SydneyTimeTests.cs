using FluentAssertions;
using Stallions.Server.Infrastructure;

namespace Stallions.Server.Tests.Infrastructure;

public class SydneyTimeTests
{
    [Fact]
    public void EndOfDay_IsTheLastSecondOfThatDayInSydney_InUtc() =>
        // 11 Oct 2026 is daylight time (UTC+11): 23:59:59 Sydney = 12:59:59 UTC.
        SydneyTime.EndOfDayUtc(new DateOnly(2026, 10, 11))
            .Should().Be(new DateTime(2026, 10, 11, 12, 59, 59, DateTimeKind.Utc));

    [Fact]
    public void Today_FollowsTheSydneyCalendar() =>
        SydneyTime.Today(new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc))
            .Should().Be(new DateOnly(2026, 10, 11));

    [Fact]
    public void StartOfDay_IsMidnightInSydney_InUtc() =>
        // 11 Oct 2026 is daylight time (UTC+11): 00:00 Sydney = 13:00 UTC the day before.
        SydneyTime.StartOfDayUtc(new DateOnly(2026, 10, 11))
            .Should().Be(new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc));
}
