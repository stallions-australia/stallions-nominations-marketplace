using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

public class Listing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StallionId { get; set; }
    public Guid SeasonId { get; set; }
    public Guid StudFarmId { get; set; }
    public ListingType ListingType { get; set; }
    public ListingStatus Status { get; set; } = ListingStatus.Draft;
    public decimal? PlatformFeePercent { get; set; }
    public string? Description { get; set; }
    public string? TermsAndConditions { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    // Navigation properties
    public Stallion Stallion { get; set; } = null!;
    public Season Season { get; set; } = null!;
    public StudFarm StudFarm { get; set; } = null!;
    public ICollection<Purchase> Purchases { get; set; } = new List<Purchase>();
    public ICollection<Enquiry> Enquiries { get; set; } = new List<Enquiry>();
}
