namespace Stallions.Server.Auctions;

public interface IAuctionCloser
{
    /// <summary>Closes due auctions, then makes due buyer-fee charges. Safe to run on several instances at once.</summary>
    Task<AuctionCloseRun> RunAsync(CancellationToken ct = default);
}

public sealed record AuctionCloseRun(int AuctionsClosed, int ChargesAttempted);
