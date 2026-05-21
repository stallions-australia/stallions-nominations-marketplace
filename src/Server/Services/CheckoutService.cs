using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Options;
using Stallions.Shared.DTOs.Checkout;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public class CheckoutService : ICheckoutService
{
    private readonly IListingRepository _listingRepo;
    private readonly IBidRepository _bidRepo;
    private readonly IPurchaseRepository _purchaseRepo;
    private readonly INominationBindingRepository _bindingRepo;
    private readonly IAuditLogRepository _auditRepo;
    private readonly IUserService _users;
    private readonly IOptions<CheckoutOptions> _options;
    private readonly AppDbContext _db;

    public CheckoutService(
        IListingRepository listingRepo,
        IBidRepository bidRepo,
        IPurchaseRepository purchaseRepo,
        INominationBindingRepository bindingRepo,
        IAuditLogRepository auditRepo,
        IUserService users,
        IOptions<CheckoutOptions> options,
        AppDbContext db)
    {
        _listingRepo = listingRepo;
        _bidRepo = bidRepo;
        _purchaseRepo = purchaseRepo;
        _bindingRepo = bindingRepo;
        _auditRepo = auditRepo;
        _users = users;
        _options = options;
        _db = db;
    }

    public async Task<ServiceResult<CheckoutResponse>> InitiateCheckoutAsync(Guid listingId, CheckoutRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<CheckoutResponse>.Forbidden();

        if (caller.Status != UserStatus.Active)
            return ServiceResult<CheckoutResponse>.Forbidden("Your account must be verified before you can purchase.");

        if (string.IsNullOrWhiteSpace(request.MareName))
            return ServiceResult<CheckoutResponse>.BadRequest("Mare name is required to complete a purchase.");

        var listing = await _listingRepo.GetByIdAsync(listingId);
        if (listing == null)
            return ServiceResult<CheckoutResponse>.NotFound("Listing not found.");

        if (listing.Status != ListingStatus.Active)
            return ServiceResult<CheckoutResponse>.BadRequest("This listing is no longer available.");

        if (listing.PlatformFeePercent == null)
            return ServiceResult<CheckoutResponse>.BadRequest("This listing is not ready for purchase. Please contact Stallions Australia.");

        decimal totalPrice;
        Guid? bidId = null;

        if (listing is FixedPriceListing fpl)
        {
            if (fpl.QuantityRemaining <= 0)
                return ServiceResult<CheckoutResponse>.BadRequest("This listing is sold out.");

            totalPrice = fpl.PriceIncGst;
        }
        else if (listing is AuctionListing auction)
        {
            var winningBid = await _bidRepo.GetHighestBidAsync(auction.Id);
            if (listing is AuctionListing auctionListing && auctionListing.EndDateTime > DateTime.UtcNow)
                return ServiceResult<CheckoutResponse>.BadRequest("This auction has not ended yet. You cannot checkout until the auction closes.");

            if (winningBid == null || winningBid.BuyerUserId != caller.Id)
                return ServiceResult<CheckoutResponse>.Forbidden("You are not the winning bidder on this auction.");

            totalPrice = winningBid.AmountIncGst;
            bidId = winningBid.Id;
        }
        else
        {
            return ServiceResult<CheckoutResponse>.BadRequest("Unknown listing type.");
        }

        // Calculate GST breakdown
        var feePercent = listing.PlatformFeePercent!.Value;
        var feeIncGst = Math.Round(totalPrice * (feePercent / 100m), 2);
        var feeGst = Math.Round(feeIncGst / 11m, 2);
        var feeExGst = feeIncGst - feeGst;

        var purchase = new Purchase
        {
            ListingId = listingId,
            BuyerUserId = caller.Id,
            BidId = bidId,
            TotalPriceIncGst = totalPrice,
            PlatformFeeIncGst = feeIncGst,
            PlatformFeeExGst = feeExGst,
            PlatformFeeGst = feeGst,
            MareName = request.MareName,
            MareRegistration = request.MareRegistration,
            MareBreed = request.MareBreed,
            Status = PurchaseStatus.Pending
        };

        var created = await _purchaseRepo.AddAsync(purchase);

        var response = new CheckoutResponse
        {
            PurchaseId = created.Id,
            Disclosure = new CheckoutDisclosureDto
            {
                TotalPriceIncGst = totalPrice,
                PlatformFeeIncGst = feeIncGst,
                StudFarmBalanceArrangement = _options.Value.StudFarmBalanceArrangement,
                RefundPolicy = _options.Value.RefundPolicy
            }
        };

        return ServiceResult<CheckoutResponse>.Created(response);
    }

    public async Task<ServiceResult> CompleteCheckoutAsync(Guid purchaseId, string? webhookSecret)
    {
        if (string.IsNullOrEmpty(webhookSecret) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(webhookSecret),
                Encoding.UTF8.GetBytes(_options.Value.WebhookSecret)))
            return ServiceResult.Forbidden("Invalid webhook secret.");

        var purchase = await _purchaseRepo.GetByIdAsync(purchaseId);
        if (purchase == null)
            return ServiceResult.NotFound("Purchase not found.");

        if (purchase.Status != PurchaseStatus.Pending)
            return ServiceResult.BadRequest("Purchase is not in Pending status.");

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            purchase.Status = PurchaseStatus.Completed;
            purchase.PaidAt = DateTime.UtcNow;
            await _purchaseRepo.UpdateAsync(purchase);

            var binding = new NominationBinding
            {
                PurchaseId = purchase.Id,
                Status = BindingStatus.PendingAcknowledgement
            };
            await _bindingRepo.AddAsync(binding);

            var listing = await _listingRepo.GetByIdAsync(purchase.ListingId);
            if (listing is FixedPriceListing fpl)
            {
                fpl.QuantityRemaining--;
                if (fpl.QuantityRemaining <= 0) { fpl.Status = ListingStatus.Sold; fpl.ClosedAt = DateTime.UtcNow; }
                await _listingRepo.UpdateAsync(fpl);
            }
            else if (listing is AuctionListing al)
            {
                al.WinningBidId = purchase.BidId;
                al.Status = ListingStatus.Sold;
                al.ClosedAt = DateTime.UtcNow;
                await _listingRepo.UpdateAsync(al);
            }

            await _auditRepo.LogAsync("Purchase", purchase.Id, "PurchaseCompleted", null,
                $"{{\"PlatformFeeIncGst\":{purchase.PlatformFeeIncGst}}}");

            await tx.CommitAsync();
            return ServiceResult.Ok();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<ServiceResult<IReadOnlyList<PurchaseDto>>> GetPurchasesAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<IReadOnlyList<PurchaseDto>>.Forbidden();

        IReadOnlyList<Purchase> purchases = caller.Role == UserRole.Staff
            ? await _purchaseRepo.GetAllAsync()
            : await _purchaseRepo.GetByBuyerIdAsync(caller.Id);

        return ServiceResult<IReadOnlyList<PurchaseDto>>.Ok(purchases.Select(MapToDto).ToList());
    }

    public async Task<ServiceResult<PurchaseDto>> GetPurchaseByIdAsync(Guid id)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<PurchaseDto>.Forbidden();

        var purchase = await _purchaseRepo.GetByIdAsync(id);
        if (purchase == null)
            return ServiceResult<PurchaseDto>.NotFound("Purchase not found.");

        if (caller.Role != UserRole.Staff && purchase.BuyerUserId != caller.Id)
            return ServiceResult<PurchaseDto>.Forbidden();

        return ServiceResult<PurchaseDto>.Ok(MapToDto(purchase));
    }

    public async Task<ServiceResult> RefundAsync(Guid id)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult.Forbidden();
        if (caller.Role != UserRole.Staff) return ServiceResult.Forbidden("Only Staff can process refunds.");

        var purchase = await _purchaseRepo.GetByIdAsync(id);
        if (purchase == null)
            return ServiceResult.NotFound("Purchase not found.");

        if (purchase.Status != PurchaseStatus.Completed)
            return ServiceResult.BadRequest("Only completed purchases can be refunded.");

        // 90% refund — platform retains 10%
        purchase.RefundAmount = Math.Round(purchase.PlatformFeeIncGst * 0.9m, 2);
        purchase.RefundedAt = DateTime.UtcNow;
        purchase.Status = PurchaseStatus.Refunded;
        await _purchaseRepo.UpdateAsync(purchase);

        await _auditRepo.LogAsync("Purchase", purchase.Id, "PurchaseRefunded",
            caller?.Id, $"{{\"RefundAmount\":{purchase.RefundAmount}}}");

        return ServiceResult.Ok();
    }

    private static PurchaseDto MapToDto(Purchase p) => new()
    {
        Id = p.Id,
        ListingId = p.ListingId,
        StallionName = p.Listing?.Stallion?.Name ?? string.Empty,
        BuyerUserId = p.BuyerUserId,
        TotalPriceIncGst = p.TotalPriceIncGst,
        PlatformFeeIncGst = p.PlatformFeeIncGst,
        PlatformFeeExGst = p.PlatformFeeExGst,
        PlatformFeeGst = p.PlatformFeeGst,
        MareName = p.MareName,
        MareRegistration = p.MareRegistration,
        MareBreed = p.MareBreed,
        PaymentProvider = p.PaymentProvider,
        PaymentReference = p.PaymentReference,
        PaidAt = p.PaidAt,
        Status = p.Status.ToString(),
        RefundAmount = p.RefundAmount,
        RefundedAt = p.RefundedAt,
        CreatedAt = p.CreatedAt,
        CompletedAt = p.PaidAt
    };
}
