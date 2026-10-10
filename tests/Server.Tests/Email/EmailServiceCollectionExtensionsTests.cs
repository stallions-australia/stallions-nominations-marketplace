using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Stallions.Server.Email;

namespace Stallions.Server.Tests.Email;

public class EmailServiceCollectionExtensionsTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Stallions.Server";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Fact]
    public void Log_RegistersTheLogSender()
    {
        var services = new ServiceCollection();
        services.AddEmail(Config(("Email:Provider", "Log"), ("Email:PublicBaseUrl", "https://localhost:7083")), new Env("Development"));

        services.Should().Contain(d => d.ServiceType == typeof(IEmailSender) && d.ImplementationType == typeof(LogEmailSender));
    }

    [Fact]
    public void Acs_RegistersTheAcsSender()
    {
        var services = new ServiceCollection();
        services.AddEmail(Config(("Email:Provider", "Acs"), ("Email:AcsEndpoint", "https://acs.example.communication.azure.com"),
            ("Email:SenderAddress", "DoNotReply@x.azurecomm.net"), ("Email:PublicBaseUrl", "https://app.example")),
            new Env("Staging"));

        services.Should().Contain(d => d.ServiceType == typeof(IEmailSender) && d.ImplementationType == typeof(AcsEmailSender));
    }

    [Fact]
    public void InvalidConfiguration_StopsStartup()
    {
        var act = () => new ServiceCollection().AddEmail(Config(("Email:Provider", "Acs")), new Env("Staging"));

        act.Should().Throw<InvalidOperationException>().WithMessage("Email configuration:*");
    }
}
