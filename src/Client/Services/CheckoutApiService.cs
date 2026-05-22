using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Checkout;

namespace Stallions.Client.Services;

public class CheckoutApiService
{
    private readonly HttpClient _http;
    public CheckoutApiService(HttpClient http) => _http = http;

    /// <summary>
    /// Posts mare details. The server creates the purchase record and returns disclosure data.
    /// The buyer reviews the disclosure, then must explicitly confirm (Plan 3c Checkout page).
    /// </summary>
    public virtual async Task<CheckoutResponse> InitiateAsync(Guid listingId, CheckoutRequest request)
    {
        var response = await _http.PostAsJsonAsync($"api/listings/{listingId}/checkout", request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ServiceHelpers.ExtractErrorMessageAsync(response);
            throw new ApiException((int)response.StatusCode, error);
        }
        return await response.Content.ReadFromJsonAsync<CheckoutResponse>()
               ?? throw new ApiException(500, "Empty response from server.");
    }

    public async Task<List<PurchaseDto>> GetMyPurchasesAsync()
    {
        var response = await _http.GetAsync("api/purchases");
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load purchases.");
        return await response.Content.ReadFromJsonAsync<List<PurchaseDto>>()
               ?? new List<PurchaseDto>();
    }
}
