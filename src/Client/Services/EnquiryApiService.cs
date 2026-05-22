using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Enquiries;

namespace Stallions.Client.Services;

public class EnquiryApiService
{
    private readonly HttpClient _http;
    public EnquiryApiService(HttpClient http) => _http = http;

    public virtual async Task<List<EnquirySummaryDto>> GetAllAsync()
    {
        var response = await _http.GetAsync("api/enquiries");
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load enquiries.");
        return await response.Content.ReadFromJsonAsync<List<EnquirySummaryDto>>()
               ?? new List<EnquirySummaryDto>();
    }

    public virtual async Task<EnquiryDto> GetByIdAsync(Guid id)
    {
        var response = await _http.GetAsync($"api/enquiries/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ApiException(404, "Enquiry not found.");
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load enquiry.");
        return await response.Content.ReadFromJsonAsync<EnquiryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task PostMessageAsync(Guid enquiryId, SendMessageRequest request)
    {
        var response = await _http.PostAsJsonAsync($"api/enquiries/{enquiryId}/messages", request);
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to send message.");
    }

    public virtual async Task<EnquiryDto> CreateAsync(Guid listingId, OpenEnquiryRequest request)
    {
        var response = await _http.PostAsJsonAsync($"api/listings/{listingId}/enquiries", request);
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to open enquiry.");
        return await response.Content.ReadFromJsonAsync<EnquiryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }
}
