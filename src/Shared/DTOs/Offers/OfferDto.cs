namespace Stallions.Shared.DTOs.Offers;

/// <summary>A buyer's view of their own offer.</summary>
public class OfferDto
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public string StallionName { get; set; } = string.Empty;
    public string SeasonName { get; set; } = string.Empty;
    public string StudFarmName { get; set; } = string.Empty;
    public decimal AmountIncGst { get; set; }
    /// <summary>Pending, Accepted, Declined, Withdrawn, Expired.</summary>
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    /// <summary>Accepted offers: the sale record and its state (Pending, Completed, Voided, Refunded).</summary>
    public Guid? PurchaseId { get; set; }
    public string? SaleStatus { get; set; }
    public DateTime? ChargeDueBy { get; set; }
    /// <summary>The listing is still taking offers.</summary>
    public bool ListingOpen { get; set; }
}
