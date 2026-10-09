using FluentAssertions;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Tests.Data.Entities;

public class SavedCardTests
{
    private static SavedCard Card(int month, int year) => new() { ExpMonth = month, ExpYear = year };

    [Theory]
    [InlineData(2026, 10, 9, true)]   // well before expiry
    [InlineData(2026, 10, 31, true)]  // last day of the expiry month
    [InlineData(2026, 11, 1, false)]  // first day after
    public void IsValidOn_IsTrueUntilTheEndOfTheExpiryMonth(int y, int m, int d, bool expected) =>
        Card(10, 2026).IsValidOn(new DateOnly(y, m, d)).Should().Be(expected);

    [Theory]
    [InlineData(0, 2028)]
    [InlineData(13, 2028)]
    [InlineData(8, 0)]
    public void IsValidOn_IsFalseForOutOfRangeExpiry(int month, int year) =>
        Card(month, year).IsValidOn(new DateOnly(2026, 10, 9)).Should().BeFalse();

    [Fact]
    public void IsValidOn_HandlesFebruaryInALeapYear() =>
        Card(2, 2028).IsValidOn(new DateOnly(2028, 2, 29)).Should().BeTrue();
}
