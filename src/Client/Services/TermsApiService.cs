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

    /// <summary>Staff: publishes the next version for "Buyer" or "Stud".</summary>
    public virtual async Task<TermsDocumentDto> PublishAsync(string body, string audience)
    {
        var r = await _http.PostAsJsonAsync("api/terms", new PublishTermsRequest { Body = body, Audience = audience });
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ReadError(r, "Failed to publish the terms."));
        return await r.Content.ReadFromJsonAsync<TermsDocumentDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    /// <summary>Staff: every version for "Buyer" or "Stud", newest first.</summary>
    public virtual async Task<List<TermsDocumentDto>> GetHistoryAsync(string audience)
    {
        var r = await _http.GetAsync($"api/terms/history?audience={Uri.EscapeDataString(audience)}");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load the terms history.");
        return await r.Content.ReadFromJsonAsync<List<TermsDocumentDto>>() ?? [];
    }

    /// <summary>Stud admins and Staff: the current stud listing terms and whether the caller's farm must accept them.</summary>
    public virtual async Task<StudTermsStatusDto> GetStudStatusAsync()
    {
        var r = await _http.GetAsync("api/terms/stud/current");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ReadError(r, "Failed to load the stud listing terms."));
        return await r.Content.ReadFromJsonAsync<StudTermsStatusDto>() ?? new StudTermsStatusDto();
    }

    /// <summary>Stud admins: accepts this version of the stud listing terms for their farm.</summary>
    public virtual async Task<StudTermsStatusDto> AcceptStudTermsAsync(int version)
    {
        var r = await _http.PostAsJsonAsync("api/terms/stud/accept", new AcceptTermsRequest { Version = version });
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ReadError(r, "Failed to accept the stud listing terms."));
        return await r.Content.ReadFromJsonAsync<StudTermsStatusDto>() ?? new StudTermsStatusDto();
    }

    private static async Task<string> ReadError(HttpResponseMessage r, string fallback)
    {
        var body = await r.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(body) ? fallback : body.Trim('"');
    }
}
