using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared;
using Stallions.Shared.DTOs.Checkout;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public class PurchaseService : IPurchaseService
{
    private readonly IPurchaseRepository _purchaseRepo;
    private readonly IAuditLogRepository _auditRepo;
    private readonly IUserService _users;

    private readonly IListingRepository _listingRepo;
    private readonly IBidRepository _bidRepo;

    public PurchaseService(IPurchaseRepository purchaseRepo, IAuditLogRepository auditRepo, IUserService users,
        IListingRepository listingRepo, IBidRepository bidRepo)
    {
        _purchaseRepo = purchaseRepo;
        _auditRepo = auditRepo;
        _users = users;
        _listingRepo = listingRepo;
        _bidRepo = bidRepo;
    }

    public async Task<ServiceResult<MyAuctionResultDto>> GetMyAuctionResultAsync(Guid listingId)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<MyAuctionResultDto>.Forbidden();

        var listing = await _listingRepo.GetAuctionByIdAsync(listingId);
        if (listing == null) return ServiceResult<MyAuctionResultDto>.NotFound("Auction listing not found.");

        var sale = await _purchaseRepo.GetByListingAndBuyerAsync(listingId, caller.Id);
        if (sale != null)
        {
            return ServiceResult<MyAuctionResultDto>.Ok(new MyAuctionResultDto
            {
                Outcome = sale.Status switch
                {
                    PurchaseStatus.Completed or PurchaseStatus.Refunded => AuctionOutcomes.Won,
                    PurchaseStatus.Pending => sale.ChargeDueBy != null ? AuctionOutcomes.PaymentFailed : AuctionOutcomes.PaymentPending,
                    _ => AuctionOutcomes.NoSale
                },
                PurchaseId = sale.Id,
                ChargeDueBy = sale.ChargeDueBy
            });
        }

        if (listing.Status == ListingStatus.Active)
            return ServiceResult<MyAuctionResultDto>.Ok(new MyAuctionResultDto());

        var bids = await _bidRepo.GetByAuctionListingIdAsync(listingId);
        if (!bids.Any(b => b.BuyerUserId == caller.Id))
            return ServiceResult<MyAuctionResultDto>.Ok(new MyAuctionResultDto());

        // Below reserve (or no snapshot) — nobody won. A failed winner's charge still means another buyer won.
        var endedWithoutSale = listing.Status == ListingStatus.Unsold && listing.CloseReason != ListingCloseReason.ChargeFailed;
        return ServiceResult<MyAuctionResultDto>.Ok(new MyAuctionResultDto
        {
            Outcome = endedWithoutSale ? AuctionOutcomes.EndedWithoutSale : AuctionOutcomes.Lost
        });
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
            return ServiceResult<PurchaseDto>.NotFound("Sale record not found.");

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
            return ServiceResult.NotFound("Sale record not found.");

        if (purchase.Status != PurchaseStatus.Completed)
            return ServiceResult.BadRequest("Only paid sale records can be refunded.");

        // Manual Staff action for genuine errors: the full buyer fee is refunded.
        purchase.RefundAmount = purchase.BuyerFeeIncGst;
        purchase.RefundedAt = DateTime.UtcNow;
        purchase.Status = PurchaseStatus.Refunded;
        await _purchaseRepo.UpdateAsync(purchase);

        await _auditRepo.LogAsync("Purchase", purchase.Id, "PurchaseRefunded",
            caller.Id, $"{{\"RefundAmount\":{purchase.RefundAmount}}}");

        return ServiceResult.Ok();
    }

    internal static PurchaseDto MapToDto(Purchase p) => new()
    {
        Id = p.Id,
        ListingId = p.ListingId,
        StallionName = p.Listing?.Stallion?.Name ?? string.Empty,
        SeasonName = p.Listing?.Season?.Name ?? string.Empty,
        StudFarmName = p.Listing?.StudFarm?.Name ?? string.Empty,
        BuyerUserId = p.BuyerUserId,
        TotalPriceIncGst = p.TotalPriceIncGst,
        BuyerFeeIncGst = p.BuyerFeeIncGst,
        BuyerFeeExGst = p.BuyerFeeExGst,
        BuyerFeeGst = p.BuyerFeeGst,
        BalancePayableToStudIncGst = p.BalancePayableToStudIncGst,
        PaymentProvider = p.PaymentProvider,
        PaymentReference = p.PaymentReference,
        PaidAt = p.PaidAt,
        Status = p.Status.ToString(),
        ChargeDueBy = p.ChargeDueBy,
        LastChargeFailure = p.LastChargeFailure,
        RefundAmount = p.RefundAmount,
        RefundedAt = p.RefundedAt,
        CreatedAt = p.CreatedAt,
        CompletedAt = p.PaidAt
    };
}
