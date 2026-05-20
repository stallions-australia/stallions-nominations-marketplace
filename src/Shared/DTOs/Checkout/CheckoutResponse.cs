namespace Stallions.Shared.DTOs.Checkout;

public class CheckoutResponse
{
    public Guid PurchaseId { get; set; }
    public CheckoutDisclosureDto Disclosure { get; set; } = new();
}
