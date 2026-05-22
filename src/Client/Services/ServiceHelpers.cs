using System.Net.Http.Json;

namespace Stallions.Client.Services;

/// <summary>Shared utilities for API service implementations.</summary>
internal static class ServiceHelpers
{
    /// <summary>
    /// Reads the error body from a failed response. Handles plain JSON strings and
    /// ASP.NET Core ProblemDetails objects. Falls back to the raw body if neither.
    /// </summary>
    internal static async Task<string> ExtractErrorMessageAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body)) return "An unexpected error occurred.";

        try
        {
            // Try ProblemDetails first (ASP.NET Core standard error format)
            var pd = System.Text.Json.JsonSerializer.Deserialize<ProblemDetailsLite>(body,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (pd?.Detail is not null) return pd.Detail;
            if (pd?.Title is not null) return pd.Title;
        }
        catch { /* not a ProblemDetails body — fall through */ }

        return body.Trim('"');
    }

    private sealed record ProblemDetailsLite(string? Title, string? Detail);
}
