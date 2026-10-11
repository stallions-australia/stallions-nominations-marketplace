namespace Stallions.Shared.Enums;

public enum ListingStatus
{
    Draft,
    Active,
    Sold,
    Expired,
    Cancelled,
    /// <summary>The auction has closed with a winner; the buyer fee is being charged.</summary>
    AwaitingPayment,
    /// <summary>The auction closed without a sale — see Listing.CloseReason. Ready for Make an Offer (Phase 4).</summary>
    Unsold
}
