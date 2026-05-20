namespace Stallions.Shared.DTOs.Enquiries;

public class EnquiryDto
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public Guid BuyerUserId { get; set; }
    public Guid StudFarmUserId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public List<EnquiryMessageDto> Messages { get; set; } = new();
}
