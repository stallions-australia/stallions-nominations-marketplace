namespace Stallions.Shared.DTOs.Admin;

public class InvoiceDto
{
    public Guid StudFarmId { get; set; }
    public string StudFarmName { get; set; } = string.Empty;
    public List<InvoiceLineDto> Lines { get; set; } = new();
    public decimal TotalSalesIncGst { get; set; }
    public decimal TotalPlatformFeesIncGst { get; set; }
    public decimal TotalRemittance { get; set; }
}
