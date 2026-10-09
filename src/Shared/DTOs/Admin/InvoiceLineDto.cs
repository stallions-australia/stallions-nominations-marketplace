namespace Stallions.Shared.DTOs.Admin;

public class InvoiceLineDto
{
    public Guid PurchaseId { get; set; }
    public string StallionName { get; set; } = string.Empty;
    public decimal SalePriceIncGst { get; set; }
    public decimal BuyerFeeIncGst { get; set; }
    public decimal BalancePayableToStudIncGst { get; set; }
    public DateTime PaidAt { get; set; }
}
