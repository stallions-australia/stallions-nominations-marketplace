using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Payments;

namespace Stallions.Client.Services;

/// <summary>The signed-in buyer's saved card.</summary>
public class PaymentsApiService
{
    private readonly HttpClient _http;
    public PaymentsApiService(HttpClient http) => _http = http;

    /// <summary>The saved card, or null if none.</summary>
    public virtual async Task<SavedCardDto?> GetMyCardAsync()
    {
        var r = await _http.GetAsync("api/payments/card");
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<SavedCardDto>();
    }

    /// <summary>Starts the hosted card page; returns the URL to send the browser to.</summary>
    public virtual async Task<string> StartCardSetupAsync()
    {
        var r = await _http.PostAsync("api/payments/card/setup-session", null);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return (await r.Content.ReadFromJsonAsync<PaymentRedirectDto>())?.Url
               ?? throw new ApiException(500, "Empty response.");
    }
}
