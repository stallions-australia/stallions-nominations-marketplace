using System.Text.Json;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.TaxDocuments;
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
    private readonly ITaxDocumentRepository _taxDocumentRepo;
    private readonly ITaxDocumentService _taxDocuments;
    private readonly ITransactionRunner _transactions;

    public PurchaseService(IPurchaseRepository purchaseRepo, IAuditLogRepository auditRepo, IUserService users,
        IListingRepository listingRepo, IBidRepository bidRepo, ITaxDocumentRepository taxDocumentRepo,
        ITaxDocumentService taxDocuments, ITransactionRunner transactions)
    {
        _purchaseRepo = purchaseRepo;
        _auditRepo = auditRepo;
        _users = users;
        _listingRepo = listingRepo;
        _bidRepo = bidRepo;
        _taxDocumentRepo = taxDocumentRepo;
        _taxDocuments = taxDocuments;
        _transactions = transactions;
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

    public async Task<ServiceResult<RefundResultDto>> RefundAsync(Guid id, RefundBuyerFeeRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<RefundResultDto>.Forbidden();
        if (caller.Role != UserRole.Staff) return ServiceResult<RefundResultDto>.Forbidden("Only Staff can process refunds.");

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note?.Length > 500) return ServiceResult<RefundResultDto>.BadRequest("The note can be at most 500 characters.");

        return await _transactions.RunAsync(async () =>
        {
            var purchase = await _purchaseRepo.GetByIdAsync(id);
            if (purchase == null)
                return ServiceResult<RefundResultDto>.NotFound("Sale record not found.");
            if (purchase.Status != PurchaseStatus.Completed)
                return ServiceResult<RefundResultDto>.BadRequest("Only paid sale records can be refunded.");

            // The adjustment note first, before any tracked entity is modified (the document insert saves
            // everything pending): if it can't be issued, nothing has been written.
            TaxDocument? adjustment = null;
            if (await _taxDocumentRepo.GetBuyerFeeInvoiceAsync(purchase.Id) is { } invoice)
            {
                var result = await _taxDocuments.IssueAdjustmentNoteAsync(invoice,
                    note == null ? "Buyer fee refunded" : $"Buyer fee refunded — {note}", caller.Id);
                if (result.Status == TaxDocumentIssueStatus.BusinessDetailsMissing)
                    return ServiceResult<RefundResultDto>.BadRequest(TaxDocumentService.BusinessDetailsMissingMessage);
                adjustment = result.Document;
            }

            // Records a refund Staff have made in the payment provider's dashboard — no money moves here.
            purchase.RefundAmount = purchase.BuyerFeeIncGst;
            purchase.RefundedAt = DateTime.UtcNow;
            purchase.Status = PurchaseStatus.Refunded;
            await _purchaseRepo.UpdateAsync(purchase);

            await _auditRepo.LogAsync("Purchase", purchase.Id, "PurchaseRefunded", caller.Id, JsonSerializer.Serialize(new
            {
                purchase.RefundAmount, purchase.PaymentProvider, purchase.PaymentReference,
                Note = note, AdjustmentNoteNumber = adjustment?.Number
            }));

            return ServiceResult<RefundResultDto>.Ok(new RefundResultDto
            {
                AdjustmentNoteId = adjustment?.Id,
                AdjustmentNoteNumber = adjustment?.Number,
                Message = adjustment != null
                    ? $"Refund recorded. Adjustment note {adjustment.Number} has been emailed to the buyer."
                    : "Refund recorded. This sale has no tax invoice, so no adjustment note was issued."
            });
        });
    }

    internal static PurchaseDto MapToDto(Purchase p) => new()
    {
        Id = p.Id,
        ListingId = p.ListingId,
        StallionName = p.Listing?.Stallion?.Name ?? string.Empty,
        SeasonName = p.Listing?.Season?.Name ?? string.Empty,
        StudFarmName = p.Listing?.StudFarm?.Name ?? string.Empty,
        BuyerUserId = p.BuyerUserId,
        Source = p.OfferId != null ? "Offer" : "Auction",
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
