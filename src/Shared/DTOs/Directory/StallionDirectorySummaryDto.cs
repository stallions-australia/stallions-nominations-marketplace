namespace Stallions.Shared.DTOs.Directory;

public class StallionDirectorySummaryDto
{
    public Guid Id { get; set; }
    public int StallionId { get; set; }
    public int ArionId { get; set; }
    public Guid StudDirectoryId { get; set; }
    public string StudName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int YearOfBirth { get; set; }
    public string? Colour { get; set; }
    public bool IsActive { get; set; }
}
