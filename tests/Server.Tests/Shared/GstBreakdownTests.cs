using FluentAssertions;
using Stallions.Shared;

namespace Stallions.Server.Tests.Shared;

public class GstBreakdownTests
{
    [Theory]
    [InlineData("150", "136.36", "13.64")]
    [InlineData("990", "900.00", "90.00")]
    [InlineData("250", "227.27", "22.73")]
    [InlineData("0", "0", "0")]
    public void FromIncGst_SplitsOneEleventhAsGst(string inc, string exGst, string gst)
    {
        var result = GstBreakdown.FromIncGst(decimal.Parse(inc));

        result.IncGst.Should().Be(decimal.Parse(inc));
        result.ExGst.Should().Be(decimal.Parse(exGst));
        result.Gst.Should().Be(decimal.Parse(gst));
    }

    [Fact]
    public void FromIncGst_ExGstPlusGstAlwaysEqualsIncGst()
    {
        for (var cents = 1; cents < 100_000; cents += 7)
        {
            var inc = cents / 100m;
            var result = GstBreakdown.FromIncGst(inc);
            (result.ExGst + result.Gst).Should().Be(inc);
        }
    }
}
