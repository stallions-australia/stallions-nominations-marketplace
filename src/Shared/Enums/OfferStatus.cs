namespace Stallions.Shared.Enums;

/// <summary>Where a buyer's offer stands. Whether an Accepted offer completed is on its sale record.</summary>
public enum OfferStatus
{
    Pending,
    Accepted,
    Declined,
    Withdrawn,
    Expired
}
