using System.ComponentModel.DataAnnotations;

namespace Stallions.Shared.DTOs.Checkout;

public class CheckoutRequest
{
    [Required(ErrorMessage = "Mare name is required.")]
    [StringLength(200, ErrorMessage = "Mare name must be 200 characters or fewer.")]
    public required string MareName { get; set; }

    [StringLength(100)]
    public string? MareRegistration { get; set; }

    [StringLength(100)]
    public string? MareBreed { get; set; }
}
