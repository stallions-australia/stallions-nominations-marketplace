using System.Net;
using System.Net.Http.Json;
using Stallions.Shared;
using Stallions.Shared.DTOs.Listings;

namespace Stallions.Client.Services;

public class ListingApiService
{
    private readonly HttpClient _http;
    public ListingApiService(HttpClient http) => _http = http;

    public virtual async Task<List<ListingCardDto>> GetListingsAsync(
        Guid? seasonId = null, Guid? studFarmId = null, string? type = null)
    {
        var qs = new List<string>();
        if (seasonId.HasValue)           qs.Add($"seasonId={seasonId}");
        if (studFarmId.HasValue)         qs.Add($"studFarmId={studFarmId}");
        if (!string.IsNullOrEmpty(type)) qs.Add($"type={Uri.EscapeDataString(type)}");
        var url = "api/listings" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");

        var response = await _http.GetAsync(url);
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load listings.");

        // Use source-generated context options — trim-safe for Blazor WASM Release builds.
        return await response.Content.ReadFromJsonAsync<List<ListingCardDto>>(
                   StallionsJsonContext.Default.Options)
               ?? new List<ListingCardDto>();
    }

    public virtual async Task<ListingDto> GetByIdAsync(Guid id)
    {
        var response = await _http.GetAsync($"api/listings/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ApiException(404, "Listing not found.");
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load listing.");

        try
        {
            // Use source-generated context options so the [JsonPolymorphic] /
            // [JsonDerivedType] discriminator is resolved at compile time, not via
            // reflection. Without this, IL trimming in a Release Blazor WASM build
            // strips the reflection members needed to discover AuctionListingDto /
            // FixedPriceListingDto at runtime.
            return await response.Content.ReadFromJsonAsync<ListingDto>(
                       StallionsJsonContext.Default.Options)
                   ?? throw new ApiException(500, "Empty response from server.");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new ApiException(500, $"Listing data could not be parsed: {ex.Message}");
        }
    }
}
