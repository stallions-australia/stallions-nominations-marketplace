using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;

namespace Stallions.Server.Tests.Data.Repositories;

public class PaymentRepositoriesTests
{
    [Fact]
    public async Task SavedCards_AddThenGetByUser_RoundTrips()
    {
        await using var db = DbContextFactory.Create(nameof(SavedCards_AddThenGetByUser_RoundTrips));
        var userId = Guid.NewGuid();
        var repo = new SavedCardRepository(db);
        await repo.AddAsync(new SavedCard
        {
            UserId = userId, Provider = "Fake", ProviderCustomerId = "cus_1",
            ProviderPaymentMethodId = "pm_1", Brand = "visa", Last4 = "4242", ExpMonth = 8, ExpYear = 2028
        });

        var card = await repo.GetByUserIdAsync(userId);

        card!.Last4.Should().Be("4242");
        (await repo.GetByUserIdAsync(Guid.NewGuid())).Should().BeNull();
    }

    private static ProcessedPaymentEvent Evt(string id) =>
        new() { EventId = id, Provider = "Fake", Type = "CardSavedEvent" };

    [Fact]
    public async Task ProcessedEvents_TryClaim_SecondClaimFromNewContextIsFalse()
    {
        var name = nameof(ProcessedEvents_TryClaim_SecondClaimFromNewContextIsFalse);
        await using (var db1 = DbContextFactory.Create(name))
            (await new ProcessedPaymentEventRepository(db1).ClaimAsync(Evt("evt_1"))).Should().Be(ClaimResult.Claimed);
        await using var db2 = DbContextFactory.Create(name);
        (await new ProcessedPaymentEventRepository(db2).ClaimAsync(Evt("evt_1"))).Should().Be(ClaimResult.InProgress);
    }

    [Fact]
    public async Task ProcessedEvents_ReleaseThenClaimAgain_IsTrue()
    {
        var name = nameof(ProcessedEvents_ReleaseThenClaimAgain_IsTrue);
        await using (var db1 = DbContextFactory.Create(name))
            (await new ProcessedPaymentEventRepository(db1).ClaimAsync(Evt("evt_1"))).Should().Be(ClaimResult.Claimed);
        await using (var db2 = DbContextFactory.Create(name))
            await new ProcessedPaymentEventRepository(db2).ReleaseAsync("evt_1");
        await using var db3 = DbContextFactory.Create(name);
        (await new ProcessedPaymentEventRepository(db3).ClaimAsync(Evt("evt_1"))).Should().Be(ClaimResult.Claimed);
    }

    [Fact]
    public async Task ProcessedEvents_ContextStaysUsableAfterFailedClaim()
    {
        var name = nameof(ProcessedEvents_ContextStaysUsableAfterFailedClaim);
        await using var db = DbContextFactory.Create(name);
        var repo = new ProcessedPaymentEventRepository(db);
        (await repo.ClaimAsync(Evt("evt_1"))).Should().Be(ClaimResult.Claimed);
        (await repo.ClaimAsync(Evt("evt_1"))).Should().Be(ClaimResult.InProgress);

        var cards = new SavedCardRepository(db);
        await cards.AddAsync(new SavedCard
        {
            UserId = Guid.NewGuid(), Provider = "Fake", ProviderCustomerId = "cus_1",
            ProviderPaymentMethodId = "pm_1", Brand = "visa", Last4 = "4242", ExpMonth = 8, ExpYear = 2028
        });
        db.SavedCards.Count().Should().Be(1);
    }

    [Fact]
    public async Task SavedCards_Update_PersistsToFreshContext()
    {
        var name = nameof(SavedCards_Update_PersistsToFreshContext);
        var userId = Guid.NewGuid();
        await using (var db1 = DbContextFactory.Create(name))
        {
            var repo = new SavedCardRepository(db1);
            var card = await repo.AddAsync(new SavedCard
            {
                UserId = userId, Provider = "Fake", ProviderCustomerId = "cus_1",
                ProviderPaymentMethodId = "pm_1", Brand = "visa", Last4 = "4242", ExpMonth = 8, ExpYear = 2028
            });
            card.Last4 = "1111";
            card.ProviderPaymentMethodId = "pm_2";
            await repo.UpdateAsync(card);
        }
        await using var db2 = DbContextFactory.Create(name);
        var reread = await new SavedCardRepository(db2).GetByUserIdAsync(userId);
        reread!.Last4.Should().Be("1111");
        reread.ProviderPaymentMethodId.Should().Be("pm_2");
    }

    private sealed class FailingSaveContext : AppDbContext
    {
        public FailingSaveContext(string name)
            : base(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options) { }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("boom");
    }

