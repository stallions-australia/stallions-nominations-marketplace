using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Terms;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Services;

public class UserApiService
{
    private readonly HttpClient _http;
    public UserApiService(HttpClient http) => _http = http;

    public virtual async Task<UserDto?> GetMeAsync()
    {
        var response = await _http.GetAsync("api/users/me");
        if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load user profile.");
        return await response.Content.ReadFromJsonAsync<UserDto>();
    }

    public virtual async Task AcceptTermsAsync(int version)
    {
        var r = await _http.PostAsJsonAsync("api/users/me/accept-terms",
            new AcceptTermsRequest { Version = version });
        if (!r.IsSuccessStatusCode)
        {
            var body = await r.Content.ReadAsStringAsync();
            throw new ApiException((int)r.StatusCode,
                string.IsNullOrWhiteSpace(body) ? "Failed to accept Terms & Conditions." : body.Trim('"'));
        }
    }

    public virtual async Task SuppressBidConfirmationAsync()
    {
        var r = await _http.PostAsync("api/users/me/suppress-bid-confirmation", null);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to save your preference.");
    }
}
