using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Sales;
using Stallions.Shared;
using Stallions.Shared.DTOs.Offers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

/// <summary>
/// Private offers on Make an Offer listings. Buyers make and withdraw them; the owning stud accepts
/// (the buyer fee is charged at once through the shared BuyerFeeCharger) or declines. Only the stud,
/// Staff and the offering buyer ever see an offer.
/// </summary>
public class OfferService : IOfferService
{
    private const decimal MaxOfferIncGst = 10_000_000m;

    private readonly IOfferRepository _offers;
    private readonly IListingRepository _listings;
    private readonly IPurchaseRepository _purchases;
    private readonly IStudFarmRepository _farms;
    private readonly IUserService _users;
    private readonly IPlatformSettingsRepository _settings;
    private readonly IAuditLogRepository _audit;
    private readonly IAuctionEmails _emails;
    private readonly IBuyerFeeCharger _charger;
    private readonly ITransactionRunner _transactions;
    private readonly BuyerEligibility _eligibility;
    private readonly TimeProvider _clock;
    private readonly ILogger<OfferService> _log;

    public OfferService(
        IOfferRepository offers, IListingRepository listings, IPurchaseRepository purchases, IStudFarmRepository farms,
        IUserService users, IPlatformSettingsRepository settings, IAuditLogRepository audit, IAuctionEmails emails,
        IBuyerFeeCharger charger, ITransactionRunner transactions, ITermsRepository terms, ICardService cards,
        TimeProvider clock, ILogger<OfferService> log)
    {
        _offers = offers; _listings = listings; _purchases = purchases; _farms = farms; _users = users;
        _settings = settings; _audit = audit; _emails = emails; _charger = charger; _transactions = transactions;
        _eligibility = new BuyerEligibility(terms, cards); _clock = clock; _log = log;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private static bool IsOpen(MakeAnOfferListing listing, DateTime now) =>
        listing.Status == ListingStatus.Active && listing.ExpiresAt > now;

    public async Task<ServiceResult<OfferDto>> MakeAsync(Guid listingId, MakeOfferRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<OfferDto>.Forbidden();
        if (await _eligibility.CheckAsync(caller, BuyerAction.Offer) is { } notEligible)
            return notEligible.Forbidden
                ? ServiceResult<OfferDto>.Forbidden(notEligible.Message)
                : ServiceResult<OfferDto>.BadRequest(notEligible.Message);

        var amount = request.AmountIncGst;
        if (decimal.Round(amount, 2) != amount)
            return ServiceResult<OfferDto>.BadRequest("Offers are in dollars and cents.");
        if (amount > MaxOfferIncGst)
            return ServiceResult<OfferDto>.BadRequest($"Offers can't be more than {MaxOfferIncGst:C0}.");

        try
        {
            return await _transactions.RunAsync(async () =>
            {
                var now = Now;
                // Re-read inside the transaction: the listing may just have closed or expired.
                var listing = await _listings.GetMakeAnOfferWithDetailsAsync(listingId);
                if (listing == null) return ServiceResult<OfferDto>.NotFound("Listing not found.");
                if (!IsOpen(listing, now) || listing.BuyerFeeIncGst is not { } fee)
                    return ServiceResult<OfferDto>.BadRequest("This listing is no longer taking offers.");
                if (amount < fee)
                    return ServiceResult<OfferDto>.BadRequest(
                        $"Offers must be at least {fee:C2}, the buyer fee, which forms part of the price.");
                if (await _offers.GetPendingAsync(listingId, caller.Id) != null)
                    return ServiceResult<OfferDto>.Conflict(
                        "You already have an offer with the stud — withdraw it to make a new one.");

                var settings = await _settings.GetAsync();
                var offer = new Offer
                {
                    ListingId = listingId, BuyerUserId = caller.Id, AmountIncGst = amount, Status = OfferStatus.Pending,
                    CreatedAt = now, ExpiresAt = now.AddDays(settings.OfferExpiryDays)
                };
                await _offers.AddAsync(offer);
                await _audit.LogAsync("Offer", offer.Id, "OfferMade", caller.Id,
                    JsonSerializer.Serialize(new { ListingId = listingId, offer.AmountIncGst, offer.ExpiresAt }));
                await _emails.OfferReceivedAsync(listing, offer, caller);
                return ServiceResult<OfferDto>.Created(ToBuyerDto(offer, listing, null, now));
            });
        }
        catch (DbUpdateException)
        {
            // The unique "one pending offer per buyer per listing" index caught a simultaneous second offer.
            return ServiceResult<OfferDto>.Conflict("You already have an offer with the stud — withdraw it to make a new one.");
        }
    }

    public async Task<ServiceResult> WithdrawAsync(Guid offerId)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult.Forbidden();

        var offer = await _offers.GetByIdAsync(offerId);
        if (offer == null) return ServiceResult.NotFound("Offer not found.");
        if (offer.BuyerUserId != caller.Id) return ServiceResult.Forbidden();
        if (offer.Status != OfferStatus.Pending) return ServiceResult.BadRequest("Only a pending offer can be withdrawn.");

        offer.Status = OfferStatus.Withdrawn;
        offer.RespondedAt = Now;
        try { await _offers.UpdateAsync(offer); }
        catch (DbUpdateConcurrencyException) { return ServiceResult.Conflict("This offer has just changed — refresh and try again."); }
        await _audit.LogAsync("Offer", offer.Id, "OfferWithdrawn", caller.Id);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<IReadOnlyList<OfferDto>>> GetMineAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<IReadOnlyList<OfferDto>>.Forbidden();

        var offers = await _offers.GetByBuyerAsync(caller.Id);
        var sales = await SalesByOfferAsync(offers);
        var now = Now;
        return ServiceResult<IReadOnlyList<OfferDto>>.Ok(offers
            .Select(o => ToBuyerDto(o, o.Listing, sales.GetValueOrDefault(o.Id), now)).ToList());
    }

