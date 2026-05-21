using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Bids;

namespace Stallions.Client.Services;

public class BidApiService
{
    private readonly HttpClient _http;
    public BidApiService(HttpClient http) => _http = http;

    public async Task PlaceBidAsync(Guid listingId, PlaceBidRequest request)
    {
        var response = await _http.PostAsJsonAsync($"api/listings/{listingId}/bids", request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new ApiException((int)response.StatusCode, error.Trim('"'));
        }
    }

    public async Task<List<BidDto>> GetMyBidsAsync()
    {
        var response = await _http.GetAsync("api/bids/mine");
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load bids.");
        return await response.Content.ReadFromJsonAsync<List<BidDto>>()
               ?? new List<BidDto>();
    }
}
