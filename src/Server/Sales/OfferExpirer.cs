using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Shared.Enums;

namespace Stallions.Server.Sales;

public interface IOfferExpirer
{
    /// <summary>Expires Make an Offer listings past their season end, then pending offers that are past their expiry or on a closed listing.</summary>
    Task<int> RunAsync(CancellationToken ct = default);
    /// <summary>After a stud closes (or Staff cancel) a Make an Offer listing.</summary>
    Task ExpirePendingOffersAsync(Guid listingId);
}

/// <summary>
/// Closes offers and Make an Offer listings that have run out of time. Each row changes in its own
/// transaction; a row another instance changed first is skipped (concurrency stamp).
/// </summary>
public class OfferExpirer : IOfferExpirer
{
    private const int BatchSize = 50;

    private readonly IOfferRepository _offers;
    private readonly IListingRepository _listings;
    private readonly IAuditLogRepository _audit;
    private readonly IAuctionEmails _emails;
    private readonly ITransactionRunner _transactions;
    private readonly TimeProvider _clock;
    private readonly ILogger<OfferExpirer> _log;

    public OfferExpirer(IOfferRepository offers, IListingRepository listings, IAuditLogRepository audit, IAuctionEmails emails,
        ITransactionRunner transactions, TimeProvider clock, ILogger<OfferExpirer> log)
    {
        _offers = offers; _listings = listings; _audit = audit; _emails = emails;
        _transactions = transactions; _clock = clock; _log = log;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var changed = 0;
        foreach (var id in await _listings.GetMakeAnOfferIdsExpiredAsync(Now, BatchSize))
        {
            ct.ThrowIfCancellationRequested();
            if (await TryAsync(() => ExpireListingAsync(id), "expire listing", id)) changed++;
        }
        foreach (var id in await _offers.GetIdsToExpireAsync(Now, BatchSize))
        {
            ct.ThrowIfCancellationRequested();
            if (await TryAsync(() => ExpireOfferAsync(id), "expire offer", id)) changed++;
        }
        return changed;
    }

    public async Task ExpirePendingOffersAsync(Guid listingId)
    {
        foreach (var id in await _offers.GetPendingIdsForListingAsync(listingId))
            await TryAsync(() => ExpireOfferAsync(id, listingClosing: true), "expire offer", id);
    }

    private async Task<bool> ExpireListingAsync(Guid listingId)
    {
        var expired = await _transactions.RunAsync(async () =>
        {
            var now = Now;
            var listing = await _listings.GetMakeAnOfferWithDetailsAsync(listingId);
            if (listing is not { Status: ListingStatus.Active } || listing.ExpiresAt > now) return false;
            listing.Status = ListingStatus.Expired;
            listing.ClosedAt = now;
            await _listings.UpdateAsync(listing);
            await _audit.LogAsync("Listing", listing.Id, "MakeAnOfferExpired", null);
            return true;
        });
        // Each pending offer then closes in its own transaction (and the buyer is told).
        if (expired) await ExpirePendingOffersAsync(listingId);
        return expired;
    }

    private Task<bool> ExpireOfferAsync(Guid offerId, bool listingClosing = false) => _transactions.RunAsync(async () =>
    {
        var now = Now;
        var offer = await _offers.GetByIdAsync(offerId);
        if (offer is not { Status: OfferStatus.Pending }) return false;
        var listingClosed = listingClosing || offer.Listing.Status != ListingStatus.Active || offer.Listing.ExpiresAt <= now;
        if (!listingClosed && offer.ExpiresAt > now) return false;

        offer.Status = OfferStatus.Expired;
        await _offers.UpdateAsync(offer);
        await _audit.LogAsync("Offer", offer.Id, "OfferExpired", null, JsonSerializer.Serialize(new { ListingClosed = listingClosed }));
        await _emails.OfferExpiredAsync(offer.Listing, offer, offer.Buyer, listingClosed);
        return true;
    });

    private async Task<bool> TryAsync(Func<Task<bool>> step, string what, Guid id)
    {
        try { return await step(); }
        catch (DbUpdateConcurrencyException)
        {
            _log.LogInformation("Skipped: could not {What} {Id} - another instance changed it first", what, id);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Could not {What} {Id}; it will be retried on the next run", what, id);
            return false;
        }
    }
}
