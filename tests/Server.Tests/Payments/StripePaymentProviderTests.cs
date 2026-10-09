using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Stallions.Server.Payments;
using Stallions.Server.Payments.Stripe;
using Stripe;
using Stripe.Checkout;

namespace Stallions.Server.Tests.Payments;

public class StripePaymentProviderTests
{
    private const string Secret = "whsec_test_secret";
    private readonly Mock<IStripeApi> _api = new();

    private StripePaymentProvider CreateSut() => new(_api.Object, Microsoft.Extensions.Options.Options.Create(new PaymentOptions
    {
        Provider = "Stripe",
        Stripe = new StripeSettings { SecretKey = "sk_test_unused", WebhookSigningSecret = Secret }
    }), NullLogger<StripePaymentProvider>.Instance);

    private static string Sign(string payload, long? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var hash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{ts}.{payload}"))).ToLowerInvariant();
        return $"t={ts},v1={hash}";
    }

    private static string SessionCompleted(string sessionJson, string type = "checkout.session.completed") => $$$"""
        {"id":"evt_123","object":"event","api_version":"2024-06-20","created":{{{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}},
         "livemode":false,"type":"{{{type}}}","data":{"object":{{{sessionJson}}}}}
        """;

    private static string PaymentSession(Guid subscriptionId, string status = "paid") => $$$"""
        {"id":"cs_1","object":"checkout.session","mode":"payment","payment_status":"{{{status}}}",
         "amount_total":99000,"currency":"aud","payment_intent":"pi_1",
         "metadata":{"kind":"listing-fee","subscriptionId":"{{{subscriptionId}}}"}}
        """;

    [Fact]
    public async Task Webhook_PaidListingFeeSession_MapsToListingFeePaid()
    {
        var subscriptionId = Guid.NewGuid();
        var payload = SessionCompleted(PaymentSession(subscriptionId));

        var evt = await CreateSut().ParseWebhookAsync(payload, Sign(payload));

        var paid = evt.Should().BeOfType<ListingFeePaidEvent>().Subject;
        paid.EventId.Should().Be("evt_123");
        paid.SubscriptionId.Should().Be(subscriptionId);
        paid.AmountCents.Should().Be(99000);
        paid.Currency.Should().Be("aud");
        paid.PaymentReference.Should().Be("pi_1");
    }

    [Fact]
    public async Task Webhook_UnpaidSession_IsUnhandled()
    {
        var payload = SessionCompleted(PaymentSession(Guid.NewGuid(), status: "unpaid"));

        (await CreateSut().ParseWebhookAsync(payload, Sign(payload))).Should().BeOfType<UnhandledPaymentEvent>();
    }

    [Fact]
    public async Task Webhook_CardSetupSession_LooksUpTheCardAndMapsToCardSaved()
    {
        var userId = Guid.NewGuid();
        var payload = SessionCompleted($$$"""
            {"id":"cs_2","object":"checkout.session","mode":"setup","customer":"cus_9","setup_intent":"seti_1",
             "metadata":{"kind":"card-setup","userId":"{{{userId}}}"}}
            """);
        _api.Setup(a => a.GetSetupIntentAsync("seti_1")).ReturnsAsync(new SetupIntent { PaymentMethodId = "pm_9" });
        _api.Setup(a => a.GetPaymentMethodAsync("pm_9")).ReturnsAsync(new PaymentMethod
        {
            Id = "pm_9",
            Card = new PaymentMethodCard { Brand = "visa", Last4 = "4242", ExpMonth = 8, ExpYear = 2028 }
        });

        var evt = await CreateSut().ParseWebhookAsync(payload, Sign(payload));

        var saved = evt.Should().BeOfType<CardSavedEvent>().Subject;
        saved.UserId.Should().Be(userId);
        saved.CustomerId.Should().Be("cus_9");
        saved.PaymentMethodId.Should().Be("pm_9");
        (saved.Brand, saved.Last4, saved.ExpMonth, saved.ExpYear).Should().Be(("visa", "4242", 8, 2028));
    }

    [Fact]
    public async Task Webhook_OtherEventType_IsUnhandled()
    {
        var payload = SessionCompleted("""{"id":"cus_1","object":"customer"}""", type: "customer.created");

        (await CreateSut().ParseWebhookAsync(payload, Sign(payload))).Should().BeOfType<UnhandledPaymentEvent>();
    }

    [Fact]
    public async Task Webhook_TamperedBody_IsRejected()
    {
        var payload = SessionCompleted(PaymentSession(Guid.NewGuid()));
        var signature = Sign(payload);

        await FluentActions.Awaiting(() => CreateSut().ParseWebhookAsync(payload.Replace("99000", "1"), signature))
            .Should().ThrowAsync<PaymentSignatureException>();
    }

