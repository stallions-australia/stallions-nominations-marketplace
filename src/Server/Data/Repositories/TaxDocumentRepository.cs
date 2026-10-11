// src/Server/Data/Repositories/TaxDocumentRepository.cs
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public class TaxDocumentRepository : ITaxDocumentRepository
{
    private readonly AppDbContext _db;
    public TaxDocumentRepository(AppDbContext db) => _db = db;

    private IQueryable<TaxDocument> WithOriginal() => _db.TaxDocuments.Include(d => d.OriginalDocument);

    public async Task<TaxDocument?> GetByIdAsync(Guid id) =>
        await WithOriginal().FirstOrDefaultAsync(d => d.Id == id);

    public virtual async Task<TaxDocument?> GetListingFeeInvoiceAsync(Guid subscriptionId) =>
        await _db.TaxDocuments.FirstOrDefaultAsync(d =>
            d.SubscriptionId == subscriptionId && d.Kind == TaxDocumentKind.ListingFeeInvoice);

    public virtual async Task<TaxDocument?> GetBuyerFeeInvoiceAsync(Guid purchaseId) =>
        await _db.TaxDocuments.FirstOrDefaultAsync(d =>
            d.PurchaseId == purchaseId && d.Kind == TaxDocumentKind.BuyerFeeInvoice);

    public virtual async Task<TaxDocument?> GetAdjustmentNoteForAsync(Guid originalDocumentId) =>
        await _db.TaxDocuments.FirstOrDefaultAsync(d =>
            d.OriginalDocumentId == originalDocumentId && d.Kind == TaxDocumentKind.AdjustmentNote);

    public async Task<IReadOnlyList<TaxDocument>> GetAdjustmentNotesForAsync(IEnumerable<Guid> originalDocumentIds)
    {
        var ids = originalDocumentIds.ToList();
        return await _db.TaxDocuments
            .Where(d => d.Kind == TaxDocumentKind.AdjustmentNote && d.OriginalDocumentId != null
                        && ids.Contains(d.OriginalDocumentId.Value))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<TaxDocument>> GetBySubscriptionIdsAsync(IEnumerable<Guid> subscriptionIds)
    {
        var ids = subscriptionIds.ToList();
        return await _db.TaxDocuments
            .Where(d => d.SubscriptionId != null && ids.Contains(d.SubscriptionId.Value))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<TaxDocument>> GetByPurchaseIdsAsync(IEnumerable<Guid> purchaseIds)
    {
        var ids = purchaseIds.ToList();
        return await _db.TaxDocuments
            .Where(d => d.PurchaseId != null && ids.Contains(d.PurchaseId.Value))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<TaxDocument>> GetForBuyerAsync(Guid buyerUserId) =>
        await WithOriginal().Where(d => d.BuyerUserId == buyerUserId)
            .OrderByDescending(d => d.IssuedAt).ThenByDescending(d => d.Number).ToListAsync();

    public async Task<IReadOnlyList<TaxDocument>> GetForStudFarmAsync(Guid studFarmId) =>
        await WithOriginal().Where(d => d.StudFarmId == studFarmId)
            .OrderByDescending(d => d.IssuedAt).ThenByDescending(d => d.Number).ToListAsync();

    public async Task<IReadOnlyList<TaxDocument>> SearchAsync(TaxDocumentKind? kind, TaxDocumentStatus? status,
        DateTime? issuedFromUtc, DateTime? issuedBeforeUtc)
    {
        var q = WithOriginal();
        if (kind.HasValue) q = q.Where(d => d.Kind == kind.Value);
        if (status.HasValue) q = q.Where(d => d.Status == status.Value);
        if (issuedFromUtc.HasValue) q = q.Where(d => d.IssuedAt >= issuedFromUtc.Value);
        if (issuedBeforeUtc.HasValue) q = q.Where(d => d.IssuedAt < issuedBeforeUtc.Value);
        return await q.OrderByDescending(d => d.IssuedAt).ThenByDescending(d => d.Number).ToListAsync();
    }

    public virtual async Task<bool> TryAddAsync(TaxDocument document)
    {
        _db.TaxDocuments.Add(document);
        try
        {
            await _db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // Inside the caller's transaction EF rolled back to its savepoint; forget the rejected row.
            _db.Entry(document).State = EntityState.Detached;
            return false;
        }
    }

    public virtual async Task<bool> TryUpdateAsync(TaxDocument document)
    {
        try
        {
            _db.TaxDocuments.Update(document);
            await _db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.Entry(document).State = EntityState.Detached;
            return false;
        }
    }
}
