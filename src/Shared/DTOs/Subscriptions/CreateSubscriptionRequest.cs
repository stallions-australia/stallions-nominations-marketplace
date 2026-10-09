namespace Stallions.Shared.DTOs.Subscriptions;

// Staff-only. The fee defaults to the standard listing fee in platform settings;
// FeeIncGstOverride applies an intro offer / discount manually.
public class CreateSubscriptionRequest
{
    public Guid StallionId { get; set; }
    public Guid SeasonId { get; set; }
    public decimal? FeeIncGstOverride { get; set; }
    public string? Notes { get; set; }
}
