using System.Net.Http.Json;
using Stallions.Shared.DTOs.Content;

namespace Stallions.Client.Services;

/// <summary>Staff-only: the disclosure wording and every system email's subject and message.</summary>
public class ContentApiService
{
    private readonly HttpClient _http;
    public ContentApiService(HttpClient http) => _http = http;

    private static string Url(string key, string suffix = "") => $"api/content/{Uri.EscapeDataString(key)}{suffix}";

    public virtual async Task<List<ContentItemSummaryDto>> GetAllAsync() =>
        await Read<List<ContentItemSummaryDto>>(await _http.GetAsync("api/content"));

    public virtual async Task<ContentItemDetailDto> GetAsync(string key) =>
        await Read<ContentItemDetailDto>(await _http.GetAsync(Url(key)));

    public virtual async Task<List<ContentVersionDto>> GetHistoryAsync(string key) =>
        await Read<List<ContentVersionDto>>(await _http.GetAsync(Url(key, "/versions")));

    public virtual async Task<ContentItemDetailDto> SaveAsync(string key, SaveContentRequest request) =>
        await Read<ContentItemDetailDto>(await _http.PutAsJsonAsync(Url(key), request));

    public virtual async Task<ContentItemDetailDto> RestoreAsync(string key, int version) =>
        await Read<ContentItemDetailDto>(await _http.PostAsync(Url(key, $"/versions/{version}/restore"), null));

    public virtual async Task<ContentItemDetailDto> ResetAsync(string key) =>
        await Read<ContentItemDetailDto>(await _http.PostAsync(Url(key, "/reset"), null));

    public virtual async Task<ContentPreviewDto> PreviewAsync(string key, SaveContentRequest request) =>
        await Read<ContentPreviewDto>(await _http.PostAsJsonAsync(Url(key, "/preview"), request));

    private static async Task<T> Read<T>(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<T>() ?? throw new ApiException(500, "Empty response.");
    }
}
