namespace Stallions.Shared.DTOs.Enquiries;

public class EnquirySummaryDto
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int MessageCount { get; set; }
    public DateTime? LastMessageAt { get; set; }
}
