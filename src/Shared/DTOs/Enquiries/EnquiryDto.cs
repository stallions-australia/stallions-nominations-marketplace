namespace Stallions.Shared.DTOs.Enquiries;

public class EnquiryDto
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public Guid BuyerUserId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<EnquiryMessageDto> Messages { get; set; } = new();
}
