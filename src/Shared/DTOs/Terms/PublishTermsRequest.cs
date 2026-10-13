namespace Stallions.Shared.DTOs.Terms;

public class PublishTermsRequest
{
    public string Body { get; set; } = string.Empty;
    /// <summary>"Buyer" (default when empty) or "Stud".</summary>
    public string? Audience { get; set; }
}
