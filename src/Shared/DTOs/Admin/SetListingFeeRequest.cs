using System.ComponentModel.DataAnnotations;

namespace Stallions.Shared.DTOs.Admin;

public class SetListingFeeRequest
{
    [Required]
    [Range(0, 100, ErrorMessage = "Platform fee percent must be between 0 and 100.")]
    public required decimal PlatformFeePercent { get; set; }
}
