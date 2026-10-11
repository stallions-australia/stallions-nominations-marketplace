namespace Stallions.Server.Email;

/// <summary>The "Email" configuration section. ACS uses the app's managed identity — there is no key.</summary>
public class EmailOptions
{
    public const string Section = "Email";
    public const string ProviderAcs = "Acs";
    public const string ProviderLog = "Log";

    /// <summary>"Acs", or "Log" (writes emails to the log; Development/Staging only).</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The Communication Services endpoint, e.g. https://acs-….communication.azure.com.</summary>
    public string AcsEndpoint { get; set; } = string.Empty;

    /// <summary>The verified sender, e.g. DoNotReply@….azurecomm.net.</summary>
    public string SenderAddress { get; set; } = string.Empty;

    /// <summary>The site's public address, used for links in emails.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;
}

public static class EmailOptionsValidator
{
    /// <summary>Returns an error message, or null when the configuration is usable.</summary>
    public static string? Validate(EmailOptions options, string environmentName)
    {
        if (!Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var baseUrl) || baseUrl.Scheme != Uri.UriSchemeHttps)
            return "Email:PublicBaseUrl must be an absolute https URL.";

        switch (options.Provider)
        {
            case EmailOptions.ProviderLog:
                // Allow-list: emails silently going to the log must never happen in Production.
                return environmentName is "Development" or "Staging"
                    ? null
                    : "The log email sender can only run in Development or Staging.";
            case EmailOptions.ProviderAcs:
                if (!Uri.TryCreate(options.AcsEndpoint, UriKind.Absolute, out _))
                    return "Email:AcsEndpoint is missing.";
                if (string.IsNullOrWhiteSpace(options.SenderAddress) || !options.SenderAddress.Contains('@'))
                    return "Email:SenderAddress is missing.";
                return null;
            default:
                return $"Unknown email provider '{options.Provider}'. Use Acs or Log.";
        }
    }
}
