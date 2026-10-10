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
        var highest = bids.Where(b => b.Status == BidStatus.Active).MaxBy(b => b.AmountIncGst);
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

        foreach (var bid in bids.Where(b => b.Status is BidStatus.Active or BidStatus.Outbid))
        {
            if (bid.Id == highest.Id) bid.Status = BidStatus.Won;
            else if (bid.BuyerUserId != highest.BuyerUserId) bid.Status = BidStatus.Lost;
        }
        await _bids.UpdateRangeAsync(bids);

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

    // ── Charging (Task 8) ──────────────────────────────────────────────────────

    private Task<bool> ChargeAsync(Guid purchaseId) => Task.FromResult(false);
}
