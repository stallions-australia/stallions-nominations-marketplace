using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Admin;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Services;

/// <summary>
/// Authenticated service for all Stallions Australia staff operations.
/// Uses BaseAddressAuthorizationMessageHandler — always requires a Bearer token.
/// Kept completely separate from AdminApiService (stud farm admin) and public browse services.
/// </summary>
public class StaffApiService
{
    private readonly HttpClient _http;
    public StaffApiService(HttpClient http) => _http = http;

    // ── Dashboard ──────────────────────────────────────────────────────────

    public virtual async Task<DashboardDto> GetDashboardAsync()
    {
        var r = await _http.GetAsync("api/admin/dashboard");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load dashboard.");
        return await r.Content.ReadFromJsonAsync<DashboardDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    // ── Users ──────────────────────────────────────────────────────────────

    public virtual async Task<List<UserDto>> GetUsersAsync()
    {
        var r = await _http.GetAsync("api/users");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load users.");
        return await r.Content.ReadFromJsonAsync<List<UserDto>>() ?? [];
    }

    public virtual async Task<UserDto> GetUserAsync(Guid id)
    {
        var r = await _http.GetAsync($"api/users/{id}");
        if (r.StatusCode == HttpStatusCode.NotFound)
            throw new ApiException(404, "User not found.");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load user.");
        return await r.Content.ReadFromJsonAsync<UserDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task VerifyUserAsync(Guid id)
    {
        var r = await _http.PostAsync($"api/users/{id}/verify", null);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to verify user.");
    }

    public virtual async Task SuspendUserAsync(Guid id)
    {
        var r = await _http.PostAsync($"api/users/{id}/suspend", null);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to suspend user.");
    }

    // ── Stud Farms ─────────────────────────────────────────────────────────

    public virtual async Task<List<StudFarmSummaryDto>> GetStudFarmsAsync()
    {
        var r = await _http.GetAsync("api/admin/studfarms");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load stud farms.");
        return await r.Content.ReadFromJsonAsync<List<StudFarmSummaryDto>>() ?? [];
    }

    public virtual async Task<StudFarmSummaryDto> CreateStudFarmAsync(CreateStudFarmRequest request)
    {
        var r = await _http.PostAsJsonAsync("api/admin/studfarms", request);
        if (!r.IsSuccessStatusCode)
        {
            var msg = await r.Content.ReadAsStringAsync();
            throw new ApiException((int)r.StatusCode, string.IsNullOrWhiteSpace(msg)
                ? "Failed to create stud farm." : msg);
        }
        return await r.Content.ReadFromJsonAsync<StudFarmSummaryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    // ── Listings ───────────────────────────────────────────────────────────

    public virtual async Task<List<ListingStaffSummaryDto>> GetAllListingsAsync()
    {
        var r = await _http.GetAsync("api/admin/listings");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load listings.");
        return await r.Content.ReadFromJsonAsync<List<ListingStaffSummaryDto>>() ?? [];
    }

    public virtual async Task SetListingFeeAsync(Guid id, decimal feePercent)
    {
        var r = await _http.PutAsJsonAsync($"api/admin/listings/{id}/fee",
            new { PlatformFeePercent = feePercent });
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to set listing fee.");
    }

    public virtual async Task ForceListingStatusAsync(Guid id, ForceListingStatusRequest request)
    {
        var r = await _http.PostAsJsonAsync($"api/admin/listings/{id}/force-status", request);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to override listing status.");
    }

    // ── Transactions ───────────────────────────────────────────────────────

    public virtual async Task<List<TransactionDto>> GetTransactionsAsync()
    {
        var r = await _http.GetAsync("api/admin/transactions");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load transactions.");
        return await r.Content.ReadFromJsonAsync<List<TransactionDto>>() ?? [];
    }

    // ── Invoices ───────────────────────────────────────────────────────────

    public virtual async Task<List<InvoiceDto>> GetInvoicesAsync()
    {
        var r = await _http.GetAsync("api/admin/invoices");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load invoices.");
        return await r.Content.ReadFromJsonAsync<List<InvoiceDto>>() ?? [];
    }

    // ── Users for stud farm onboarding dropdown ────────────────────────────

    /// <summary>
    /// Returns all users with the StudFarmAdmin role.
    /// Used to populate the User dropdown on the new stud farm form.
    /// Server validates at submission time that the selected user is not already linked to a farm.
    /// </summary>
    public virtual async Task<List<UserDto>> GetStudFarmAdminsAsync()
    {
        var all = await GetUsersAsync();
        return all.Where(u => u.Role == "StudFarmAdmin").ToList();
    }
}
