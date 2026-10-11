using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Auctions;

namespace Stallions.Server.Tests.Auctions;

public class AuctionCloseServiceTests
{
    private readonly Mock<IAuctionCloser> _closer = new();

    private AuctionCloseService CreateSut(bool enabled)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _closer.Object);
        var scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new AuctionCloseService(scopes,
            Microsoft.Extensions.Options.Options.Create(new AuctionCloseOptions { Enabled = enabled, IntervalSeconds = 60 }),
            NullLogger<AuctionCloseService>.Instance);
    }

    [Fact]
    public async Task WhenEnabled_RunsTheCloserStraightAway()
    {
        var ran = new TaskCompletionSource();
        _closer.Setup(c => c.RunAsync(It.IsAny<CancellationToken>()))
            .Callback(() => ran.TrySetResult())
            .ReturnsAsync(new AuctionCloseRun(0, 0));
        var sut = CreateSut(enabled: true);

        await sut.StartAsync(CancellationToken.None);
        var finished = await Task.WhenAny(ran.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        await sut.StopAsync(CancellationToken.None);

        finished.Should().Be(ran.Task);
    }

    [Fact]
    public async Task WhenDisabled_NeverRunsTheCloser()
    {
        var sut = CreateSut(enabled: false);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await sut.StopAsync(CancellationToken.None);

        _closer.Verify(c => c.RunAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
