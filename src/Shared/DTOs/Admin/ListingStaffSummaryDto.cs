namespace Stallions.Shared.DTOs.Admin;

public class ListingStaffSummaryDto
{
    public Guid Id { get; set; }
    public string StallionName { get; set; } = string.Empty;
    public string StudFarmName { get; set; } = string.Empty;
    public string ListingType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal? PriceIncGst { get; set; }
    public decimal? BuyerFeeIncGst { get; set; }  // null until first published
    public DateTime? PublishedAt { get; set; }
}
