namespace Stallions.Shared.DTOs.Admin;

public class InvoiceSummaryDto
{
    public Guid PurchaseId { get; set; }
    public Guid ListingId { get; set; }
    public decimal PlatformFeeIncGst { get; set; }
    public decimal StudFarmRemittance { get; set; }
    public DateTime? CompletedAt { get; set; }
}
