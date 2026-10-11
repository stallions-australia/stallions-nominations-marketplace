namespace Stallions.Shared.DTOs.Offers;

public class MakeOfferRequest
{
    /// <summary>The offered price inc. GST; at least the listing's buyer fee.</summary>
    public decimal AmountIncGst { get; set; }
}
