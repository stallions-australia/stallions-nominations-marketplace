using FluentAssertions;
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
            (await new ProcessedPaymentEventRepository(db1).TryClaimAsync(Evt("evt_1"))).Should().BeTrue();
        await using var db2 = DbContextFactory.Create(name);
        (await new ProcessedPaymentEventRepository(db2).TryClaimAsync(Evt("evt_1"))).Should().BeFalse();
    }

    [Fact]
    public async Task ProcessedEvents_ReleaseThenClaimAgain_IsTrue()
    {
        var name = nameof(ProcessedEvents_ReleaseThenClaimAgain_IsTrue);
        await using (var db1 = DbContextFactory.Create(name))
            (await new ProcessedPaymentEventRepository(db1).TryClaimAsync(Evt("evt_1"))).Should().BeTrue();
        await using (var db2 = DbContextFactory.Create(name))
            await new ProcessedPaymentEventRepository(db2).ReleaseAsync("evt_1");
        await using var db3 = DbContextFactory.Create(name);
        (await new ProcessedPaymentEventRepository(db3).TryClaimAsync(Evt("evt_1"))).Should().BeTrue();
    }

    [Fact]
    public async Task ProcessedEvents_ContextStaysUsableAfterFailedClaim()
    {
        var name = nameof(ProcessedEvents_ContextStaysUsableAfterFailedClaim);
        await using var db = DbContextFactory.Create(name);
        var repo = new ProcessedPaymentEventRepository(db);
        (await repo.TryClaimAsync(Evt("evt_1"))).Should().BeTrue();
        (await repo.TryClaimAsync(Evt("evt_1"))).Should().BeFalse();

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

    [Fact]
    public void Model_SavedCardUserIdIsUnique()
    {
        using var db = DbContextFactory.Create(nameof(Model_SavedCardUserIdIsUnique));
        db.Model.FindEntityType(typeof(SavedCard))!.GetIndexes()
            .Single(i => i.Properties.Single().Name == nameof(SavedCard.UserId))
            .IsUnique.Should().BeTrue();
    }
}
