namespace Stallions.Shared.DTOs.Listings;

public class ListingFeeDto
{
    public Guid ListingId { get; set; }
    public decimal PlatformFeePercent { get; set; }
}
