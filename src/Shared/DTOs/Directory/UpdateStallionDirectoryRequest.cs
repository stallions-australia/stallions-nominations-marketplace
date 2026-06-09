using System.ComponentModel.DataAnnotations;

namespace Stallions.Shared.DTOs.Directory;

public class UpdateStallionDirectoryRequest
{
    public Guid StudDirectoryId { get; set; }
    public int StallionId { get; set; }
    public int ArionId { get; set; }
    [Required]
    public string Name { get; set; } = string.Empty;
    public int YearOfBirth { get; set; }
    public string? Colour { get; set; }
    public string? Height { get; set; }
    public string? SireName { get; set; }
    public string? DamName { get; set; }
    public bool IsActive { get; set; } = true;
}
