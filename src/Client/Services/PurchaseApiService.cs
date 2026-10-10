using System.Net.Http.Json;
using Stallions.Shared.DTOs.Checkout;

namespace Stallions.Client.Services;

/// <summary>Sale records (the platform creates them when an auction closes).</summary>
public class PurchaseApiService
{
    private readonly HttpClient _http;
    public PurchaseApiService(HttpClient http) => _http = http;

    public virtual async Task<List<PurchaseDto>> GetMyPurchasesAsync()
    {
        var response = await _http.GetAsync("api/purchases");
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load sale records.");
        return await response.Content.ReadFromJsonAsync<List<PurchaseDto>>() ?? new List<PurchaseDto>();
    }
}
