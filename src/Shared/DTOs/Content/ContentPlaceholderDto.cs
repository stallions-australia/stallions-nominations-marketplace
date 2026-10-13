namespace Stallions.Shared.DTOs.Content;

public class ContentPlaceholderDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>The example value the preview uses.</summary>
    public string Sample { get; set; } = string.Empty;
    public bool Required { get; set; }
}
