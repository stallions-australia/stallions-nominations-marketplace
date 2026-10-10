namespace Stallions.Shared.DTOs.Checkout;

/// <summary>The signed-in buyer's result on one auction — see AuctionOutcomes.</summary>
public class MyAuctionResultDto
{
    public string Outcome { get; set; } = AuctionOutcomes.None;
    public Guid? PurchaseId { get; set; }
    /// <summary>After a failed charge: update the card by this time (UTC).</summary>
    public DateTime? ChargeDueBy { get; set; }
}
