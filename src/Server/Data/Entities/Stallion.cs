namespace Stallions.Server.Data.Entities;

public class Stallion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StudFarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? YearOfBirth { get; set; }
    public string? Colour { get; set; }
    public string? Sire { get; set; }
    public string? Dam { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public StudFarm StudFarm { get; set; } = null!;
    public ICollection<StallionImage> Images { get; set; } = new List<StallionImage>();
    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
}
