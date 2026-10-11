using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.TaxDocuments;

namespace Stallions.Client.Tests.Pages;

public class TaxDocumentPageTests : TestContext
{
    private readonly Mock<TaxDocumentApiService> _api =
        new(MockBehavior.Loose, new HttpClient { BaseAddress = new Uri("https://localhost/") });

    private static TaxDocumentDto ListingFeeInvoice() => new()
    {
        Id = Guid.NewGuid(), Number = "SA-000123", Kind = "ListingFeeInvoice", Status = "Paid",
        IssuedOn = new DateOnly(2026, 10, 11), PaidOn = new DateOnly(2026, 10, 11),
        Description = "Listing fee — Dundeel, 2026 Season", AmountIncGst = 495m, AmountExGst = 450m, GstAmount = 45m,
        StandardAmountIncGst = 990m, IntroOfferName = "Launch 2026", DiscountIncGst = 495m,
        SupplierName = "Stallions Australia Pty Ltd", SupplierAbn = "12 345 678 901", SupplierAddress = "1 Racecourse Road",
        RecipientName = "Arrowfield", RecipientEmail = "sales@arrowfield.example", RecipientAbn = "98 765 432 109"
    };

    private IRenderedComponent<TaxDocumentPage> Render(TaxDocumentDto? doc)
    {
        this.AddTestAuthorization().SetAuthorized("user@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        var id = doc?.Id ?? Guid.NewGuid();
        _api.Setup(a => a.GetAsync(id)).ReturnsAsync(doc);
        Services.AddSingleton(_api.Object);
        return RenderComponent<TaxDocumentPage>(p => p.Add(c => c.Id, id));
    }

    [Fact]
    public void APaidInvoice_ShowsTheTaxInvoiceDetails_AndTheIntroOffer()
    {
        var cut = Render(ListingFeeInvoice());

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("SA-000123"));
        cut.Find("h1").TextContent.Should().Be("Tax invoice");
        cut.Markup.Should().Contain("ABN 12 345 678 901").And.Contain("ABN 98 765 432 109")
            .And.Contain("Intro offer: Launch 2026").And.Contain("−$495.00").And.Contain("$990.00")
            .And.Contain("Amount (excl. GST)").And.Contain("$450.00").And.Contain("GST").And.Contain("$45.00")
            .And.Contain("Total (incl. GST)").And.Contain("11 Oct 2026").And.Contain("Paid")
            .And.NotContain("payment reference");
    }

    [Fact]
    public void AnUnpaidInvoice_ShowsTheDueDateBankDetailsAndPaymentReference()
    {
        var doc = ListingFeeInvoice();
        doc.Status = "Unpaid";
        doc.PaidOn = null;
        doc.DueOn = new DateOnly(2026, 10, 25);
        doc.BankAccountName = "Stallions Australia Pty Ltd";
        doc.BankBsb = "062-000";
        doc.BankAccountNumber = "12345678";

        var cut = Render(doc);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Unpaid"));
        cut.Markup.Should().Contain("25 Oct 2026").And.Contain("062-000").And.Contain("12345678")
            .And.Contain("Please use SA-000123 as your payment reference.");
    }

    [Fact]
    public void AnUnpaidInvoiceReversedByAnAdjustmentNote_DoesNotAskForPayment()
    {
        var doc = ListingFeeInvoice();
        doc.Status = "Unpaid";
        doc.PaidOn = null;
        doc.DueOn = new DateOnly(2026, 10, 25);
        doc.BankAccountName = "Stallions Australia Pty Ltd";
        doc.BankBsb = "062-000";
        doc.BankAccountNumber = "12345678";
        doc.ReversedById = Guid.NewGuid();
        doc.ReversedByNumber = "SA-000130";

        var cut = Render(doc);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Reversed by SA-000130"));
        cut.Markup.Should().NotContain("Unpaid").And.NotContain("25 Oct 2026").And.NotContain("How to pay")
            .And.NotContain("062-000").And.NotContain("12345678").And.NotContain("payment reference");
        cut.Find($"a[href='/tax-documents/{doc.ReversedById}']").Should().NotBeNull();
    }

    [Fact]
    public void AnAdjustmentNote_ShowsWhatItAdjusts_AndWhy()
    {
        var doc = ListingFeeInvoice();
        doc.Kind = "AdjustmentNote";
        doc.Number = "SA-000130";
        doc.StandardAmountIncGst = null;
        doc.DiscountIncGst = null;
        doc.OriginalDocumentId = Guid.NewGuid();
        doc.OriginalNumber = "SA-000123";
        doc.OriginalIssuedOn = new DateOnly(2026, 10, 11);
        doc.Reason = "Listing fee waived — Founding stud";

        var cut = Render(doc);

        cut.WaitForAssertion(() => cut.Find("h1").TextContent.Should().Be("Adjustment note"));
        cut.Markup.Should().Contain("Adjusts tax invoice").And.Contain("SA-000123").And.Contain("dated 11 Oct 2026")
            .And.Contain("Listing fee waived — Founding stud");
        cut.Find($"a[href='/tax-documents/{doc.OriginalDocumentId}']").Should().NotBeNull();
    }

    [Fact]
    public void ABuyerFeeInvoice_ShowsTheConfiguredFeeNote()
    {
        var doc = ListingFeeInvoice();
        doc.Kind = "BuyerFeeInvoice";
        doc.StandardAmountIncGst = null;
        doc.DiscountIncGst = null;
        doc.FeeNote = "CONFIGURED fee wording.";

        var cut = Render(doc);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("CONFIGURED fee wording."));
    }

    [Fact]
    public void ADocumentThatIsNotTheCallers_ShowsNotAvailable()
    {
        var cut = Render(null);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("This tax document isn't available."));
    }

    [Fact]
    public void ThePrintButton_OpensTheBrowsersPrintDialog()
    {
        var cut = Render(ListingFeeInvoice());
        cut.WaitForAssertion(() => cut.Find("button.tax-doc-print"));

        cut.Find("button.tax-doc-print").Click();

        JSInterop.VerifyInvoke("print");
    }
}
