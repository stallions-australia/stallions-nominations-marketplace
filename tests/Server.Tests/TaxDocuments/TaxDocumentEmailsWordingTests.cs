using FluentAssertions;
using Moq;
using Stallions.Server.Content;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Email;
using Stallions.Server.TaxDocuments;
using Stallions.Server.Tests.Email;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.TaxDocuments;

/// <summary>The tax document emails' wording. The fixtures are the ones DefaultEmailGolden was captured with.</summary>
public class TaxDocumentEmailsWordingTests : IDisposable
{
    private static readonly DateTime Issued = new(2026, 10, 11, 2, 0, 0, DateTimeKind.Utc);

    private readonly AppDbContext _db = DbContextFactory.Create(Guid.NewGuid().ToString());
    private readonly List<OutgoingEmail> _queued = new();
    private readonly CapturingLogger<ContentService> _contentLog = new();

    public TaxDocumentEmailsWordingTests() => _db.Database.EnsureCreated();

    public void Dispose() => _db.Dispose();

    private TaxDocumentEmails CreateSut()
    {
        var outbox = new Mock<IEmailOutbox>();
        outbox.Setup(o => o.EnqueueAsync(It.IsAny<OutgoingEmail>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .Callback<OutgoingEmail, string?, Guid?>((e, _, _) => _queued.Add(e))
            .Returns(Task.CompletedTask);
        return new TaxDocumentEmails(outbox.Object, ContentTestData.Service(_db, log: _contentLog),
            Microsoft.Extensions.Options.Options.Create(new EmailOptions { PublicBaseUrl = "https://app.example/" }));
    }

    private static TaxDocument Doc(TaxDocumentKind kind = TaxDocumentKind.ListingFeeInvoice) => new()
    {
        Id = Guid.Parse("44444444-4444-4444-4444-444444444444"), Number = "SA-000123", Kind = kind, Status = TaxDocumentStatus.Paid,
        IssuedAt = Issued, PaidAt = Issued, Description = "Listing fee — Dundeel, 2026 Season", AmountIncGst = 990m,
        AmountExGst = 900m, GstAmount = 90m, SupplierName = "Stallions Australia Pty Ltd", SupplierAbn = "12345678901",
        SupplierAddress = "1 Racecourse Road", RecipientName = "Arrowfield", RecipientEmail = "sales@arrowfield.example"
    };

    private static TaxDocument DueInvoice()
    {
        var d = Doc();
        d.Status = TaxDocumentStatus.Unpaid;
        d.PaidAt = null;
        d.DueAt = d.IssuedAt.AddDays(14);
        d.BankAccountName = "Stallions Australia Pty Ltd";
        d.BankBsb = "062000";
        d.BankAccountNumber = "12345678";
        return d;
    }

    private static TaxDocument BuyerFeeInvoice()
    {
        var d = Doc(TaxDocumentKind.BuyerFeeInvoice);
        d.Description = "Buyer fee — nomination to Snitzel, 2026 Season (auction)";
        d.AmountIncGst = 150m;
        d.AmountExGst = 136.36m;
        d.GstAmount = 13.64m;
        d.RecipientName = "Jane Buyer";
        d.RecipientEmail = "winner@example.com";
        return d;
    }

    private static TaxDocument Note()
    {
        var n = Doc(TaxDocumentKind.AdjustmentNote);
        n.Id = Guid.Parse("88888888-8888-8888-8888-888888888888");
        n.Number = "SA-000130";
        n.Reason = "Buyer fee refunded — duplicate charge";
        return n;
    }

    private static readonly IReadOnlyDictionary<string, Func<TaxDocumentEmails, Task>> Calls = new Dictionary<string, Func<TaxDocumentEmails, Task>>
    {
        [ContentKeys.ListingFeeTaxInvoice] = s => s.ListingFeeTaxInvoiceAsync(Doc()),
        [ContentKeys.ListingFeeInvoiceDue] = s => s.ListingFeeInvoiceDueAsync(DueInvoice()),
        [ContentKeys.ListingFeeReceipt] = s => s.ListingFeeReceiptAsync(Doc()),
        [ContentKeys.BuyerFeeTaxInvoice] = s => s.BuyerFeeTaxInvoiceAsync(BuyerFeeInvoice()),
        [ContentKeys.AdjustmentNote] = s => s.AdjustmentNoteAsync(Note(), Doc(TaxDocumentKind.BuyerFeeInvoice))
    };

    public static IEnumerable<object[]> EmailKeys() => Calls.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(EmailKeys))]
    public async Task DefaultWording_MatchesTodaysEmailsExactly(string key)
    {
        await Calls[key](CreateSut());

        var email = _queued.Single();
        var expected = DefaultEmailGolden.Emails[key];
        email.Subject.Should().Be(expected.Subject);
        email.TextBody.Should().Be(expected.Text);
        email.HtmlBody.Should().Be(expected.Html);
        _contentLog.Warnings.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(EmailKeys))]
    public async Task EveryEmail_SuppliesExactlyTheCatalogsPlaceholders(string key)
    {
        var entry = ContentCatalog.Find(key)!;
        var everything = string.Join(" ", entry.Placeholders.Select(p => $"[{p.Name}={{{p.Name}}}]"));
        ContentTestData.SetWording(_db, key, "S " + everything, everything);

        await Calls[key](CreateSut());

        _contentLog.Warnings.Should().BeEmpty("the email code must supply every placeholder its catalog entry lists, and no others");
        foreach (var p in entry.Placeholders)
            _queued.Single().TextBody.Should().Contain($"[{p.Name}=");
    }

    [Fact]
    public void EveryTaxDocumentEmailKey_IsCovered() =>
        Calls.Keys.Should().BeEquivalentTo(ContentCatalog.Entries
            .Where(e => e.Group == ContentGroups.TaxDocumentEmails).Select(e => e.Key));

    [Fact]
    public async Task BuyerFeeTaxInvoice_WithEditedWording_KeepsTheInvoiceTableAndFeeExplanation()
    {
        ContentTestData.SetWording(_db, ContentKeys.BuyerFeeTaxInvoice, "Your receipt {InvoiceNumber}",
            "Hi {BuyerName}, thanks for your purchase.");

        await Calls[ContentKeys.BuyerFeeTaxInvoice](CreateSut());

        var email = _queued.Single();
        email.Subject.Should().Be("Your receipt SA-000123");
        email.TextBody.Should().StartWith("Hi Jane Buyer, thanks for your purchase.\n\nTax invoice: SA-000123\nDate: 11 Oct 2026\n")
            .And.Contain("GST: $13.64\nTotal (incl. GST): $150.00")
            .And.Contain(ContentCatalog.Find(ContentKeys.BuyerFeeExplanation)!.DefaultBody)
            .And.Contain("View or print your tax invoice: https://app.example/tax-documents/44444444-4444-4444-4444-444444444444");
    }

    [Fact]
    public async Task InvoiceDue_WithEditedWording_KeepsTheBankDetailsAndPaymentReference()
    {
        ContentTestData.SetWording(_db, ContentKeys.ListingFeeInvoiceDue, "Invoice {InvoiceNumber}",
            "{Amount} is due by {DueDate}.");

        await Calls[ContentKeys.ListingFeeInvoiceDue](CreateSut());

        _queued.Single().TextBody.Should().StartWith("$990.00 is due by 25 Oct 2026.\n\nTax invoice: SA-000123")
            .And.Contain("Account name: Stallions Australia Pty Ltd\nBSB: 062-000\nAccount number: 12345678")
            .And.Contain("Please use SA-000123 as your payment reference.");
    }

    [Fact]
    public async Task EditedFeeExplanation_IsWhatTheBuyerFeeInvoiceShows()
    {
        ContentTestData.SetWording(_db, ContentKeys.BuyerFeeExplanation, null, "EDITED fee wording.");

        await Calls[ContentKeys.BuyerFeeTaxInvoice](CreateSut());

        _queued.Single().TextBody.Should().Contain("Total (incl. GST): $150.00\n\nEDITED fee wording.\n\nView or print");
    }

    [Fact]
    public async Task Subject_NeverContainsLineBreaks_EvenWhenADocumentValueDoes()
    {
        ContentTestData.SetWording(_db, ContentKeys.ListingFeeTaxInvoice, "Invoice {InvoiceNumber}: {Description}", "Thanks.");
        var d = Doc();
        d.Description = "Listing fee\r\nBcc: x@evil.example";

        await CreateSut().ListingFeeTaxInvoiceAsync(d);

        _queued.Single().Subject.Should().Be("Invoice SA-000123: Listing fee  Bcc: x@evil.example");
    }
}
