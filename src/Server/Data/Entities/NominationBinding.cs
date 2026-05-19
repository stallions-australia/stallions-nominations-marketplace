using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

public class NominationBinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PurchaseId { get; set; }
    public BindingStatus Status { get; set; } = BindingStatus.PendingAcknowledgement;
    public string? PdfBlobPath { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public Guid? AcknowledgedByUserId { get; set; }
    public DateTime? BuyerSignedAt { get; set; }
    public DateTime? FarmSignedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Navigation properties
    public Purchase Purchase { get; set; } = null!;
    public User? AcknowledgedBy { get; set; }
}
