namespace Stallions.Server.Data.Entities;

public class AuctionListing : Listing
{
    /// <summary>Hidden reserve — never shown to buyers, only whether it has been met.</summary>
    public decimal? ReservePrice { get; set; }
    public bool IsNoReserve { get; set; } = false;
    /// <summary>Copied from PlatformSettings when the listing is created; studs cannot edit it.</summary>
    public decimal MinimumBidIncrement { get; set; }
    public DateTime EndDateTime { get; set; }
    public Guid? WinningBidId { get; set; }

    // Navigation properties
    public Bid? WinningBid { get; set; }
    public ICollection<Bid> Bids { get; set; } = new List<Bid>();

    /// <summary>A blank reserve is treated as no reserve, whether or not IsNoReserve was ticked.</summary>
    public bool HasReserve => !IsNoReserve && ReservePrice.HasValue;

    /// <summary>
    /// Null when there is no reserve or no bids yet (avoids "Reserve not met" on day one);
    /// otherwise whether the highest bid is at or above the reserve.
    /// </summary>
    public bool? IsReserveMetBy(decimal? highestBid) =>
        !HasReserve || highestBid == null ? null : highestBid >= ReservePrice;
}
