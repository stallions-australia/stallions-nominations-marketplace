using FluentAssertions;
using Stallions.Server.Payments;

namespace Stallions.Server.Tests.Payments;

public class PaymentOptionsValidatorTests
{
    private static PaymentOptions Options(string provider, string key = "", string secret = "") => new()
    {
        Provider = provider,
        Stripe = new StripeSettings { SecretKey = key, WebhookSigningSecret = secret }
    };

    [Fact]
    public void Fake_IsAllowedOutsideProduction() =>
        PaymentOptionsValidator.Validate(Options("Fake"), isProduction: false).Should().BeNull();

    [Fact]
    public void Fake_IsRefusedInProduction() =>
        PaymentOptionsValidator.Validate(Options("Fake"), isProduction: true)
            .Should().Contain("cannot run in Production");

    [Fact]
    public void Stripe_WithBothSecrets_IsValid() =>
        PaymentOptionsValidator.Validate(Options("Stripe", "sk_test_x", "whsec_x"), isProduction: true)
            .Should().BeNull();

    [Theory]
    [InlineData("", "whsec_x")]
    [InlineData("sk_test_x", "")]
    [InlineData("@Microsoft.KeyVault(SecretUri=https://kv/secrets/StripeSecretKey/)", "whsec_x")]
    public void Stripe_WithMissingOrUnresolvedSecret_IsRefused(string key, string secret) =>
        PaymentOptionsValidator.Validate(Options("Stripe", key, secret), isProduction: false)
            .Should().Contain("Stripe");

    [Theory]
    [InlineData("")]
    [InlineData("PayPal")]
    public void UnknownProvider_IsRefused(string provider) =>
        PaymentOptionsValidator.Validate(Options(provider), isProduction: false)
            .Should().Contain("Unknown payment provider");
}
