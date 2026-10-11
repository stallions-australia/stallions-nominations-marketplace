using FluentAssertions;
using Stallions.Client.Components;

namespace Stallions.Client.Tests.Components;

public class ListingStatusTextTests
{
    [Theory]
    [InlineData("AwaitingPayment", null, "Awaiting payment")]
    [InlineData("Unsold", "NoBids", "Unsold — no bids")]
    [InlineData("Unsold", "ReserveNotMet", "Unsold — reserve not met")]
    [InlineData("Unsold", "ChargeFailed", "Unsold — payment failed")]
    [InlineData("Active", null, "Active")]
    public void Label(string status, string? reason, string expected) =>
        ListingStatusText.Label(status, reason).Should().Be(expected);

    [Theory]
    [InlineData("Sold", true)]
    [InlineData("Cancelled", true)]
    [InlineData("AwaitingPayment", true)]
    [InlineData("Unsold", true)]
    [InlineData("Active", false)]
    [InlineData("Draft", false)]
    public void IsFinished(string status, bool expected) =>
        ListingStatusText.IsFinished(status).Should().Be(expected);
}
