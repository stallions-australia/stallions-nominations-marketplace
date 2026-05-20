namespace Stallions.Shared.DTOs.Admin;

public class TransactionDto
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public Guid BuyerUserId { get; set; }
    public decimal TotalPriceIncGst { get; set; }
    public decimal PlatformFeeIncGst { get; set; }
    public decimal PlatformFeeExGst { get; set; }
    public decimal PlatformFeeGst { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? CompletedAt { get; set; }
}
