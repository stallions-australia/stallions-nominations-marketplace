using System.Globalization;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.ChangeLog;

namespace Stallions.Client.Services;

/// <summary>Staff-only: the change log of settings, business details, intro offers, wording and terms.</summary>
public class ChangeLogApiService
{
    private readonly HttpClient _http;
    public ChangeLogApiService(HttpClient http) => _http = http;

    public virtual async Task<ChangeLogPageDto> GetAsync(ChangeLogFilter filter)
    {
        var r = await _http.GetAsync("api/audit/changes" + BuildQuery(filter));
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<ChangeLogPageDto>() ?? throw new ApiException(500, "Empty response.");
    }

    public static string BuildQuery(ChangeLogFilter filter)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(filter.Area)) query.Add($"area={Uri.EscapeDataString(filter.Area)}");
        if (filter.From is { } from) query.Add($"from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        if (filter.To is { } to) query.Add($"to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        query.Add($"page={Math.Max(1, filter.Page)}");
        return "?" + string.Join("&", query);
    }
}
