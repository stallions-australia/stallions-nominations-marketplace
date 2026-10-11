namespace Stallions.Shared.DTOs.IntroOffers;

public class IntroOfferDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;          // Waiver | AmountOff
    public decimal? AmountOffIncGst { get; set; }
    public Guid? StudFarmId { get; set; }                     // null = all studs
    public string? StudFarmName { get; set; }
    public Guid? SeasonId { get; set; }                       // null = any season
    public string? SeasonName { get; set; }
    public DateTime EndsAt { get; set; }                      // UTC
    public DateOnly EndsOn { get; set; }                      // the Sydney date it ends (inclusive)
    public bool IsRetired { get; set; }
    public string State { get; set; } = string.Empty;         // Active | Ended | Retired
    public int TimesApplied { get; set; }
    public DateTime CreatedAt { get; set; }
}
