using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EntraObjectId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public UserStatus Status { get; set; } = UserStatus.PendingVerification;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? VerifiedAt { get; set; }
    public Guid? VerifiedByUserId { get; set; }

    // Navigation properties
    public User? VerifiedBy { get; set; }
    public StudFarm? StudFarm { get; set; }
    public ICollection<Bid> Bids { get; set; } = new List<Bid>();
    public ICollection<Purchase> Purchases { get; set; } = new List<Purchase>();
    public ICollection<Enquiry> Enquiries { get; set; } = new List<Enquiry>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
