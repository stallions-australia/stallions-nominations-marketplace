namespace Stallions.Shared.DTOs.Enquiries;

public class EnquirySummaryDto
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int MessageCount { get; set; }
    public DateTime? LastMessageAt { get; set; }
    // Admin inbox fields — populated by EnquiryService.MapToSummary when the caller is Staff
    public string StallionName { get; set; } = string.Empty;
    public string ListingTitle { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public bool IsUnread { get; set; }
}
