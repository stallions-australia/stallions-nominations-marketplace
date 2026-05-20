namespace Stallions.Shared.DTOs.Enquiries;

public class EnquiryMessageDto
{
    public Guid Id { get; set; }
    public Guid EnquiryId { get; set; }
    public Guid SenderUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
}
