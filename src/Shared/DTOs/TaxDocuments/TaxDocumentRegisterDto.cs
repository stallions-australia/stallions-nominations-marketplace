namespace Stallions.Shared.DTOs.TaxDocuments;

public class TaxDocumentRegisterDto
{
    public List<TaxDocumentSummaryDto> Documents { get; set; } = new();
    public TaxDocumentTotalsDto Totals { get; set; } = new();
    /// <summary>Fees recorded without a document because the business details were incomplete.</summary>
    public int PendingCount { get; set; }
}
