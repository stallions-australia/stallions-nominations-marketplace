using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Stallions.Server.Payments;

namespace Stallions.Server.Tests.Payments;

public class PaymentServiceCollectionExtensionsTests
{
    private static (IServiceCollection, IConfiguration, IHostEnvironment) Setup(string provider, string env)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Payments:Provider"] = provider })
            .Build();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(env);
        return (new ServiceCollection(), config, environment.Object);
    }

    [Fact]
    public void Fake_InProduction_ThrowsAtStartup()
    {
        var (services, config, env) = Setup("Fake", "Production");

        FluentActions.Invoking(() => services.AddPayments(config, env))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Fake_InDevelopment_RegistersFakeProvider()
    {
        var (services, config, env) = Setup("Fake", "Development");

        services.AddPayments(config, env);

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IPaymentProvider>().Should().BeOfType<FakePaymentProvider>();
        sp.GetRequiredService<IPaymentProvider>().Should().BeSameAs(sp.GetRequiredService<FakePaymentProvider>());
    }
}
