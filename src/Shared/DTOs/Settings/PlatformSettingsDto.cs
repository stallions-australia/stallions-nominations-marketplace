namespace Stallions.Shared.DTOs.Settings;

public class PlatformSettingsDto
{
    public decimal BuyerFeeIncGst { get; set; }
    public decimal StandardListingFeeIncGst { get; set; }
    public decimal MinimumBidIncrement { get; set; }
    public int ChargeGracePeriodHours { get; set; }
    public int OfferExpiryDays { get; set; }
    public DateTime UpdatedAt { get; set; }
}
