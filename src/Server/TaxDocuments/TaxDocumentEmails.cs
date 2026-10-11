// src/Server/TaxDocuments/TaxDocumentEmails.cs
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;
using Stallions.Server.Data.Entities;
using Stallions.Server.Email;
using Stallions.Server.Infrastructure;
using Stallions.Server.Options;
using Stallions.Shared;
using Stallions.Shared.Enums;

namespace Stallions.Server.TaxDocuments;

/// <summary>The tax document emails, queued through the outbox in the caller's transaction.</summary>
public interface ITaxDocumentEmails
{
    /// <summary>To the stud: a Paid listing fee invoice (card payment or Staff mark paid).</summary>
    Task ListingFeeTaxInvoiceAsync(TaxDocument invoice);
    /// <summary>To the stud: an Unpaid listing fee invoice, with the due date and bank details.</summary>
    Task ListingFeeInvoiceDueAsync(TaxDocument invoice);
    /// <summary>To the stud: their Unpaid invoice has been paid.</summary>
    Task ListingFeeReceiptAsync(TaxDocument invoice);
    /// <summary>To the buyer: the buyer fee tax invoice.</summary>
    Task BuyerFeeTaxInvoiceAsync(TaxDocument invoice);
    /// <summary>To the original invoice's recipient.</summary>
    Task AdjustmentNoteAsync(TaxDocument note, TaxDocument original);
}

/// <summary>
/// Plain, factual wording (Staff-editable wording comes in 5b); amounts inc. GST with the ex-GST and GST split;
/// dates as Sydney dates. Buyer-fee policy wording comes from DisclosureOptions.
/// </summary>
public class TaxDocumentEmails : ITaxDocumentEmails
{
    private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");

    private readonly IEmailOutbox _outbox;
    private readonly DisclosureOptions _disclosure;
    private readonly string _baseUrl;

    public TaxDocumentEmails(IEmailOutbox outbox, IOptions<DisclosureOptions> disclosure, IOptions<EmailOptions> email)
    {
        _outbox = outbox;
        _disclosure = disclosure.Value;
        _baseUrl = email.Value.PublicBaseUrl.TrimEnd('/');
    }

    public Task ListingFeeTaxInvoiceAsync(TaxDocument d) =>
        QueueAsync(d, "ListingFeeTaxInvoice", $"Tax invoice {d.Number}: {d.Description}",
            Para("Thank you — your listing fee has been paid. Your tax invoice is below."),
            Details(d),
            Para("Status: Paid"),
            DocumentLink(d, "View or print your tax invoice"));

    public Task ListingFeeInvoiceDueAsync(TaxDocument d) =>
        QueueAsync(d, "ListingFeeInvoiceDue", $"Tax invoice {d.Number} — amount due {Money(d.AmountIncGst)} by {Date(d.DueAt)}",
            Para($"Here is your tax invoice for the listing fee. Please pay {Money(d.AmountIncGst)} by {Date(d.DueAt)}."),
            Details(d),
            Rows(("Account name", d.BankAccountName ?? string.Empty),
                 ("BSB", BusinessNumbers.FormatBsb(d.BankBsb)),
                 ("Account number", d.BankAccountNumber ?? string.Empty)),
            Para($"Please use {d.Number} as your payment reference."),
            DocumentLink(d, "View or print your tax invoice"));

    public Task ListingFeeReceiptAsync(TaxDocument d) =>
        QueueAsync(d, "ListingFeeReceipt", $"Receipt: tax invoice {d.Number} paid",
            Para($"We've received your payment of {Money(d.AmountIncGst)} for tax invoice {d.Number}. Thank you."),
            Details(d),
            Para($"Paid: {Date(d.PaidAt)}"),
            DocumentLink(d, "View or print your tax invoice"));

    public Task BuyerFeeTaxInvoiceAsync(TaxDocument d) =>
        QueueAsync(d, "BuyerFeeTaxInvoice", $"Tax invoice {d.Number}: buyer fee",
            Para("The buyer fee has been charged to your saved card. Your tax invoice is below."),
            Details(d),
            Para(_disclosure.BuyerFeeExplanation),
            DocumentLink(d, "View or print your tax invoice"));

    public Task AdjustmentNoteAsync(TaxDocument note, TaxDocument original) =>
        QueueAsync(note, "AdjustmentNote", $"Adjustment note {note.Number} for tax invoice {original.Number}",
            Para($"This adjustment note reverses tax invoice {original.Number} dated {Date(original.IssuedAt)} in full."),
            Para($"Reason: {note.Reason}"),
            Details(note),
            DocumentLink(note, "View or print your adjustment note"));

    // ── Building blocks ────────────────────────────────────────────────────────

    private sealed record Block(string Html, string Text);

    private async Task QueueAsync(TaxDocument d, string template, string subject, params Block[] blocks)
    {
        var html = "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\">"
                   + string.Concat(blocks.Select(b => b.Html))
                   + "<p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>";
        var text = string.Join("\n\n", blocks.Select(b => b.Text)) + "\n\nStallions Australia";
        var trimmedSubject = subject.Length > 300 ? subject[..300] : subject;
        await _outbox.EnqueueAsync(new OutgoingEmail(d.RecipientEmail, trimmedSubject, html, text, template), "TaxDocument", d.Id);
    }

    private static Block Para(string text) => new($"<p>{Enc(text)}</p>", text);

    private Block DocumentLink(TaxDocument d, string label)
    {
        var url = $"{_baseUrl}/tax-documents/{d.Id}";
        return new($"<p><a href=\"{Enc(url)}\">{Enc(label)}</a></p>", $"{label}: {url}");
    }

    /// <summary>The document's key details: heading and number, date, supplier and ABN, recipient, amounts.</summary>
    private static Block Details(TaxDocument d)
    {
        var rows = new List<(string, string)>
        {
            (d.Kind == TaxDocumentKind.AdjustmentNote ? "Adjustment note" : "Tax invoice", d.Number),
            ("Date", Date(d.IssuedAt)),
            ("Supplier", $"{d.SupplierName}, ABN {BusinessNumbers.FormatAbn(d.SupplierAbn)}"),
            ("To", d.RecipientName),
            ("Description", d.Description)
        };
        if (d.StandardAmountIncGst is { } standard && d.DiscountIncGst is { } discount)
        {
            rows.Add(("Standard listing fee", Money(standard)));
            rows.Add(($"Intro offer: {d.IntroOfferName}", "−" + Money(discount)));
        }
        rows.Add(("Amount (excl. GST)", Money(d.AmountExGst)));
        rows.Add(("GST", Money(d.GstAmount)));
        rows.Add(("Total (incl. GST)", Money(d.AmountIncGst)));
        return Rows(rows.ToArray());
    }

    private static Block Rows(params (string Label, string Value)[] rows)
    {
        var html = "<table style=\"border-collapse:collapse\">"
                   + string.Concat(rows.Select(r =>
                       $"<tr><td style=\"padding:4px 16px 4px 0\">{Enc(r.Label)}</td><td style=\"padding:4px 0;text-align:right\"><strong>{Enc(r.Value)}</strong></td></tr>"))
                   + "</table>";
        var text = string.Join("\n", rows.Select(r => $"{r.Label}: {r.Value}"));
        return new(html, text);
    }

    private static string Money(decimal amount) => amount.ToString("C2", Au);
    private static string Date(DateTime? utc) => utc is { } v ? SydneyTime.Today(v).ToString("d MMM yyyy", Au) : string.Empty;
    private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
