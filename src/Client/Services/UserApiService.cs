using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Services;

public class UserApiService
{
    private readonly HttpClient _http;
    public UserApiService(HttpClient http) => _http = http;

    public async Task<UserDto?> GetMeAsync()
    {
        var response = await _http.GetAsync("api/users/me");
        if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load user profile.");
        return await response.Content.ReadFromJsonAsync<UserDto>();
    }
}
