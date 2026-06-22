namespace Stallions.Shared.DTOs.Terms;

public class TermsDocumentDto
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
