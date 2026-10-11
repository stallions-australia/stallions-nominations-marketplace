using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.TaxDocuments;

namespace Stallions.Client.Tests.Pages;

public class MyTaxInvoicesTests : TestContext
{
    [Fact]
    public void ListsTheBuyersDocuments_WithLinks_AndAdjustmentsShownNegative()
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        var invoiceId = Guid.NewGuid();
        var api = new Mock<TaxDocumentApiService>(MockBehavior.Loose, new HttpClient { BaseAddress = new Uri("https://localhost/") });
        api.Setup(a => a.GetMineAsync()).ReturnsAsync(new List<TaxDocumentSummaryDto>
        {
            new() { Id = Guid.NewGuid(), Number = "SA-000130", Kind = "AdjustmentNote", Status = "Paid", IssuedOn = new DateOnly(2026, 10, 12),
                Description = "Adjustment to SA-000124", AmountIncGst = 150m, OriginalNumber = "SA-000124" },
            new() { Id = invoiceId, Number = "SA-000124", Kind = "BuyerFeeInvoice", Status = "Paid", IssuedOn = new DateOnly(2026, 10, 11),
                Description = "Buyer fee — nomination to Dundeel, 2026 Season (auction)", AmountIncGst = 150m, ReversedByNumber = "SA-000130" }
        });
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<MyTaxInvoices>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("SA-000124"));
        cut.Find($"a[href='/tax-documents/{invoiceId}']").TextContent.Should().Be("SA-000124");
        cut.Markup.Should().Contain("Buyer fee invoice").And.Contain("Adjustment note").And.Contain("−$150.00")
            .And.Contain("Reversed by SA-000130");
    }

    [Fact]
    public void WithNoDocuments_ShowsAnEmptyState()
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        var api = new Mock<TaxDocumentApiService>(MockBehavior.Loose, new HttpClient { BaseAddress = new Uri("https://localhost/") });
        api.Setup(a => a.GetMineAsync()).ReturnsAsync(new List<TaxDocumentSummaryDto>());
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<MyTaxInvoices>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No tax invoices yet"));
    }
}
