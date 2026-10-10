using FluentAssertions;
using Stallions.Server.Payments;

namespace Stallions.Server.Tests.Payments;

public class FakePaymentProviderTests
{
    private readonly FakePaymentProvider _fake = new();

    [Fact]
    public async Task CardSetup_ApproveProducesCardSavedForThatUser()
    {
        var userId = Guid.NewGuid();
        var url = await _fake.CreateCardSetupSessionAsync(userId, "cus_fake_1", "https://app/ok", "https://app/no");
        var sessionId = url.Split('/').Last();

        var (_, evt, redirect, _) = _fake.Approve(sessionId)!.Value;

        url.Should().StartWith("/payments/fake/");
        redirect.Should().Be("https://app/ok");
        var saved = evt.Should().BeOfType<CardSavedEvent>().Subject;
        saved.UserId.Should().Be(userId);
        saved.CustomerId.Should().Be("cus_fake_1");
        saved.Last4.Should().Be("4242");
        saved.ExpYear.Should().BeGreaterThan(DateTime.UtcNow.Year);
    }

    [Fact]
    public async Task ListingFee_ApproveProducesPaymentForTheSessionAmountInCents()
    {
        var subscriptionId = Guid.NewGuid();
        var url = await _fake.CreateListingFeeSessionAsync(subscriptionId, 990m, "Listing fee", "stud@x",
            "https://app/ok", "https://app/no");

        var (_, evt, _, _) = _fake.Approve(url.Split('/').Last())!.Value;

        var paid = evt.Should().BeOfType<ListingFeePaidEvent>().Subject;
        paid.SubscriptionId.Should().Be(subscriptionId);
        paid.AmountCents.Should().Be(99000);
        paid.Currency.Should().Be("aud");
    }

    [Fact]
    public async Task Session_ApproveIsSingleUse()
    {
        var url = await _fake.CreateCardSetupSessionAsync(Guid.NewGuid(), "cus", "https://ok", "https://no");
        var id = url.Split('/').Last();

        _fake.Approve(id).Should().NotBeNull();
        _fake.Approve(id).Should().BeNull();
        _fake.GetSession(id).Should().BeNull();
    }

    [Fact]
    public async Task Session_StaysPayableAfterDecline()
    {
        // Like Stripe: cancelling leaves the Checkout session open until it expires.
        var url = await _fake.CreateCardSetupSessionAsync(Guid.NewGuid(), "cus", "https://ok", "https://no");
        var id = url.Split('/').Last();

        _fake.Decline(id).Should().Be("https://no");
        _fake.GetSession(id).Should().NotBeNull();
        _fake.Approve(id).Should().NotBeNull();
    }

    [Fact]
    public async Task ParseWebhook_IsNotSupported() =>
        await FluentActions.Awaiting(() => _fake.ParseWebhookAsync("{}", null))
            .Should().ThrowAsync<NotSupportedException>();

    private static ChargeRequest Charge(string paymentMethodId, string key = "buyer-fee-x-1") =>
        new("cus_fake_1", paymentMethodId, 150m, "Buyer fee", new Dictionary<string, string>(), key);

    [Fact]
    public async Task ApprovingWithADecliningCard_SavesACardEndingIn0002()
    {
        var sut = new FakePaymentProvider();
        var url = await sut.CreateCardSetupSessionAsync(Guid.NewGuid(), "cus_fake_1", "/ok", "/cancel");

        var approved = sut.Approve(url.Split('/').Last(), decliningCard: true);

        var card = approved!.Value.Event.Should().BeOfType<CardSavedEvent>().Subject;
        card.Last4.Should().Be(FakePaymentProvider.DecliningLast4);
    }

    [Fact]
    public async Task ChargingADecliningCard_Fails_AndAnyOtherCard_Succeeds()
    {
        var sut = new FakePaymentProvider();
        var url = await sut.CreateCardSetupSessionAsync(Guid.NewGuid(), "cus_fake_1", "/ok", "/cancel");
        var declining = (CardSavedEvent)sut.Approve(url.Split('/').Last(), decliningCard: true)!.Value.Event;

        var failed = await sut.ChargeSavedCardAsync(Charge(declining.PaymentMethodId, "k1"));
        var paid = await sut.ChargeSavedCardAsync(Charge("pm_fake_good", "k2"));

        failed.Succeeded.Should().BeFalse();
        failed.FailureMessage.Should().Be("Your card was declined.");
        paid.Succeeded.Should().BeTrue();
        paid.PaymentReference.Should().StartWith("pi_fake_");
    }

    [Fact]
    public async Task RepeatingAChargeWithTheSameKey_ReturnsTheSameResult()
    {
        var sut = new FakePaymentProvider();

        var first = await sut.ChargeSavedCardAsync(Charge("pm_fake_good", "same-key"));
        var again = await sut.ChargeSavedCardAsync(Charge("pm_fake_good", "same-key"));

        again.Should().Be(first);
    }
}
