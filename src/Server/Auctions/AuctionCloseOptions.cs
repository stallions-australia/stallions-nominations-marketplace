namespace Stallions.Server.Auctions;

/// <summary>The "AuctionClose" section — operational settings, not business rules.</summary>
public class AuctionCloseOptions
{
    public const string Section = "AuctionClose";

    /// <summary>Switch the job off (maintenance, tests). On by default.</summary>
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 60;
    /// <summary>Most auctions closed, and most charges made, per run.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>A bid that passed the end-time check may still be committing just after the end.</summary>
    public static readonly TimeSpan CloseDelay = TimeSpan.FromSeconds(30);
    /// <summary>A charge attempt with no recorded result after this long was interrupted.</summary>
    public static readonly TimeSpan InterruptedAttemptAge = TimeSpan.FromMinutes(2);
    /// <summary>An attempt still unresolved after this long is flagged for Staff and never repeated automatically.</summary>
    public static readonly TimeSpan StuckAttemptAge = TimeSpan.FromHours(1);
}
