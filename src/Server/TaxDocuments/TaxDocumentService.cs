// src/Server/TaxDocuments/TaxDocumentService.cs
using System.Text.Json;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared;
using Stallions.Shared.Enums;

namespace Stallions.Server.TaxDocuments;

public enum TaxDocumentIssueStatus
{
    /// <summary>A new document was issued and emailed.</summary>
    Issued,
    /// <summary>An Unpaid invoice was marked Paid and a receipt emailed.</summary>
    MarkedPaid,
    /// <summary>The document already existed (or was already Paid): nothing new was sent.</summary>
    Existing,
    /// <summary>Legal name, ABN or address (or, for an unpaid invoice, the bank details) are not set.</summary>
    BusinessDetailsMissing,
    /// <summary>Nothing to issue (a $0 fee, or no invoice to mark paid).</summary>
    NotNeeded
}

public sealed record TaxDocumentIssueResult(TaxDocumentIssueStatus Status, TaxDocument? Document)
{
    public static TaxDocumentIssueResult Missing { get; } = new(TaxDocumentIssueStatus.BusinessDetailsMissing, null);
    public static TaxDocumentIssueResult NotNeeded { get; } = new(TaxDocumentIssueStatus.NotNeeded, null);
}

/// <summary>
/// Issues tax invoices and adjustment notes. Every method runs inside the caller's transaction (it never
/// starts one), so the document, its audit row, its email and the money state commit together. Each issue
/// is idempotent: an existing document is returned and nothing new is sent.
/// </summary>
/// <remarks>
/// Precondition: callers must have no unsaved tracked changes when they call an Issue*, Settle* or
/// MarkListingFeeInvoicePaid* method. Inserting a document calls SaveChanges, which flushes every pending
/// tracked change, and on a unique-index violation detaches only the document. So either call the service
/// before modifying tracked entities, or save your own changes first.
/// </remarks>
public interface ITaxDocumentService
{
    /// <summary>Legal name, ABN and address are set (and, for an unpaid invoice, the bank details).</summary>
    Task<bool> CanIssueAsync(bool unpaidInvoice = false);

    /// <summary>The subscription must have Stallion, Season and StudFarm loaded.</summary>
    Task<TaxDocumentIssueResult> IssueListingFeeInvoiceAsync(StallionSeasonSubscription subscription, bool paid,
        Guid? userId, DateTime? paidAt = null);

    /// <summary>Marks the subscription's Unpaid invoice Paid and emails a receipt; NotNeeded if it has no invoice.</summary>
    Task<TaxDocumentIssueResult> MarkListingFeeInvoicePaidAsync(StallionSeasonSubscription subscription,
        DateTime paidAt, Guid? userId);

    /// <summary>The listing fee was paid (card or Staff): mark the Unpaid invoice Paid, or issue a Paid one.</summary>
    Task<TaxDocumentIssueResult> SettleListingFeeAsync(StallionSeasonSubscription subscription, DateTime paidAt, Guid? userId);

    /// <summary>The purchase must have Buyer and the listing's Stallion and Season loaded.</summary>
    Task<TaxDocumentIssueResult> IssueBuyerFeeInvoiceAsync(Purchase purchase);

    /// <summary>Reverses the original invoice in full.</summary>
    Task<TaxDocumentIssueResult> IssueAdjustmentNoteAsync(TaxDocument original, string reason, Guid? userId);

    /// <summary>An automatic issue couldn't happen (business details incomplete): log and audit TaxDocumentPending.</summary>
    Task RecordPendingAsync(string entityType, Guid entityId, TaxDocumentKind kind);
}

public class TaxDocumentService : ITaxDocumentService
{
    public const string PendingAction = "TaxDocumentPending";
    public const string BusinessDetailsMissingMessage = "Complete the business details in Settings first.";
    private const string AuditEntity = "TaxDocument";

    private readonly ITaxDocumentRepository _documents;
    private readonly ITaxDocumentNumberSource _numbers;
    private readonly IPlatformSettingsRepository _settings;
    private readonly IStudFarmRepository _farms;
    private readonly IUserRepository _users;
    private readonly IIntroOfferRepository _offers;
    private readonly ITaxDocumentEmails _emails;
    private readonly IAuditLogRepository _audit;
    private readonly TimeProvider _clock;
    private readonly ILogger<TaxDocumentService> _log;

