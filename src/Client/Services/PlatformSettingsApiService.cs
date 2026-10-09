using System.Net.Http.Json;
using Stallions.Shared.DTOs.Settings;

namespace Stallions.Client.Services;

/// <summary>Platform settings (fees, increment, grace period, offer expiry). Updates are Staff-only.</summary>
public class PlatformSettingsApiService
{
    private readonly HttpClient _http;
    public PlatformSettingsApiService(HttpClient http) => _http = http;

    public virtual async Task<PlatformSettingsDto> GetAsync()
    {
        var r = await _http.GetAsync("api/settings");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<PlatformSettingsDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<PlatformSettingsDto> UpdateAsync(UpdatePlatformSettingsRequest request)
    {
        var r = await _http.PutAsJsonAsync("api/settings", request);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<PlatformSettingsDto>()
               ?? throw new ApiException(500, "Empty response.");
    }
}
