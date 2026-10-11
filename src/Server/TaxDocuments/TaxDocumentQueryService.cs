using System.Text.Json;
using Microsoft.Extensions.Options;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Infrastructure;
using Stallions.Server.Options;
using Stallions.Server.Services;
using Stallions.Shared;
using Stallions.Shared.DTOs.TaxDocuments;
using Stallions.Shared.Enums;

namespace Stallions.Server.TaxDocuments;

/// <summary>Reading tax documents: the document page, buyer and stud lists, the Staff register, CSV, issue missing.</summary>
public interface ITaxDocumentQueryService
{
    /// <summary>The recipient's owner (buyer / stud admins of the farm) or Staff; anyone else gets 404.</summary>
    Task<ServiceResult<TaxDocumentDto>> GetAsync(Guid id);
    Task<ServiceResult<IReadOnlyList<TaxDocumentSummaryDto>>> GetMineAsync();
    Task<ServiceResult<IReadOnlyList<TaxDocumentSummaryDto>>> GetForStudAsync();
    Task<ServiceResult<TaxDocumentRegisterDto>> GetRegisterAsync(TaxDocumentFilter filter);
    Task<ServiceResult<string>> ExportCsvAsync(TaxDocumentFilter filter);
    /// <summary>Issues the documents for fees recorded with a TaxDocumentPending audit row and still none.</summary>
    Task<ServiceResult<IssueMissingResultDto>> IssueMissingAsync();
}

public class TaxDocumentQueryService : ITaxDocumentQueryService
{
    private const string StaffOnlyMessage = "Only Stallions Australia staff can use the tax document register.";
    private const string NotFoundMessage = "Tax document not found.";

    private readonly ITaxDocumentRepository _documents;
    private readonly ITaxDocumentService _taxDocuments;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IPurchaseRepository _purchases;
    private readonly IStudFarmRepository _farms;
    private readonly IAuditLogRepository _audit;
    private readonly IUserService _users;
    private readonly ITransactionRunner _transactions;
    private readonly DisclosureOptions _disclosure;
    private readonly ILogger<TaxDocumentQueryService> _log;

    public TaxDocumentQueryService(ITaxDocumentRepository documents, ITaxDocumentService taxDocuments,
        ISubscriptionRepository subscriptions, IPurchaseRepository purchases, IStudFarmRepository farms,
        IAuditLogRepository audit, IUserService users, ITransactionRunner transactions,
        IOptions<DisclosureOptions> disclosure, ILogger<TaxDocumentQueryService> log)
    {
        _documents = documents; _taxDocuments = taxDocuments; _subscriptions = subscriptions; _purchases = purchases;
        _farms = farms; _audit = audit; _users = users; _transactions = transactions; _disclosure = disclosure.Value; _log = log;
    }

    public async Task<ServiceResult<TaxDocumentDto>> GetAsync(Guid id)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        var document = await _documents.GetByIdAsync(id);
        // Not found and not yours look the same, so a document id reveals nothing.
        if (caller == null || document == null || !await CanSeeAsync(caller, document))
            return ServiceResult<TaxDocumentDto>.NotFound(NotFoundMessage);

