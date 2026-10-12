// src/Server/TaxDocuments/TaxDocumentEmails.cs
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;
using Stallions.Server.Content;
using Stallions.Server.Data.Entities;
using Stallions.Server.Email;
using Stallions.Server.Infrastructure;
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
/// Staff edit each email's subject and message (Staff → Wording); the document details, bank details, payment
/// reference, buyer fee explanation and links are generated here. Amounts inc. GST with the ex-GST and GST split;
/// dates as Sydney dates.
/// </summary>
public class TaxDocumentEmails : ITaxDocumentEmails
{
    private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");

    private readonly IEmailOutbox _outbox;
    private readonly IContentService _content;
    private readonly string _baseUrl;

    public TaxDocumentEmails(IEmailOutbox outbox, IContentService content, IOptions<EmailOptions> email)
    {
        _outbox = outbox;
        _content = content;
        _baseUrl = email.Value.PublicBaseUrl.TrimEnd('/');
    }

    public async Task ListingFeeTaxInvoiceAsync(TaxDocument d) =>
        await QueueAsync(d, "ListingFeeTaxInvoice",
            await RenderAsync(ContentKeys.ListingFeeTaxInvoice, d, ("StudName", d.RecipientName)),
            Details(d),
            Para("Status: Paid"),
            DocumentLink(d, "View or print your tax invoice"));

    public async Task ListingFeeInvoiceDueAsync(TaxDocument d) =>
        await QueueAsync(d, "ListingFeeInvoiceDue",
            await RenderAsync(ContentKeys.ListingFeeInvoiceDue, d, ("StudName", d.RecipientName), ("DueDate", Date(d.DueAt))),
            Details(d),
            Rows(("Account name", d.BankAccountName ?? string.Empty),
                 ("BSB", BusinessNumbers.FormatBsb(d.BankBsb)),
                 ("Account number", d.BankAccountNumber ?? string.Empty)),
            Para($"Please use {d.Number} as your payment reference."),
            DocumentLink(d, "View or print your tax invoice"));

    public async Task ListingFeeReceiptAsync(TaxDocument d) =>
        await QueueAsync(d, "ListingFeeReceipt",
            await RenderAsync(ContentKeys.ListingFeeReceipt, d, ("StudName", d.RecipientName)),
            Details(d),
            Para($"Paid: {Date(d.PaidAt)}"),
            DocumentLink(d, "View or print your tax invoice"));

    public async Task BuyerFeeTaxInvoiceAsync(TaxDocument d)
    {
        var email = await RenderAsync(ContentKeys.BuyerFeeTaxInvoice, d, ("BuyerName", d.RecipientName));
        var blocks = new List<Block> { Details(d) };
        blocks.AddRange((await _content.RenderTextAsync(ContentKeys.BuyerFeeExplanation)).Select(ToBlock));
        blocks.Add(DocumentLink(d, "View or print your tax invoice"));
        await QueueAsync(d, "BuyerFeeTaxInvoice", email, blocks.ToArray());
    }

    public async Task AdjustmentNoteAsync(TaxDocument note, TaxDocument original) =>
        await QueueAsync(note, "AdjustmentNote",
            await RenderAsync(ContentKeys.AdjustmentNote, note,
                ("OriginalNumber", original.Number), ("OriginalDate", Date(original.IssuedAt)), ("Reason", note.Reason)),
            Details(note),
            DocumentLink(note, "View or print your adjustment note"));

    // ── Building blocks ────────────────────────────────────────────────────────

    private sealed record Block(string Html, string Text);

    private Task<RenderedEmail> RenderAsync(string key, TaxDocument d, params (string Name, string? Value)[] values)
    {
        var all = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["InvoiceNumber"] = d.Number,
            ["Description"] = d.Description,
            ["Amount"] = Money(d.AmountIncGst)
        };
        foreach (var (name, value) in values) all[name] = value;
        return RenderSingleLineSubjectAsync(key, all);
    }

    private async Task<RenderedEmail> RenderSingleLineSubjectAsync(string key, Dictionary<string, string?> values) =>
        (await _content.RenderEmailAsync(key, values)).WithSingleLineSubject();

    private static Block ToBlock(RenderedParagraph p) => new($"<p>{p.Html}</p>", p.Text);

    /// <summary>The Staff message paragraphs first, then the generated blocks.</summary>
    private async Task QueueAsync(TaxDocument d, string template, RenderedEmail email, params Block[] blocks)
    {
        var all = email.Paragraphs.Select(ToBlock).Concat(blocks).ToList();
        var html = "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\">"
                   + string.Concat(all.Select(b => b.Html))
                   + "<p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>";
        var text = string.Join("\n\n", all.Select(b => b.Text)) + "\n\nStallions Australia";
        var subject = email.Subject.Length > 300 ? email.Subject[..300] : email.Subject;
        await _outbox.EnqueueAsync(new OutgoingEmail(d.RecipientEmail, subject, html, text, template), "TaxDocument", d.Id);
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
