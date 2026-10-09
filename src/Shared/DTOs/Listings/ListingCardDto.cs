namespace Stallions.Shared.DTOs.Listings;

public class ListingCardDto
{
    public Guid Id { get; set; }
    public string ListingType { get; set; } = string.Empty; // "Auction"
    public Guid StallionId { get; set; }
    public string StallionName { get; set; } = string.Empty;
    public string? PrimaryImagePath { get; set; }
    public Guid StudFarmId { get; set; }
    public string StudFarmName { get; set; } = string.Empty;
    public string? SeasonName { get; set; }

    // Auction-specific. No starting or asking price is ever shown — only the current high bid.
    public decimal? CurrentHighestBidIncGst { get; set; }
    public int? BidCount { get; set; }
    public DateTime? AuctionClosesAt { get; set; }
    /// <summary>null = no reserve (IsNoReserve=true); true/false = reserve met status.</summary>
    public bool? ReserveMet { get; set; }
}
