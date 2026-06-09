using System.ComponentModel.DataAnnotations;

namespace Stallions.Shared.DTOs.Admin;

public class SetUserRoleRequest
{
    [Required]
    public string Role { get; set; } = string.Empty;
}
