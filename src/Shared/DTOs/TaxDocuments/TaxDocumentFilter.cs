namespace Stallions.Shared.DTOs.TaxDocuments;

/// <summary>Staff register filters (query string). Dates are Sydney calendar dates, both inclusive.</summary>
public class TaxDocumentFilter
{
    public string? Kind { get; set; }      // ListingFeeInvoice | BuyerFeeInvoice | AdjustmentNote
    public string? Status { get; set; }    // Unpaid | Paid
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}
