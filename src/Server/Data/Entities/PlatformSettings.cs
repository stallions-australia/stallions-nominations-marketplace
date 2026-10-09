namespace Stallions.Server.Data.Entities;

/// <summary>
/// Staff-managed platform settings. Exactly one row, with Id = <see cref="SingletonId"/>.
/// The default values are seeded in AppDbContext (HasData) — that seed is the only place
/// default amounts appear in code.
/// </summary>
public class PlatformSettings
{
    public static readonly Guid SingletonId = new("5e771265-0000-4000-8000-000000000001");

    public Guid Id { get; set; } = SingletonId;
    public decimal BuyerFeeIncGst { get; set; }
    public decimal StandardListingFeeIncGst { get; set; }
    public decimal MinimumBidIncrement { get; set; }
    public int ChargeGracePeriodHours { get; set; }
    public int OfferExpiryDays { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    // Navigation properties
    public User? UpdatedBy { get; set; }
}
