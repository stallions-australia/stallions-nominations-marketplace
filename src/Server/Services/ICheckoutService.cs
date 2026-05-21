using Stallions.Shared.DTOs.Checkout;

namespace Stallions.Server.Services;

public interface ICheckoutService
{
    Task<ServiceResult<CheckoutResponse>> InitiateCheckoutAsync(Guid listingId, CheckoutRequest request);
    Task<ServiceResult> CompleteCheckoutAsync(Guid purchaseId, string? webhookSecret);
    Task<ServiceResult<IReadOnlyList<PurchaseDto>>> GetPurchasesAsync();
    Task<ServiceResult<PurchaseDto>> GetPurchaseByIdAsync(Guid id);
    Task<ServiceResult> RefundAsync(Guid id);
}
