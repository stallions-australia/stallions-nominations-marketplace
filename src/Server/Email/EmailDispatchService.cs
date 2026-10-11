using Stallions.Server.Infrastructure;

namespace Stallions.Server.Email;

/// <summary>Sends queued emails every 30 seconds.</summary>
public class EmailDispatchService : PeriodicScopedService
{
    public EmailDispatchService(IServiceScopeFactory scopes, ILogger<EmailDispatchService> log) : base(scopes, log) { }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(30);

    protected override Task RunOnceAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IEmailDispatcher>().SendDueAsync(ct);
}
