namespace Stallions.Shared.DTOs.Listings;

/// <summary>A nomination open to private offers. Never carries a price or any offer data.</summary>
public class MakeAnOfferListingDto : ListingDto
{
    // The polymorphic reader consumes "listingType" as the discriminator; the concrete type sets it.
    public MakeAnOfferListingDto() => ListingType = "MakeAnOffer";

    /// <summary>When the listing stops taking offers (end of the season), UTC.</summary>
    public DateTime ExpiresAt { get; set; }
    public Guid SourceAuctionListingId { get; set; }
}
