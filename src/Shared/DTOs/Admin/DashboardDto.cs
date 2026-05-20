namespace Stallions.Shared.DTOs.Admin;

public class DashboardDto
{
    public int TotalActiveListings { get; set; }
    public int TotalDraftListings { get; set; }
    public int TotalCompletedPurchases { get; set; }
    public decimal TotalRevenue { get; set; }
}
