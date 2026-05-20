namespace Stallions.Shared.DTOs.Admin;

public class InvoiceLineDto
{
    public Guid PurchaseId { get; set; }
    public string StallionName { get; set; } = string.Empty;
    public decimal SalePriceIncGst { get; set; }
    public decimal PlatformFeeIncGst { get; set; }
    public decimal RemittanceAmount { get; set; }
    public DateTime PaidAt { get; set; }
}
