namespace Stallions.Server.Data.Entities;

public class StallionDirectory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int StallionId { get; set; }           // original stallionID from external DB
    public int ArionId { get; set; }              // original arionID; 0 when not applicable
    public Guid StudDirectoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int YearOfBirth { get; set; }
    public string? Colour { get; set; }
    public string? Height { get; set; }
    public string? SireName { get; set; }
    public string? DamName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public StudDirectory StudDirectory { get; set; } = null!;
}
