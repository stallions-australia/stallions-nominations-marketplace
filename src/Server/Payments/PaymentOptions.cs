namespace Stallions.Server.Payments;

/// <summary>The "Payments" configuration section.</summary>
public class PaymentOptions
{
    public const string Section = "Payments";
    public const string ProviderFake = "Fake";
    public const string ProviderStripe = "Stripe";

    /// <summary>"Fake" (dev only) or "Stripe".</summary>
    public string Provider { get; set; } = string.Empty;
    public StripeSettings Stripe { get; set; } = new();
}

/// <summary>Values come only from Key Vault references in App Service settings — never from a committed file.</summary>
public class StripeSettings
{
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSigningSecret { get; set; } = string.Empty;
}

public static class PaymentOptionsValidator
{
    /// <summary>Returns an error message, or null when the configuration is usable.</summary>
    public static string? Validate(PaymentOptions options, string environmentName)
    {
        switch (options.Provider)
        {
            case PaymentOptions.ProviderFake:
                return IsFakeAllowed(environmentName) ? null : "The fake payment provider can only run in Development or Staging.";
            case PaymentOptions.ProviderStripe:
                if (!IsUsable(options.Stripe.SecretKey))
                    return "Payments:Stripe:SecretKey is missing or its Key Vault reference did not resolve.";
                if (!IsUsable(options.Stripe.WebhookSigningSecret))
                    return "Payments:Stripe:WebhookSigningSecret is missing or its Key Vault reference did not resolve.";
                return null;
            default:
                return $"Unknown payment provider '{options.Provider}'. Use Fake or Stripe.";
        }
    }

    // Allow-list, not a Production deny-list: an unexpected environment name must not enable the fake.
    private static bool IsFakeAllowed(string? environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(environmentName, "Staging", StringComparison.OrdinalIgnoreCase);

    // App Service leaves the literal "@Microsoft.KeyVault(...)" in place when it can't resolve a reference.
    private static bool IsUsable(string value) =>
        !string.IsNullOrWhiteSpace(value) && !value.StartsWith("@Microsoft.KeyVault(", StringComparison.OrdinalIgnoreCase);
}
