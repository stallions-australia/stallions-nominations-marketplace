using Stallions.Shared.DTOs.Settings;

namespace Stallions.Server.Services;

public interface IPlatformSettingsService
{
    Task<ServiceResult<PlatformSettingsDto>> GetAsync();
    Task<ServiceResult<PlatformSettingsDto>> UpdateAsync(UpdatePlatformSettingsRequest request);
}
