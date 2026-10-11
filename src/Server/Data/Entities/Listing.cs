using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

public class Listing : IHasConcurrencyStamp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StallionId { get; set; }
    public Guid SeasonId { get; set; }
    public Guid StudFarmId { get; set; }
    public ListingType ListingType { get; set; }
    public ListingStatus Status { get; set; } = ListingStatus.Draft;
    /// <summary>
    /// Buyer fee (inc. GST) copied from PlatformSettings on first publish, so a later settings
    /// change never alters a live listing. Null until the listing is first published.
    /// </summary>
    public decimal? BuyerFeeIncGst { get; set; }
    public string? Description { get; set; }
    public string? TermsAndConditions { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    /// <summary>Why an auction closed without a sale. Set only when Status is Unsold.</summary>
    public ListingCloseReason? CloseReason { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    // Navigation properties
    public Stallion Stallion { get; set; } = null!;
    public Season Season { get; set; } = null!;
    public StudFarm StudFarm { get; set; } = null!;
    public ICollection<Purchase> Purchases { get; set; } = new List<Purchase>();
    public ICollection<Enquiry> Enquiries { get; set; } = new List<Enquiry>();
}
