using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;
using Stallions.Server.Content;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.Enums;

namespace Stallions.Server.Email;

/// <summary>
/// The auction and Make an Offer emails. Staff edit each email's subject and message (Staff → Wording); the amounts
/// table, the mandatory buyer-fee disclosure block, contact details and links are generated here and always added.
/// Amounts inc. GST; times in Sydney time. Bidders are never told who else bid.
/// </summary>
public class AuctionEmails : IAuctionEmails
{
    private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");
    private static readonly TimeZoneInfo Sydney = FindSydney();

    private readonly IEmailOutbox _outbox;
    private readonly IUserRepository _users;
    private readonly ITaxDocumentRepository _taxDocuments;
    private readonly IContentService _content;
    private readonly string _baseUrl;
    private readonly ILogger<AuctionEmails> _log;

    public AuctionEmails(IEmailOutbox outbox, IUserRepository users, ITaxDocumentRepository taxDocuments,
        IContentService content, IOptions<EmailOptions> email, ILogger<AuctionEmails> log)
    {
        _outbox = outbox;
        _users = users;
        _taxDocuments = taxDocuments;
        _content = content;
        _baseUrl = email.Value.PublicBaseUrl.TrimEnd('/');
        _log = log;
    }

    public async Task OutbidAsync(AuctionListing listing, Guid previousBidderUserId, decimal newHighBidIncGst)
    {
        var bidder = await _users.GetByIdAsync(previousBidderUserId);
        if (bidder == null) return;
        var email = await RenderAsync(ContentKeys.Outbid, listing,
            ("HighBid", Money(newHighBidIncGst)), ("ClosesAt", When(listing.EndDateTime)));
        await QueueAsync(bidder.Email, "Outbid", email, listing,
            Link($"/listings/{listing.Id}", "Bid again"));
    }

    public async Task AuctionLostAsync(AuctionListing listing, User bidder) =>
        await QueueAsync(bidder.Email, "AuctionLost", await RenderAsync(ContentKeys.AuctionLost, listing), listing);

    public async Task AuctionEndedWithoutSaleAsync(AuctionListing listing, User bidder) =>
        await QueueAsync(bidder.Email, "AuctionEndedWithoutSale",
            await RenderAsync(ContentKeys.AuctionEndedWithoutSale, listing), listing);

    private static bool FromOffer(Purchase p) => p.OfferId != null;

    public async Task WonAndChargedAsync(Listing listing, Purchase purchase, User winner)
    {
        // The charger issues the buyer fee tax invoice just before this email, in the same transaction.
        var invoice = await _taxDocuments.GetBuyerFeeInvoiceAsync(purchase.Id);
        var fromOffer = FromOffer(purchase);
        var email = await RenderAsync(fromOffer ? ContentKeys.OfferAcceptedAndCharged : ContentKeys.WonAndCharged, listing,
            ("Amount", Money(purchase.TotalPriceIncGst)), ("BuyerFee", Money(purchase.BuyerFeeIncGst)));

        var blocks = new List<Block>
        {
            fromOffer
                ? Amounts(purchase, "Price (your accepted offer)", "Balance you pay the stud directly")
                : Amounts(purchase, "Price (your winning bid)", "Balance you pay the stud directly"),
            // The mandatory buyer-fee disclosure block: the fee charged, then the Staff-managed disclosure wording.
            Para($"The buyer fee of {Money(purchase.BuyerFeeIncGst)} has been charged to your saved card.")
        };
        blocks.AddRange(await DisclosureAsync(ContentKeys.BuyerFeeExplanation));
        blocks.AddRange(await DisclosureAsync(ContentKeys.StudFarmBalanceArrangement));
        blocks.Add(Para(StudContact(listing)));
        if (invoice != null)
            blocks.Add(Link($"/tax-documents/{invoice.Id}", $"Your tax invoice {invoice.Number} is available here"));
        blocks.Add(Link("/my-purchases", "View your sale record"));

        await QueueAsync(winner.Email, fromOffer ? "OfferAcceptedAndCharged" : "WonAndCharged", email, listing, blocks.ToArray());
    }

