// tests/Server.Tests/TaxDocuments/TaxDocumentEmailsTests.cs
using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Email;
using Stallions.Server.Options;
using Stallions.Server.TaxDocuments;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.TaxDocuments;

public class TaxDocumentEmailsTests
{
    private readonly List<(OutgoingEmail Email, string? EntityType, Guid? EntityId)> _queued = new();

    private TaxDocumentEmails CreateSut()
    {
        var outbox = new Mock<IEmailOutbox>();
        outbox.Setup(o => o.EnqueueAsync(It.IsAny<OutgoingEmail>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .Callback<OutgoingEmail, string?, Guid?>((e, t, id) => _queued.Add((e, t, id)))
            .Returns(Task.CompletedTask);
        return new TaxDocumentEmails(outbox.Object,
            Microsoft.Extensions.Options.Options.Create(new DisclosureOptions { BuyerFeeExplanation = "CONFIGURED fee wording." }),
            Microsoft.Extensions.Options.Options.Create(new EmailOptions { PublicBaseUrl = "https://app.example/" }));
    }

    private static readonly DateTime Issued = new(2026, 10, 11, 2, 0, 0, DateTimeKind.Utc);

    private static TaxDocument Invoice(TaxDocumentKind kind = TaxDocumentKind.ListingFeeInvoice) => new()
    {
        Id = Guid.NewGuid(), Number = "SA-000123", Kind = kind, Status = TaxDocumentStatus.Paid, IssuedAt = Issued,
        PaidAt = Issued, Description = "Listing fee — Dundeel, 2026 Season", AmountIncGst = 990m, AmountExGst = 900m,
        GstAmount = 90m, SupplierName = "Stallions Australia Pty Ltd", SupplierAbn = "12345678901",
        SupplierAddress = "1 Racecourse Road", RecipientName = "Arrowfield", RecipientEmail = "sales@arrowfield.example"
    };

    [Fact]
    public async Task ListingFeeTaxInvoice_CarriesTheKeyDetails_AndALink()
    {
        var doc = Invoice();

        await CreateSut().ListingFeeTaxInvoiceAsync(doc);

        var (email, type, id) = _queued.Single();
        email.Template.Should().Be("ListingFeeTaxInvoice");
        email.ToAddress.Should().Be("sales@arrowfield.example");
        email.Subject.Should().Contain("SA-000123");
        email.TextBody.Should().Contain("Tax invoice: SA-000123").And.Contain("Date: 11 Oct 2026")
            .And.Contain("Stallions Australia Pty Ltd, ABN 12 345 678 901").And.Contain("Amount (excl. GST): $900.00")
            .And.Contain("GST: $90.00").And.Contain("Total (incl. GST): $990.00")
            .And.Contain($"https://app.example/tax-documents/{doc.Id}");
        (type, id).Should().Be(("TaxDocument", doc.Id));
    }

    [Fact]
    public async Task ListingFeeTaxInvoice_WithAnIntroOffer_ShowsTheDiscount()
    {
        var doc = Invoice();
        doc.StandardAmountIncGst = 990m;
        doc.IntroOfferName = "Launch 2026";
        doc.DiscountIncGst = 495m;
        doc.AmountIncGst = 495m;

        await CreateSut().ListingFeeTaxInvoiceAsync(doc);

        _queued.Single().Email.TextBody.Should().Contain("Standard listing fee: $990.00")
            .And.Contain("Intro offer: Launch 2026: −$495.00");
    }

    [Fact]
    public async Task InvoiceDue_ShowsTheDueDate_BankDetails_AndPaymentReference()
    {
        var doc = Invoice();
        doc.Status = TaxDocumentStatus.Unpaid;
        doc.PaidAt = null;
        doc.DueAt = Issued.AddDays(14);
        doc.BankAccountName = "Stallions Australia Pty Ltd";
        doc.BankBsb = "062000";
        doc.BankAccountNumber = "12345678";

        await CreateSut().ListingFeeInvoiceDueAsync(doc);

        var email = _queued.Single().Email;
        email.Template.Should().Be("ListingFeeInvoiceDue");
        email.Subject.Should().Be("Tax invoice SA-000123 — amount due $990.00 by 25 Oct 2026");
        email.TextBody.Should().Contain("Account name: Stallions Australia Pty Ltd").And.Contain("BSB: 062-000")
            .And.Contain("Account number: 12345678").And.Contain("Please use SA-000123 as your payment reference.");
    }

    [Fact]
    public async Task Receipt_SaysThePaymentWasReceived()
    {
        await CreateSut().ListingFeeReceiptAsync(Invoice());

        var email = _queued.Single().Email;
        email.Template.Should().Be("ListingFeeReceipt");
        email.TextBody.Should().Contain("We've received your payment of $990.00 for tax invoice SA-000123");
    }

    [Fact]
    public async Task BuyerFeeTaxInvoice_IncludesTheConfiguredWording()
    {
        var doc = Invoice(TaxDocumentKind.BuyerFeeInvoice);
        doc.RecipientEmail = "jane@example.com";

        await CreateSut().BuyerFeeTaxInvoiceAsync(doc);

        var email = _queued.Single().Email;
        email.Template.Should().Be("BuyerFeeTaxInvoice");
        email.ToAddress.Should().Be("jane@example.com");
        email.TextBody.Should().Contain("CONFIGURED fee wording.");
    }

    [Fact]
    public async Task AdjustmentNote_ReferencesTheOriginalInvoice()
    {
        var original = Invoice(TaxDocumentKind.BuyerFeeInvoice);
        var note = Invoice(TaxDocumentKind.AdjustmentNote);
        note.Number = "SA-000130";
        note.Reason = "Buyer fee refunded — duplicate charge";

        await CreateSut().AdjustmentNoteAsync(note, original);

        var email = _queued.Single().Email;
        email.Template.Should().Be("AdjustmentNote");
        email.Subject.Should().Be("Adjustment note SA-000130 for tax invoice SA-000123");
        email.TextBody.Should().Contain("Adjustment note: SA-000130")
            .And.Contain("This adjustment note reverses tax invoice SA-000123 dated 11 Oct 2026 in full.")
            .And.Contain("Reason: Buyer fee refunded — duplicate charge");
    }

    [Fact]
    public async Task RecipientText_IsHtmlEncoded()
    {
        var doc = Invoice();
        doc.RecipientName = "<b>Arrowfield</b>";

        await CreateSut().ListingFeeTaxInvoiceAsync(doc);

        _queued.Single().Email.HtmlBody.Should().Contain("&lt;b&gt;Arrowfield&lt;/b&gt;").And.NotContain("<b>Arrowfield</b>");
    }
}
