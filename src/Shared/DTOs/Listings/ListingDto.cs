namespace Stallions.Shared.DTOs.Listings;

public class ListingDto
{
    public Guid Id { get; set; }
    public Guid StallionId { get; set; }
    public string StallionName { get; set; } = string.Empty;
    public Guid SeasonId { get; set; }
    public string SeasonName { get; set; } = string.Empty;
    public Guid StudFarmId { get; set; }
    public string StudFarmName { get; set; } = string.Empty;
    public string ListingType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal? PlatformFeePercent { get; set; }  // null when caller is not Staff
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}
