namespace Stallions.Server.Data.Entities;

public class Season
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsOpen { get; set; } = false;
    public DateTime? OpenedAt { get; set; }
    public Guid? OpenedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User? OpenedBy { get; set; }
    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
}
