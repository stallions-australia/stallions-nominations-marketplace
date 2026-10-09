using FluentAssertions;
using Stallions.Server.Payments;

namespace Stallions.Server.Tests.Payments;

public class PaymentAmountsTests
{
    [Theory]
    [InlineData(990.00, 99000)]
    [InlineData(136.365, 13637)]
    [InlineData(0.005, 1)]
    public void ToCents_RoundsHalfAwayFromZero(double amount, long cents) =>
        PaymentAmounts.ToCents((decimal)amount).Should().Be(cents);
}
