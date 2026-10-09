// src/Shared/DTOs/Payments/PaymentRedirectDto.cs
namespace Stallions.Shared.DTOs.Payments;

/// <summary>Where to send the browser next: the provider's hosted page.</summary>
public class PaymentRedirectDto
{
    public string Url { get; set; } = string.Empty;
}
