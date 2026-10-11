namespace Stallions.Shared.DTOs.TaxDocuments;

/// <summary>BAS totals for the filtered documents. Net = invoices − adjustment notes. Unpaid excludes reversed invoices.</summary>
public class TaxDocumentTotalsDto
{
    public int InvoiceCount { get; set; }
    public decimal InvoicesIncGst { get; set; }
    public decimal InvoicesExGst { get; set; }
    public decimal InvoicesGst { get; set; }
    public int AdjustmentCount { get; set; }
    public decimal AdjustmentsIncGst { get; set; }
    public decimal AdjustmentsExGst { get; set; }
    public decimal AdjustmentsGst { get; set; }
    public decimal NetIncGst { get; set; }
    public decimal NetExGst { get; set; }
    public decimal NetGst { get; set; }
    public int UnpaidCount { get; set; }
    public decimal UnpaidIncGst { get; set; }
}
