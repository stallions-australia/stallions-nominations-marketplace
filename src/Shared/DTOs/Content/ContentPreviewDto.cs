namespace Stallions.Shared.DTOs.Content;

/// <summary>Either the validation errors, or the text rendered with the example values (plain text; "\n" = line break).</summary>
public class ContentPreviewDto
{
    public List<string> Errors { get; set; } = [];
    public string? Subject { get; set; }
    public List<string> Paragraphs { get; set; } = [];
}
