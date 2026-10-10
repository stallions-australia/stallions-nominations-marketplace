using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Checkout;

namespace Stallions.Client.Tests.Pages;

public class MyPurchasesTests : TestContext
{
    [Fact]
    public void MyPurchases_WithNoPurchases_ShowsEmptyState()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("buyer@test.com");
        auth.SetRoles("Buyer");

        var mock = new Mock<PurchaseApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        mock.Setup(s => s.GetMyPurchasesAsync()).ReturnsAsync(new List<PurchaseDto>());
        Services.AddSingleton(mock.Object);

        var cut = RenderComponent<MyPurchases>();

        cut.WaitForAssertion(() => cut.Find(".empty-state").Should().NotBeNull());
    }

    [Fact]
    public void AFailedPayment_ShowsTheDeadlineAndTheCardLink_AndAVoidedSaleShowsNoSale()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("buyer@test.com");
        var mock = new Mock<PurchaseApiService>(MockBehavior.Loose, new HttpClient { BaseAddress = new Uri("https://localhost/") });
        mock.Setup(s => s.GetMyPurchasesAsync()).ReturnsAsync(new List<PurchaseDto>
        {
            new() { StallionName = "Snitzel", Status = "Pending", ChargeDueBy = DateTime.UtcNow.AddHours(2) },
            new() { StallionName = "Zoustar", Status = "Voided", ChargeDueBy = DateTime.UtcNow.AddHours(-1) }
        });
        Services.AddSingleton(mock.Object);

        var cut = RenderComponent<MyPurchases>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("My sale records"));
        cut.Markup.Should().Contain("Payment failed — update your card by").And.Contain("No sale — payment not completed");
        cut.Find("a[href='/account/card']").Should().NotBeNull();
    }
}
