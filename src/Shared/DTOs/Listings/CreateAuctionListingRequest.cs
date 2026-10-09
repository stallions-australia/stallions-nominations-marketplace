namespace Stallions.Shared.DTOs.Listings;

public class CreateAuctionListingRequest
{
    public required Guid StallionId { get; set; }
    public required Guid SeasonId { get; set; }
    // Hidden reserve. There is no starting price, and the bid increment comes from Staff settings.
    public decimal? ReservePrice { get; set; }
    public bool IsNoReserve { get; set; }
    public required DateTime EndDateTime { get; set; }
    public required string TermsAndConditions { get; set; }
    public string? Description { get; set; }
}
