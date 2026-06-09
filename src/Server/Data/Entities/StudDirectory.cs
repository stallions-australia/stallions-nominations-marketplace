namespace Stallions.Server.Data.Entities;

public class StudDirectory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int? ArionStudId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Website { get; set; }
    public string? Address { get; set; }
    public string? Town { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<StallionDirectory> Stallions { get; set; } = new List<StallionDirectory>();
}
