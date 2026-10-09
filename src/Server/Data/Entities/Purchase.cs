using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

public class Purchase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public Guid BuyerUserId { get; set; }
    public Guid? BidId { get; set; }
    public decimal TotalPriceIncGst { get; set; }
    // Flat buyer fee paid to Stallions Australia, split for BAS. It forms part of the price.
    public decimal BuyerFeeIncGst { get; set; }
    public decimal BuyerFeeExGst { get; set; }
    public decimal BuyerFeeGst { get; set; }
    // What the buyer pays the stud directly: TotalPriceIncGst − BuyerFeeIncGst.
    public decimal BalancePayableToStudIncGst { get; set; }
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