    private static async Task SeedEventAsync(string name, ProcessedPaymentEvent e)
    {
        await using var db = DbContextFactory.Create(name);
        db.ProcessedPaymentEvents.Add(e);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ProcessedEvents_TryClaim_RethrowsNonDuplicateFailure()
    {
        await using var db = new FailingSaveContext(nameof(ProcessedEvents_TryClaim_RethrowsNonDuplicateFailure));
        var act = () => new ProcessedPaymentEventRepository(db).ClaimAsync(Evt("evt_1"));
        await act.Should().ThrowAsync<DbUpdateException>().WithMessage("boom");
    }

    [Fact]
    public async Task ProcessedEvents_Release_WorksWhenContextTracksAFailedWrite()
    {
        var name = nameof(ProcessedEvents_Release_WorksWhenContextTracksAFailedWrite);
        await using (var db1 = DbContextFactory.Create(name))
        {
            var repo = new ProcessedPaymentEventRepository(db1);
            (await repo.ClaimAsync(Evt("evt_1"))).Should().Be(ClaimResult.Claimed);
            db1.SavedCards.Add(new SavedCard { UserId = Guid.NewGuid() }); // left unsaved and tracked
            await repo.ReleaseAsync("evt_1");
        }
        await using var db2 = DbContextFactory.Create(name);
        (await new ProcessedPaymentEventRepository(db2).ClaimAsync(Evt("evt_1"))).Should().Be(ClaimResult.Claimed);
        db2.SavedCards.Count().Should().Be(0);
    }

    [Fact]
    public async Task ProcessedEvents_TryClaim_ReclaimsStaleIncompleteClaim()
    {
        var name = nameof(ProcessedEvents_TryClaim_ReclaimsStaleIncompleteClaim);
        var old = DateTime.UtcNow - ProcessedPaymentEventRepository.ClaimTimeout - TimeSpan.FromMinutes(1);
        await SeedEventAsync(name, new ProcessedPaymentEvent { EventId = "evt_1", Provider = "Fake", Type = "T", ProcessedAt = old });

        await using var db = DbContextFactory.Create(name);
        (await new ProcessedPaymentEventRepository(db).ClaimAsync(Evt("evt_1"))).Should().Be(ClaimResult.Claimed);

        await using var db2 = DbContextFactory.Create(name);
        (await db2.ProcessedPaymentEvents.SingleAsync()).ProcessedAt.Should().BeAfter(old.AddMinutes(5));
    }

    [Fact]
    public async Task ProcessedEvents_TryClaim_FreshIncompleteClaimIsFalse()
    {
        var name = nameof(ProcessedEvents_TryClaim_FreshIncompleteClaimIsFalse);
        await SeedEventAsync(name, new ProcessedPaymentEvent { EventId = "evt_1", Provider = "Fake", Type = "T", ProcessedAt = DateTime.UtcNow.AddMinutes(-1) });

        await using var db = DbContextFactory.Create(name);
        (await new ProcessedPaymentEventRepository(db).ClaimAsync(Evt("evt_1"))).Should().Be(ClaimResult.InProgress);
    }

    [Fact]
    public async Task ProcessedEvents_TryClaim_CompletedOldClaimIsFalse()
    {
        var name = nameof(ProcessedEvents_TryClaim_CompletedOldClaimIsFalse);
        var old = DateTime.UtcNow.AddHours(-2);
        await SeedEventAsync(name, new ProcessedPaymentEvent { EventId = "evt_1", Provider = "Fake", Type = "T", ProcessedAt = old, CompletedAt = old });

        await using var db = DbContextFactory.Create(name);
        (await new ProcessedPaymentEventRepository(db).ClaimAsync(Evt("evt_1"))).Should().Be(ClaimResult.AlreadyCompleted);
    }

    [Fact]
    public async Task ProcessedEvents_MarkCompleted_SetsCompletedAt()
    {
        var name = nameof(ProcessedEvents_MarkCompleted_SetsCompletedAt);
        await using (var db1 = DbContextFactory.Create(name))
        {
            var repo = new ProcessedPaymentEventRepository(db1);
            await repo.ClaimAsync(Evt("evt_1"));
            await repo.MarkCompletedAsync("evt_1");
        }
        await using var db2 = DbContextFactory.Create(name);
        (await db2.ProcessedPaymentEvents.SingleAsync()).CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Model_SavedCardUserIdIsUnique()
    {
        using var db = DbContextFactory.Create(nameof(Model_SavedCardUserIdIsUnique));
        db.Model.FindEntityType(typeof(SavedCard))!.GetIndexes()
            .Single(i => i.Properties.Single().Name == nameof(SavedCard.UserId))
            .IsUnique.Should().BeTrue();
    }
}
