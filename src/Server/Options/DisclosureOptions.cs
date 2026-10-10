namespace Stallions.Server.Options;

/// <summary>
/// The configured buyer-fee wording shown before bidding and in win emails. Bound from the
/// existing "Checkout" section so no deployed setting has to change. Never hardcode this text.
/// </summary>
public class DisclosureOptions
{
    public const string Section = "Checkout";
    public string StudFarmBalanceArrangement { get; set; } = string.Empty;
    public string BuyerFeeExplanation { get; set; } = string.Empty;
    public string SavedCardExplanation { get; set; } = string.Empty;
}
