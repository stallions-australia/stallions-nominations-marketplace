namespace Stallions.Shared.DTOs.Content;

/// <summary>Plain text. Subject is ignored for disclosures. Limits: subject 200, body 4,000 characters.</summary>
public class SaveContentRequest
{
    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;
}
