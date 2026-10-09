namespace Stallions.Server.Payments;

public static class PaymentAmounts
{
    /// <summary>Converts a dollar amount to cents, rounding half away from zero.</summary>
    public static long ToCents(decimal amount) =>
        (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}
