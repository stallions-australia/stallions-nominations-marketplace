namespace Stallions.Server.Data.Entities;

public class EnquiryMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EnquiryId { get; set; }
    public Guid SenderUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public bool IsReadByRecipient { get; set; } = false;

    // Navigation properties
    public Enquiry Enquiry { get; set; } = null!;
    public User Sender { get; set; } = null!;
}
