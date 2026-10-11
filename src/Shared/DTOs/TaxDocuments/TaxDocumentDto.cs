namespace Stallions.Shared.DTOs.TaxDocuments;

/// <summary>One tax invoice or adjustment note, as printed. ABN / BSB already grouped; dates are Sydney dates.</summary>
public class TaxDocumentDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;          // ListingFeeInvoice | BuyerFeeInvoice | AdjustmentNote
    public string Status { get; set; } = string.Empty;        // Unpaid | Paid
    public DateTime IssuedAt { get; set; }
    public DateOnly IssuedOn { get; set; }
    public DateOnly? DueOn { get; set; }
    public DateOnly? PaidOn { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal? StandardAmountIncGst { get; set; }
    public string? IntroOfferName { get; set; }
    public decimal? DiscountIncGst { get; set; }
    public decimal AmountIncGst { get; set; }
    public decimal AmountExGst { get; set; }
    public decimal GstAmount { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string SupplierAbn { get; set; } = string.Empty;
    public string SupplierAddress { get; set; } = string.Empty;
    public string? SupplierEmail { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public string? RecipientAbn { get; set; }
    public string? RecipientAddress { get; set; }
    public string? BankAccountName { get; set; }
    public string? BankBsb { get; set; }
    public string? BankAccountNumber { get; set; }
    public Guid? OriginalDocumentId { get; set; }
    public string? OriginalNumber { get; set; }
    public DateOnly? OriginalIssuedOn { get; set; }
    public string? Reason { get; set; }
    /// <summary>Buyer fee invoices: the configured explanation that the fee forms part of the price.</summary>
    public string? FeeNote { get; set; }
    public Guid? ReversedById { get; set; }
    public string? ReversedByNumber { get; set; }
}
