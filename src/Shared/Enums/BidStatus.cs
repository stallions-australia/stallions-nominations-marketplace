namespace Stallions.Shared.Enums;

public enum BidStatus
{
    Active,
    Outbid,
    Won,
    SecondChance,
    Declined,
    Expired,
    /// <summary>Another bid won the auction (set when the auction closes).</summary>
    Lost
}
