namespace Stallions.Server.Data.Entities;

public class StallionImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StallionId { get; set; }
    public string BlobPath { get; set; } = string.Empty;
    public bool IsPrimary { get; set; } = false;
    public int DisplayOrder { get; set; } = 0;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Stallion Stallion { get; set; } = null!;
}
