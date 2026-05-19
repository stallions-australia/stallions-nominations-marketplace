namespace Stallions.Server.Data.Entities;

public class StudFarm
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ABN { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string? Address { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public User User { get; set; } = null!;
    public ICollection<Stallion> Stallions { get; set; } = new List<Stallion>();
    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
}
