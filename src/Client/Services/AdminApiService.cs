using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Forms;
using Stallions.Shared.DTOs.Enquiries;
using Stallions.Shared.DTOs.Listings;
using Stallions.Shared.DTOs.Seasons;
using Stallions.Shared.DTOs.Stallions;

namespace Stallions.Client.Services;

/// <summary>
/// Authenticated service for all stud farm admin operations.
/// Uses BaseAddressAuthorizationMessageHandler — always requires a Bearer token.
/// Keep this separate from public browse services (ListingApiService, StallionApiService)
/// which must NOT require a token.
/// </summary>
public class AdminApiService
{
    private readonly HttpClient _http;
    public AdminApiService(HttpClient http) => _http = http;

    // ── Stallions ──────────────────────────────────────────────────────────

    public virtual async Task<List<StallionSummaryDto>> GetMyStallionsAsync()
    {
        var r = await _http.GetAsync("api/stallions/mine");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load stallions.");
        return await r.Content.ReadFromJsonAsync<List<StallionSummaryDto>>() ?? [];
    }

    public virtual async Task<StallionDto> GetStallionAsync(Guid id)
    {
        var r = await _http.GetAsync($"api/stallions/{id}");
        if (r.StatusCode == HttpStatusCode.NotFound)
            throw new ApiException(404, "Stallion not found.");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load stallion.");
        return await r.Content.ReadFromJsonAsync<StallionDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<StallionDto> CreateStallionAsync(CreateStallionRequest request)
    {
        var r = await _http.PostAsJsonAsync("api/stallions", request);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to create stallion.");
        return await r.Content.ReadFromJsonAsync<StallionDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<StallionDto> UpdateStallionAsync(Guid id, UpdateStallionRequest request)
    {
        var r = await _http.PutAsJsonAsync($"api/stallions/{id}", request);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to update stallion.");
        return await r.Content.ReadFromJsonAsync<StallionDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<StallionDto> UploadStallionImageAsync(Guid stallionId, IBrowserFile file)
    {
        using var content = new MultipartFormDataContent();
        var stream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
        content.Add(fileContent, "file", file.Name);

        var r = await _http.PostAsync($"api/stallions/{stallionId}/images", content);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to upload image.");
        return await r.Content.ReadFromJsonAsync<StallionDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task DeleteStallionImageAsync(Guid stallionId, Guid imageId)
    {
        var r = await _http.DeleteAsync($"api/stallions/{stallionId}/images/{imageId}");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to delete image.");
    }

    public virtual async Task SetPrimaryImageAsync(Guid stallionId, Guid imageId)
    {
        var r = await _http.PutAsync($"api/stallions/{stallionId}/images/{imageId}/primary", null);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to set primary image.");
    }

    // ── Listings ───────────────────────────────────────────────────────────

    public virtual async Task<List<ListingDto>> GetMyListingsAsync()
    {
        var r = await _http.GetAsync("api/listings/mine");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load listings.");
        return await r.Content.ReadFromJsonAsync<List<ListingDto>>() ?? [];
    }

    public virtual async Task<ListingDto> GetListingAsync(Guid id)
    {
        var r = await _http.GetAsync($"api/listings/{id}");
        if (r.StatusCode == HttpStatusCode.NotFound)
            throw new ApiException(404, "Listing not found.");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load listing.");
        return await r.Content.ReadFromJsonAsync<ListingDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<ListingDto> CreateFixedPriceListingAsync(CreateFixedPriceListingRequest request)
    {
        var r = await _http.PostAsJsonAsync("api/listings/fixed-price", request);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to create listing.");
        return await r.Content.ReadFromJsonAsync<ListingDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<ListingDto> CreateAuctionListingAsync(CreateAuctionListingRequest request)
    {
        var r = await _http.PostAsJsonAsync("api/listings/auction", request);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to create listing.");
        return await r.Content.ReadFromJsonAsync<ListingDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<ListingDto> UpdateListingAsync(Guid id, UpdateListingRequest request)
    {
        var r = await _http.PutAsJsonAsync($"api/listings/{id}", request);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to update listing.");
        return await r.Content.ReadFromJsonAsync<ListingDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task PublishListingAsync(Guid id)
    {
        var r = await _http.PostAsync($"api/listings/{id}/publish", null);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to publish listing.");
    }

    public virtual async Task UnpublishListingAsync(Guid id)
    {
        var r = await _http.PostAsync($"api/listings/{id}/unpublish", null);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to unpublish listing.");
    }

    public virtual async Task CloseListingAsync(Guid id)
    {
        var r = await _http.PostAsync($"api/listings/{id}/close", null);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to close listing.");
    }

    // ── Enquiries ──────────────────────────────────────────────────────────

    public virtual async Task<List<EnquirySummaryDto>> GetMyEnquiriesAsync()
    {
        var r = await _http.GetAsync("api/enquiries");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load enquiries.");
        return await r.Content.ReadFromJsonAsync<List<EnquirySummaryDto>>() ?? [];
    }

    public virtual async Task<EnquiryDto> GetEnquiryAsync(Guid id)
    {
        var r = await _http.GetAsync($"api/enquiries/{id}");
        if (r.StatusCode == HttpStatusCode.NotFound)
            throw new ApiException(404, "Enquiry not found.");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load enquiry.");
        return await r.Content.ReadFromJsonAsync<EnquiryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task SendReplyAsync(Guid enquiryId, SendMessageRequest request)
    {
        var r = await _http.PostAsJsonAsync($"api/enquiries/{enquiryId}/messages", request);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to send reply.");
    }

    // ── Seasons (for listing form dropdowns) ──────────────────────────────

    public virtual async Task<List<SeasonDto>> GetSeasonsAsync()
    {
        var r = await _http.GetAsync("api/seasons");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load seasons.");
        return await r.Content.ReadFromJsonAsync<List<SeasonDto>>() ?? [];
    }
}
