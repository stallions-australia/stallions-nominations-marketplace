namespace Stallions.Shared.DTOs.IntroOffers;

/// <summary>What a new subscription for this stud and season would cost now, with the best intro offer.</summary>
public class IntroOfferPreviewDto
{
    public decimal StandardFeeIncGst { get; set; }
    public decimal FeeIncGst { get; set; }
    public Guid? IntroOfferId { get; set; }
    public string? IntroOfferName { get; set; }
}
