namespace Stallions.Shared.Enums;

public enum TaxDocumentKind
{
    /// <summary>Listing fee tax invoice, issued to the stud.</summary>
    ListingFeeInvoice,
    /// <summary>Buyer fee tax invoice, issued to the buyer.</summary>
    BuyerFeeInvoice,
    /// <summary>Reverses one invoice in full (refund or waiver).</summary>
    AdjustmentNote
}
