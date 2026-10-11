// src/Server/Data/Repositories/ITaxDocumentRepository.cs
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public interface ITaxDocumentRepository
{
    /// <summary>With OriginalDocument.</summary>
    Task<TaxDocument?> GetByIdAsync(Guid id);
    Task<TaxDocument?> GetListingFeeInvoiceAsync(Guid subscriptionId);
    Task<TaxDocument?> GetBuyerFeeInvoiceAsync(Guid purchaseId);
    Task<TaxDocument?> GetAdjustmentNoteForAsync(Guid originalDocumentId);
    Task<IReadOnlyList<TaxDocument>> GetAdjustmentNotesForAsync(IEnumerable<Guid> originalDocumentIds);
    /// <summary>Every document (any kind) for these subscriptions.</summary>
    Task<IReadOnlyList<TaxDocument>> GetBySubscriptionIdsAsync(IEnumerable<Guid> subscriptionIds);
    /// <summary>Every document (any kind) for these sale records.</summary>
    Task<IReadOnlyList<TaxDocument>> GetByPurchaseIdsAsync(IEnumerable<Guid> purchaseIds);
    /// <summary>The buyer's documents, newest first, with OriginalDocument.</summary>
    Task<IReadOnlyList<TaxDocument>> GetForBuyerAsync(Guid buyerUserId);
    /// <summary>The stud farm's documents, newest first, with OriginalDocument.</summary>
    Task<IReadOnlyList<TaxDocument>> GetForStudFarmAsync(Guid studFarmId);
    /// <summary>Newest first, with OriginalDocument. issuedFromUtc inclusive, issuedBeforeUtc exclusive.</summary>
    Task<IReadOnlyList<TaxDocument>> SearchAsync(TaxDocumentKind? kind, TaxDocumentStatus? status,
        DateTime? issuedFromUtc, DateTime? issuedBeforeUtc);
    /// <summary>Inserts the document; false (and no longer tracked) when a unique index says it already exists.</summary>
    Task<bool> TryAddAsync(TaxDocument document);
    /// <summary>Saves a status change; false (and no longer tracked) when another instance changed it first.</summary>
    Task<bool> TryUpdateAsync(TaxDocument document);
}
