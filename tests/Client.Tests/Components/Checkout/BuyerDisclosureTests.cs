using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Stallions.Client.Components.Checkout;

namespace Stallions.Client.Tests.Components.Checkout;

public class BuyerDisclosureTests : TestContext
{
    [Fact]
    public void BuyerDisclosure_ConfirmButtonDisabled_UntilCheckboxTicked()
    {
        var confirmed = false;
        var cut = RenderComponent<BuyerDisclosure>(p => p
            .Add(c => c.TotalPriceIncGst, 10000m)
            .Add(c => c.BuyerFeeIncGst, 150m)
            .Add(c => c.BalancePayableToStudIncGst, 9850m)
            .Add(c => c.BalanceArrangementText, "The stud farm will contact you.")
            .Add(c => c.OnConfirmed, EventCallback.Factory.Create(this, () => confirmed = true)));

        // Confirm button disabled initially
        cut.Find("button.btn-gold").HasAttribute("disabled").Should().BeTrue();

        // Tick the acknowledgment checkbox
        cut.Find("input[type='checkbox']").Change(true);

        // Now enabled — click it and verify callback fires
        cut.Find("button.btn-gold").HasAttribute("disabled").Should().BeFalse();
        cut.Find("button.btn-gold").Click();
        cut.WaitForAssertion(() => confirmed.Should().BeTrue());
    }

    [Fact]
    public void BuyerDisclosure_DisplaysCorrectFeeAndBalance()
    {
        var cut = RenderComponent<BuyerDisclosure>(p => p
            .Add(c => c.TotalPriceIncGst, 10000m)
            .Add(c => c.BuyerFeeIncGst, 150m)
            .Add(c => c.BalancePayableToStudIncGst, 9850m)
            .Add(c => c.BalanceArrangementText, "Stud farm invoices separately.")
            .Add(c => c.OnConfirmed, EventCallback.Empty));

        // $10,000 price, $150 buyer fee → $9,850 balance payable to the stud
        cut.Markup.Should().Contain("10,000");
        cut.Markup.Should().Contain("150");
        cut.Markup.Should().Contain("9,850");
    }
}
