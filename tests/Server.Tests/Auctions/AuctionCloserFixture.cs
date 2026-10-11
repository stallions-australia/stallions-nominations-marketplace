using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Stallions.Server.Auctions;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Payments;
using Stallions.Server.Tests.Helpers;

namespace Stallions.Server.Tests.Auctions;

/// <summary>
/// An AuctionCloser over real repositories on a shared in-memory database, with a mocked payment
/// provider, mocked emails and a test clock. Seed with Seed(); assert with Read().
/// </summary>
public class AuctionCloserFixture
{
    public string DbName { get; } = Guid.NewGuid().ToString();
    public TestClock Clock { get; } = new();
    public Mock<IPaymentProvider> Provider { get; } = new();
    public Mock<IAuctionEmails> Emails { get; } = new();
    public Mock<IAuditLogRepository> Audit { get; } = new();
    public PlatformSettings Settings { get; } = new() { ChargeGracePeriodHours = 2, BuyerFeeIncGst = 150m };
    public List<ChargeRequest> Charges { get; } = new();

    public AuctionCloserFixture()
    {
        Provider.SetupGet(p => p.Name).Returns("Fake");
        ChargesSucceed();
    }

    public void ChargesSucceed() =>
        Provider.Setup(p => p.ChargeSavedCardAsync(It.IsAny<ChargeRequest>()))
            .Callback<ChargeRequest>(Charges.Add)
            .ReturnsAsync(() => ChargeResult.Success($"pi_{Charges.Count}"));

    public void ChargesDecline() =>
        Provider.Setup(p => p.ChargeSavedCardAsync(It.IsAny<ChargeRequest>()))
            .Callback<ChargeRequest>(Charges.Add)
            .ReturnsAsync(ChargeResult.Declined("card_declined", "Your card was declined."));

    public AppDbContext Read() => DbContextFactory.Create(DbName);

    public T Seed<T>(Func<AppDbContext, T> seed)
    {
        using var db = DbContextFactory.Create(DbName);
        return seed(db);
    }

    /// <summary>A closer on its own context, as a separate run (or app instance) would have.</summary>
    public AuctionCloser CreateSut(AppDbContext? db = null, IListingRepository? listings = null)
    {
        db ??= DbContextFactory.Create(DbName);
        var settings = new Mock<IPlatformSettingsRepository>();
        settings.Setup(s => s.GetAsync()).ReturnsAsync(Settings);
        return new AuctionCloser(
            listings ?? new ListingRepository(db), new BidRepository(db), new PurchaseRepository(db), new SavedCardRepository(db),
            settings.Object, Audit.Object, Emails.Object, Provider.Object, new ClearingTransactionRunner(db), Clock,
            Microsoft.Extensions.Options.Options.Create(new AuctionCloseOptions()), NullLogger<AuctionCloser>.Instance);
    }

    /// <summary>Like the real runner, starts every unit of work with a clean change tracker (no stale rows).</summary>
    private sealed class ClearingTransactionRunner(AppDbContext db) : ITransactionRunner
    {
        public Task<T> RunAsync<T>(Func<Task<T>> work)
        {
            db.ChangeTracker.Clear();
            return work();
        }
    }
}
