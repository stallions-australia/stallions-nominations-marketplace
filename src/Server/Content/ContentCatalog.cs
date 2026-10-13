using System.Security.Cryptography;
using System.Text;

namespace Stallions.Server.Content;

/// <summary>A placeholder Staff can use in one wording item, with the example value the preview shows.</summary>
public sealed record ContentPlaceholder(string Name, string Description, string Sample);

/// <summary>
/// One Staff-editable wording item. The default subject/body are the wording the code used before Phase 5b: the
/// migration seeds them as version 1 and "Reset to default" republishes them. Required placeholders must appear in
/// the message body. Never change a default after it has shipped — Staff save new versions instead.
/// </summary>
public sealed record ContentCatalogEntry(
    string Key,
    string Group,
    string Label,
    string Description,
    bool HasSubject,
    bool SingleParagraph,
    IReadOnlyList<ContentPlaceholder> Placeholders,
    IReadOnlyList<string> Required,
    string? DefaultSubject,
    string DefaultBody)
{
    public bool Allows(string name) => Placeholders.Any(p => p.Name == name);
}

public static class ContentGroups
{
    public const string Disclosures = "Disclosures";
    public const string BuyerEmails = "Buyer emails";
    public const string StudEmails = "Stud emails";
    public const string TaxDocumentEmails = "Tax document emails";
}

public static class ContentKeys
{
    public const string BuyerFeeExplanation = "Disclosure.BuyerFeeExplanation";
    public const string StudFarmBalanceArrangement = "Disclosure.StudFarmBalanceArrangement";
    public const string SavedCardExplanation = "Disclosure.SavedCardExplanation";

    public const string Outbid = "Email.Outbid";
    public const string AuctionLost = "Email.AuctionLost";
    public const string AuctionEndedWithoutSale = "Email.AuctionEndedWithoutSale";
    public const string WonAndCharged = "Email.WonAndCharged";
    public const string OfferAcceptedAndCharged = "Email.OfferAcceptedAndCharged";
    public const string PaymentFailed = "Email.PaymentFailed";
    public const string PaymentFailedOffer = "Email.PaymentFailedOffer";
    public const string PaymentAbandoned = "Email.PaymentAbandoned";
    public const string PaymentAbandonedOffer = "Email.PaymentAbandonedOffer";
    public const string OfferDeclined = "Email.OfferDeclined";
    public const string OfferExpired = "Email.OfferExpired";
    public const string OfferExpiredListingClosed = "Email.OfferExpiredListingClosed";

    public const string StudSaleConfirmation = "Email.StudSaleConfirmation";
    public const string StudSaleConfirmationOffer = "Email.StudSaleConfirmationOffer";
    public const string StudNoSaleAuction = "Email.StudNoSaleAuction";
    public const string StudNoSaleMakeAnOffer = "Email.StudNoSaleMakeAnOffer";
    public const string OfferReceived = "Email.OfferReceived";

    public const string ListingFeeTaxInvoice = "Email.ListingFeeTaxInvoice";
    public const string ListingFeeInvoiceDue = "Email.ListingFeeInvoiceDue";
    public const string ListingFeeReceipt = "Email.ListingFeeReceipt";
    public const string BuyerFeeTaxInvoice = "Email.BuyerFeeTaxInvoice";
    public const string AdjustmentNote = "Email.AdjustmentNote";
}

public static class ContentCatalog
{
    public const int MaxKeyLength = 100;
    public const int MaxSubjectLength = 200;
    public const int MaxBodyLength = 4000;

    /// <summary>CreatedAt of the seeded version 1 rows.</summary>
    public static readonly DateTime SeededAt = new(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);

    public static IReadOnlyList<ContentCatalogEntry> Entries { get; } = Build();

    private static readonly Dictionary<string, ContentCatalogEntry> ByKey =
        Entries.ToDictionary(e => e.Key, StringComparer.Ordinal);

    public static ContentCatalogEntry? Find(string? key) =>
        key != null && ByKey.TryGetValue(key, out var entry) ? entry : null;

