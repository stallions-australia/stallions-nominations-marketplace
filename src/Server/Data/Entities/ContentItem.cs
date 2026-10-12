namespace Stallions.Server.Data.Entities;

/// <summary>
/// One version of a Staff-editable wording item (a disclosure or an email's subject and message). Never edited:
/// each save adds the next version, and the highest version of a key is current. Keys come from ContentCatalog.
/// </summary>
public class ContentItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = string.Empty;
    public int Version { get; set; }
    /// <summary>Emails only.</summary>
    public string? Subject { get; set; }
    /// <summary>Plain text; a blank line starts a new paragraph.</summary>
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Null for the seeded version 1.</summary>
    public Guid? CreatedByUserId { get; set; }

    public User? CreatedBy { get; set; }
}
