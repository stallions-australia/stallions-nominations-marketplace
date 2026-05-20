namespace Stallions.Shared.DTOs.Bindings;

public class NominationBindingDto
{
    public Guid Id { get; set; }
    public Guid PurchaseId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PdfBlobPath { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? BuyerSignedAt { get; set; }
    public DateTime? FarmSignedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
