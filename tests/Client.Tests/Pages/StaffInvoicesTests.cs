using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Staff;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.TaxDocuments;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class StaffInvoicesTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };
    private readonly Mock<TaxDocumentApiService> _api = new(MockBehavior.Loose, Http());

    private static TaxDocumentRegisterDto Register(int pending = 0) => new()
    {
        PendingCount = pending,
        Totals = new TaxDocumentTotalsDto
        {
            InvoiceCount = 4, InvoicesIncGst = 2625m, InvoicesExGst = 2386.36m, InvoicesGst = 238.64m,
            AdjustmentCount = 2, AdjustmentsIncGst = 645m, AdjustmentsExGst = 586.36m, AdjustmentsGst = 58.64m,
            NetIncGst = 1980m, NetExGst = 1800m, NetGst = 180m, UnpaidCount = 1, UnpaidIncGst = 990m
        },
        Documents =
        [
            new() { Id = Guid.NewGuid(), Number = "SA-000006", Kind = "AdjustmentNote", Status = "Paid", IssuedOn = new DateOnly(2026, 10, 12),
                RecipientName = "Jane Buyer", RecipientEmail = "jane@example.com", Description = "Adjustment to SA-000005",
                AmountIncGst = 150m, AmountExGst = 136.36m, GstAmount = 13.64m, OriginalNumber = "SA-000005" },
            new() { Id = Guid.NewGuid(), Number = "SA-000002", Kind = "ListingFeeInvoice", Status = "Unpaid", IssuedOn = new DateOnly(2026, 10, 11),
                DueOn = new DateOnly(2026, 10, 25), RecipientName = "Arrowfield", RecipientEmail = "sales@arrowfield.example",
                Description = "Listing fee — Dundeel, 2026 Season", AmountIncGst = 990m, AmountExGst = 900m, GstAmount = 90m }
        ]
    };

    private IRenderedComponent<StaffInvoices> Render(TaxDocumentRegisterDto register)
    {
        this.AddTestAuthorization().SetAuthorized("staff@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.Setup(a => a.GetRegisterAsync(It.IsAny<TaxDocumentFilter>())).ReturnsAsync(register);
        Services.AddSingleton(_api.Object);
        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "Staff", Email = "staff@example.com", Role = "Staff", Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));
        return RenderComponent<StaffInvoices>();
    }

    private static void Click(IRenderedComponent<StaffInvoices> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == text).Click();

    [Fact]
    public void ShowsTheTotals_WithAdjustmentsSubtracted_AndUnpaidSeparately()
    {
        var cut = Render(Register());

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("SA-000002"));
        var totals = cut.Find(".invoice-totals-grid").TextContent;
        totals.Should().Contain("Invoices (4)").And.Contain("$2,625.00").And.Contain("Adjustment notes (2)")
            .And.Contain("−$645.00").And.Contain("Net").And.Contain("$1,980.00").And.Contain("GST $180.00")
            .And.Contain("Unpaid (1)").And.Contain("$990.00");
        cut.Markup.Should().Contain("−$150.00").And.Contain("Unpaid — due 25 Oct 2026").And.Contain("Arrowfield");
    }

    [Fact]
    public void ApplyingFilters_SendsThem()
    {
        var cut = Render(Register());
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("SA-000002"));

        cut.Find("#filter-kind").Change("AdjustmentNote");
        cut.Find("#filter-status").Change("Paid");
        cut.Find("#filter-from").Change("2026-10-01");
        Click(cut, "Apply");

        _api.Verify(a => a.GetRegisterAsync(It.Is<TaxDocumentFilter>(f => f.Kind == "AdjustmentNote" && f.Status == "Paid"
            && f.From == new DateOnly(2026, 10, 1) && f.To == null)), Times.Once);
    }

    [Fact]
    public void FeesWithoutAnInvoice_ShowABanner_AndCanBeIssued()
    {
        _api.Setup(a => a.IssueMissingAsync()).ReturnsAsync(new IssueMissingResultDto { Issued = 2, Remaining = 0 });
        var cut = Render(Register(pending: 2));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("2 fees have no tax invoice because business details are incomplete"));
        Click(cut, "Issue missing tax invoices");

        _api.Verify(a => a.IssueMissingAsync(), Times.Once);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("2 tax document(s) issued."));
    }

    [Fact]
    public void ExportCsv_DownloadsTheFilteredDocuments()
    {
        _api.Setup(a => a.ExportCsvAsync(It.IsAny<TaxDocumentFilter>())).ReturnsAsync("Number\r\n");
        var cut = Render(Register());
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("SA-000002"));

        Click(cut, "Export CSV");

        var call = JSInterop.VerifyInvoke("stallionsDownload.text");
        call.Arguments[0]!.ToString().Should().StartWith("tax-documents-").And.EndWith(".csv");
        call.Arguments[1].Should().Be("text/csv");
        call.Arguments[2].Should().Be("Number\r\n");
    }

    [Fact]
    public void AFailedExport_ShowsAnError_AndKeepsTheTotalsAndTable()
    {
        _api.Setup(a => a.ExportCsvAsync(It.IsAny<TaxDocumentFilter>()))
            .ThrowsAsync(new ApiException(500, "Couldn't export the tax documents."));
        var cut = Render(Register());
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("SA-000002"));

        Click(cut, "Export CSV");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Couldn't export the tax documents."));
        cut.FindAll(".invoice-totals-grid").Should().HaveCount(1);
        cut.FindAll("table.admin-table").Should().HaveCount(1);
        cut.Markup.Should().Contain("SA-000002");
    }

    [Fact]
    public void AFailedIssueMissing_ShowsAnError_AndKeepsTheTotalsAndTable()
    {
        _api.Setup(a => a.IssueMissingAsync()).ThrowsAsync(new ApiException(400, "Complete the business details in Settings first."));
        var cut = Render(Register(pending: 2));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("SA-000002"));

        Click(cut, "Issue missing tax invoices");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Complete the business details in Settings first."));
        cut.FindAll(".invoice-totals-grid").Should().HaveCount(1);
        cut.FindAll("table.admin-table").Should().HaveCount(1);
    }
}
