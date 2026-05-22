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
            .Add(c => c.PlatformFeeIncGst, 250m)
            .Add(c => c.BalanceArrangementText, "The stud farm will contact you.")
            .Add(c => c.RefundPolicyText, "90% refund if arrangement fails.")
            .Add(c => c.OnConfirmed, EventCallback.Factory.Create(this, () => confirmed = true)));

        // Confirm button disabled initially
        cut.Find("button.btn-gold").HasAttribute("disabled").Should().BeTrue();

        // Tick the acknowledgment checkbox
        cut.Find("input[type='checkbox']").Change(true);

        // Now enabled
        cut.Find("button.btn-gold").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void BuyerDisclosure_DisplaysCorrectFeeAndBalance()
    {
        var cut = RenderComponent<BuyerDisclosure>(p => p
            .Add(c => c.TotalPriceIncGst, 10000m)
            .Add(c => c.PlatformFeeIncGst, 250m)
            .Add(c => c.BalanceArrangementText, "Stud farm invoices separately.")
            .Add(c => c.RefundPolicyText, "90% refund policy.")
            .Add(c => c.OnConfirmed, EventCallback.Empty));

        // $10,000 total, $250 fee → $9,750 balance
        cut.Markup.Should().Contain("10,000");
        cut.Markup.Should().Contain("250");
        cut.Markup.Should().Contain("9,750");
    }
}