    public async Task PaymentFailedAsync(Listing listing, Purchase purchase, User winner)
    {
        var email = await RenderAsync(FromOffer(purchase) ? ContentKeys.PaymentFailedOffer : ContentKeys.PaymentFailed, listing,
            ("Amount", Money(purchase.TotalPriceIncGst)), ("BuyerFee", Money(purchase.BuyerFeeIncGst)),
            ("Reason", purchase.LastChargeFailure), ("Deadline", When(purchase.ChargeDueBy ?? DateTime.UtcNow)));
        await QueueAsync(winner.Email, "PaymentFailed", email, listing,
            Link("/account/card", "Update your card"));
    }

    public async Task PaymentAbandonedAsync(Listing listing, Purchase purchase, User winner)
    {
        var email = await RenderAsync(FromOffer(purchase) ? ContentKeys.PaymentAbandonedOffer : ContentKeys.PaymentAbandoned, listing,
            ("Amount", Money(purchase.TotalPriceIncGst)), ("BuyerFee", Money(purchase.BuyerFeeIncGst)));
        await QueueAsync(winner.Email, "PaymentAbandoned", email, listing);
    }

    public async Task StudSaleConfirmationAsync(Listing listing, Purchase purchase, User winner)
    {
        var email = await RenderAsync(FromOffer(purchase) ? ContentKeys.StudSaleConfirmationOffer : ContentKeys.StudSaleConfirmation, listing,
            ("BuyerName", winner.DisplayName), ("BuyerEmail", winner.Email),
            ("Amount", Money(purchase.TotalPriceIncGst)), ("BuyerFee", Money(purchase.BuyerFeeIncGst)));
        await QueueAsync(await StudAddressAsync(listing), "StudSaleConfirmation", email, listing,
            Amounts(purchase, "Price", "Balance the buyer pays you directly"),
            Para("Please arrange the balance with the buyer under your own terms."),
            Link($"/admin/listings/{listing.Id}", "View the listing"));
    }

    public async Task StudNoSaleAsync(Listing listing, ListingCloseReason reason)
    {
        var to = await StudAddressAsync(listing);
        if (listing is MakeAnOfferListing)
            await QueueAsync(to, "StudNoSale", await RenderAsync(ContentKeys.StudNoSaleMakeAnOffer, listing), listing,
                Link($"/admin/listings/{listing.Id}", "View the listing"));
        else
            await QueueAsync(to, "StudNoSale",
                await RenderAsync(ContentKeys.StudNoSaleAuction, listing, ("Reason", ReasonText(reason))), listing,
                Link($"/admin/listings/{listing.Id}", "Offer it as Make an Offer"));
    }

    public async Task OfferReceivedAsync(MakeAnOfferListing listing, Offer offer, User buyer)
    {
        var email = await RenderAsync(ContentKeys.OfferReceived, listing,
            ("BuyerName", buyer.DisplayName), ("BuyerEmail", buyer.Email),
            ("Amount", Money(offer.AmountIncGst)), ("Deadline", When(offer.ExpiresAt)));
        await QueueAsync(await StudAddressAsync(listing), "OfferReceived", email, listing,
            Link($"/admin/listings/{listing.Id}", "Review the offer"));
    }

    public async Task OfferDeclinedAsync(MakeAnOfferListing listing, Offer offer, User buyer) =>
        await QueueAsync(buyer.Email, "OfferDeclined",
            await RenderAsync(ContentKeys.OfferDeclined, listing, ("Amount", Money(offer.AmountIncGst))), listing,
            Link($"/listings/{listing.Id}", "View the listing"));

    public async Task OfferExpiredAsync(MakeAnOfferListing listing, Offer offer, User buyer, bool listingClosed) =>
        await QueueAsync(buyer.Email, "OfferExpired",
            await RenderAsync(listingClosed ? ContentKeys.OfferExpiredListingClosed : ContentKeys.OfferExpired, listing,
                ("Amount", Money(offer.AmountIncGst))), listing,
            Link($"/listings/{listing.Id}", "View the listing"));

    // ── Building blocks ────────────────────────────────────────────────────────

    private sealed record Block(string Html, string Text);

