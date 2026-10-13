using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

public class TermsDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Buyer T&amp;C or stud listing terms; versions are numbered per audience.</summary>
    public TermsAudience Audience { get; set; } = TermsAudience.Buyer;
    public int Version { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedByUserId { get; set; }

    public User? CreatedBy { get; set; }
}
