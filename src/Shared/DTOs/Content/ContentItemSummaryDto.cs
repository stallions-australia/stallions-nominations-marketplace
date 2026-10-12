namespace Stallions.Shared.DTOs.Content;

/// <summary>One wording item in the Staff list. CurrentVersion 0 = nothing stored (catalog default).</summary>
public class ContentItemSummaryDto
{
    public string Key { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool HasSubject { get; set; }
    public int CurrentVersion { get; set; }
    public DateTime? ChangedAt { get; set; }
    /// <summary>Null for the seeded default.</summary>
    public string? ChangedByName { get; set; }
}
