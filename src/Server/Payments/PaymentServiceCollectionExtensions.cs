namespace Stallions.Server.Payments;

public static class PaymentServiceCollectionExtensions
{
    /// <summary>Registers the configured payment provider. Throws at startup on an invalid configuration.</summary>
    public static IServiceCollection AddPayments(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(PaymentOptions.Section);
        services.Configure<PaymentOptions>(section);
        var options = section.Get<PaymentOptions>() ?? new PaymentOptions();

        var error = PaymentOptionsValidator.Validate(options, environment.IsProduction());
        if (error != null) throw new InvalidOperationException($"Payment configuration: {error}");

        if (options.Provider == PaymentOptions.ProviderFake)
        {
            services.AddSingleton<FakePaymentProvider>();
            services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<FakePaymentProvider>());
        }

        // One database transaction per payment event (claim + writes + completion).
        services.AddScoped<Stallions.Server.Data.ITransactionRunner, Stallions.Server.Data.EfTransactionRunner>();
        services.AddScoped<IPaymentEventProcessor, PaymentEventProcessor>();
        return services;
    }
}
