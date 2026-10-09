namespace Stallions.Shared.DTOs.Checkout;

public class PurchaseDto
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public string StallionName { get; set; } = string.Empty;
    public Guid BuyerUserId { get; set; }
    public decimal TotalPriceIncGst { get; set; }
    public decimal BuyerFeeIncGst { get; set; }
    public decimal BuyerFeeExGst { get; set; }
    public decimal BuyerFeeGst { get; set; }
    public decimal BalancePayableToStudIncGst { get; set; }
    public string? PaymentProvider { get; set; }
    public string? PaymentReference { get; set; }
    public DateTime? PaidAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal? RefundAmount { get; set; }
    public DateTime? RefundedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
