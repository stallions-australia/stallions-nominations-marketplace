namespace Stallions.Shared.DTOs.Listings;

public class CreateFixedPriceListingRequest
{
    public required Guid StallionId { get; set; }
    public required Guid SeasonId { get; set; }
    public required decimal PriceIncGst { get; set; }
    public required int Quantity { get; set; }
}
