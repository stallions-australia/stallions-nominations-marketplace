namespace Stallions.Shared;

/// <summary>How a closed auction turned out for the signed-in buyer (MyAuctionResultDto.Outcome).</summary>
public static class AuctionOutcomes
{
    public const string None = "None";
    public const string Won = "Won";
    public const string PaymentPending = "PaymentPending";
    public const string PaymentFailed = "PaymentFailed";
    public const string NoSale = "NoSale";
    public const string Lost = "Lost";
    public const string EndedWithoutSale = "EndedWithoutSale";
}
