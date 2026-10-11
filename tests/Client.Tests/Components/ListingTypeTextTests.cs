using FluentAssertions;
using Stallions.Client.Components;

namespace Stallions.Client.Tests.Components;

public class ListingTypeTextTests
{
    [Theory]
    [InlineData("Auction", "Auction")]
    [InlineData("MakeAnOffer", "Make an Offer")]
    public void Label(string type, string expected) => ListingTypeText.Label(type).Should().Be(expected);
}
