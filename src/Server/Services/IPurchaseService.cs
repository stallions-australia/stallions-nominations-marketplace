using Stallions.Shared.DTOs.Checkout;

namespace Stallions.Server.Services;

/// <summary>Sale records (Purchase): created by the auction closer, read by buyers and Staff.</summary>
public interface IPurchaseService
{
    Task<ServiceResult<IReadOnlyList<PurchaseDto>>> GetPurchasesAsync();
    Task<ServiceResult<PurchaseDto>> GetPurchaseByIdAsync(Guid id);
    Task<ServiceResult> RefundAsync(Guid id);
}