        var reversedBy = document.Kind == TaxDocumentKind.AdjustmentNote
            ? null
            : await _documents.GetAdjustmentNoteForAsync(document.Id);
        return ServiceResult<TaxDocumentDto>.Ok(ToDto(document, reversedBy));
    }

    public async Task<ServiceResult<IReadOnlyList<TaxDocumentSummaryDto>>> GetMineAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.Buyer)
            return ServiceResult<IReadOnlyList<TaxDocumentSummaryDto>>.Forbidden("Only buyers have buyer fee tax invoices.");
        return ServiceResult<IReadOnlyList<TaxDocumentSummaryDto>>.Ok(await SummariesAsync(await _documents.GetForBuyerAsync(caller.Id)));
    }

    public async Task<ServiceResult<IReadOnlyList<TaxDocumentSummaryDto>>> GetForStudAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.StudFarmAdmin)
            return ServiceResult<IReadOnlyList<TaxDocumentSummaryDto>>.Forbidden("Only stud farm admins have listing fee tax invoices.");
        var farm = await _farms.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult<IReadOnlyList<TaxDocumentSummaryDto>>.NotFound("No stud farm found for the current user.");
        return ServiceResult<IReadOnlyList<TaxDocumentSummaryDto>>.Ok(await SummariesAsync(await _documents.GetForStudFarmAsync(farm.Id)));
    }

    public async Task<ServiceResult<TaxDocumentRegisterDto>> GetRegisterAsync(TaxDocumentFilter filter)
    {
        if (!await IsStaffAsync()) return ServiceResult<TaxDocumentRegisterDto>.Forbidden(StaffOnlyMessage);

        var (rows, error) = await SearchAsync(filter);
        if (error != null) return ServiceResult<TaxDocumentRegisterDto>.BadRequest(error);

        var reversedBy = await ReversalsAsync(rows);
        var (pendingSubscriptions, pendingPurchases) = await FindPendingAsync();
        return ServiceResult<TaxDocumentRegisterDto>.Ok(new TaxDocumentRegisterDto
        {
            Documents = rows.Select(d => ToSummary(d, reversedBy)).ToList(),
            Totals = Totals(rows, reversedBy),
            PendingCount = pendingSubscriptions.Count + pendingPurchases.Count
        });
    }

    public async Task<ServiceResult<string>> ExportCsvAsync(TaxDocumentFilter filter)
    {
        if (!await IsStaffAsync()) return ServiceResult<string>.Forbidden(StaffOnlyMessage);

        var (rows, error) = await SearchAsync(filter);
        if (error != null) return ServiceResult<string>.BadRequest(error);

        var reversedBy = await ReversalsAsync(rows);
        return ServiceResult<string>.Ok(TaxDocumentCsv.Build(rows.Select(d => ToSummary(d, reversedBy))));
    }

    public async Task<ServiceResult<IssueMissingResultDto>> IssueMissingAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.Staff)
            return ServiceResult<IssueMissingResultDto>.Forbidden(StaffOnlyMessage);
        if (!await _taxDocuments.CanIssueAsync())
            return ServiceResult<IssueMissingResultDto>.BadRequest(TaxDocumentService.BusinessDetailsMissingMessage);

        var (subscriptionIds, purchaseIds) = await FindPendingAsync();
        var issued = 0;
        foreach (var id in subscriptionIds)
        {
            var done = await _transactions.RunAsync(async () =>
            {
                var subscription = await _subscriptions.GetByIdAsync(id);
                if (subscription is not { Status: SubscriptionStatus.Paid }) return false;
                var result = await _taxDocuments.IssueListingFeeInvoiceAsync(subscription, paid: true, caller.Id, subscription.PaidAt);
                return result.Status == TaxDocumentIssueStatus.Issued;
            });
            if (done) issued++;
        }
        foreach (var id in purchaseIds)
        {
            var done = await _transactions.RunAsync(async () =>
            {
                var purchase = await _purchases.GetForChargeAsync(id);
                if (purchase is not { Status: PurchaseStatus.Completed }) return false;
                return (await _taxDocuments.IssueBuyerFeeInvoiceAsync(purchase)).Status == TaxDocumentIssueStatus.Issued;
            });
            if (done) issued++;
        }

        var (stillSubscriptions, stillPurchases) = await FindPendingAsync();
        var remaining = stillSubscriptions.Count + stillPurchases.Count;
        await _audit.LogAsync("TaxDocument", Guid.Empty, "IssueMissingTaxDocuments", caller.Id,
            JsonSerializer.Serialize(new { Issued = issued, Remaining = remaining }));
        _log.LogInformation("Staff {UserId} issued {Issued} missing tax documents; {Remaining} remain", caller.Id, issued, remaining);
        return ServiceResult<IssueMissingResultDto>.Ok(new IssueMissingResultDto { Issued = issued, Remaining = remaining });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<bool> IsStaffAsync() => (await _users.GetOrCreateCurrentUserAsync())?.Role == UserRole.Staff;

    private async Task<bool> CanSeeAsync(User caller, TaxDocument document) => caller.Role switch
    {
        UserRole.Staff => true,
        UserRole.Buyer => document.BuyerUserId == caller.Id,
        UserRole.StudFarmAdmin => document.StudFarmId != null
                                  && (await _farms.GetByUserIdAsync(caller.Id))?.Id == document.StudFarmId,
        _ => false
    };

    private async Task<(IReadOnlyList<TaxDocument> Rows, string? Error)> SearchAsync(TaxDocumentFilter filter)
    {
        TaxDocumentKind? kind = null;
        if (!string.IsNullOrWhiteSpace(filter.Kind))
        {
            var name = Enum.GetNames<TaxDocumentKind>().FirstOrDefault(n => string.Equals(n, filter.Kind.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name == null) return (Array.Empty<TaxDocument>(), $"'{filter.Kind}' is not a tax document kind.");
            kind = Enum.Parse<TaxDocumentKind>(name);
        }
        TaxDocumentStatus? status = null;
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var name = Enum.GetNames<TaxDocumentStatus>().FirstOrDefault(n => string.Equals(n, filter.Status.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name == null) return (Array.Empty<TaxDocument>(), $"'{filter.Status}' is not a tax document status.");
            status = Enum.Parse<TaxDocumentStatus>(name);
        }
        if (filter.From is { } from && filter.To is { } to && from > to)
            return (Array.Empty<TaxDocument>(), "The from date must be on or before the to date.");

        var fromUtc = filter.From is { } f ? SydneyTime.StartOfDayUtc(f) : (DateTime?)null;
        var beforeUtc = filter.To is { } t ? SydneyTime.StartOfDayUtc(t.AddDays(1)) : (DateTime?)null;
        return (await _documents.SearchAsync(kind, status, fromUtc, beforeUtc), null);
    }

    /// <summary>Invoice id → the number of the adjustment note that reversed it.</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> ReversalsAsync(IReadOnlyList<TaxDocument> rows)
    {
        var invoiceIds = rows.Where(d => d.Kind != TaxDocumentKind.AdjustmentNote).Select(d => d.Id).ToList();
        if (invoiceIds.Count == 0) return new Dictionary<Guid, string>();
        var notes = await _documents.GetAdjustmentNotesForAsync(invoiceIds);
        return notes.ToDictionary(n => n.OriginalDocumentId!.Value, n => n.Number);
    }

    private async Task<IReadOnlyList<TaxDocumentSummaryDto>> SummariesAsync(IReadOnlyList<TaxDocument> rows)
    {
        var reversedBy = await ReversalsAsync(rows);
        return rows.Select(d => ToSummary(d, reversedBy)).ToList();
    }

    /// <summary>Fees with a TaxDocumentPending audit row that still have no document and still need one.</summary>
    private async Task<(List<Guid> Subscriptions, List<Guid> Purchases)> FindPendingAsync()
    {
        var subscriptionIds = await _audit.GetEntityIdsWithActionAsync("StallionSeasonSubscription", TaxDocumentService.PendingAction);
        var purchaseIds = await _audit.GetEntityIdsWithActionAsync("Purchase", TaxDocumentService.PendingAction);
        var subscriptionDocs = await _documents.GetBySubscriptionIdsAsync(subscriptionIds);
        var purchaseDocs = await _documents.GetByPurchaseIdsAsync(purchaseIds);

        var subscriptions = new List<Guid>();
        foreach (var id in subscriptionIds.Where(id =>
                     !subscriptionDocs.Any(d => d.SubscriptionId == id && d.Kind == TaxDocumentKind.ListingFeeInvoice)))
        {
            if (await _subscriptions.GetByIdAsync(id) is { Status: SubscriptionStatus.Paid, FeeIncGst: > 0m })
                subscriptions.Add(id);
        }

        var purchases = new List<Guid>();
        foreach (var id in purchaseIds.Where(id =>
                     !purchaseDocs.Any(d => d.PurchaseId == id && d.Kind == TaxDocumentKind.BuyerFeeInvoice)))
        {
            if (await _purchases.GetByIdAsync(id) is { Status: PurchaseStatus.Completed })
                purchases.Add(id);
        }
        return (subscriptions, purchases);
    }

    internal static TaxDocumentTotalsDto Totals(IReadOnlyList<TaxDocument> rows, IReadOnlyDictionary<Guid, string> reversedBy)
    {
        var invoices = rows.Where(d => d.Kind != TaxDocumentKind.AdjustmentNote).ToList();
        var notes = rows.Where(d => d.Kind == TaxDocumentKind.AdjustmentNote).ToList();
        var unpaid = invoices.Where(d => d.Status == TaxDocumentStatus.Unpaid && !reversedBy.ContainsKey(d.Id)).ToList();
        var totals = new TaxDocumentTotalsDto
        {
            InvoiceCount = invoices.Count,
            InvoicesIncGst = invoices.Sum(d => d.AmountIncGst),
            InvoicesExGst = invoices.Sum(d => d.AmountExGst),
            InvoicesGst = invoices.Sum(d => d.GstAmount),
            AdjustmentCount = notes.Count,
            AdjustmentsIncGst = notes.Sum(d => d.AmountIncGst),
            AdjustmentsExGst = notes.Sum(d => d.AmountExGst),
            AdjustmentsGst = notes.Sum(d => d.GstAmount),
            UnpaidCount = unpaid.Count,
            UnpaidIncGst = unpaid.Sum(d => d.AmountIncGst)
        };
        totals.NetIncGst = totals.InvoicesIncGst - totals.AdjustmentsIncGst;
        totals.NetExGst = totals.InvoicesExGst - totals.AdjustmentsExGst;
        totals.NetGst = totals.InvoicesGst - totals.AdjustmentsGst;
        return totals;
    }

    private static DateOnly? SydneyDate(DateTime? utc) => utc is { } value ? SydneyTime.Today(value) : null;

    private TaxDocumentDto ToDto(TaxDocument d, TaxDocument? reversedBy) => new()
    {
        Id = d.Id,
        Number = d.Number,
        Kind = d.Kind.ToString(),
        Status = d.Status.ToString(),
        IssuedAt = d.IssuedAt,
        IssuedOn = SydneyTime.Today(d.IssuedAt),
        DueOn = SydneyDate(d.DueAt),
        PaidOn = SydneyDate(d.PaidAt),
        Description = d.Description,
        StandardAmountIncGst = d.StandardAmountIncGst,
        IntroOfferName = d.IntroOfferName,
        DiscountIncGst = d.DiscountIncGst,
        AmountIncGst = d.AmountIncGst,
        AmountExGst = d.AmountExGst,
        GstAmount = d.GstAmount,
        SupplierName = d.SupplierName,
        SupplierAbn = BusinessNumbers.FormatAbn(d.SupplierAbn),
        SupplierAddress = d.SupplierAddress,
        SupplierEmail = d.SupplierEmail,
        RecipientName = d.RecipientName,
        RecipientEmail = d.RecipientEmail,
        RecipientAbn = d.RecipientAbn == null ? null : BusinessNumbers.FormatAbn(d.RecipientAbn),
        RecipientAddress = d.RecipientAddress,
        BankAccountName = d.BankAccountName,
        BankBsb = d.BankBsb == null ? null : BusinessNumbers.FormatBsb(d.BankBsb),
        BankAccountNumber = d.BankAccountNumber,
        OriginalDocumentId = d.OriginalDocumentId,
        OriginalNumber = d.OriginalDocument?.Number,
        OriginalIssuedOn = SydneyDate(d.OriginalDocument?.IssuedAt),
        Reason = d.Reason,
        FeeNote = d.Kind == TaxDocumentKind.BuyerFeeInvoice ? _disclosure.BuyerFeeExplanation : null,
        ReversedById = reversedBy?.Id,
        ReversedByNumber = reversedBy?.Number
    };

    private static TaxDocumentSummaryDto ToSummary(TaxDocument d, IReadOnlyDictionary<Guid, string> reversedBy) => new()
    {
        Id = d.Id,
        Number = d.Number,
        Kind = d.Kind.ToString(),
        Status = d.Status.ToString(),
        IssuedOn = SydneyTime.Today(d.IssuedAt),
        DueOn = SydneyDate(d.DueAt),
        PaidOn = SydneyDate(d.PaidAt),
        Description = d.Description,
        RecipientName = d.RecipientName,
        RecipientEmail = d.RecipientEmail,
        AmountIncGst = d.AmountIncGst,
        AmountExGst = d.AmountExGst,
        GstAmount = d.GstAmount,
        OriginalNumber = d.OriginalDocument?.Number,
        ReversedByNumber = reversedBy.TryGetValue(d.Id, out var number) ? number : null,
        Reason = d.Reason
    };
}
