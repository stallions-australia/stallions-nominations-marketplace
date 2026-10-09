namespace Stallions.Shared.DTOs.Settings;

// Staff-only (PUT api/settings). All values must be greater than zero.
public class UpdatePlatformSettingsRequest
{
    public decimal BuyerFeeIncGst { get; set; }
    public decimal StandardListingFeeIncGst { get; set; }
    public decimal MinimumBidIncrement { get; set; }
    public int ChargeGracePeriodHours { get; set; }
    public int OfferExpiryDays { get; set; }
}
