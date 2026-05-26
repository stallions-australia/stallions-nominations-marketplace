namespace Stallions.Shared.DTOs.Admin;

public class ForceListingStatusRequest
{
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
