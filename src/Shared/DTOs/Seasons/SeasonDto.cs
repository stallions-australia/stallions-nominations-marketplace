namespace Stallions.Shared.DTOs.Seasons;

public class SeasonDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsOpen { get; set; }
    public DateTime? OpenedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
