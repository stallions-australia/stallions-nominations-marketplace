namespace Stallions.Shared.DTOs.Admin;

public class DashboardDto
{
    public int ActiveListingCount { get; set; }
    public int AuctionListingCount { get; set; }
    public int FixedPriceListingCount { get; set; }
    public int RecentPurchaseCount { get; set; }
    public decimal RecentFeeRevenueIncGst { get; set; }
    public int PendingVerificationCount { get; set; }
}
