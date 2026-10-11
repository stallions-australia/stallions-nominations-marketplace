namespace Stallions.Shared.DTOs.Offers;

/// <summary>What happened when the stud accepted an offer.</summary>
public class AcceptOfferResultDto
{
    public Guid OfferId { get; set; }
    public Guid PurchaseId { get; set; }
    /// <summary>"Paid", "PaymentFailed", "NoSale", "Skipped" or "Processing" (provider unreachable; retried shortly).</summary>
    public string Charge { get; set; } = string.Empty;
    public string SaleStatus { get; set; } = string.Empty;
    public DateTime? ChargeDueBy { get; set; }
    public string? LastChargeFailure { get; set; }
}