    [Fact]
    public async Task Webhook_StaleTimestamp_IsRejected()
    {
        var payload = SessionCompleted(PaymentSession(Guid.NewGuid()));
        var tenMinutesAgo = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();

        await FluentActions.Awaiting(() => CreateSut().ParseWebhookAsync(payload, Sign(payload, tenMinutesAgo)))
            .Should().ThrowAsync<PaymentSignatureException>();
    }

    [Fact]
    public async Task Webhook_MissingSignature_IsRejected() =>
        await FluentActions.Awaiting(() => CreateSut().ParseWebhookAsync("{}", null))
            .Should().ThrowAsync<PaymentSignatureException>();

    [Fact]
    public async Task ListingFeeSession_ChargesTheServerAmountInAudCents()
    {
        SessionCreateOptions? sent = null;
        _api.Setup(a => a.CreateCheckoutSessionAsync(It.IsAny<SessionCreateOptions>()))
            .Callback<SessionCreateOptions>(o => sent = o)
            .ReturnsAsync("https://checkout.stripe.com/c/pay/cs_1");
        var subscriptionId = Guid.NewGuid();

        var url = await CreateSut().CreateListingFeeSessionAsync(subscriptionId, 990m, "Listing fee — Snitzel, 2026 Season",
            "stud@x", "https://app/ok", "https://app/no");

        url.Should().Be("https://checkout.stripe.com/c/pay/cs_1");
        sent!.Mode.Should().Be("payment");
        sent.CustomerEmail.Should().Be("stud@x");
        var line = sent.LineItems.Should().ContainSingle().Subject;
        line.PriceData.Currency.Should().Be("aud");
        line.PriceData.UnitAmount.Should().Be(99000);
        sent.Metadata.Should().Contain("kind", "listing-fee").And.Contain("subscriptionId", subscriptionId.ToString());
    }

    [Fact]
    public async Task CardSetupSession_IsSetupModeForTheCustomer()
    {
        SessionCreateOptions? sent = null;
        _api.Setup(a => a.CreateCheckoutSessionAsync(It.IsAny<SessionCreateOptions>()))
            .Callback<SessionCreateOptions>(o => sent = o)
            .ReturnsAsync("https://checkout.stripe.com/c/setup/cs_2");
        var userId = Guid.NewGuid();

        await CreateSut().CreateCardSetupSessionAsync(userId, "cus_9", "https://app/ok", "https://app/no");

        sent!.Mode.Should().Be("setup");
        sent.Customer.Should().Be("cus_9");
        sent.Currency.Should().Be("aud");
        sent.Metadata.Should().Contain("kind", "card-setup").And.Contain("userId", userId.ToString());
    }

    private static string SetupSession(string extra = "", string? metadata = null, string mode = "setup")
    {
        metadata ??= "{\"kind\":\"card-setup\",\"userId\":\"" + Guid.NewGuid() + "\"}";
        return "{\"id\":\"cs_x\",\"object\":\"checkout.session\",\"mode\":\"" + mode + "\",\"customer\":\"cus_9\"" + extra +
               ",\"metadata\":" + metadata + "}";
    }

    private async Task<PaymentEvent> Parse(string sessionJson)
    {
        var payload = SessionCompleted(sessionJson);
        return await CreateSut().ParseWebhookAsync(payload, Sign(payload));
    }

    [Fact]
    public async Task Webhook_SetupSession_CardNull_IsUnhandled()
    {
        _api.Setup(a => a.GetSetupIntentAsync("seti_1")).ReturnsAsync(new SetupIntent { PaymentMethodId = "pm_9" });
        _api.Setup(a => a.GetPaymentMethodAsync("pm_9")).ReturnsAsync(new PaymentMethod { Id = "pm_9", Card = null });

        (await Parse(SetupSession(",\"setup_intent\":\"seti_1\""))).Should().BeOfType<UnhandledPaymentEvent>();
    }

    [Fact]
    public async Task Webhook_SetupSession_NoPaymentMethodOnIntent_IsUnhandled()
    {
        _api.Setup(a => a.GetSetupIntentAsync("seti_1")).ReturnsAsync(new SetupIntent { PaymentMethodId = null });

        (await Parse(SetupSession(",\"setup_intent\":\"seti_1\""))).Should().BeOfType<UnhandledPaymentEvent>();
    }

    [Fact]
    public async Task Webhook_SetupSession_NoSetupIntent_IsUnhandled() =>
        (await Parse(SetupSession())).Should().BeOfType<UnhandledPaymentEvent>();

    [Theory]
    [InlineData("""{"kind":"card-setup"}""")]
    [InlineData("""{"kind":"card-setup","userId":"not-a-guid"}""")]
    [InlineData("{}")]
    public async Task Webhook_SetupSession_BadMetadata_IsUnhandled(string metadata) =>
        (await Parse(SetupSession(",\"setup_intent\":\"seti_1\"", metadata))).Should().BeOfType<UnhandledPaymentEvent>();

