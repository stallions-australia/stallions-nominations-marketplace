namespace Stallions.Shared.DTOs.Admin;

public class ListingStaffSummaryDto
{
    public Guid Id { get; set; }
    public string StallionName { get; set; } = string.Empty;
    public string StudFarmName { get; set; } = string.Empty;
    public string ListingType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal? HighestBidIncGst { get; set; }  // null when no bids
    public decimal? ReservePrice { get; set; }      // Staff-only view; null when no reserve
    public decimal? BuyerFeeIncGst { get; set; }  // null until first published
    public DateTime? PublishedAt { get; set; }
    /// <summary>Why the auction closed without a sale (NoBids, ReserveNotMet, ChargeFailed); null otherwise.</summary>
    public string? CloseReason { get; set; }
}
