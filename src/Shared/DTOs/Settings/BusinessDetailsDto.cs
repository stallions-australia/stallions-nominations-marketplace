namespace Stallions.Shared.DTOs.Settings;

/// <summary>Staff-only: Stallions Australia's details printed on tax documents. ABN and BSB are digits only.</summary>
public class BusinessDetailsDto
{
    public string? LegalName { get; set; }
    public string? Abn { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public string? BankAccountName { get; set; }
    public string? BankBsb { get; set; }
    public string? BankAccountNumber { get; set; }
    public int InvoicePaymentTermsDays { get; set; }
    /// <summary>Legal name, ABN and address are set.</summary>
    public bool CanIssueTaxDocuments { get; set; }
    /// <summary>…and the bank details too (needed for an unpaid invoice).</summary>
    public bool CanIssueUnpaidInvoices { get; set; }
}
