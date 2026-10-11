namespace Stallions.Shared.DTOs.IntroOffers;

// Staff-only. Create or edit. Once an offer has been applied, only Name and EndsOn may change.
public class SaveIntroOfferRequest
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;          // Waiver | AmountOff
    public decimal? AmountOffIncGst { get; set; }             // required (> 0) for AmountOff; ignored for Waiver
    public Guid? StudFarmId { get; set; }
    public Guid? SeasonId { get; set; }
    /// <summary>The last Sydney day the offer applies (to subscriptions created by 23:59:59 that day).</summary>
    public DateOnly EndsOn { get; set; }
}
