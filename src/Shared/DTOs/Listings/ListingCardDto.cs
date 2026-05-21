namespace Stallions.Shared.DTOs.Listings;

public class ListingCardDto
{
    public Guid Id { get; set; }
    public string ListingType { get; set; } = string.Empty; // "Auction" | "FixedPrice"
    public Guid StallionId { get; set; }
    public string StallionName { get; set; } = string.Empty;
    public string? PrimaryImagePath { get; set; }
    public Guid StudFarmId { get; set; }
    public string StudFarmName { get; set; } = string.Empty;
    public string? SeasonName { get; set; }

    /// <summary>Starting price for auctions; fixed price for fixed-price listings.</summary>
    public decimal PriceIncGst { get; set; }

    // Auction-specific (null for FixedPrice listings)
    public decimal? CurrentHighestBidIncGst { get; set; }
    public int? BidCount { get; set; }
    public DateTime? AuctionClosesAt { get; set; }
    /// <summary>null = no reserve (IsNoReserve=true); true/false = reserve met status.</summary>
    public bool? ReserveMet { get; set; }

    // FixedPrice-specific (null for Auction listings)
    public int? QuantityRemaining { get; set; }
    /// <summary>Original total quantity for fixed-price listings (used for progress bar fill).</summary>
    public int? TotalQuantity { get; set; }
}
