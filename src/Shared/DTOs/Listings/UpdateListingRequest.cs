namespace Stallions.Shared.DTOs.Listings;

// Only fields editable by the stud farm on a Draft listing.
// PlatformFeePercent is deliberately absent — it is only writable via PUT /admin/listings/{id}/fee.
public class UpdateListingRequest
{
    public decimal? StartingPrice { get; set; }
    public decimal? ReservePrice { get; set; }
    public bool? IsNoReserve { get; set; }
    public decimal? MinimumBidIncrement { get; set; }
    public DateTime? EndDateTime { get; set; }
    public decimal? PriceIncGst { get; set; }
    public int? Quantity { get; set; }
}
