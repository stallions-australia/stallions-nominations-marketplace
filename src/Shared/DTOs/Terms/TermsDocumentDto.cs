namespace Stallions.Shared.DTOs.Terms;

public class TermsDocumentDto
{
    public Guid Id { get; set; }
    /// <summary>"Buyer" (T&amp;C) or "Stud" (stud listing terms).</summary>
    public string Audience { get; set; } = "Buyer";
    public int Version { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? CreatedByName { get; set; }
}
