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
    public async Task Session_CanOnlyBeCompletedOnce()
    {
        var url = await _fake.CreateCardSetupSessionAsync(Guid.NewGuid(), "cus", "https://ok", "https://no");
        var id = url.Split('/').Last();

        _fake.Decline(id).Should().Be("https://no");
        _fake.Approve(id).Should().BeNull();
        _fake.GetSession(id).Should().BeNull();
    }

    [Fact]
    public async Task ParseWebhook_IsNotSupported() =>
        await FluentActions.Awaiting(() => _fake.ParseWebhookAsync("{}", null))
            .Should().ThrowAsync<NotSupportedException>();
}
