namespace Stallions.Shared.DTOs.Checkout;

// Staff-only. Records a buyer fee refund already made in the payment provider's dashboard.
public class RefundBuyerFeeRequest
{
    /// <summary>Optional, up to 500 characters; printed on the adjustment note after "Buyer fee refunded — ".</summary>
    public string? Note { get; set; }
}
