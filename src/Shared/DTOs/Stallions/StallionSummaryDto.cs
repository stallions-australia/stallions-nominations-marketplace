namespace Stallions.Shared.DTOs.Stallions;

public class StallionSummaryDto
{
    public Guid Id { get; set; }
    public Guid StudFarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? YearOfBirth { get; set; }
    public string? Colour { get; set; }
    public string? PrimaryImagePath { get; set; }
    public int ActiveListingCount { get; set; }
    public int TotalListingCount { get; set; }
    public bool IsActive { get; set; }
}
