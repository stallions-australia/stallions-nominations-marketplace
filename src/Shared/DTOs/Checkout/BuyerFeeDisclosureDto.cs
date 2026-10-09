namespace Stallions.Shared.DTOs.Checkout;

/// <summary>
/// Mandatory buyer-fee disclosure wording, served from configuration (never hardcoded in the
/// client) so Stallions Australia can change it without a release.
/// </summary>
public class BuyerFeeDisclosureDto
{
    public string BuyerFeeExplanation { get; set; } = string.Empty;
    public string BalanceArrangement { get; set; } = string.Empty;
    /// <summary>What the buyer's saved card is (and isn't) charged for — shown on the card page.</summary>
    public string SavedCardExplanation { get; set; } = string.Empty;
}
