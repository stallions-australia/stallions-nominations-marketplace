namespace Stallions.Shared.DTOs.Subscriptions;

// Staff-only. Without FeeIncGstOverride the standard listing fee applies, with the best intro offer
// (a $0 result is waived at once). An override is used as-is and no intro offer is recorded.
public class CreateSubscriptionRequest
{
    public Guid StallionId { get; set; }
    public Guid SeasonId { get; set; }
    public decimal? FeeIncGstOverride { get; set; }
    public string? Notes { get; set; }
}
