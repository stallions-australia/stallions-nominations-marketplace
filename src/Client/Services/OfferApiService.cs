using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Offers;

namespace Stallions.Client.Services;

/// <summary>Private offers on Make an Offer listings (buyer and stud calls).</summary>
public class OfferApiService
{
    private readonly HttpClient _http;
    public OfferApiService(HttpClient http) => _http = http;

    public virtual async Task<OfferDto> MakeOfferAsync(Guid listingId, decimal amount)
    {
        var r = await _http.PostAsJsonAsync($"api/listings/{listingId}/offers", new MakeOfferRequest { AmountIncGst = amount });
        await ThrowIfFailed(r, "Your offer could not be sent.");
        return await r.Content.ReadFromJsonAsync<OfferDto>() ?? throw new ApiException(500, "Empty response from server.");
    }

    public virtual async Task WithdrawAsync(Guid offerId) =>
        await ThrowIfFailed(await _http.PostAsync($"api/offers/{offerId}/withdraw", null), "Your offer could not be withdrawn.");

    public virtual async Task<List<OfferDto>> GetMineAsync()
    {
        var r = await _http.GetAsync("api/offers/mine");
        await ThrowIfFailed(r, "Failed to load your offers.");
        return await r.Content.ReadFromJsonAsync<List<OfferDto>>() ?? new List<OfferDto>();
    }

    /// <summary>The buyer's latest offer on the listing, or null when they have none.</summary>
    public virtual async Task<OfferDto?> GetMyOfferAsync(Guid listingId)
    {
        var r = await _http.GetAsync($"api/listings/{listingId}/my-offer");
        if (r.StatusCode == HttpStatusCode.NoContent) return null;
        await ThrowIfFailed(r, "Failed to load your offer.");
        return await r.Content.ReadFromJsonAsync<OfferDto>();
    }

    public virtual async Task<List<StudOfferDto>> GetForListingAsync(Guid listingId)
    {
        var r = await _http.GetAsync($"api/listings/{listingId}/offers");
        await ThrowIfFailed(r, "Failed to load offers.");
        return await r.Content.ReadFromJsonAsync<List<StudOfferDto>>() ?? new List<StudOfferDto>();
    }

    public virtual async Task<AcceptOfferResultDto> AcceptAsync(Guid offerId)
    {
        var r = await _http.PostAsync($"api/offers/{offerId}/accept", null);
        await ThrowIfFailed(r, "The offer could not be accepted.");
        return await r.Content.ReadFromJsonAsync<AcceptOfferResultDto>() ?? throw new ApiException(500, "Empty response from server.");
    }

    public virtual async Task DeclineAsync(Guid offerId) =>
        await ThrowIfFailed(await _http.PostAsync($"api/offers/{offerId}/decline", null), "The offer could not be declined.");

    private static async Task ThrowIfFailed(HttpResponseMessage r, string fallback)
    {
        if (r.IsSuccessStatusCode) return;
        var msg = await ServiceHelpers.ExtractErrorMessageAsync(r);
        throw new ApiException((int)r.StatusCode, string.IsNullOrWhiteSpace(msg) ? fallback : msg);
    }
}
