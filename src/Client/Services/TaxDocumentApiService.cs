using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.TaxDocuments;

namespace Stallions.Client.Services;

/// <summary>Tax invoices and adjustment notes: one document, the buyer's and stud's lists, and the Staff register.</summary>
public class TaxDocumentApiService
{
    private readonly HttpClient _http;
    public TaxDocumentApiService(HttpClient http) => _http = http;

    /// <summary>Null when the document doesn't exist or isn't the caller's (the API answers 404 for both).</summary>
    public virtual async Task<TaxDocumentDto?> GetAsync(Guid id)
    {
        var r = await _http.GetAsync($"api/tax-documents/{id}");
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<TaxDocumentDto>();
    }

    public virtual Task<List<TaxDocumentSummaryDto>> GetMineAsync() => GetListAsync("api/tax-documents/mine");

    public virtual Task<List<TaxDocumentSummaryDto>> GetForStudAsync() => GetListAsync("api/tax-documents/stud");

    public virtual async Task<TaxDocumentRegisterDto> GetRegisterAsync(TaxDocumentFilter filter)
    {
        var r = await _http.GetAsync("api/tax-documents" + BuildQuery(filter));
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<TaxDocumentRegisterDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<string> ExportCsvAsync(TaxDocumentFilter filter)
    {
        var r = await _http.GetAsync("api/tax-documents/export.csv" + BuildQuery(filter));
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadAsStringAsync();
    }

    public virtual async Task<IssueMissingResultDto> IssueMissingAsync()
    {
        var r = await _http.PostAsync("api/tax-documents/issue-missing", null);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<IssueMissingResultDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public static string BuildQuery(TaxDocumentFilter filter)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(filter.Kind)) query.Add($"kind={Uri.EscapeDataString(filter.Kind)}");
        if (!string.IsNullOrEmpty(filter.Status)) query.Add($"status={Uri.EscapeDataString(filter.Status)}");
        if (filter.From is { } from) query.Add($"from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        if (filter.To is { } to) query.Add($"to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        return query.Count == 0 ? string.Empty : "?" + string.Join("&", query);
    }

    private async Task<List<TaxDocumentSummaryDto>> GetListAsync(string url)
    {
        var r = await _http.GetAsync(url);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<List<TaxDocumentSummaryDto>>() ?? [];
    }
}
