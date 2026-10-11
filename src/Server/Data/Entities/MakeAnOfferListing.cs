namespace Stallions.Server.Data.Entities;

/// <summary>
/// A nomination open to private offers, created by the stud from an Unsold auction lot. Stays open
/// after an accepted offer (supply is unlimited) until the stud closes it or the season ends.
/// </summary>
public class MakeAnOfferListing : Listing
{
    public Guid SourceAuctionListingId { get; set; }
    /// <summary>End of the season's last day (Sydney time), in UTC.</summary>
    public DateTime ExpiresAt { get; set; }

    public AuctionListing SourceAuctionListing { get; set; } = null!;
    public ICollection<Offer> Offers { get; set; } = new List<Offer>();
}
