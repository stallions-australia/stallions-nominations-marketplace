using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Terms;

namespace Stallions.Client.Services;

/// <summary>Authenticated access to the platform Terms & Conditions endpoints.</summary>
public class TermsApiService
{
    private readonly HttpClient _http;
    public TermsApiService(HttpClient http) => _http = http;

    /// <summary>Returns the current published T&C, or null if none has been published.</summary>
    public virtual async Task<TermsDocumentDto?> GetCurrentAsync()
    {
        var r = await _http.GetAsync("api/terms/current");
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load Terms & Conditions.");
        return await r.Content.ReadFromJsonAsync<TermsDocumentDto>();
    }

    public virtual async Task<TermsDocumentDto> PublishAsync(string body)
    {
        var r = await _http.PostAsJsonAsync("api/terms", new PublishTermsRequest { Body = body });
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ReadError(r, "Failed to publish Terms & Conditions."));
        return await r.Content.ReadFromJsonAsync<TermsDocumentDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<List<TermsDocumentDto>> GetHistoryAsync()
    {
        var r = await _http.GetAsync("api/terms/history");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load Terms history.");
        return await r.Content.ReadFromJsonAsync<List<TermsDocumentDto>>() ?? [];
    }

    private static async Task<string> ReadError(HttpResponseMessage r, string fallback)
    {
        var body = await r.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(body) ? fallback : body.Trim('"');
    }
}
