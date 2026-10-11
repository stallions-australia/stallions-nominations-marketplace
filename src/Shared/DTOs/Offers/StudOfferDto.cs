namespace Stallions.Shared.DTOs.Offers;

/// <summary>The stud's (or Staff's) view of an offer on their listing.</summary>
public class StudOfferDto
{
    public Guid Id { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public string BuyerEmail { get; set; } = string.Empty;
    public decimal AmountIncGst { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public Guid? PurchaseId { get; set; }
    public string? SaleStatus { get; set; }
    public DateTime? ChargeDueBy { get; set; }
}
