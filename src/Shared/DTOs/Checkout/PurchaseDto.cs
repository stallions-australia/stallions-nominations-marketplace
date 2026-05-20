namespace Stallions.Shared.DTOs.Checkout;

public class PurchaseDto
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public string StallionName { get; set; } = string.Empty;
    public Guid BuyerUserId { get; set; }
    public decimal TotalPriceIncGst { get; set; }
    public decimal PlatformFeeIncGst { get; set; }
    public decimal PlatformFeeExGst { get; set; }
    public decimal PlatformFeeGst { get; set; }
    public string MareName { get; set; } = string.Empty;
    public string? MareRegistration { get; set; }
    public string? MareBreed { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
