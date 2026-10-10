using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

public class Purchase : IHasConcurrencyStamp
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

    // ── Automatic buyer-fee charge (Phase 3). The purchase is the sale record. ──
    /// <summary>Charge attempts made so far; also numbers the idempotency key of each attempt.</summary>
    public int ChargeAttempts { get; set; }
    /// <summary>Set while an attempt is in flight. Older than 2 minutes = interrupted, repeated with the same key.</summary>
    public DateTime? ChargeAttemptStartedAt { get; set; }
    /// <summary>End of the grace period, set at the first failed charge.</summary>
    public DateTime? ChargeDueBy { get; set; }
    public string? LastChargeFailure { get; set; }
    /// <summary>The winner saved a new card after a failed charge; the next run retries.</summary>
    public bool RetryRequested { get; set; }
    /// <summary>Snapshot of the card used for the current/last attempt, so a repeat sends the identical request.</summary>
    public string? ChargeCustomerId { get; set; }
    public string? ChargePaymentMethodId { get; set; }
    public string? ChargeDescription { get; set; }
    /// <summary>An attempt has been stuck for too long; Staff must check the payment provider before any further charge.</summary>
    public bool ChargeNeedsAttention { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    // Navigation properties
    public Listing Listing { get; set; } = null!;
    public User Buyer { get; set; } = null!;
    public Bid? Bid { get; set; }
    public NominationBinding? NominationBinding { get; set; }
}
