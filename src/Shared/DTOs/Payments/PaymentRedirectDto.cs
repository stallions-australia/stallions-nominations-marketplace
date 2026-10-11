// src/Shared/DTOs/Payments/PaymentRedirectDto.cs
namespace Stallions.Shared.DTOs.Payments;

/// <summary>
/// Where to send the browser next: the provider's hosted page. When an intro offer made the fee $0 the
/// stallion is already active, ActivatedWithoutPayment is true and Url is empty.
/// </summary>
public class PaymentRedirectDto
{
    public string Url { get; set; } = string.Empty;
    public bool ActivatedWithoutPayment { get; set; }
}
