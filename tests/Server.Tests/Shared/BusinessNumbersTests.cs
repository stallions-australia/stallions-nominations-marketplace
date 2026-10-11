using FluentAssertions;
using Stallions.Shared;

namespace Stallions.Server.Tests.Shared;

public class BusinessNumbersTests
{
    [Theory]
    [InlineData(" 12 345 678 901 ", "12345678901")]
    [InlineData("12-345-678-901", "12345678901")]
    [InlineData("062-000", "062000")]
    [InlineData(null, "")]
    public void Compact_RemovesSpacesAndHyphens(string? input, string expected) =>
        BusinessNumbers.Compact(input).Should().Be(expected);

    [Theory]
    [InlineData("12345678901", true)]
    [InlineData("1234567890", false)]
    [InlineData("123456789012", false)]
    [InlineData("1234567890A", false)]
    [InlineData(null, false)]
    public void IsAbn_NeedsExactlyElevenDigits(string? abn, bool expected) =>
        BusinessNumbers.IsAbn(abn).Should().Be(expected);

    [Theory]
    [InlineData("062000", true)]
    [InlineData("06200", false)]
    [InlineData("06A000", false)]
    public void IsBsb_NeedsExactlySixDigits(string bsb, bool expected) =>
        BusinessNumbers.IsBsb(bsb).Should().Be(expected);

    [Fact]
    public void Format_GroupsTheDigits()
    {
        BusinessNumbers.FormatAbn("12345678901").Should().Be("12 345 678 901");
        BusinessNumbers.FormatAbn("12 345 678 901").Should().Be("12 345 678 901");
        BusinessNumbers.FormatBsb("062000").Should().Be("062-000");
    }

    [Fact]
    public void Format_LeavesAnythingElseAsItIs()
    {
        BusinessNumbers.FormatAbn("not an abn").Should().Be("not an abn");
        BusinessNumbers.FormatAbn(null).Should().Be("");
        BusinessNumbers.FormatBsb("12").Should().Be("12");
    }
}
