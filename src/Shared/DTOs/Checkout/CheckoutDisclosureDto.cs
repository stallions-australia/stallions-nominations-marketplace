namespace Stallions.Shared.DTOs.Checkout;

public class CheckoutDisclosureDto
{
    public decimal TotalPriceIncGst { get; set; }
    public decimal PlatformFeeIncGst { get; set; }
    public string StudFarmBalanceArrangement { get; set; } = string.Empty;
    public string RefundPolicy { get; set; } = string.Empty;
}
