using System.Text.Json;
using Stallions.Server.Auctions;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Payments;
using Stallions.Shared.Enums;

namespace Stallions.Server.Sales;

/// <summary>What one charge run did to a sale record.</summary>
public enum ChargeOutcome
{
    /// <summary>Not due, already handled, or flagged for Staff — nothing sent.</summary>
    Skipped,
    Paid,
    /// <summary>Declined; the sale record waits in its grace period.</summary>
    PaymentFailed,
    /// <summary>The final attempt failed: the sale record is Voided.</summary>
    NoSale
}

public interface IBuyerFeeCharger
{
    /// <summary>
    /// Makes one charge attempt for a Pending sale record (auction win or accepted offer) if it is due.
    /// Throws when the provider is unreachable; the attempt is then repeated later with the same key.
    /// </summary>
    Task<ChargeOutcome> ChargeAsync(Guid purchaseId);
}

/// <summary>
/// The buyer-fee charge pipeline (moved from AuctionCloser): snapshot the request, call the provider
/// between two transactions, record the result. Only the listing-side effect depends on whether the
/// sale came from an auction win (listing Sold / Unsold) or an accepted offer (listing unchanged).
/// </summary>
public class BuyerFeeCharger : IBuyerFeeCharger
{
    private readonly IPurchaseRepository _purchases;
    private readonly IListingRepository _listings;
    private readonly IBidRepository _bids;
    private readonly ISavedCardRepository _cards;
    private readonly IPlatformSettingsRepository _settings;
    private readonly IAuditLogRepository _audit;
    private readonly IAuctionEmails _emails;
    private readonly IPaymentProvider _provider;
    private readonly ITransactionRunner _transactions;
    private readonly TimeProvider _clock;
    private readonly ILogger<BuyerFeeCharger> _log;

