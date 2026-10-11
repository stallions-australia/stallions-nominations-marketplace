using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

/// <summary>
/// An issued tax invoice or adjustment note. Numbered SA-000001… from the TaxDocumentNumbers sequence.
/// Never edited or deleted after issue — the only change is an Unpaid invoice becoming Paid. Supplier,
/// recipient and bank details are snapshots taken at issue. Adjustment notes hold the positive amounts
/// they reverse.
/// </summary>
public class TaxDocument : IHasConcurrencyStamp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = string.Empty;
    public TaxDocumentKind Kind { get; set; }
    public DateTime IssuedAt { get; set; }
    public TaxDocumentStatus Status { get; set; }
    public DateTime? PaidAt { get; set; }
    /// <summary>Unpaid listing fee invoices only: IssuedAt + payment terms.</summary>
    public DateTime? DueAt { get; set; }
    public string Description { get; set; } = string.Empty;

    // Listing fee invoices with an intro offer applied
    public decimal? StandardAmountIncGst { get; set; }
    public string? IntroOfferName { get; set; }
    public decimal? DiscountIncGst { get; set; }

    public decimal AmountIncGst { get; set; }
    public decimal AmountExGst { get; set; }
    public decimal GstAmount { get; set; }

    // Supplier snapshot (Stallions Australia's business details at issue)
    public string SupplierName { get; set; } = string.Empty;
    public string SupplierAbn { get; set; } = string.Empty;
    public string SupplierAddress { get; set; } = string.Empty;
    public string? SupplierEmail { get; set; }

    // Recipient snapshot (stud: farm name, ABN, address, contact email; buyer: name + email)
    public string RecipientName { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public string? RecipientAbn { get; set; }
    public string? RecipientAddress { get; set; }

    // Bank snapshot — unpaid invoices only
    public string? BankAccountName { get; set; }
    public string? BankBsb { get; set; }
    public string? BankAccountNumber { get; set; }

    public Guid? SubscriptionId { get; set; }
    public Guid? PurchaseId { get; set; }
    /// <summary>Adjustment notes only: the invoice reversed.</summary>
    public Guid? OriginalDocumentId { get; set; }
    /// <summary>Adjustment notes: "Buyer fee refunded — …" / "Listing fee waived — …".</summary>
    public string? Reason { get; set; }

    // The recipient's owner, for access checks and lists
    public Guid? StudFarmId { get; set; }
    public Guid? BuyerUserId { get; set; }

    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    // Navigation properties
    public TaxDocument? OriginalDocument { get; set; }
}
