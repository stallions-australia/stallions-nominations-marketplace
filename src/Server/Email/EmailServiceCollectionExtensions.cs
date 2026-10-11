using Stallions.Server.Data.Repositories;

namespace Stallions.Server.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>Registers the outbox, the configured sender and the dispatcher. Throws at startup on an invalid configuration.</summary>
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(EmailOptions.Section);
        services.Configure<EmailOptions>(section);
        var options = section.Get<EmailOptions>() ?? new EmailOptions();

        var error = EmailOptionsValidator.Validate(options, environment.EnvironmentName);
        if (error != null) throw new InvalidOperationException($"Email configuration: {error}");

        if (options.Provider == EmailOptions.ProviderAcs)
            services.AddSingleton<IEmailSender, AcsEmailSender>();
        else
            services.AddSingleton<IEmailSender, LogEmailSender>();

        services.AddScoped<IOutboundEmailRepository, OutboundEmailRepository>();
        services.AddScoped<IEmailOutbox, EmailOutbox>();
        services.AddScoped<IEmailDispatcher, EmailDispatcher>();
        services.AddHostedService<EmailDispatchService>();
        return services;
    }
}