    public BuyerFeeCharger(
        IPurchaseRepository purchases, IListingRepository listings, IBidRepository bids, ISavedCardRepository cards,
        IPlatformSettingsRepository settings, IAuditLogRepository audit, IAuctionEmails emails,
        IPaymentProvider provider, ITransactionRunner transactions, TimeProvider clock, ILogger<BuyerFeeCharger> log)
    {
        _purchases = purchases; _listings = listings; _bids = bids; _cards = cards; _settings = settings;
        _audit = audit; _emails = emails; _provider = provider; _transactions = transactions; _clock = clock; _log = log;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    /// <summary>The exact request for one attempt, snapshotted on the sale record so a repeat can't differ.</summary>
    private sealed record ChargeAttempt(
        Guid PurchaseId, Guid ListingId, int AttemptNo, decimal AmountIncGst,
        string? CustomerId, string? PaymentMethodId, string? Description);

    public async Task<ChargeOutcome> ChargeAsync(Guid purchaseId)
    {
        var attempt = await _transactions.RunAsync(() => StartAttemptAsync(purchaseId));
        if (attempt == null) return ChargeOutcome.Skipped;

        // Outside any transaction. If this throws (provider unreachable) the attempt stays
        // "started" and is repeated with the same key and the same request.
        var result = await ChargeCardAsync(attempt);

        return await _transactions.RunAsync(() => RecordResultAsync(attempt, result));
    }

    private async Task<ChargeAttempt?> StartAttemptAsync(Guid purchaseId)
    {
        var now = Now;
        var purchase = await _purchases.GetForChargeAsync(purchaseId);
        if (purchase is not { Status: PurchaseStatus.Pending } || purchase.ChargeNeedsAttention || !IsDue(purchase, now))
            return null;

        if (purchase.ChargeAttemptStartedAt is { } started)
        {
            if (started <= now - AuctionCloseOptions.StuckAttemptAge)
            {
                // The provider has not answered for too long: the charge may or may not have gone
                // through, so nothing more is sent until Staff have checked.
                purchase.ChargeNeedsAttention = true;
                await _purchases.UpdateAsync(purchase);
                await _audit.LogAsync("Purchase", purchase.Id, "BuyerFeeChargeStuck", null, JsonSerializer.Serialize(new
                {
                    PurchaseId = purchase.Id, AttemptNo = purchase.ChargeAttempts, StartedAt = started
                }));
                _log.LogError("Buyer-fee charge for sale record {PurchaseId} attempt {AttemptNo} has had no result since {StartedAt}: " +
                    "needs Staff attention: check the payment provider for this purchase before any further charge",
                    purchase.Id, purchase.ChargeAttempts, started);
                return null;
            }

            // An interrupted attempt repeats exactly: same number (so the provider's idempotency key
            // returns the original result instead of charging twice) and the same snapshotted card.
            // The start time is left alone so the attempt's age keeps counting towards "stuck".
            // A retry request made meanwhile is kept for the next attempt.
            purchase.ChargeAttemptLastSentAt = now;
            await _purchases.UpdateAsync(purchase);
            return new ChargeAttempt(purchase.Id, purchase.ListingId, purchase.ChargeAttempts, purchase.BuyerFeeIncGst,
                purchase.ChargeCustomerId, purchase.ChargePaymentMethodId, purchase.ChargeDescription);
        }

        // A new attempt looks up the buyer's current card and snapshots the request.
        var card = await _cards.GetByUserIdAsync(purchase.BuyerUserId);
        var usable = card != null && card.Provider == _provider.Name && card.IsValidOn(DateOnly.FromDateTime(now));
        purchase.ChargeAttempts += 1;
        purchase.ChargeAttemptStartedAt = now;
        purchase.ChargeAttemptLastSentAt = now;
        purchase.RetryRequested = false;
        purchase.ChargeCustomerId = usable ? card!.ProviderCustomerId : null;
        purchase.ChargePaymentMethodId = usable ? card!.ProviderPaymentMethodId : null;
        var description = $"Buyer fee — {purchase.Listing.Stallion?.Name ?? "nomination"}, {purchase.Listing.Season?.Name ?? "season"}";
        purchase.ChargeDescription = description.Length > 300 ? description[..300] : description;
        await _purchases.UpdateAsync(purchase);

        return new ChargeAttempt(purchase.Id, purchase.ListingId, purchase.ChargeAttempts, purchase.BuyerFeeIncGst,
            purchase.ChargeCustomerId, purchase.ChargePaymentMethodId, purchase.ChargeDescription);
    }

    private static bool IsDue(Purchase p, DateTime now) => p.ChargeAttemptStartedAt != null
        ? p.ChargeAttemptLastSentAt <= now - AuctionCloseOptions.InterruptedAttemptAge
        : p.ChargeAttempts == 0 || p.RetryRequested || p.ChargeDueBy <= now;

    private async Task<ChargeResult> ChargeCardAsync(ChargeAttempt attempt)
    {
        if (string.IsNullOrEmpty(attempt.PaymentMethodId) || string.IsNullOrEmpty(attempt.CustomerId))
            return ChargeResult.Declined("no_valid_card", "No valid card on file.");

        return await _provider.ChargeSavedCardAsync(new ChargeRequest(
            attempt.CustomerId,
            attempt.PaymentMethodId,
            attempt.AmountIncGst,
            attempt.Description ?? "Buyer fee",
            new Dictionary<string, string>
            {
                ["kind"] = "buyer-fee",
                ["purchaseId"] = attempt.PurchaseId.ToString(),
                ["listingId"] = attempt.ListingId.ToString()
            },
            $"buyer-fee-{attempt.PurchaseId}-{attempt.AttemptNo}"));
    }

    private async Task<ChargeOutcome> RecordResultAsync(ChargeAttempt attempt, ChargeResult result)
    {
        var now = Now;
        var purchase = await _purchases.GetForChargeAsync(attempt.PurchaseId);
        if (purchase is not { Status: PurchaseStatus.Pending } || purchase.ChargeAttempts != attempt.AttemptNo
            || purchase.ChargeAttemptStartedAt == null)
        {
            if (result.Succeeded && purchase is { Status: PurchaseStatus.Completed } && purchase.PaymentReference == result.PaymentReference)
            {
                _log.LogInformation("Duplicate confirmation of the recorded buyer-fee charge {PaymentReference} for sale record {PurchaseId}",
                    result.PaymentReference, attempt.PurchaseId);
            }
            else if (result.Succeeded)
            {
                // Money was taken but the sale record can no longer take it: Staff must refund or reconcile.
                _log.LogError("Buyer-fee charge succeeded for sale record {PurchaseId} attempt {AttemptNo} (reference {PaymentReference}) " +
                    "but the sale record had changed; Staff must refund or reconcile it",
                    attempt.PurchaseId, attempt.AttemptNo, result.PaymentReference);
                await _audit.LogAsync("Purchase", attempt.PurchaseId, "BuyerFeeChargeUnmatched", null, JsonSerializer.Serialize(new
                {
                    attempt.PurchaseId, attempt.AttemptNo, result.PaymentReference, attempt.AmountIncGst
                }));
            }
            else
            {
                _log.LogWarning("Charge result for sale record {PurchaseId} attempt {AttemptNo} arrived after it changed; ignored",
                    attempt.PurchaseId, attempt.AttemptNo);
            }
            return ChargeOutcome.Skipped;
        }

        // An auction win changes the listing (Sold / Unsold); an accepted offer leaves its listing open.
        var listing = purchase.Listing;
        var auction = purchase.BidId != null ? listing as AuctionListing : null;
        purchase.ChargeAttemptStartedAt = null;
        purchase.ChargeAttemptLastSentAt = null;
        purchase.ChargeNeedsAttention = false; // a real result arrived, so the attempt is no longer stuck

        if (result.Succeeded)
        {
            purchase.Status = PurchaseStatus.Completed;
            purchase.PaymentProvider = _provider.Name;
            purchase.PaymentReference = result.PaymentReference;
            purchase.PaidAt = now;
            purchase.LastChargeFailure = null;
            await _purchases.UpdateAsync(purchase);

            if (auction != null)
            {
                auction.Status = ListingStatus.Sold;
                auction.WinningBidId = purchase.BidId;
                await _listings.UpdateAsync(auction);
            }

            await _audit.LogAsync("Purchase", purchase.Id, "BuyerFeeCharged", null, JsonSerializer.Serialize(new
            {
                purchase.BuyerFeeIncGst, purchase.BuyerFeeExGst, purchase.BuyerFeeGst, purchase.PaymentReference, attempt.AttemptNo
            }));
            await _emails.WonAndChargedAsync(listing, purchase, purchase.Buyer);
            await _emails.StudSaleConfirmationAsync(listing, purchase, purchase.Buyer);
            return ChargeOutcome.Paid;
        }

        var failure = result.FailureMessage ?? "The payment was declined.";
        purchase.LastChargeFailure = failure.Length > 500 ? failure[..500] : failure;

        if (purchase.ChargeDueBy == null)
        {
            // First failure: the grace period is read from Staff settings now.
            var settings = await _settings.GetAsync();
            purchase.ChargeDueBy = now.AddHours(settings.ChargeGracePeriodHours);
            await _purchases.UpdateAsync(purchase);
            await _audit.LogAsync("Purchase", purchase.Id, "BuyerFeeChargeFailed", null, JsonSerializer.Serialize(new
            {
                attempt.AttemptNo, result.FailureCode, purchase.ChargeDueBy
            }));
            await _emails.PaymentFailedAsync(listing, purchase, purchase.Buyer);
        }
        else if (now >= purchase.ChargeDueBy)
        {
            // Final attempt failed: no sale. Not offered to the next bidder.
            purchase.Status = PurchaseStatus.Voided;
            await _purchases.UpdateAsync(purchase);
            if (auction != null)
            {
                auction.Status = ListingStatus.Unsold;
                auction.CloseReason = ListingCloseReason.ChargeFailed;
                await _listings.UpdateAsync(auction);
                var winningBid = purchase.BidId is { } bidId ? await _bids.GetByIdAsync(bidId) : null;
                if (winningBid != null)
                {
                    winningBid.Status = BidStatus.Lost; // no sale, so the bid no longer shows as Won
                    await _bids.UpdateAsync(winningBid);
                }
            }
            await _audit.LogAsync("Purchase", purchase.Id, "BuyerFeeChargeAbandoned", null, JsonSerializer.Serialize(new
            {
                attempt.AttemptNo, result.FailureCode
            }));
            await _emails.PaymentAbandonedAsync(listing, purchase, purchase.Buyer);
            await _emails.StudNoSaleAsync(listing, ListingCloseReason.ChargeFailed);
            return ChargeOutcome.NoSale;
        }
        else
        {
            // A retry during the grace period failed too: the deadline stands; no repeat email.
            await _purchases.UpdateAsync(purchase);
            await _audit.LogAsync("Purchase", purchase.Id, "BuyerFeeChargeFailed", null, JsonSerializer.Serialize(new
            {
                attempt.AttemptNo, result.FailureCode, purchase.ChargeDueBy
            }));
        }
        return ChargeOutcome.PaymentFailed;
    }
}
