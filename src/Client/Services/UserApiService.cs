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
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<UserDto>();
    }
}
