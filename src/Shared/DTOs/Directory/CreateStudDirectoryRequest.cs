using System.ComponentModel.DataAnnotations;

namespace Stallions.Shared.DTOs.Directory;

public class CreateStudDirectoryRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;
    public int? ArionStudId { get; set; }
    public string? Website { get; set; }
    public string? Address { get; set; }
    public string? Town { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; } = true;
}
