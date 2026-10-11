using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

/// <summary>
/// A Staff-configured waiver or discount on the listing fee, applied automatically to subscriptions
/// created before EndsAt. Never deleted; once applied, only Name and EndsAt may change (or it is retired).
/// </summary>
public class IntroOffer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Shown on the tax invoice line: "Intro offer: {Name}".</summary>
    public string Name { get; set; } = string.Empty;
    public IntroOfferType Type { get; set; }
    /// <summary>Dollars off the standard listing fee (inc. GST) for AmountOff; null for Waiver.</summary>
    public decimal? AmountOffIncGst { get; set; }
    /// <summary>Null = all studs.</summary>
    public Guid? StudFarmId { get; set; }
    /// <summary>Null = any season.</summary>
    public Guid? SeasonId { get; set; }
    /// <summary>UTC. Applies to subscriptions created before this instant (end of the chosen Sydney day).</summary>
    public DateTime EndsAt { get; set; }
    public bool IsRetired { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    // Navigation properties
    public StudFarm? StudFarm { get; set; }
    public Season? Season { get; set; }
}
