namespace Stallions.Shared.DTOs.Listings;

public class FixedPriceListingDto : ListingDto
{
    public decimal PriceIncGst { get; set; }
    public int Quantity { get; set; }
    public int QuantityRemaining { get; set; }
}
