using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

public class Purchase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public Guid BuyerUserId { get; set; }
    public Guid? BidId { get; set; }
    public decimal TotalPriceIncGst { get; set; }
    public decimal PlatformFeeIncGst { get; set; }
    public decimal PlatformFeeExGst { get; set; }
    public decimal PlatformFeeGst { get; set; }
    public string MareName { get; set; } = string.Empty;
    public string? MareRegistration { get; set; }
    public string? MareBreed { get; set; }
    public string? PaymentProvider { get; set; }
    public string? PaymentReference { get; set; }
    public DateTime? PaidAt { get; set; }
    public PurchaseStatus Status { get; set; } = PurchaseStatus.Pending;
    public decimal? RefundAmount { get; set; }
    public DateTime? RefundedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Listing Listing { get; set; } = null!;
    public User Buyer { get; set; } = null!;
    public Bid? Bid { get; set; }
    public NominationBinding? NominationBinding { get; set; }
}
