namespace Stallions.Server.Data.Entities;

public class AuctionListing : Listing
{
    public decimal StartingPrice { get; set; }
    public decimal? ReservePrice { get; set; }
    public bool IsNoReserve { get; set; } = false;
    public decimal MinimumBidIncrement { get; set; } = 25m;
    public DateTime EndDateTime { get; set; }
    public Guid? WinningBidId { get; set; }

    // Navigation properties
    public Bid? WinningBid { get; set; }
    public ICollection<Bid> Bids { get; set; } = new List<Bid>();
}
