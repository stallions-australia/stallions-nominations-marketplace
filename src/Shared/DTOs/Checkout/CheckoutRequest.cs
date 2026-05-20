namespace Stallions.Shared.DTOs.Checkout;

public class CheckoutRequest
{
    public required string MareName { get; set; }
    public string? MareRegistration { get; set; }
    public string? MareBreed { get; set; }
}
