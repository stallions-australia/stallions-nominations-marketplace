namespace Stallions.Server.Infrastructure;

/// <summary>
/// Runs a unit of work in a fresh DI scope on a fixed interval, starting immediately. A failing
/// run is logged and the next run goes ahead; stopping the app ends the loop.
/// </summary>
public abstract class PeriodicScopedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger _log;

    protected PeriodicScopedService(IServiceScopeFactory scopes, ILogger log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected abstract TimeSpan Interval { get; }

    protected abstract Task RunOnceAsync(IServiceProvider services, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    await RunOnceAsync(scope.ServiceProvider, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogError(ex, "{Service} run failed", GetType().Name);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}
