namespace Stallions.Shared.DTOs.Listings;

public class AuctionListingDto : ListingDto
{
    public decimal StartingPrice { get; set; }
    public decimal? ReservePrice { get; set; }  // null when caller is not Staff (hidden)
    public bool IsNoReserve { get; set; }
    public decimal MinimumBidIncrement { get; set; }
    public DateTime EndDateTime { get; set; }
    public decimal? CurrentHighestBidIncGst { get; set; }
}
