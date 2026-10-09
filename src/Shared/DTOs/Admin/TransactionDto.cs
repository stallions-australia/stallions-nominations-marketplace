namespace Stallions.Shared.DTOs.Admin;

public class TransactionDto
{
    public Guid PurchaseId { get; set; }
    public string StallionName { get; set; } = string.Empty;
    public string BuyerDisplayName { get; set; } = string.Empty;
    public string StudFarmName { get; set; } = string.Empty;
    public decimal TotalPriceIncGst { get; set; }
    public decimal BuyerFeeIncGst { get; set; }
    public decimal BuyerFeeExGst { get; set; }
    public decimal BuyerFeeGst { get; set; }
    public decimal BalancePayableToStudIncGst { get; set; }
    public DateTime? PaidAt { get; set; }
    public string Status { get; set; } = string.Empty;
}
