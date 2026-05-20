namespace Stallions.Shared.DTOs.Seasons;

public class UpdateSeasonRequest
{
    public required string Name { get; set; }
    public required DateOnly StartDate { get; set; }
    public required DateOnly EndDate { get; set; }
}