    public TaxDocumentService(ITaxDocumentRepository documents, ITaxDocumentNumberSource numbers,
        IPlatformSettingsRepository settings, IStudFarmRepository farms, IUserRepository users, IIntroOfferRepository offers,
        ITaxDocumentEmails emails, IAuditLogRepository audit, TimeProvider clock, ILogger<TaxDocumentService> log)
    {
        _documents = documents; _numbers = numbers; _settings = settings; _farms = farms; _users = users;
        _offers = offers; _emails = emails; _audit = audit; _clock = clock; _log = log;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public static string FormatNumber(long value) => $"SA-{value:D6}";

    public async Task<bool> CanIssueAsync(bool unpaidInvoice = false)
    {
        var settings = await _settings.GetAsync();
        return settings.HasBusinessDetails && (!unpaidInvoice || settings.HasBankDetails);
    }

    public async Task<TaxDocumentIssueResult> IssueListingFeeInvoiceAsync(StallionSeasonSubscription subscription,
        bool paid, Guid? userId, DateTime? paidAt = null)
    {
        if (subscription.FeeIncGst <= 0) return TaxDocumentIssueResult.NotNeeded;
        if (await _documents.GetListingFeeInvoiceAsync(subscription.Id) is { } existing)
            return new TaxDocumentIssueResult(TaxDocumentIssueStatus.Existing, existing);

        var settings = await _settings.GetAsync();
        if (!settings.HasBusinessDetails || (!paid && !settings.HasBankDetails)) return TaxDocumentIssueResult.Missing;

        var now = Now;
        var farm = subscription.StudFarm ?? await _farms.GetByIdAsync(subscription.StudFarmId)
                   ?? throw new InvalidOperationException($"Stud farm {subscription.StudFarmId} not found.");
        var document = NewDocument(TaxDocumentKind.ListingFeeInvoice, settings, now, subscription.FeeIncGst);
        document.Description = Truncate(
            $"Listing fee — {subscription.Stallion?.Name ?? "stallion"}, {subscription.Season?.Name ?? "season"}", 300);
        document.SubscriptionId = subscription.Id;
        document.StudFarmId = farm.Id;
        document.RecipientName = farm.Name;
        document.RecipientEmail = await StudEmailAsync(farm);
        document.RecipientAbn = string.IsNullOrWhiteSpace(farm.ABN) ? null : farm.ABN.Trim();
        document.RecipientAddress = string.IsNullOrWhiteSpace(farm.Address) ? null : farm.Address.Trim();

        if (subscription.IntroOfferId is { } offerId && await _offers.GetByIdAsync(offerId) is { } offer)
        {
            document.StandardAmountIncGst = subscription.StandardFeeIncGst;
            document.IntroOfferName = offer.Name;
            document.DiscountIncGst = subscription.StandardFeeIncGst - subscription.FeeIncGst;
        }

        if (paid)
        {
            document.Status = TaxDocumentStatus.Paid;
            document.PaidAt = paidAt ?? subscription.PaidAt ?? now;
        }
        else
        {
            document.Status = TaxDocumentStatus.Unpaid;
            document.DueAt = now.AddDays(settings.InvoicePaymentTermsDays);
            document.BankAccountName = settings.BankAccountName;
            document.BankBsb = settings.BankBsb;
            document.BankAccountNumber = settings.BankAccountNumber;
        }

        var result = await AddAsync(document, () => _documents.GetListingFeeInvoiceAsync(subscription.Id), userId);
        if (result.Status == TaxDocumentIssueStatus.Issued)
        {
            if (paid) await _emails.ListingFeeTaxInvoiceAsync(document);
            else await _emails.ListingFeeInvoiceDueAsync(document);
        }
        return result;
    }

    public async Task<TaxDocumentIssueResult> MarkListingFeeInvoicePaidAsync(StallionSeasonSubscription subscription,
        DateTime paidAt, Guid? userId)
    {
        var invoice = await _documents.GetListingFeeInvoiceAsync(subscription.Id);
        if (invoice == null) return TaxDocumentIssueResult.NotNeeded;
        if (invoice.Status == TaxDocumentStatus.Paid) return new TaxDocumentIssueResult(TaxDocumentIssueStatus.Existing, invoice);

        invoice.Status = TaxDocumentStatus.Paid;
        invoice.PaidAt = paidAt;
        if (!await _documents.TryUpdateAsync(invoice))
        {
            // Staff and the card webhook paid it at the same time: the other one won; it is Paid now.
            _log.LogInformation("Tax invoice {Number} was marked paid concurrently; keeping the first", invoice.Number);
            return new TaxDocumentIssueResult(TaxDocumentIssueStatus.Existing,
                await _documents.GetListingFeeInvoiceAsync(subscription.Id));
        }

        await _audit.LogAsync(AuditEntity, invoice.Id, "TaxInvoicePaid", userId,
            JsonSerializer.Serialize(new { invoice.Number, invoice.PaidAt, invoice.AmountIncGst }));
        await _emails.ListingFeeReceiptAsync(invoice);
        return new TaxDocumentIssueResult(TaxDocumentIssueStatus.MarkedPaid, invoice);
    }

    public async Task<TaxDocumentIssueResult> SettleListingFeeAsync(StallionSeasonSubscription subscription,
        DateTime paidAt, Guid? userId)
    {
        var marked = await MarkListingFeeInvoicePaidAsync(subscription, paidAt, userId);
        return marked.Status == TaxDocumentIssueStatus.NotNeeded
            ? await IssueListingFeeInvoiceAsync(subscription, paid: true, userId, paidAt)
            : marked;
    }

    public async Task<TaxDocumentIssueResult> IssueBuyerFeeInvoiceAsync(Purchase purchase)
    {
        if (purchase.BuyerFeeIncGst <= 0) return TaxDocumentIssueResult.NotNeeded;
        if (await _documents.GetBuyerFeeInvoiceAsync(purchase.Id) is { } existing)
            return new TaxDocumentIssueResult(TaxDocumentIssueStatus.Existing, existing);

        var settings = await _settings.GetAsync();
        if (!settings.HasBusinessDetails) return TaxDocumentIssueResult.Missing;

        var now = Now;
        var buyer = purchase.Buyer ?? await _users.GetByIdAsync(purchase.BuyerUserId)
                    ?? throw new InvalidOperationException($"Buyer {purchase.BuyerUserId} not found.");
        var listing = purchase.Listing;
        var source = purchase.OfferId != null ? "accepted offer" : "auction";
        var document = NewDocument(TaxDocumentKind.BuyerFeeInvoice, settings, now, purchase.BuyerFeeIncGst);
        document.Description = Truncate(
            $"Buyer fee — nomination to {listing?.Stallion?.Name ?? "stallion"}, {listing?.Season?.Name ?? "season"} ({source})", 300);
        document.PurchaseId = purchase.Id;
        document.BuyerUserId = buyer.Id;
        document.RecipientName = buyer.DisplayName;
        document.RecipientEmail = buyer.Email;
        document.Status = TaxDocumentStatus.Paid;
        document.PaidAt = purchase.PaidAt ?? now;

        var result = await AddAsync(document, () => _documents.GetBuyerFeeInvoiceAsync(purchase.Id), userId: null);
        if (result.Status == TaxDocumentIssueStatus.Issued) await _emails.BuyerFeeTaxInvoiceAsync(document);
        return result;
    }

    public async Task<TaxDocumentIssueResult> IssueAdjustmentNoteAsync(TaxDocument original, string reason, Guid? userId)
    {
        if (original.Kind == TaxDocumentKind.AdjustmentNote)
            throw new ArgumentException("An adjustment note can't itself be adjusted.", nameof(original));
        if (await _documents.GetAdjustmentNoteForAsync(original.Id) is { } existing)
            return new TaxDocumentIssueResult(TaxDocumentIssueStatus.Existing, existing);

        var settings = await _settings.GetAsync();
        if (!settings.HasBusinessDetails) return TaxDocumentIssueResult.Missing;

        var now = Now;
        var note = NewDocument(TaxDocumentKind.AdjustmentNote, settings, now, original.AmountIncGst);
        note.Description = Truncate($"Adjustment to {original.Number}: {original.Description}", 300);
        note.Status = TaxDocumentStatus.Paid;
        note.PaidAt = now;
        note.SubscriptionId = original.SubscriptionId;
        note.PurchaseId = original.PurchaseId;
        note.OriginalDocumentId = original.Id;
        note.Reason = Truncate(reason.Trim(), 600);
        note.StudFarmId = original.StudFarmId;
        note.BuyerUserId = original.BuyerUserId;
        note.RecipientName = original.RecipientName;
        note.RecipientEmail = original.RecipientEmail;
        note.RecipientAbn = original.RecipientAbn;
        note.RecipientAddress = original.RecipientAddress;

        var result = await AddAsync(note, () => _documents.GetAdjustmentNoteForAsync(original.Id), userId);
        if (result.Status == TaxDocumentIssueStatus.Issued) await _emails.AdjustmentNoteAsync(note, original);
        return result;
    }

    public async Task RecordPendingAsync(string entityType, Guid entityId, TaxDocumentKind kind)
    {
        _log.LogError("No {Kind} was issued for {EntityType} {EntityId}: the business details are incomplete. " +
                      "Staff can issue it from Invoices once Settings are complete.", kind, entityType, entityId);
        await _audit.LogAsync(entityType, entityId, PendingAction, null, JsonSerializer.Serialize(new { Kind = kind.ToString() }));
    }

    /// <summary>Numbers and inserts the document; a unique-index hit means another instance issued it first.</summary>
    private async Task<TaxDocumentIssueResult> AddAsync(TaxDocument document, Func<Task<TaxDocument?>> findExisting, Guid? userId)
    {
        document.Number = FormatNumber(await _numbers.NextAsync());
        if (!await _documents.TryAddAsync(document))
        {
            var winner = await findExisting()
                ?? throw new InvalidOperationException($"Tax document {document.Number} hit a unique index but no existing document was found.");
            _log.LogInformation("{Kind} was issued concurrently as {Number}; number {Unused} not used",
                document.Kind, winner.Number, document.Number);
            return new TaxDocumentIssueResult(TaxDocumentIssueStatus.Existing, winner);
        }

        await _audit.LogAsync(AuditEntity, document.Id, "TaxDocumentIssued", userId, JsonSerializer.Serialize(new
        {
            document.Number, Kind = document.Kind.ToString(), Status = document.Status.ToString(),
            document.AmountIncGst, document.AmountExGst, document.GstAmount,
            document.SubscriptionId, document.PurchaseId, document.OriginalDocumentId
        }));
        _log.LogInformation("Issued {Kind} {Number} for {AmountIncGst}", document.Kind, document.Number, document.AmountIncGst);
        return new TaxDocumentIssueResult(TaxDocumentIssueStatus.Issued, document);
    }

    private static TaxDocument NewDocument(TaxDocumentKind kind, PlatformSettings settings, DateTime now, decimal amountIncGst)
    {
        var amount = GstBreakdown.FromIncGst(amountIncGst);
        return new TaxDocument
        {
            Kind = kind,
            IssuedAt = now,
            AmountIncGst = amount.IncGst,
            AmountExGst = amount.ExGst,
            GstAmount = amount.Gst,
            SupplierName = settings.BusinessLegalName!.Trim(),
            SupplierAbn = settings.BusinessAbn!,
            SupplierAddress = settings.BusinessAddress!.Trim(),
            SupplierEmail = string.IsNullOrWhiteSpace(settings.BusinessEmail) ? null : settings.BusinessEmail.Trim()
        };
    }

    // The stud's contact email, falling back to the stud admin's own email.
    private async Task<string> StudEmailAsync(StudFarm farm)
    {
        if (!string.IsNullOrWhiteSpace(farm.ContactEmail)) return farm.ContactEmail.Trim();
        var owner = farm.User ?? await _users.GetByIdAsync(farm.UserId);
        if (string.IsNullOrWhiteSpace(owner?.Email))
            _log.LogWarning("Stud farm {StudFarmId} has no contact or owner email for its tax invoice", farm.Id);
        return owner?.Email ?? string.Empty;
    }

    private static string Truncate(string value, int max) => value.Length > max ? value[..max] : value;
}
