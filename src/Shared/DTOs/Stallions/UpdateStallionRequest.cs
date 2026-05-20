namespace Stallions.Shared.DTOs.Stallions;

public class UpdateStallionRequest
{
    public required string Name { get; set; }
    public int? YearOfBirth { get; set; }
    public string? Colour { get; set; }
    public string? Sire { get; set; }
    public string? Dam { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? Description { get; set; }
}
