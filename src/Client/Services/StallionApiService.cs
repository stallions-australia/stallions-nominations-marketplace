using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Stallions;

namespace Stallions.Client.Services;

public class StallionApiService
{
    private readonly HttpClient _http;
    public StallionApiService(HttpClient http) => _http = http;

    public async Task<StallionDto> GetByIdAsync(Guid id)
    {
        var response = await _http.GetAsync($"api/stallions/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ApiException(404, "Stallion not found.");
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load stallion.");
        return await response.Content.ReadFromJsonAsync<StallionDto>()
               ?? throw new ApiException(500, "Empty response from server.");
    }
}
