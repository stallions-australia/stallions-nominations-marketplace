using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

public class Bid
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AuctionListingId { get; set; }
    public Guid BuyerUserId { get; set; }
    public decimal AmountIncGst { get; set; }
    public DateTime PlacedAt { get; set; } = DateTime.UtcNow;
    public BidStatus Status { get; set; } = BidStatus.Active;

    // Navigation properties
    public AuctionListing AuctionListing { get; set; } = null!;
    public User Buyer { get; set; } = null!;
}
