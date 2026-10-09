namespace Stallions.Shared.DTOs.Payments;

public class SavedCardDto
{
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public int ExpMonth { get; set; }
    public int ExpYear { get; set; }
    /// <summary>False once the card is past the end of its expiry month — bidding is then blocked.</summary>
    public bool IsValid { get; set; }
}
