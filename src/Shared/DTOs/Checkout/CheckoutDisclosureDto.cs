namespace Stallions.Shared.DTOs.Checkout;

public class CheckoutDisclosureDto
{
    public decimal TotalPriceIncGst { get; set; }
    public decimal BuyerFeeIncGst { get; set; }
    public decimal BalancePayableToStudIncGst { get; set; }
    public string StudFarmBalanceArrangement { get; set; } = string.Empty;
}
