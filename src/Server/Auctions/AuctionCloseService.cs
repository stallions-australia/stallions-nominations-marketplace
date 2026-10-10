using Microsoft.Extensions.Options;
using Stallions.Server.Infrastructure;

namespace Stallions.Server.Auctions;

/// <summary>
/// Runs IAuctionCloser every AuctionClose:IntervalSeconds (default 60). Runs inside the API (dev's
/// App Service has Always On so it keeps running when the site is idle); safe on several instances.
/// </summary>
public class AuctionCloseService : PeriodicScopedService
{
    private readonly AuctionCloseOptions _options;
    private readonly ILogger<AuctionCloseService> _log;

    public AuctionCloseService(IServiceScopeFactory scopes, IOptions<AuctionCloseOptions> options, ILogger<AuctionCloseService> log)
        : base(scopes, log)
    {
        _options = options.Value;
        _log = log;
    }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(10, _options.IntervalSeconds));

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _log.LogWarning("Auction closing is switched off (AuctionClose:Enabled = false)");
            return Task.CompletedTask;
        }
        return base.ExecuteAsync(stoppingToken);
    }

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var run = await services.GetRequiredService<IAuctionCloser>().RunAsync(ct);
        if (run.AuctionsClosed > 0 || run.ChargesAttempted > 0)
            _log.LogInformation("Auction close run: {Closed} auctions closed, {Charged} buyer-fee charges made",
                run.AuctionsClosed, run.ChargesAttempted);
    }
}
