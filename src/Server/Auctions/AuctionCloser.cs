using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Payments;
using Stallions.Shared;
using Stallions.Shared.Enums;

namespace Stallions.Server.Auctions;

/// <summary>
/// Closes ended auctions (hidden reserve; highest bid wins) and charges each winner's saved card
/// the listing's buyer fee, with a grace period after a failed charge. Every change runs in a
/// transaction; the provider is called between two transactions, never inside one. A row changed
/// by another instance in the meantime raises a concurrency conflict and is skipped.
/// </summary>
public class AuctionCloser : IAuctionCloser
{
    private readonly IListingRepository _listings;
    private readonly IBidRepository _bids;
    private readonly IPurchaseRepository _purchases;
    private readonly ISavedCardRepository _cards;
    private readonly IPlatformSettingsRepository _settings;
    private readonly IAuditLogRepository _audit;
    private readonly IAuctionEmails _emails;
    private readonly IPaymentProvider _provider;
    private readonly ITransactionRunner _transactions;
    private readonly TimeProvider _clock;
    private readonly AuctionCloseOptions _options;
    private readonly ILogger<AuctionCloser> _log;

    public AuctionCloser(
        IListingRepository listings, IBidRepository bids, IPurchaseRepository purchases, ISavedCardRepository cards,
        IPlatformSettingsRepository settings, IAuditLogRepository audit, IAuctionEmails emails,
        IPaymentProvider provider, ITransactionRunner transactions, TimeProvider clock,
        IOptions<AuctionCloseOptions> options, ILogger<AuctionCloser> log)
    {
        _listings = listings; _bids = bids; _purchases = purchases; _cards = cards; _settings = settings;
        _audit = audit; _emails = emails; _provider = provider; _transactions = transactions; _clock = clock;
        _options = options.Value; _log = log;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<AuctionCloseRun> RunAsync(CancellationToken ct = default)
    {
        var closed = 0;
        foreach (var id in await _listings.GetAuctionIdsDueToCloseAsync(Now - AuctionCloseOptions.CloseDelay, _options.BatchSize))
        {
            ct.ThrowIfCancellationRequested();
            if (await TryAsync(() => CloseAuctionAsync(id), "close auction", id)) closed++;
        }

        var charged = 0;
        foreach (var id in await _purchases.GetIdsDueForChargeAsync(Now, Now - AuctionCloseOptions.InterruptedAttemptAge, _options.BatchSize))
        {
            ct.ThrowIfCancellationRequested();
            if (await TryAsync(() => ChargeAsync(id), "charge sale record", id)) charged++;
        }

        return new AuctionCloseRun(closed, charged);
    }

    // One auction or sale record failing never stops the run; the next run picks it up again.
    private async Task<bool> TryAsync(Func<Task<bool>> step, string what, Guid id)
    {
        try
        {
            return await step();
        }
        catch (DbUpdateConcurrencyException)
        {
            _log.LogInformation("Skipped: could not {What} {Id} — another instance changed it first", what, id);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Could not {What} {Id}; it will be retried on the next run", what, id);
            return false;
        }
    }

    // ── Closing ────────────────────────────────────────────────────────────────

    private Task<bool> CloseAuctionAsync(Guid listingId) => _transactions.RunAsync(async () =>
    {
        var now = Now;
        var listing = await _listings.GetAuctionWithDetailsAsync(listingId);
        // Re-checked inside the transaction: another run may have closed it already.
        if (listing is not { Status: ListingStatus.Active } || listing.EndDateTime > now - AuctionCloseOptions.CloseDelay)
            return false;

        var bids = await _bids.GetByAuctionWithBuyersAsync(listingId);
        var highest = bids.Where(b => b.Status == BidStatus.Active)
            .OrderByDescending(b => b.AmountIncGst).ThenBy(b => b.PlacedAt).FirstOrDefault();
        listing.ClosedAt = now;

        if (highest == null)
            return await CloseUnsoldAsync(listing, bids, ListingCloseReason.NoBids);
        if (listing.IsReserveMetBy(highest.AmountIncGst) == false)
            return await CloseUnsoldAsync(listing, bids, ListingCloseReason.ReserveNotMet);
        if (listing.BuyerFeeIncGst is not { } fee)
        {
            _log.LogError("Auction {ListingId} has no buyer-fee snapshot, so the winner can't be charged; closing it unsold", listing.Id);
            return await CloseUnsoldAsync(listing, bids, ListingCloseReason.ChargeFailed, "MissingBuyerFeeSnapshot");
        }

        // The listing is saved first: if another instance got there first, this throws before anything else changes.
        listing.Status = ListingStatus.AwaitingPayment;
        await _listings.UpdateAsync(listing);

        // The winner's other open bids are just outbid; everyone else's open bids lose.
        var changed = new List<Bid>();
        foreach (var bid in bids.Where(b => b.Status is BidStatus.Active or BidStatus.Outbid))
        {
            var before = bid.Status;
            if (bid.Id == highest.Id) bid.Status = BidStatus.Won;
            else if (bid.BuyerUserId != highest.BuyerUserId) bid.Status = BidStatus.Lost;
            else bid.Status = BidStatus.Outbid;
            if (bid.Status != before) changed.Add(bid);
        }
        await _bids.UpdateRangeAsync(changed);

        var split = GstBreakdown.FromIncGst(fee);
        var purchase = new Purchase
        {
            ListingId = listing.Id,
            BuyerUserId = highest.BuyerUserId,
            BidId = highest.Id,
            TotalPriceIncGst = highest.AmountIncGst,
            BuyerFeeIncGst = split.IncGst,
            BuyerFeeExGst = split.ExGst,
            BuyerFeeGst = split.Gst,
            BalancePayableToStudIncGst = highest.AmountIncGst - split.IncGst,
            Status = PurchaseStatus.Pending,
            CreatedAt = now
        };
        await _purchases.AddAsync(purchase);

        await _audit.LogAsync("Listing", listing.Id, "AuctionClosed", null, JsonSerializer.Serialize(new
        {
            Outcome = "Won", WinningBidId = highest.Id, highest.AmountIncGst, PurchaseId = purchase.Id
        }));
        foreach (var bidder in Bidders(bids, except: highest.BuyerUserId))
            await _emails.AuctionLostAsync(listing, bidder);
        return true;
    });

    private async Task<bool> CloseUnsoldAsync(AuctionListing listing, IReadOnlyList<Bid> bids,
        ListingCloseReason reason, string? detail = null)
    {
        listing.Status = ListingStatus.Unsold;
        listing.CloseReason = reason;
        await _listings.UpdateAsync(listing);

        var open = bids.Where(b => b.Status is BidStatus.Active or BidStatus.Outbid).ToList();
        foreach (var bid in open) bid.Status = BidStatus.Lost;
        if (open.Count > 0) await _bids.UpdateRangeAsync(open);

        await _audit.LogAsync("Listing", listing.Id, "AuctionClosed", null,
            JsonSerializer.Serialize(new { Outcome = "Unsold", Reason = reason.ToString(), Detail = detail }));
        foreach (var bidder in Bidders(bids, except: null))
            await _emails.AuctionEndedWithoutSaleAsync(listing, bidder);
        await _emails.StudNoSaleAsync(listing, reason);
        return true;
    }

    // One email per bidder, however many bids they placed.
    private static IEnumerable<User> Bidders(IEnumerable<Bid> bids, Guid? except) =>
        bids.Where(b => b.BuyerUserId != except).GroupBy(b => b.BuyerUserId).Select(g => g.First().Buyer);

    // ── Charging ───────────────────────────────────────────────────────────────

    private sealed record ChargeAttempt(Guid PurchaseId, int AttemptNo, Guid BuyerUserId, decimal AmountIncGst, string Description);

    private async Task<bool> ChargeAsync(Guid purchaseId)
    {
        var attempt = await _transactions.RunAsync(() => StartAttemptAsync(purchaseId));
        if (attempt == null) return false;

        // Outside any transaction. If this throws (provider unreachable) the attempt stays
        // "started" and is repeated with the same key once it counts as interrupted.
        var result = await ChargeCardAsync(attempt);

        return await _transactions.RunAsync(() => RecordResultAsync(attempt, result));
    }

    private async Task<ChargeAttempt?> StartAttemptAsync(Guid purchaseId)
    {
        var now = Now;
        var purchase = await _purchases.GetForChargeAsync(purchaseId);
        if (purchase is not { Status: PurchaseStatus.Pending } || !IsDue(purchase, now)) return null;

        // An interrupted attempt keeps its number, so the provider's idempotency key returns the
        // original result instead of charging twice.
        var attemptNo = purchase.ChargeAttemptStartedAt != null ? purchase.ChargeAttempts : purchase.ChargeAttempts + 1;
        purchase.ChargeAttempts = attemptNo;
        purchase.ChargeAttemptStartedAt = now;
        purchase.RetryRequested = false;
        await _purchases.UpdateAsync(purchase);

        return new ChargeAttempt(purchase.Id, attemptNo, purchase.BuyerUserId, purchase.BuyerFeeIncGst,
            $"Buyer fee — {purchase.Listing.Stallion?.Name}, {purchase.Listing.Season?.Name}");
    }

    private static bool IsDue(Purchase p, DateTime now) => p.ChargeAttemptStartedAt is { } started
        ? started <= now - AuctionCloseOptions.InterruptedAttemptAge
        : p.ChargeAttempts == 0 || p.RetryRequested || p.ChargeDueBy <= now;

    private async Task<ChargeResult> ChargeCardAsync(ChargeAttempt attempt)
    {
        var card = await _cards.GetByUserIdAsync(attempt.BuyerUserId);
        if (card == null || card.Provider != _provider.Name || !card.IsValidOn(DateOnly.FromDateTime(Now)))
            return ChargeResult.Declined("no_valid_card", "No valid card on file.");

        return await _provider.ChargeSavedCardAsync(new ChargeRequest(
            card.ProviderCustomerId,
            card.ProviderPaymentMethodId,
            attempt.AmountIncGst,
            attempt.Description,
            new Dictionary<string, string> { ["kind"] = "buyer-fee", ["purchaseId"] = attempt.PurchaseId.ToString() },
            $"buyer-fee-{attempt.PurchaseId}-{attempt.AttemptNo}"));
    }

    private async Task<bool> RecordResultAsync(ChargeAttempt attempt, ChargeResult result)
    {
        var now = Now;
        var purchase = await _purchases.GetForChargeAsync(attempt.PurchaseId);
        if (purchase is not { Status: PurchaseStatus.Pending } || purchase.ChargeAttempts != attempt.AttemptNo
            || purchase.ChargeAttemptStartedAt == null)
        {
            _log.LogWarning("Charge result for sale record {PurchaseId} attempt {AttemptNo} arrived after it changed; ignored",
                attempt.PurchaseId, attempt.AttemptNo);
            return false;
        }

        var listing = (AuctionListing)purchase.Listing;
        purchase.ChargeAttemptStartedAt = null;

        if (result.Succeeded)
        {
            purchase.Status = PurchaseStatus.Completed;
            purchase.PaymentProvider = _provider.Name;
            purchase.PaymentReference = result.PaymentReference;
            purchase.PaidAt = now;
            purchase.LastChargeFailure = null;
            await _purchases.UpdateAsync(purchase);

            listing.Status = ListingStatus.Sold;
            listing.WinningBidId = purchase.BidId;
            await _listings.UpdateAsync(listing);

            await _audit.LogAsync("Purchase", purchase.Id, "BuyerFeeCharged", null, JsonSerializer.Serialize(new
            {
                purchase.BuyerFeeIncGst, purchase.BuyerFeeExGst, purchase.BuyerFeeGst, purchase.PaymentReference, attempt.AttemptNo
            }));
            await _emails.WonAndChargedAsync(listing, purchase, purchase.Buyer);
            await _emails.StudSaleConfirmationAsync(listing, purchase, purchase.Buyer);
            return true;
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
            listing.Status = ListingStatus.Unsold;
            listing.CloseReason = ListingCloseReason.ChargeFailed;
            await _listings.UpdateAsync(listing);
            await _audit.LogAsync("Purchase", purchase.Id, "BuyerFeeChargeAbandoned", null, JsonSerializer.Serialize(new
            {
                attempt.AttemptNo, result.FailureCode
            }));
            await _emails.PaymentAbandonedAsync(listing, purchase, purchase.Buyer);
            await _emails.StudNoSaleAsync(listing, ListingCloseReason.ChargeFailed);
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
        return true;
    }
}