    /// <summary>The Staff wording for this email, with the listing's names plus these values.</summary>
    private async Task<RenderedEmail> RenderAsync(string key, Listing listing, params (string Name, string? Value)[] values)
    {
        var all = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["StallionName"] = Name(listing),
            ["Season"] = Season(listing),
            ["StudName"] = Farm(listing)
        };
        foreach (var (name, value) in values) all[name] = value;
        return (await _content.RenderEmailAsync(key, all)).WithSingleLineSubject();
    }

    private async Task<IEnumerable<Block>> DisclosureAsync(string key) =>
        (await _content.RenderTextAsync(key)).Select(ToBlock);

    private static Block ToBlock(RenderedParagraph p) => new($"<p>{p.Html}</p>", p.Text);

    /// <summary>The Staff message paragraphs first, then the generated blocks.</summary>
    private async Task QueueAsync(string? to, string template, RenderedEmail email, Listing listing, params Block[] blocks)
    {
        var all = email.Paragraphs.Select(ToBlock).Concat(blocks).ToList();
        var html = "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\">"
                   + string.Concat(all.Select(b => b.Html))
                   + "<p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>";
        var text = string.Join("\n\n", all.Select(b => b.Text)) + "\n\nStallions Australia";
        await _outbox.EnqueueAsync(new OutgoingEmail(to ?? string.Empty, email.Subject, html, text, template), "Listing", listing.Id);
    }

    private static Block Para(string text) => new($"<p>{Enc(text)}</p>", text);

    private Block Link(string path, string label)
    {
        var url = _baseUrl + path;
        return new($"<p><a href=\"{Enc(url)}\">{Enc(label)}</a></p>", $"{label}: {url}");
    }

    private static Block Amounts(Purchase p, string priceLabel, string balanceLabel)
    {
        var rows = new[]
        {
            (priceLabel, Money(p.TotalPriceIncGst)),
            ("Buyer fee paid to Stallions Australia", Money(p.BuyerFeeIncGst)),
            (balanceLabel, Money(p.BalancePayableToStudIncGst))
        };
        var html = "<table style=\"border-collapse:collapse\">"
                   + string.Concat(rows.Select(r =>
                       $"<tr><td style=\"padding:4px 16px 4px 0\">{Enc(r.Item1)}</td><td style=\"padding:4px 0;text-align:right\"><strong>{Enc(r.Item2)}</strong></td></tr>"))
                   + "</table><p style=\"color:#6b7280;font-size:13px\">All amounts include GST.</p>";
        var text = string.Join("\n", rows.Select(r => $"{r.Item1}: {r.Item2}")) + "\nAll amounts include GST.";
        return new(html, text);
    }

    private async Task<string?> StudAddressAsync(Listing listing)
    {
        var farm = listing.StudFarm;
        if (!string.IsNullOrWhiteSpace(farm?.ContactEmail)) return farm.ContactEmail;
        var owner = farm?.User ?? (farm != null ? await _users.GetByIdAsync(farm.UserId) : null);
        if (string.IsNullOrWhiteSpace(owner?.Email))
            _log.LogWarning("Stud for listing {ListingId} has no contact or owner email", listing.Id);
        return owner?.Email;
    }

    private static string StudContact(Listing listing)
    {
        var farm = listing.StudFarm;
        var parts = new[] { farm?.Name, farm?.ContactEmail, farm?.ContactPhone }.Where(p => !string.IsNullOrWhiteSpace(p));
        return $"Stud contact: {string.Join(", ", parts)}";
    }

    private static string ReasonText(ListingCloseReason reason) => reason switch
    {
        ListingCloseReason.NoBids => "there were no bids.",
        ListingCloseReason.ReserveNotMet => "the highest bid was below your reserve.",
        ListingCloseReason.ChargeFailed => "the winning buyer's payment couldn't be completed.",
        _ => "no sale was made."
    };

    private static string Name(Listing l) => l.Stallion?.Name ?? "stallion";
    private static string Season(Listing l) => l.Season?.Name ?? "this season";
    private static string Farm(Listing l) => l.StudFarm?.Name ?? "the stud";
    private static string Money(decimal amount) => amount.ToString("C2", Au);
    private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string When(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Sydney)
            .ToString("d MMM yyyy, h:mm tt", Au) + " (Sydney time)";

    private static TimeZoneInfo FindSydney()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time"); }
    }
}
