namespace Stallions.Shared.DTOs.Directory;

/// <summary>
/// Returned by GET /api/stallions/authorized.
/// IsLinked = false means the farm has no StudDirectoryId set; the UI shows an unlinked notice.
/// Available = directory entries that haven't yet been added to the farm's stable.
/// </summary>
public class AuthorizedStallionsDto
{
    public bool IsLinked { get; set; }
    public string FarmName { get; set; } = string.Empty;
    public List<StallionDirectorySummaryDto> Available { get; set; } = new();
}
