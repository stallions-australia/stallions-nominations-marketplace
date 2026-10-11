using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Options;
using Stallions.Shared.Enums;

namespace Stallions.Server.Email;

/// <summary>
/// The auction emails. Plain, factual wording; amounts inc. GST; times in Sydney time. Buyer-fee
/// policy wording comes from DisclosureOptions (configured), never from this file. Bidders are
/// never told who else bid.
/// </summary>
public class AuctionEmails : IAuctionEmails
{
    private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");
    private static readonly TimeZoneInfo Sydney = FindSydney();

    private readonly IEmailOutbox _outbox;
    private readonly IUserRepository _users;
    private readonly DisclosureOptions _disclosure;
    private readonly string _baseUrl;
    private readonly ILogger<AuctionEmails> _log;

    public AuctionEmails(IEmailOutbox outbox, IUserRepository users, IOptions<DisclosureOptions> disclosure,
        IOptions<EmailOptions> email, ILogger<AuctionEmails> log)
    {
        _outbox = outbox;
        _users = users;
        _disclosure = disclosure.Value;
        _baseUrl = email.Value.PublicBaseUrl.TrimEnd('/');
        _log = log;
    }

    public async Task OutbidAsync(AuctionListing listing, Guid previousBidderUserId, decimal newHighBidIncGst)
    {
        var bidder = await _users.GetByIdAsync(previousBidderUserId);
        if (bidder == null) return;
        await QueueAsync(bidder.Email, "Outbid", $"You've been outbid on {Name(listing)}", listing,
            Para($"Another buyer has bid {Money(newHighBidIncGst)} on the {Name(listing)} nomination, so you are no longer the highest bidder."),
            Para($"The auction closes {When(listing.EndDateTime)}."),
            Link($"/listings/{listing.Id}", "Bid again"));
    }

    public Task AuctionLostAsync(AuctionListing listing, User bidder) =>
        QueueAsync(bidder.Email, "AuctionLost", $"Auction closed: {Name(listing)}", listing,
            Para($"The auction for the {Name(listing)} nomination ({Season(listing)}) has closed and another bid won."),
            Para("Thank you for bidding."));

    public Task AuctionEndedWithoutSaleAsync(AuctionListing listing, User bidder) =>
        QueueAsync(bidder.Email, "AuctionEndedWithoutSale", $"Auction closed: {Name(listing)}", listing,
            Para($"The auction for the {Name(listing)} nomination ({Season(listing)}) has closed without a sale."),
            Para("Thank you for bidding."));

    public Task WonAndChargedAsync(AuctionListing listing, Purchase purchase, User winner) =>
        QueueAsync(winner.Email, "WonAndCharged", $"You won the {Name(listing)} nomination", listing,
            Para($"Your bid of {Money(purchase.TotalPriceIncGst)} won the auction for the {Name(listing)} nomination ({Season(listing)}), offered by {Farm(listing)}."),
            Amounts(purchase, balanceLabel: "Balance you pay the stud directly"),
            Para($"The buyer fee of {Money(purchase.BuyerFeeIncGst)} has been charged to your saved card."),
            Para(_disclosure.BuyerFeeExplanation),
            Para(_disclosure.StudFarmBalanceArrangement),
            Para(StudContact(listing)),
            Link("/my-purchases", "View your sale record"));

    public Task PaymentFailedAsync(AuctionListing listing, Purchase purchase, User winner) =>
        QueueAsync(winner.Email, "PaymentFailed", $"Action needed: payment for {Name(listing)} failed", listing,
            Para($"Your bid of {Money(purchase.TotalPriceIncGst)} won the auction for the {Name(listing)} nomination, but we couldn't charge the buyer fee of {Money(purchase.BuyerFeeIncGst)} to your saved card: {purchase.LastChargeFailure}"),
            Para($"Please update your card by {When(purchase.ChargeDueBy ?? DateTime.UtcNow)}. Saving a new card retries the payment straight away. If the payment can't be completed by then, the sale won't go ahead."),
            Link("/account/card", "Update your card"));

    public Task PaymentAbandonedAsync(AuctionListing listing, Purchase purchase, User winner) =>
        QueueAsync(winner.Email, "PaymentAbandoned", $"No sale: {Name(listing)}", listing,
            Para($"We couldn't charge the buyer fee for the {Name(listing)} nomination by the deadline, so the sale has not gone ahead and the nomination is no longer yours."));

    public async Task StudSaleConfirmationAsync(AuctionListing listing, Purchase purchase, User winner) =>
        await QueueAsync(await StudAddressAsync(listing), "StudSaleConfirmation", $"Sale confirmed: {Name(listing)} nomination", listing,
            Para($"The auction for the {Name(listing)} nomination ({Season(listing)}) has closed with a sale, and the buyer fee has been paid."),
            Para($"Buyer: {winner.DisplayName}, {winner.Email}"),
            Amounts(purchase, balanceLabel: "Balance the buyer pays you directly"),
            Para("Please arrange the balance with the buyer under your own terms."),
            Link($"/admin/listings/{listing.Id}", "View the listing"));

    public async Task StudNoSaleAsync(AuctionListing listing, ListingCloseReason reason) =>
        await QueueAsync(await StudAddressAsync(listing), "StudNoSale", $"No sale: {Name(listing)} nomination", listing,
            Para($"The auction for the {Name(listing)} nomination ({Season(listing)}) has closed without a sale: {ReasonText(reason)}"),
            Para("The lot is marked Unsold in My Listings."),
            Link($"/admin/listings/{listing.Id}", "View the listing"));

    // ── Building blocks ────────────────────────────────────────────────────────

    private sealed record Block(string Html, string Text);

    private async Task QueueAsync(string? to, string template, string subject, AuctionListing listing, params Block[] blocks)
    {
        var html = "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\">"
                   + string.Concat(blocks.Select(b => b.Html))
                   + "<p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>";
        var text = string.Join("\n\n", blocks.Select(b => b.Text)) + "\n\nStallions Australia";
        await _outbox.EnqueueAsync(new OutgoingEmail(to ?? string.Empty, subject, html, text, template), "Listing", listing.Id);
    }

    private static Block Para(string text) => new($"<p>{Enc(text)}</p>", text);

    private Block Link(string path, string label)
    {
        var url = _baseUrl + path;
        return new($"<p><a href=\"{Enc(url)}\">{Enc(label)}</a></p>", $"{label}: {url}");
    }

    private static Block Amounts(Purchase p, string balanceLabel)
    {
        var rows = new[]
        {
            ("Price (your winning bid)", Money(p.TotalPriceIncGst)),
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

    private async Task<string?> StudAddressAsync(AuctionListing listing)
    {
        var farm = listing.StudFarm;
        if (!string.IsNullOrWhiteSpace(farm?.ContactEmail)) return farm.ContactEmail;
        var owner = farm?.User ?? (farm != null ? await _users.GetByIdAsync(farm.UserId) : null);
        if (string.IsNullOrWhiteSpace(owner?.Email))
            _log.LogWarning("Stud for listing {ListingId} has no contact or owner email", listing.Id);
        return owner?.Email;
    }

    private static string StudContact(AuctionListing listing)
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

    private static string Name(AuctionListing l) => l.Stallion?.Name ?? "stallion";
    private static string Season(AuctionListing l) => l.Season?.Name ?? "this season";
    private static string Farm(AuctionListing l) => l.StudFarm?.Name ?? "the stud";
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
