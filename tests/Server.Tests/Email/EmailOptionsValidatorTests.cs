using FluentAssertions;
using Stallions.Server.Email;

namespace Stallions.Server.Tests.Email;

public class EmailOptionsValidatorTests
{
    private static EmailOptions Acs() => new()
    {
        Provider = EmailOptions.ProviderAcs,
        AcsEndpoint = "https://acs-stallions-noms-dev.australia.communication.azure.com",
        SenderAddress = "DoNotReply@example.azurecomm.net",
        PublicBaseUrl = "https://app-stallions-noms-dev.azurewebsites.net"
    };

    [Fact]
    public void Acs_WithEndpointSenderAndBaseUrl_IsValid() =>
        EmailOptionsValidator.Validate(Acs(), "Staging").Should().BeNull();

    [Fact]
    public void Acs_WithoutEndpoint_IsRejected() =>
        EmailOptionsValidator.Validate(new EmailOptions
        {
            Provider = "Acs", SenderAddress = "a@b.net", PublicBaseUrl = "https://x"
        }, "Staging").Should().Contain("AcsEndpoint");

    [Fact]
    public void Acs_WithoutSender_IsRejected()
    {
        var o = Acs(); o.SenderAddress = "";
        EmailOptionsValidator.Validate(o, "Production").Should().Contain("SenderAddress");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://app.example.com")]
    public void PublicBaseUrl_MustBeAbsoluteHttps(string url)
    {
        var o = Acs(); o.PublicBaseUrl = url;
        EmailOptionsValidator.Validate(o, "Staging").Should().Contain("PublicBaseUrl");
    }

    [Fact]
    public void Log_IsAllowedInDevelopment() =>
        EmailOptionsValidator.Validate(new EmailOptions { Provider = "Log", PublicBaseUrl = "https://localhost:7083" },
            "Development").Should().BeNull();

    [Fact]
    public void Log_IsRefusedInProduction() =>
        EmailOptionsValidator.Validate(new EmailOptions { Provider = "Log", PublicBaseUrl = "https://x.example" },
            "Production").Should().NotBeNull();

    [Fact]
    public void UnknownProvider_IsRejected() =>
        EmailOptionsValidator.Validate(new EmailOptions { Provider = "Smtp", PublicBaseUrl = "https://x" },
            "Development").Should().Contain("Unknown");
}
