namespace Stallions.Server.Data.Entities;

/// <summary>
/// A buyer's saved card at the payment provider — one per buyer. Holds provider references and
/// display details only; raw card data is never stored.
/// </summary>
public class SavedCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderCustomerId { get; set; } = string.Empty;
    public string ProviderPaymentMethodId { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public int ExpMonth { get; set; }
    public int ExpYear { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User User { get; set; } = null!;

    /// <summary>A card is usable until the last day of its expiry month.</summary>
    public bool IsValidOn(DateOnly date) =>
        ExpMonth is >= 1 and <= 12 && ExpYear is >= 1 and <= 9998
        && date <= new DateOnly(ExpYear, ExpMonth, DateTime.DaysInMonth(ExpYear, ExpMonth));
}