    /// <summary>A fixed id for the seeded version 1 row of a key (the same on every build and machine).</summary>
    public static Guid SeedId(string key) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("ContentItem:" + key)).AsSpan(0, 16));

    private static ContentCatalogEntry Disclosure(string key, string label, string description, string body) =>
        new(key, ContentGroups.Disclosures, label, description, HasSubject: false, SingleParagraph: true,
            [], [], null, body);

    private static ContentCatalogEntry Email(string key, string group, string label, string description,
        ContentPlaceholder[] placeholders, string[] required, string subject, string body) =>
        new(key, group, label, description, HasSubject: true, SingleParagraph: false, placeholders, required, subject, body);

    /// <summary>Placeholders (a nested class so its fields exist before Build runs).</summary>
    private static class P
    {
        public static readonly ContentPlaceholder StallionName = new("StallionName", "The stallion's name", "Snitzel");
        public static readonly ContentPlaceholder Season = new("Season", "The season, e.g. 2026 Season", "2026 Season");
        public static readonly ContentPlaceholder StudName = new("StudName", "The stud farm's name", "Arrowfield");
        public static readonly ContentPlaceholder BuyerName = new("BuyerName", "The buyer's name", "Jane Buyer");
        public static readonly ContentPlaceholder BuyerEmail = new("BuyerEmail", "The buyer's email address", "jane@example.com");
        public static readonly ContentPlaceholder BuyerFee = new("BuyerFee", "The buyer fee (inc. GST)", "$150.00");
        public static readonly ContentPlaceholder HighBid = new("HighBid", "The new highest bid (inc. GST)", "$12,500.00");
        public static readonly ContentPlaceholder ClosesAt = new("ClosesAt", "When the auction closes (Sydney time)", "10 Oct 2026, 8:00 pm (Sydney time)");
        public static readonly ContentPlaceholder WinningBid = new("Amount", "The winning bid (inc. GST)", "$10,000.00");
        public static readonly ContentPlaceholder OfferAmount = new("Amount", "The offer amount (inc. GST)", "$9,000.00");
        public static readonly ContentPlaceholder SalePrice = new("Amount", "The sale price (inc. GST)", "$10,000.00");
        public static readonly ContentPlaceholder PaymentDeadline = new("Deadline", "The deadline to update the card (Sydney time)", "10 Oct 2026, 10:00 pm (Sydney time)");
        public static readonly ContentPlaceholder OfferDeadline = new("Deadline", "When the offer expires (Sydney time)", "18 Oct 2026, 12:00 pm (Sydney time)");
        public static readonly ContentPlaceholder CardFailure = new("Reason", "Why the card was declined, from the card issuer", "Your card was declined.");
        public static readonly ContentPlaceholder NoSaleReason = new("Reason", "Why there was no sale, e.g. “the highest bid was below your reserve.”", "the highest bid was below your reserve.");
        public static readonly ContentPlaceholder InvoiceNumber = new("InvoiceNumber", "The tax invoice number", "SA-000123");
        public static readonly ContentPlaceholder NoteNumber = new("InvoiceNumber", "The adjustment note number", "SA-000130");
        public static readonly ContentPlaceholder ListingFeeLine = new("Description", "The invoice line", "Listing fee — Dundeel, 2026 Season");
        public static readonly ContentPlaceholder BuyerFeeLine = new("Description", "The invoice line", "Buyer fee — nomination to Snitzel, 2026 Season (auction)");
        public static readonly ContentPlaceholder NoteLine = new("Description", "The line of the invoice being reversed", "Buyer fee — nomination to Snitzel, 2026 Season (auction)");
        public static readonly ContentPlaceholder InvoiceTotal = new("Amount", "The invoice total (inc. GST)", "$990.00");
        public static readonly ContentPlaceholder BuyerFeeTotal = new("Amount", "The buyer fee charged (inc. GST)", "$150.00");
        public static readonly ContentPlaceholder NoteTotal = new("Amount", "The amount reversed (inc. GST)", "$150.00");
        public static readonly ContentPlaceholder InvoiceStud = new("StudName", "The stud farm the invoice is addressed to", "Arrowfield");
        public static readonly ContentPlaceholder InvoiceBuyer = new("BuyerName", "The buyer the invoice is addressed to", "Jane Buyer");
        public static readonly ContentPlaceholder DueDate = new("DueDate", "The payment due date", "25 Oct 2026");
        public static readonly ContentPlaceholder OriginalNumber = new("OriginalNumber", "The number of the invoice being reversed", "SA-000123");
        public static readonly ContentPlaceholder OriginalDate = new("OriginalDate", "The date of the invoice being reversed", "11 Oct 2026");
        public static readonly ContentPlaceholder AdjustmentReason = new("Reason", "The reason Staff gave for the refund or waiver", "Buyer fee refunded — duplicate charge");
    }

    private static IReadOnlyList<ContentCatalogEntry> Build() =>
    [
        // ── Disclosures (single paragraph, no placeholders) ─────────────────────
        Disclosure(ContentKeys.BuyerFeeExplanation, "Buyer fee explanation",
            "Shown before bidding or making an offer, in the bid and offer confirmations, in win and offer-accepted emails, and on buyer fee tax invoices.",
            "The buyer fee is paid to Stallions Australia and forms part of the price — it is not added on top. You pay the balance (the price less the buyer fee) directly to the stud, under the stud's own terms. The price arrangement is entirely between you and the stud."),
        Disclosure(ContentKeys.StudFarmBalanceArrangement, "Paying the stud (balance arrangement)",
            "Shown before bidding or making an offer, in the offer confirmation and in win and offer-accepted emails: how the balance is paid to the stud.",
            "The stud farm will contact you separately to arrange payment of the remaining balance. The balance amount, payment terms, and any foal guarantee conditions are a matter between you and the stud farm only. Stallions Australia is not party to that arrangement."),
        Disclosure(ContentKeys.SavedCardExplanation, "Saved card explanation",
            "Shown on the buyer's saved card page: what the card is (and isn't) charged for.",
            "Your saved card is charged the buyer fee automatically when you win an auction or a stud accepts your offer. The nomination price is not charged to this card — you pay the balance directly to the stud, under the stud's own terms."),

        // ── Buyer emails ───────────────────────────────────────────────────────
        Email(ContentKeys.Outbid, ContentGroups.BuyerEmails, "Outbid",
            "To a bidder when another buyer places a higher bid. A “Bid again” link is added below your text.",
            [P.StallionName, P.Season, P.StudName, P.HighBid, P.ClosesAt], ["StallionName", "HighBid"],
            "You've been outbid on {StallionName}",
            "Another buyer has bid {HighBid} on the {StallionName} nomination, so you are no longer the highest bidder.\n\nThe auction closes {ClosesAt}."),
        Email(ContentKeys.AuctionLost, ContentGroups.BuyerEmails, "Auction lost",
            "To each losing bidder when an auction closes with a sale.",
            [P.StallionName, P.Season, P.StudName], ["StallionName"],
            "Auction closed: {StallionName}",
            "The auction for the {StallionName} nomination ({Season}) has closed and another bid won.\n\nThank you for bidding."),
        Email(ContentKeys.AuctionEndedWithoutSale, ContentGroups.BuyerEmails, "Auction ended without a sale",
            "To each bidder when an auction closes without a sale.",
            [P.StallionName, P.Season, P.StudName], ["StallionName"],
            "Auction closed: {StallionName}",
            "The auction for the {StallionName} nomination ({Season}) has closed without a sale.\n\nThank you for bidding."),
        Email(ContentKeys.WonAndCharged, ContentGroups.BuyerEmails, "Auction won — buyer fee charged",
            "To the winning bidder once the buyer fee is charged. The amounts table, the buyer fee disclosure, the stud's contact details, the tax invoice link and the sale record link are added below your text.",
            [P.StallionName, P.Season, P.StudName, P.WinningBid, P.BuyerFee], ["StallionName", "Amount"],
            "You won the {StallionName} nomination",
            "Your bid of {Amount} won the auction for the {StallionName} nomination ({Season}), offered by {StudName}."),
        Email(ContentKeys.OfferAcceptedAndCharged, ContentGroups.BuyerEmails, "Offer accepted — buyer fee charged",
            "To the buyer once the stud accepts their offer and the buyer fee is charged. The amounts table, the buyer fee disclosure, the stud's contact details, the tax invoice link and the sale record link are added below your text.",
            [P.StallionName, P.Season, P.StudName, P.OfferAmount, P.BuyerFee], ["StallionName", "Amount"],
            "Your offer for the {StallionName} nomination was accepted",
            "Your offer of {Amount} for the {StallionName} nomination ({Season}) was accepted by {StudName}."),
        Email(ContentKeys.PaymentFailed, ContentGroups.BuyerEmails, "Buyer fee payment failed (auction)",
            "To the winning bidder when the buyer fee can't be charged; they have until the deadline to update their card. An “Update your card” link is added below your text.",
            [P.StallionName, P.Season, P.StudName, P.WinningBid, P.BuyerFee, P.CardFailure, P.PaymentDeadline], ["BuyerFee", "Deadline"],
            "Action needed: payment for {StallionName} failed",
            "Your bid of {Amount} won the auction for the {StallionName} nomination, but we couldn't charge the buyer fee of {BuyerFee} to your saved card: {Reason}\n\nPlease update your card by {Deadline}. Saving a new card retries the payment straight away. If the payment can't be completed by then, the sale won't go ahead."),
        Email(ContentKeys.PaymentFailedOffer, ContentGroups.BuyerEmails, "Buyer fee payment failed (accepted offer)",
            "To the buyer when the buyer fee for their accepted offer can't be charged; they have until the deadline to update their card. An “Update your card” link is added below your text.",
            [P.StallionName, P.Season, P.StudName, P.OfferAmount, P.BuyerFee, P.CardFailure, P.PaymentDeadline], ["BuyerFee", "Deadline"],
            "Action needed: payment for {StallionName} failed",
            "Your offer of {Amount} for the {StallionName} nomination was accepted, but we couldn't charge the buyer fee of {BuyerFee} to your saved card: {Reason}\n\nPlease update your card by {Deadline}. Saving a new card retries the payment straight away. If the payment can't be completed by then, the sale won't go ahead."),
        Email(ContentKeys.PaymentAbandoned, ContentGroups.BuyerEmails, "No sale — payment not completed (auction)",
            "To the winning bidder when the deadline passes without a successful buyer fee charge.",
            [P.StallionName, P.Season, P.StudName, P.WinningBid, P.BuyerFee], ["StallionName"],
            "No sale: {StallionName}",
            "We couldn't charge the buyer fee for the {StallionName} nomination by the deadline, so the sale has not gone ahead and the nomination is no longer yours."),
        Email(ContentKeys.PaymentAbandonedOffer, ContentGroups.BuyerEmails, "No sale — payment not completed (accepted offer)",
            "To the buyer when the deadline passes without a successful buyer fee charge for their accepted offer.",
            [P.StallionName, P.Season, P.StudName, P.OfferAmount, P.BuyerFee], ["StallionName"],
            "No sale: {StallionName}",
            "We couldn't charge the buyer fee for your accepted offer on the {StallionName} nomination by the deadline, so the sale has not gone ahead."),
        Email(ContentKeys.OfferDeclined, ContentGroups.BuyerEmails, "Offer declined",
            "To the buyer when the stud declines their offer. A “View the listing” link is added below your text.",
            [P.StallionName, P.Season, P.StudName, P.OfferAmount], ["Amount"],
            "Offer declined: {StallionName}",
            "The stud has declined your offer of {Amount} for the {StallionName} nomination ({Season})."),
        Email(ContentKeys.OfferExpired, ContentGroups.BuyerEmails, "Offer expired",
            "To the buyer when their offer expires without a response. A “View the listing” link is added below your text.",
            [P.StallionName, P.Season, P.StudName, P.OfferAmount], ["Amount"],
            "Offer closed: {StallionName}",
            "Your offer of {Amount} for the {StallionName} nomination ({Season}) has expired without a response from the stud."),
        Email(ContentKeys.OfferExpiredListingClosed, ContentGroups.BuyerEmails, "Offer closed — listing closed",
            "To the buyer when the listing stops taking offers while their offer is open. A “View the listing” link is added below your text.",
            [P.StallionName, P.Season, P.StudName, P.OfferAmount], ["Amount"],
            "Offer closed: {StallionName}",
            "The {StallionName} nomination ({Season}) is no longer taking offers, so your offer of {Amount} has closed without a response."),

        // ── Stud emails ────────────────────────────────────────────────────────
        Email(ContentKeys.StudSaleConfirmation, ContentGroups.StudEmails, "Sale confirmed (auction)",
            "To the stud when an auction sells and the buyer fee is paid. The amounts table, “Please arrange the balance with the buyer under your own terms.” and the listing link are added below your text.",
            [P.StallionName, P.Season, P.StudName, P.BuyerName, P.BuyerEmail, P.SalePrice, P.BuyerFee], ["BuyerName", "BuyerEmail"],
            "Sale confirmed: {StallionName} nomination",
            "The auction for the {StallionName} nomination ({Season}) has closed with a sale, and the buyer fee has been paid.\n\nBuyer: {BuyerName}, {BuyerEmail}"),
        Email(ContentKeys.StudSaleConfirmationOffer, ContentGroups.StudEmails, "Sale confirmed (accepted offer)",
            "To the stud when an accepted offer's buyer fee is paid. The amounts table, “Please arrange the balance with the buyer under your own terms.” and the listing link are added below your text.",
            [P.StallionName, P.Season, P.StudName, P.BuyerName, P.BuyerEmail, P.SalePrice, P.BuyerFee], ["BuyerName", "BuyerEmail"],
            "Sale confirmed: {StallionName} nomination",
            "You accepted an offer for the {StallionName} nomination ({Season}), and the buyer fee has been paid.\n\nBuyer: {BuyerName}, {BuyerEmail}"),
        Email(ContentKeys.StudNoSaleAuction, ContentGroups.StudEmails, "No sale (auction)",
            "To the stud when an auction closes without a sale (no bids, reserve not met, or the winner's payment failed). An “Offer it as Make an Offer” link is added below your text.",
            [P.StallionName, P.Season, P.StudName, P.NoSaleReason], ["Reason"],
            "No sale: {StallionName} nomination",
            "The auction for the {StallionName} nomination ({Season}) has closed without a sale: {Reason}\n\nThe lot is marked Unsold in My Listings."),
        Email(ContentKeys.StudNoSaleMakeAnOffer, ContentGroups.StudEmails, "No sale (accepted offer)",
            "To the stud when the buyer fee for an offer they accepted couldn't be charged. A “View the listing” link is added below your text.",
            [P.StallionName, P.Season, P.StudName], ["StallionName"],
            "No sale: {StallionName} nomination",
            "The sale from the offer you accepted for the {StallionName} nomination ({Season}) has not gone ahead: the buyer's payment couldn't be completed.\n\nYour Make an Offer listing stays open for other offers."),
        Email(ContentKeys.OfferReceived, ContentGroups.StudEmails, "New offer",
            "To the stud when a buyer makes an offer. A “Review the offer” link is added below your text.",
            [P.StallionName, P.Season, P.StudName, P.BuyerName, P.BuyerEmail, P.OfferAmount, P.OfferDeadline], ["BuyerName", "BuyerEmail", "Amount", "Deadline"],
            "New offer: {StallionName} nomination",
            "{BuyerName} ({BuyerEmail}) has offered {Amount} for the {StallionName} nomination ({Season}).\n\nThe offer is open until {Deadline}. Accepting it charges the buyer the buyer fee straight away."),

        // ── Tax document emails ────────────────────────────────────────────────
        Email(ContentKeys.ListingFeeTaxInvoice, ContentGroups.TaxDocumentEmails, "Listing fee tax invoice (paid)",
            "To the stud when a listing fee is paid by card or marked paid. The invoice details, “Status: Paid” and the invoice link are added below your text.",
            [P.InvoiceNumber, P.ListingFeeLine, P.InvoiceTotal, P.InvoiceStud], [],
            "Tax invoice {InvoiceNumber}: {Description}",
            "Thank you — your listing fee has been paid. Your tax invoice is below."),
        Email(ContentKeys.ListingFeeInvoiceDue, ContentGroups.TaxDocumentEmails, "Listing fee tax invoice (amount due)",
            "To the stud when Staff issue an unpaid listing fee invoice. The invoice details, bank details, payment reference and invoice link are added below your text.",
            [P.InvoiceNumber, P.ListingFeeLine, P.InvoiceTotal, P.InvoiceStud, P.DueDate], ["Amount", "DueDate"],
            "Tax invoice {InvoiceNumber} — amount due {Amount} by {DueDate}",
            "Here is your tax invoice for the listing fee. Please pay {Amount} by {DueDate}."),
        Email(ContentKeys.ListingFeeReceipt, ContentGroups.TaxDocumentEmails, "Listing fee receipt",
            "To the stud when an unpaid listing fee invoice is marked paid. The invoice details, the paid date and the invoice link are added below your text.",
            [P.InvoiceNumber, P.ListingFeeLine, P.InvoiceTotal, P.InvoiceStud], ["Amount"],
            "Receipt: tax invoice {InvoiceNumber} paid",
            "We've received your payment of {Amount} for tax invoice {InvoiceNumber}. Thank you."),
        Email(ContentKeys.BuyerFeeTaxInvoice, ContentGroups.TaxDocumentEmails, "Buyer fee tax invoice",
            "To the buyer when the buyer fee is charged. The invoice details, the buyer fee explanation and the invoice link are added below your text.",
            [P.InvoiceNumber, P.BuyerFeeLine, P.BuyerFeeTotal, P.InvoiceBuyer], [],
            "Tax invoice {InvoiceNumber}: buyer fee",
            "The buyer fee has been charged to your saved card. Your tax invoice is below."),
        Email(ContentKeys.AdjustmentNote, ContentGroups.TaxDocumentEmails, "Adjustment note",
            "To the original invoice's recipient when a fee is refunded or an invoiced fee is waived. The note's details and link are added below your text.",
            [P.NoteNumber, P.OriginalNumber, P.OriginalDate, P.NoteLine, P.NoteTotal, P.AdjustmentReason], ["OriginalNumber", "Reason"],
            "Adjustment note {InvoiceNumber} for tax invoice {OriginalNumber}",
            "This adjustment note reverses tax invoice {OriginalNumber} dated {OriginalDate} in full.\n\nReason: {Reason}")
    ];
}
