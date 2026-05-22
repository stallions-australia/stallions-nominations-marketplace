using Microsoft.Extensions.DependencyInjection;
using Stallions.Client.Services;
using Xunit;

namespace Stallions.Client.Tests.Services;

public class AdminApiServiceRegistrationTests
{
    [Fact]
    public void AdminApiService_IsRegistered_InServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddHttpClient<AdminApiService>(c => c.BaseAddress = new Uri("https://localhost/"));
        var provider = services.BuildServiceProvider();

        var service = provider.GetService<AdminApiService>();
        Assert.NotNull(service);
    }
}
