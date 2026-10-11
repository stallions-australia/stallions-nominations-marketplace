using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Email;

/// <summary>
/// Builds and queues the auction emails (through the outbox, in the caller's transaction).
/// The listing should have Stallion, Season and StudFarm loaded.
/// </summary>
public interface IAuctionEmails
{
    Task OutbidAsync(AuctionListing listing, Guid previousBidderUserId, decimal newHighBidIncGst);
    /// <summary>To a bidder who didn't win an auction that sold.</summary>
    Task AuctionLostAsync(AuctionListing listing, User bidder);
    /// <summary>To every bidder when the auction closed without a sale.</summary>
    Task AuctionEndedWithoutSaleAsync(AuctionListing listing, User bidder);
    Task WonAndChargedAsync(AuctionListing listing, Purchase purchase, User winner);
    Task PaymentFailedAsync(AuctionListing listing, Purchase purchase, User winner);
    Task PaymentAbandonedAsync(AuctionListing listing, Purchase purchase, User winner);
    Task StudSaleConfirmationAsync(AuctionListing listing, Purchase purchase, User winner);
    Task StudNoSaleAsync(AuctionListing listing, ListingCloseReason reason);
}
