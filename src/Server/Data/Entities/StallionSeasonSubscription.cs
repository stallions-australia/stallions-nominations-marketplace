using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

/// <summary>
/// A stud's right to list one stallion for one season, bought with the listing fee (or waived).
/// A listing cannot be published unless its stallion has a Paid or Waived subscription for the
/// listing's season. One row per (StallionId, SeasonId).
/// </summary>
public class StallionSeasonSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StallionId { get; set; }
    public Guid SeasonId { get; set; }
    public Guid StudFarmId { get; set; }

    // Listing fee charged, split for BAS. All zero when waived.
    public decimal FeeIncGst { get; set; }
    public decimal FeeExGst { get; set; }
    public decimal GstAmount { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Pending;
    public SubscriptionPaymentMethod? PaymentMethod { get; set; }
    public string? PaymentReference { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? WaiverReason { get; set; }
    public string? Notes { get; set; }
    // The hosted payment page currently open for this Pending subscription; reused while it has
    // at least 5 minutes left so a double-click or second tab can't create a second payable session.
    public string? PendingCheckoutUrl { get; set; }
    public DateTime? PendingCheckoutExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedByUserId { get; set; }

    // Navigation properties
    public Stallion Stallion { get; set; } = null!;
    public Season Season { get; set; } = null!;
    public StudFarm StudFarm { get; set; } = null!;
    public User CreatedBy { get; set; } = null!;
}
