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

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("development")]
    public void Fake_IsAllowedInDevelopmentAndStaging(string env) =>
        PaymentOptionsValidator.Validate(Options("Fake"), env).Should().BeNull();

    [Theory]
    [InlineData("Production")]
    [InlineData("Prod")]
    [InlineData("")]
    public void Fake_IsRefusedElsewhere(string env) =>
        PaymentOptionsValidator.Validate(Options("Fake"), env)
            .Should().Contain("can only run in Development or Staging");

    [Fact]
    public void Stripe_WithBothSecrets_IsValid() =>
        PaymentOptionsValidator.Validate(Options("Stripe", "sk_test_x", "whsec_x"), "Production")
            .Should().BeNull();

    [Theory]
    [InlineData("", "whsec_x")]
    [InlineData("sk_test_x", "")]
    [InlineData("@Microsoft.KeyVault(SecretUri=https://kv/secrets/StripeSecretKey/)", "whsec_x")]
    public void Stripe_WithMissingOrUnresolvedSecret_IsRefused(string key, string secret) =>
        PaymentOptionsValidator.Validate(Options("Stripe", key, secret), "Development")
            .Should().Contain("Stripe");

    [Theory]
    [InlineData("")]
    [InlineData("PayPal")]
    public void UnknownProvider_IsRefused(string provider) =>
        PaymentOptionsValidator.Validate(Options(provider), "Development")
            .Should().Contain("Unknown payment provider");
}
