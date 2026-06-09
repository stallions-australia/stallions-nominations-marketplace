using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Directory;

namespace Stallions.Client.Services;

/// <summary>
/// Staff-only service for Stud Directory and Stallion Directory CRUD.
/// Registered with BaseAddressAuthorizationMessageHandler — always requires a Bearer token.
/// </summary>
public class DirectoryApiService
{
    private readonly HttpClient _http;
    public DirectoryApiService(HttpClient http) => _http = http;

    // ── Stud Directory ────────────────────────────────────────────────────

    public virtual async Task<List<StudDirectorySummaryDto>> GetStudDirectoriesAsync(bool includeInactive = false)
    {
        var r = await _http.GetAsync($"api/stud-directory?includeInactive={includeInactive}");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load stud directory.");
        return await r.Content.ReadFromJsonAsync<List<StudDirectorySummaryDto>>() ?? [];
    }

    public virtual async Task<StudDirectoryDto> GetStudDirectoryAsync(Guid id)
    {
        var r = await _http.GetAsync($"api/stud-directory/{id}");
        if (r.StatusCode == HttpStatusCode.NotFound)
            throw new ApiException(404, "Stud directory entry not found.");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load stud directory entry.");
        return await r.Content.ReadFromJsonAsync<StudDirectoryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<StudDirectoryDto> CreateStudDirectoryAsync(CreateStudDirectoryRequest request)
    {
        var r = await _http.PostAsJsonAsync("api/stud-directory", request);
        if (!r.IsSuccessStatusCode)
        {
            var msg = await r.Content.ReadAsStringAsync();
            throw new ApiException((int)r.StatusCode, string.IsNullOrWhiteSpace(msg)
                ? "Failed to create stud directory entry." : msg);
        }
        return await r.Content.ReadFromJsonAsync<StudDirectoryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<StudDirectoryDto> UpdateStudDirectoryAsync(Guid id, UpdateStudDirectoryRequest request)
    {
        var r = await _http.PutAsJsonAsync($"api/stud-directory/{id}", request);
        if (!r.IsSuccessStatusCode)
        {
            var msg = await r.Content.ReadAsStringAsync();
            throw new ApiException((int)r.StatusCode, string.IsNullOrWhiteSpace(msg)
                ? "Failed to update stud directory entry." : msg);
        }
        return await r.Content.ReadFromJsonAsync<StudDirectoryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    // ── Stallion Directory ────────────────────────────────────────────────

    public virtual async Task<List<StallionDirectorySummaryDto>> GetStallionDirectoriesAsync(
        bool includeInactive = false, Guid? studDirectoryId = null)
    {
        var url = $"api/stallion-directory?includeInactive={includeInactive}";
        if (studDirectoryId.HasValue) url += $"&studDirectoryId={studDirectoryId}";
        var r = await _http.GetAsync(url);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load stallion directory.");
        return await r.Content.ReadFromJsonAsync<List<StallionDirectorySummaryDto>>() ?? [];
    }

    public virtual async Task<StallionDirectoryDto> GetStallionDirectoryAsync(Guid id)
    {
        var r = await _http.GetAsync($"api/stallion-directory/{id}");
        if (r.StatusCode == HttpStatusCode.NotFound)
            throw new ApiException(404, "Stallion directory entry not found.");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load stallion directory entry.");
        return await r.Content.ReadFromJsonAsync<StallionDirectoryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<StallionDirectoryDto> CreateStallionDirectoryAsync(CreateStallionDirectoryRequest request)
    {
        var r = await _http.PostAsJsonAsync("api/stallion-directory", request);
        if (!r.IsSuccessStatusCode)
        {
            var msg = await r.Content.ReadAsStringAsync();
            throw new ApiException((int)r.StatusCode, string.IsNullOrWhiteSpace(msg)
                ? "Failed to create stallion directory entry." : msg);
        }
        return await r.Content.ReadFromJsonAsync<StallionDirectoryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<StallionDirectoryDto> UpdateStallionDirectoryAsync(Guid id, UpdateStallionDirectoryRequest request)
    {
        var r = await _http.PutAsJsonAsync($"api/stallion-directory/{id}", request);
        if (!r.IsSuccessStatusCode)
        {
            var msg = await r.Content.ReadAsStringAsync();
            throw new ApiException((int)r.StatusCode, string.IsNullOrWhiteSpace(msg)
                ? "Failed to update stallion directory entry." : msg);
        }
        return await r.Content.ReadFromJsonAsync<StallionDirectoryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }
}
