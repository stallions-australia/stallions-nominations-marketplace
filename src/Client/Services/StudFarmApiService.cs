using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.StudFarms;

namespace Stallions.Client.Services;

public class StudFarmApiService
{
    private readonly HttpClient _http;
    public StudFarmApiService(HttpClient http) => _http = http;

    public virtual async Task<StudFarmDto> GetByIdAsync(Guid id)
    {
        var response = await _http.GetAsync($"api/studfarms/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ApiException(404, "Stud farm not found.");
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load stud farm.");
        return await response.Content.ReadFromJsonAsync<StudFarmDto>()
               ?? throw new ApiException(500, "Empty response.");
    }
}
