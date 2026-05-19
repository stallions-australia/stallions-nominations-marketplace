using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

public class Enquiry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public Guid BuyerUserId { get; set; }
    public Guid StudFarmUserId { get; set; }
    public EnquiryStatus Status { get; set; } = EnquiryStatus.Open;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }

    // Navigation properties
    public Listing Listing { get; set; } = null!;
    public User Buyer { get; set; } = null!;
    public User StudFarmUser { get; set; } = null!;
    public ICollection<EnquiryMessage> Messages { get; set; } = new List<EnquiryMessage>();
}
