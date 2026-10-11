using Stallions.Shared.DTOs.Settings;

namespace Stallions.Server.Services;

public interface IPlatformSettingsService
{
    Task<ServiceResult<PlatformSettingsDto>> GetAsync();
    Task<ServiceResult<PlatformSettingsDto>> UpdateAsync(UpdatePlatformSettingsRequest request);

    /// <summary>Staff-only: Stallions Australia's business details (includes bank details).</summary>
    Task<ServiceResult<BusinessDetailsDto>> GetBusinessDetailsAsync();
    Task<ServiceResult<BusinessDetailsDto>> UpdateBusinessDetailsAsync(UpdateBusinessDetailsRequest request);
}
