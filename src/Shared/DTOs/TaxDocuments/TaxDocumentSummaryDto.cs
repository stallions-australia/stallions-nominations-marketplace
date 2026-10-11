namespace Stallions.Shared.DTOs.TaxDocuments;

/// <summary>A row in a list of documents. Amounts are positive; an AdjustmentNote reduces the totals.</summary>
public class TaxDocumentSummaryDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateOnly IssuedOn { get; set; }
    public DateOnly? DueOn { get; set; }
    public DateOnly? PaidOn { get; set; }
    public string Description { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public decimal AmountIncGst { get; set; }
    public decimal AmountExGst { get; set; }
    public decimal GstAmount { get; set; }
    public string? OriginalNumber { get; set; }
    public string? ReversedByNumber { get; set; }
    public string? Reason { get; set; }
}
