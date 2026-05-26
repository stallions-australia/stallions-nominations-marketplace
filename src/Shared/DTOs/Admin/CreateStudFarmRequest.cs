namespace Stallions.Shared.DTOs.Admin;

public class CreateStudFarmRequest
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ABN { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string? Address { get; set; }
}
