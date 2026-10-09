namespace Stallions.Shared.DTOs.Listings;

public class AuctionListingDto : ListingDto
{
    // When reading JSON, the polymorphic reader consumes "listingType" as the type discriminator
    // and never assigns it to the property, so the concrete type sets its own value.
    public AuctionListingDto() => ListingType = "Auction";

    // Null unless the caller is Staff or the owning stud's admin — the reserve is hidden from buyers.
    public decimal? ReservePrice { get; set; }
    public bool IsNoReserve { get; set; }
    // Null when there is no reserve or no bids yet; otherwise whether the high bid meets the reserve.
    public bool? ReserveMet { get; set; }
    public decimal MinimumBidIncrement { get; set; }
    public DateTime EndDateTime { get; set; }
    public decimal? CurrentHighestBidIncGst { get; set; }
}
