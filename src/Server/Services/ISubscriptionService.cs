using Stallions.Shared.DTOs.Payments;
using Stallions.Shared.DTOs.Subscriptions;

namespace Stallions.Server.Services;

public interface ISubscriptionService
{
    Task<ServiceResult<SubscriptionDto>> CreateAsync(CreateSubscriptionRequest request);
    Task<ServiceResult<SubscriptionDto>> MarkPaidAsync(Guid id, MarkSubscriptionPaidRequest request);
    Task<ServiceResult<SubscriptionDto>> WaiveAsync(Guid id, WaiveSubscriptionRequest request);
    Task<ServiceResult<IReadOnlyList<SubscriptionDto>>> GetForStudFarmAsync();
    Task<ServiceResult<IReadOnlyList<SubscriptionDto>>> GetAllAsync(Guid? seasonId, string? status);

    /// <summary>StudFarmAdmin: start paying the listing fee by card for one of their stallions.</summary>
    Task<ServiceResult<PaymentRedirectDto>> ActivateAsync(ActivateStallionRequest request, string successUrl, string cancelUrl);

    /// <summary>True when the stallion has a Paid or Waived subscription for the season.</summary>
    Task<bool> HasActiveSubscriptionAsync(Guid stallionId, Guid seasonId);
}
