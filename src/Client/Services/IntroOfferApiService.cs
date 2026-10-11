using System.Net.Http.Json;
using Stallions.Shared.DTOs.IntroOffers;

namespace Stallions.Client.Services;

/// <summary>Staff-only: intro offers on the listing fee, and the fee preview for a new subscription.</summary>
public class IntroOfferApiService
{
    private readonly HttpClient _http;
    public IntroOfferApiService(HttpClient http) => _http = http;

    public virtual async Task<List<IntroOfferDto>> GetAllAsync()
    {
        var r = await _http.GetAsync("api/intro-offers");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<List<IntroOfferDto>>() ?? [];
    }

    public virtual async Task<IntroOfferDto> CreateAsync(SaveIntroOfferRequest request) =>
        await ReadOffer(await _http.PostAsJsonAsync("api/intro-offers", request));

    public virtual async Task<IntroOfferDto> UpdateAsync(Guid id, SaveIntroOfferRequest request) =>
        await ReadOffer(await _http.PutAsJsonAsync($"api/intro-offers/{id}", request));

    public virtual async Task<IntroOfferDto> RetireAsync(Guid id) =>
        await ReadOffer(await _http.PostAsync($"api/intro-offers/{id}/retire", null));

    public virtual async Task<IntroOfferPreviewDto> PreviewAsync(Guid studFarmId, Guid seasonId)
    {
        var r = await _http.GetAsync($"api/intro-offers/preview?studFarmId={studFarmId}&seasonId={seasonId}");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<IntroOfferPreviewDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    private static async Task<IntroOfferDto> ReadOffer(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<IntroOfferDto>()
               ?? throw new ApiException(500, "Empty response.");
    }
}