    public async Task<ServiceResult<OfferDto?>> GetMyOfferForListingAsync(Guid listingId)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<OfferDto?>.Forbidden();

        var offer = await _offers.GetLatestForBuyerAsync(listingId, caller.Id);
        if (offer == null) return ServiceResult<OfferDto?>.Ok(null);
        var sales = await SalesByOfferAsync(new[] { offer });
        return ServiceResult<OfferDto?>.Ok(ToBuyerDto(offer, offer.Listing, sales.GetValueOrDefault(offer.Id), Now));
    }

    public async Task<ServiceResult<IReadOnlyList<StudOfferDto>>> GetForListingAsync(Guid listingId)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<IReadOnlyList<StudOfferDto>>.Forbidden();

        var listing = await _listings.GetMakeAnOfferWithDetailsAsync(listingId);
        if (listing == null) return ServiceResult<IReadOnlyList<StudOfferDto>>.NotFound("Listing not found.");
        if (!await CanManageAsync(caller, listing, allowStaff: true)) return ServiceResult<IReadOnlyList<StudOfferDto>>.Forbidden();

        var offers = (await _offers.GetForListingAsync(listingId))
            .Where(o => o.Status is OfferStatus.Pending or OfferStatus.Accepted).ToList();
        var sales = await SalesByOfferAsync(offers);
        return ServiceResult<IReadOnlyList<StudOfferDto>>.Ok(offers.Select(o =>
        {
            var sale = sales.GetValueOrDefault(o.Id);
            return new StudOfferDto
            {
                Id = o.Id, BuyerName = o.Buyer?.DisplayName ?? string.Empty, BuyerEmail = o.Buyer?.Email ?? string.Empty,
                AmountIncGst = o.AmountIncGst, Status = o.Status.ToString(), CreatedAt = o.CreatedAt,
                ExpiresAt = o.ExpiresAt, RespondedAt = o.RespondedAt,
                PurchaseId = sale?.Id, SaleStatus = sale?.Status.ToString(), ChargeDueBy = sale?.ChargeDueBy
            };
        }).ToList());
    }

    public async Task<ServiceResult> DeclineAsync(Guid offerId)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult.Forbidden();

        var offer = await _offers.GetByIdAsync(offerId);
        if (offer == null) return ServiceResult.NotFound("Offer not found.");
        if (!await CanManageAsync(caller, offer.Listing, allowStaff: false)) return ServiceResult.Forbidden();
        if (offer.Status != OfferStatus.Pending) return ServiceResult.BadRequest("Only a pending offer can be declined.");

        offer.Status = OfferStatus.Declined;
        offer.RespondedAt = Now;
        try { await _offers.UpdateAsync(offer); }
        catch (DbUpdateConcurrencyException) { return ServiceResult.Conflict("This offer has just changed — refresh and try again."); }
        await _audit.LogAsync("Offer", offer.Id, "OfferDeclined", caller.Id);
        await _emails.OfferDeclinedAsync(offer.Listing, offer, offer.Buyer);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<AcceptOfferResultDto>> AcceptAsync(Guid offerId)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<AcceptOfferResultDto>.Forbidden();

        var check = await _offers.GetByIdAsync(offerId);
        if (check == null) return ServiceResult<AcceptOfferResultDto>.NotFound("Offer not found.");
        if (!await CanManageAsync(caller, check.Listing, allowStaff: false)) return ServiceResult<AcceptOfferResultDto>.Forbidden();

        Purchase? sale;
        try
        {
            sale = await _transactions.RunAsync(async () =>
            {
                var now = Now;
                var offer = await _offers.GetByIdAsync(offerId);
                if (offer is not { Status: OfferStatus.Pending } || offer.ExpiresAt <= now
                    || !IsOpen(offer.Listing, now) || offer.Listing.BuyerFeeIncGst is not { } fee)
                    return null;

                offer.Status = OfferStatus.Accepted;
                offer.RespondedAt = now;
                await _offers.UpdateAsync(offer); // a concurrent accept/withdraw makes this throw

                var split = GstBreakdown.FromIncGst(fee);
                var purchase = new Purchase
                {
                    ListingId = offer.ListingId, BuyerUserId = offer.BuyerUserId, OfferId = offer.Id,
                    TotalPriceIncGst = offer.AmountIncGst,
                    BuyerFeeIncGst = split.IncGst, BuyerFeeExGst = split.ExGst, BuyerFeeGst = split.Gst,
                    BalancePayableToStudIncGst = offer.AmountIncGst - split.IncGst,
                    Status = PurchaseStatus.Pending, CreatedAt = now
                };
                await _purchases.AddAsync(purchase);
                await _audit.LogAsync("Offer", offer.Id, "OfferAccepted", caller.Id,
                    JsonSerializer.Serialize(new { offer.AmountIncGst, PurchaseId = purchase.Id }));
                return purchase;
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<AcceptOfferResultDto>.Conflict("This offer has just changed — refresh and try again.");
        }
        if (sale == null)
            return ServiceResult<AcceptOfferResultDto>.BadRequest("This offer can no longer be accepted (it has expired, been withdrawn or answered, or the listing has closed).");

        string charge;
        try
        {
            charge = (await _charger.ChargeAsync(sale.Id)).ToString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The attempt stays interrupted; the background job repeats it with the same key.
            _log.LogWarning(ex, "Buyer-fee charge for accepted offer {OfferId} (sale record {PurchaseId}) could not reach the provider", offerId, sale.Id);
            charge = "Processing";
        }

        var current = await _purchases.GetByIdAsync(sale.Id) ?? sale;
        return ServiceResult<AcceptOfferResultDto>.Ok(new AcceptOfferResultDto
        {
            OfferId = offerId, PurchaseId = sale.Id, Charge = charge, SaleStatus = current.Status.ToString(),
            ChargeDueBy = current.ChargeDueBy, LastChargeFailure = current.LastChargeFailure
        });
    }

    private async Task<bool> CanManageAsync(User caller, Listing listing, bool allowStaff)
    {
        if (allowStaff && caller.Role == UserRole.Staff) return true;
        if (caller.Role != UserRole.StudFarmAdmin) return false;
        var farm = await _farms.GetByUserIdAsync(caller.Id);
        return farm != null && farm.Id == listing.StudFarmId;
    }

    private async Task<Dictionary<Guid, Purchase>> SalesByOfferAsync(IEnumerable<Offer> offers)
    {
        var accepted = offers.Where(o => o.Status == OfferStatus.Accepted).Select(o => o.Id).ToList();
        if (accepted.Count == 0) return new();
        return (await _purchases.GetByOfferIdsAsync(accepted))
            .GroupBy(p => p.OfferId!.Value).ToDictionary(g => g.Key, g => g.First());
    }

    private static OfferDto ToBuyerDto(Offer o, MakeAnOfferListing listing, Purchase? sale, DateTime now) => new()
    {
        Id = o.Id, ListingId = o.ListingId,
        StallionName = listing.Stallion?.Name ?? string.Empty, SeasonName = listing.Season?.Name ?? string.Empty,
        StudFarmName = listing.StudFarm?.Name ?? string.Empty,
        AmountIncGst = o.AmountIncGst, Status = o.Status.ToString(), CreatedAt = o.CreatedAt, ExpiresAt = o.ExpiresAt,
        RespondedAt = o.RespondedAt, PurchaseId = sale?.Id, SaleStatus = sale?.Status.ToString(),
        ChargeDueBy = sale?.ChargeDueBy, ListingOpen = IsOpen(listing, now)
    };
}
