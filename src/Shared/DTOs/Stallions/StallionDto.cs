namespace Stallions.Shared.DTOs.Stallions;

public class StallionDto
{
    public Guid Id { get; set; }
    public Guid StudFarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? YearOfBirth { get; set; }
    public string? Colour { get; set; }
    public string? Sire { get; set; }
    public string? Dam { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<StallionImageDto> Images { get; set; } = new();
}
