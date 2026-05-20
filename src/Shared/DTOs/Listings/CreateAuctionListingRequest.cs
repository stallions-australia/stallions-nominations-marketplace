namespace Stallions.Shared.DTOs.Listings;

public class CreateAuctionListingRequest
{
    public required Guid StallionId { get; set; }
    public required Guid SeasonId { get; set; }
    public required decimal StartingPrice { get; set; }
    public decimal? ReservePrice { get; set; }
    public bool IsNoReserve { get; set; }
    public decimal MinimumBidIncrement { get; set; } = 25m;
    public required DateTime EndDateTime { get; set; }
}
