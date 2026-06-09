namespace Stallions.Shared.DTOs.Directory;

public class StudDirectorySummaryDto
{
    public Guid Id { get; set; }
    public int? ArionStudId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? State { get; set; }
    public string? Website { get; set; }
    public bool IsActive { get; set; } = true;
    public int StallionCount { get; set; }
}
