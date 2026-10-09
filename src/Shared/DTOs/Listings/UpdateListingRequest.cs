namespace Stallions.Shared.DTOs.Listings;

// Only fields editable by the stud farm on a Draft listing.
// BuyerFeeIncGst is deliberately absent — it is snapshotted from Staff-managed settings on publish.
public class UpdateListingRequest
{
    public decimal? ReservePrice { get; set; }
    public bool? IsNoReserve { get; set; }
    public DateTime? EndDateTime { get; set; }
    public string? Description { get; set; }
    public string? TermsAndConditions { get; set; }
}
