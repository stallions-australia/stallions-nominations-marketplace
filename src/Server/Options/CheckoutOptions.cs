namespace Stallions.Server.Options;

public class CheckoutOptions
{
    public string WebhookSecret { get; set; } = string.Empty;
    public string StudFarmBalanceArrangement { get; set; } = string.Empty;
    public string BuyerFeeExplanation { get; set; } = string.Empty;
    public string SavedCardExplanation { get; set; } = string.Empty;
}
