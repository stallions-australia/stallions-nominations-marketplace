namespace Stallions.Shared.DTOs.Checkout;

/// <summary>A sale record: the price, the buyer fee (three GST values) and the balance payable to the stud.</summary>
public class PurchaseDto
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public string StallionName { get; set; } = string.Empty;
    public string SeasonName { get; set; } = string.Empty;
    public string StudFarmName { get; set; } = string.Empty;
    public Guid BuyerUserId { get; set; }
    public decimal TotalPriceIncGst { get; set; }
    public decimal BuyerFeeIncGst { get; set; }
    public decimal BuyerFeeExGst { get; set; }
    public decimal BuyerFeeGst { get; set; }
    public decimal BalancePayableToStudIncGst { get; set; }
    public string? PaymentProvider { get; set; }
    public string? PaymentReference { get; set; }
    public DateTime? PaidAt { get; set; }
    /// <summary>Pending (charging), Completed (paid), Voided (no sale), Refunded.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Set after a failed charge: the winner must update their card by this time (UTC).</summary>
    public DateTime? ChargeDueBy { get; set; }
    public string? LastChargeFailure { get; set; }
    public decimal? RefundAmount { get; set; }
    public DateTime? RefundedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
