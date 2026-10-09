namespace Stallions.Shared.DTOs.Subscriptions;

// Staff-only. PaymentMethod: Card | Invoice | BankTransfer (use the waive endpoint for waivers).
public class MarkSubscriptionPaidRequest
{
    public string PaymentMethod { get; set; } = string.Empty;
    public string? PaymentReference { get; set; }
}
