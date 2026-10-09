namespace Stallions.Shared.DTOs.Bids;

/// <summary>
/// One bid in an auction's public history. Bidders are anonymised ("Bidder 1", "Bidder 2", …
/// in order of their first bid on this auction); no user id or name is ever included.
/// </summary>
public class PublicBidDto
{
    public decimal AmountIncGst { get; set; }
    public DateTime PlacedAt { get; set; }
    public string Bidder { get; set; } = string.Empty;
}
