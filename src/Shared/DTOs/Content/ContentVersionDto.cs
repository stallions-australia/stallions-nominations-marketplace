namespace Stallions.Shared.DTOs.Content;

public class ContentVersionDto
{
    public int Version { get; set; }
    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    /// <summary>Null for the seeded default.</summary>
    public string? CreatedByName { get; set; }
    public bool IsCurrent { get; set; }
}
