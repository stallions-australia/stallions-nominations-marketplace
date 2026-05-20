namespace Stallions.Shared.DTOs.Bids;

public class BidDto
{
    public Guid Id { get; set; }
    public Guid AuctionListingId { get; set; }
    public Guid BuyerUserId { get; set; }
    public decimal AmountIncGst { get; set; }
    public DateTime PlacedAt { get; set; }
    public string Status { get; set; } = string.Empty;
}
