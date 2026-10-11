namespace Stallions.Shared.DTOs.Settings;

// Staff-only (PUT api/settings/business-details). Blank values clear a field. ABN: 11 digits; BSB: 6 digits;
// spaces and hyphens are ignored. Payment terms 1–90 days.
public class UpdateBusinessDetailsRequest
{
    public string? LegalName { get; set; }
    public string? Abn { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public string? BankAccountName { get; set; }
    public string? BankBsb { get; set; }
    public string? BankAccountNumber { get; set; }
    public int InvoicePaymentTermsDays { get; set; }
}
