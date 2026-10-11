using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Staff;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Admin;
using Stallions.Shared.DTOs.Checkout;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class StaffTransactionsTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };
    private readonly Mock<StaffApiService> _api = new(MockBehavior.Loose, Http());

    private IRenderedComponent<StaffTransactions> Render(params TransactionDto[] transactions)
    {
        this.AddTestAuthorization().SetAuthorized("staff@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.Setup(a => a.GetTransactionsAsync()).ReturnsAsync(transactions.ToList());
        Services.AddSingleton(_api.Object);
        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "Staff", Email = "staff@example.com", Role = "Staff", Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));
        return RenderComponent<StaffTransactions>();
    }

    private static TransactionDto Completed() => new()
    {
        PurchaseId = Guid.NewGuid(), StallionName = "Dundeel", StudFarmName = "Arrowfield", BuyerDisplayName = "Jane Buyer",
        TotalPriceIncGst = 20000m, BuyerFeeIncGst = 150m, BuyerFeeExGst = 136.36m, BuyerFeeGst = 13.64m,
        BalancePayableToStudIncGst = 19850m, Status = "Completed", PaidAt = new DateTime(2026, 10, 11),
        TaxInvoiceId = Guid.NewGuid(), TaxInvoiceNumber = "SA-000124"
    };

    [Fact]
    public void Refund_WarnsToRefundInStripeFirst_ThenRecordsItWithTheNote()
    {
        var sale = Completed();
        _api.Setup(a => a.RefundPurchaseAsync(sale.PurchaseId, It.IsAny<RefundBuyerFeeRequest>())).ReturnsAsync(new RefundResultDto
            { AdjustmentNoteNumber = "SA-000130", Message = "Refund recorded. Adjustment note SA-000130 has been emailed to the buyer." });
        var cut = Render(sale);
        cut.WaitForAssertion(() => cut.Find($"a[href='/tax-documents/{sale.TaxInvoiceId}']").TextContent.Should().Be("SA-000124"));

        cut.FindAll("button").Single(b => b.TextContent.Contains("Refund buyer fee")).Click();

        cut.Markup.Should().Contain("Refund $150.00 in Stripe first. This records the refund and emails the buyer an adjustment note.");
        cut.Find("#refund-note").Change("Duplicate charge");
        cut.FindAll("button").Single(b => b.TextContent.Contains("Record refund")).Click();

        _api.Verify(a => a.RefundPurchaseAsync(sale.PurchaseId,
            It.Is<RefundBuyerFeeRequest>(r => r.Note == "Duplicate charge")), Times.Once);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Adjustment note SA-000130 has been emailed to the buyer."));
    }

    [Fact]
    public void ARefundedSale_ShowsItsAdjustmentNote_AndCannotBeRefundedAgain()
    {
        var sale = Completed();
        sale.Status = "Refunded";
        sale.AdjustmentNoteNumber = "SA-000130";

        var cut = Render(sale);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Adjusted by SA-000130"));
        cut.FindAll("button").Should().NotContain(b => b.TextContent.Contains("Refund buyer fee"));
    }
}
