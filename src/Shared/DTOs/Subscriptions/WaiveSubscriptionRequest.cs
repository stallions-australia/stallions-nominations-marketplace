namespace Stallions.Shared.DTOs.Subscriptions;

// Staff-only. A reason is required and is kept on the subscription for the record.
public class WaiveSubscriptionRequest
{
    public string Reason { get; set; } = string.Empty;
}
