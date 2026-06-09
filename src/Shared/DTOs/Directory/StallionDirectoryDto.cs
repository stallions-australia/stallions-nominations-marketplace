namespace Stallions.Shared.DTOs.Directory;

public class StallionDirectoryDto
{
    public Guid Id { get; set; }
    public int StallionId { get; set; }
    public int ArionId { get; set; }
    public Guid StudDirectoryId { get; set; }
    public string StudName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int YearOfBirth { get; set; }
    public string? Colour { get; set; }
    public string? Height { get; set; }
    public string? SireName { get; set; }
    public string? DamName { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
