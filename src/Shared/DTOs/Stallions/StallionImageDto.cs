namespace Stallions.Shared.DTOs.Stallions;

public class StallionImageDto
{
    public Guid Id { get; set; }
    public string BlobPath { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public int DisplayOrder { get; set; }
}
