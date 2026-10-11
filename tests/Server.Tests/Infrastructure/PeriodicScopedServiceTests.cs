using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Stallions.Server.Infrastructure;

namespace Stallions.Server.Tests.Infrastructure;

public class PeriodicScopedServiceTests
{
    private sealed class CountingService(IServiceScopeFactory scopes) : PeriodicScopedService(scopes, NullLogger.Instance)
    {
        public int Runs;

        protected override TimeSpan Interval => TimeSpan.FromMilliseconds(20);

        protected override Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
        {
            Interlocked.Increment(ref Runs);
            // A failing run must not stop the loop.
            return Runs == 1 ? throw new InvalidOperationException("first run fails") : Task.CompletedTask;
        }
    }

    [Fact]
    public async Task KeepsRunning_AfterARunFails_AndStopsCleanly()
    {
        var scopes = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var sut = new CountingService(scopes);

        await sut.StartAsync(CancellationToken.None);
        for (var i = 0; i < 250 && Volatile.Read(ref sut.Runs) < 3; i++) await Task.Delay(20);
        await sut.StopAsync(CancellationToken.None);

        sut.Runs.Should().BeGreaterThanOrEqualTo(3);
    }
}
