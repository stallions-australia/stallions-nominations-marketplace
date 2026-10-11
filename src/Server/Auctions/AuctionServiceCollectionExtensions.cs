namespace Stallions.Server.Auctions;

public static class AuctionServiceCollectionExtensions
{
    /// <summary>The auction closer and its background service. Needs AddPayments, AddEmail and TimeProvider.</summary>
    public static IServiceCollection AddAuctionClosing(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuctionCloseOptions>(configuration.GetSection(AuctionCloseOptions.Section));
        services.AddScoped<IAuctionCloser, AuctionCloser>();
        services.AddHostedService<AuctionCloseService>();
        return services;
    }
}