    [Theory]
    [InlineData("""{"kind":"listing-fee"}""")]
    [InlineData("""{"kind":"listing-fee","subscriptionId":"nope"}""")]
    public async Task Webhook_PaymentSession_BadMetadata_IsUnhandled(string metadata) =>
        (await Parse(SetupSession(",\"payment_status\":\"paid\",\"payment_intent\":\"pi_1\"", metadata, "payment")))
            .Should().BeOfType<UnhandledPaymentEvent>();

    [Fact]
    public async Task Webhook_PaymentSession_WithCardSetupKind_IsUnhandled() =>
        (await Parse(SetupSession(",\"payment_status\":\"paid\"", mode: "payment"))).Should().BeOfType<UnhandledPaymentEvent>();

    [Fact]
    public async Task Webhook_ResourceMissing_IsUnhandled()
    {
        _api.Setup(a => a.GetSetupIntentAsync("seti_1")).ReturnsAsync(new SetupIntent { PaymentMethodId = "pm_9" });
        _api.Setup(a => a.GetPaymentMethodAsync("pm_9"))
            .ThrowsAsync(new StripeException { StripeError = new StripeError { Code = "resource_missing" } });

        (await Parse(SetupSession(",\"setup_intent\":\"seti_1\""))).Should().BeOfType<UnhandledPaymentEvent>();
    }

    [Fact]
    public async Task Webhook_OtherStripeError_Rethrows()
    {
        _api.Setup(a => a.GetSetupIntentAsync("seti_1")).ReturnsAsync(new SetupIntent { PaymentMethodId = "pm_9" });
        _api.Setup(a => a.GetPaymentMethodAsync("pm_9"))
            .ThrowsAsync(new StripeException { StripeError = new StripeError { Code = "rate_limit" } });

        await FluentActions.Awaiting(() => Parse(SetupSession(",\"setup_intent\":\"seti_1\"")))
            .Should().ThrowAsync<StripeException>();
    }

    [Fact]
    public async Task ListingFeeSession_HasUrlsMetadataCardOnlyAndShortExpiry()
    {
        SessionCreateOptions? sent = null;
        _api.Setup(a => a.CreateCheckoutSessionAsync(It.IsAny<SessionCreateOptions>()))
            .Callback<SessionCreateOptions>(o => sent = o).ReturnsAsync("u");
        var id = Guid.NewGuid();

        await CreateSut().CreateListingFeeSessionAsync(id, 990m, "d", null, "https://app/ok", "https://app/no");

        sent!.SuccessUrl.Should().Be("https://app/ok");
        sent.CancelUrl.Should().Be("https://app/no");
        sent.AllowedPaymentMethodTypes.Should().Equal("card");
        sent.PaymentIntentData.Metadata.Should().Contain("kind", "listing-fee").And.Contain("subscriptionId", id.ToString());
        sent.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(1), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task CardSetupSession_HasUrlsMetadataAndCardOnly()
    {
        SessionCreateOptions? sent = null;
        _api.Setup(a => a.CreateCheckoutSessionAsync(It.IsAny<SessionCreateOptions>()))
            .Callback<SessionCreateOptions>(o => sent = o).ReturnsAsync("u");
        var id = Guid.NewGuid();

        await CreateSut().CreateCardSetupSessionAsync(id, "cus_9", "https://app/ok", "https://app/no");

        sent!.SuccessUrl.Should().Be("https://app/ok");
        sent.CancelUrl.Should().Be("https://app/no");
        sent.AllowedPaymentMethodTypes.Should().Equal("card");
        sent.SetupIntentData.Metadata.Should().Contain("kind", "card-setup").And.Contain("userId", id.ToString());
    }

    [Fact]
    public async Task CreateCustomer_PassesUserMetadataAndIdempotencyKey()
    {
        CustomerCreateOptions? sent = null;
        string? key = null;
        _api.Setup(a => a.CreateCustomerAsync(It.IsAny<CustomerCreateOptions>(), It.IsAny<string>()))
            .Callback<CustomerCreateOptions, string>((o, k) => { sent = o; key = k; }).ReturnsAsync("cus_1");
        var id = Guid.NewGuid();

        (await CreateSut().CreateCustomerAsync(id, "a@b", "A B")).Should().Be("cus_1");

        sent!.Metadata.Should().Contain("userId", id.ToString());
        key.Should().Be($"customer-{id}");
    }

    [Fact]
    public async Task DetachCard_DelegatesToTheApi()
    {
        await CreateSut().DetachCardAsync("pm_9");

        _api.Verify(a => a.DetachPaymentMethodAsync("pm_9"), Times.Once);
    }
}
