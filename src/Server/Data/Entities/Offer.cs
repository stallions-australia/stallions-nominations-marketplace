using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

/// <summary>A buyer's private offer on a Make an Offer listing. Only the stud (and Staff) see it.</summary>
public class Offer : IHasConcurrencyStamp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public Guid BuyerUserId { get; set; }
    public decimal AmountIncGst { get; set; }
    public OfferStatus Status { get; set; } = OfferStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public MakeAnOfferListing Listing { get; set; } = null!;
    public User Buyer { get; set; } = null!;
}
