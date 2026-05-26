namespace Stallions.Shared.DTOs.Admin;

public class StudFarmSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ABN { get; set; }
    public string? ContactEmail { get; set; }
    public string LinkedUserDisplayName { get; set; } = string.Empty;
    public string LinkedUserEmail { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
