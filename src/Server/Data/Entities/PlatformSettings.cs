namespace Stallions.Server.Data.Entities;

/// <summary>
/// Staff-managed platform settings. Exactly one row, with Id = <see cref="SingletonId"/>.
/// The default values are seeded in AppDbContext (HasData) — that seed is the only place
/// default amounts appear in code.
/// </summary>
public class PlatformSettings
{
    public static readonly Guid SingletonId = new("5e771265-0000-4000-8000-000000000001");

    public Guid Id { get; set; } = SingletonId;
    public decimal BuyerFeeIncGst { get; set; }
    public decimal StandardListingFeeIncGst { get; set; }
    public decimal MinimumBidIncrement { get; set; }
    public int ChargeGracePeriodHours { get; set; }
    public int OfferExpiryDays { get; set; }

    // Stallions Australia's business details, printed on every tax document. Issuing a document needs
    // legal name, ABN and address; an unpaid invoice also needs the bank details. ABN and BSB are digits only.
    public string? BusinessLegalName { get; set; }
    public string? BusinessAbn { get; set; }
    public string? BusinessAddress { get; set; }
    public string? BusinessEmail { get; set; }
    public string? BankAccountName { get; set; }
    public string? BankBsb { get; set; }
    public string? BankAccountNumber { get; set; }
    public int InvoicePaymentTermsDays { get; set; }

    /// <summary>A tax document can be issued (get-only: not a column).</summary>
    public bool HasBusinessDetails => !string.IsNullOrWhiteSpace(BusinessLegalName)
                                      && !string.IsNullOrWhiteSpace(BusinessAbn)
                                      && !string.IsNullOrWhiteSpace(BusinessAddress);

    /// <summary>An unpaid invoice can show where to pay.</summary>
    public bool HasBankDetails => !string.IsNullOrWhiteSpace(BankAccountName)
                                  && !string.IsNullOrWhiteSpace(BankBsb)
                                  && !string.IsNullOrWhiteSpace(BankAccountNumber);

    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    // Navigation properties
    public User? UpdatedBy { get; set; }
}
