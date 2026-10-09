using System.Net.Http.Json;
using Stallions.Shared.DTOs.Subscriptions;

namespace Stallions.Client.Services;

/// <summary>Stallion season subscriptions (listing fees). Staff manage them; stud admins read their own.</summary>
public class SubscriptionApiService
{
    private readonly HttpClient _http;
    public SubscriptionApiService(HttpClient http) => _http = http;

    public virtual async Task<List<SubscriptionDto>> GetAllAsync(Guid? seasonId = null, string? status = null)
    {
        var query = new List<string>();
        if (seasonId.HasValue) query.Add($"seasonId={seasonId.Value}");
        if (!string.IsNullOrEmpty(status)) query.Add($"status={Uri.EscapeDataString(status)}");
        var url = "api/subscriptions" + (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);

        var r = await _http.GetAsync(url);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<List<SubscriptionDto>>() ?? [];
    }

    public virtual async Task<List<SubscriptionDto>> GetMineAsync()
    {
        var r = await _http.GetAsync("api/subscriptions/mine");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<List<SubscriptionDto>>() ?? [];
    }

    public virtual async Task<SubscriptionDto> CreateAsync(CreateSubscriptionRequest request)
    {
        var r = await _http.PostAsJsonAsync("api/subscriptions", request);
        return await ReadSubscription(r);
    }

    public virtual async Task<SubscriptionDto> MarkPaidAsync(Guid id, MarkSubscriptionPaidRequest request)
    {
        var r = await _http.PostAsJsonAsync($"api/subscriptions/{id}/mark-paid", request);
        return await ReadSubscription(r);
    }

    public virtual async Task<SubscriptionDto> WaiveAsync(Guid id, WaiveSubscriptionRequest request)
    {
        var r = await _http.PostAsJsonAsync($"api/subscriptions/{id}/waive", request);
        return await ReadSubscription(r);
    }

    /// <summary>Starts the listing-fee payment for one of the stud's stallions; returns the URL to send the browser to.</summary>
    public virtual async Task<string> ActivateAsync(Guid stallionId)
    {
        var r = await _http.PostAsJsonAsync("api/subscriptions/activate", new ActivateStallionRequest { StallionId = stallionId });
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return (await r.Content.ReadFromJsonAsync<Stallions.Shared.DTOs.Payments.PaymentRedirectDto>())?.Url
               ?? throw new ApiException(500, "Empty response.");
    }

    private static async Task<SubscriptionDto> ReadSubscription(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<SubscriptionDto>()
               ?? throw new ApiException(500, "Empty response.");
    }
}
