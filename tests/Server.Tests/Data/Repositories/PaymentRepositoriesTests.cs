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

    [Fact]
    public async Task ProcessedEvents_ExistsAfterAdd()
    {
        await using var db = DbContextFactory.Create(nameof(ProcessedEvents_ExistsAfterAdd));
        var repo = new ProcessedPaymentEventRepository(db);

        (await repo.ExistsAsync("evt_1")).Should().BeFalse();
        await repo.AddAsync(new ProcessedPaymentEvent { EventId = "evt_1", Provider = "Fake", Type = "CardSavedEvent" });
        (await repo.ExistsAsync("evt_1")).Should().BeTrue();
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
