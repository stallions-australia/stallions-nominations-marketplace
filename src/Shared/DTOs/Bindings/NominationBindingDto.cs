namespace Stallions.Shared.DTOs.Bindings;

public class NominationBindingDto
{
    public Guid Id { get; set; }
    public Guid PurchaseId { get; set; }
    public string StallionName { get; set; } = string.Empty;
    public string StudFarmName { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public string MareName { get; set; } = string.Empty;
    public string? MareRegistration { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? BuyerSignedAt { get; set; }
    public DateTime? FarmSignedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
