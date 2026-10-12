namespace Stallions.Shared.DTOs.Content;

/// <summary>The editor's view of one wording item: its current text and what may go in it.</summary>
public class ContentItemDetailDto
{
    public string Key { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool HasSubject { get; set; }
    /// <summary>Disclosures: one paragraph, no line breaks.</summary>
    public bool SingleParagraph { get; set; }
    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTime? ChangedAt { get; set; }
    public string? ChangedByName { get; set; }
    /// <summary>The current text is the catalog default (Reset would change nothing).</summary>
    public bool IsDefault { get; set; }
    public List<ContentPlaceholderDto> Placeholders { get; set; } = [];
    public int MaxSubjectLength { get; set; }
    public int MaxBodyLength { get; set; }
}
