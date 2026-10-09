using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data.Repositories;

public class SubscriptionRepositoryTests
{
    [Fact]
    public async Task SetPendingCheckout_WritesOnlyTheCheckoutColumns_SoAConcurrentPaymentSurvives()
    {
        var dbName = Guid.NewGuid().ToString();
        var id = Guid.NewGuid();
        var stallionId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        await using (var seed = DbContextFactory.Create(dbName))
        {
            seed.StallionSeasonSubscriptions.Add(new StallionSeasonSubscription
            {
                Id = id, StallionId = stallionId, SeasonId = seasonId, StudFarmId = Guid.NewGuid(),
                FeeIncGst = 990m, Status = SubscriptionStatus.Pending
            });
            await seed.SaveChangesAsync();
        }

        await using var ctxA = DbContextFactory.Create(dbName);
        await using var ctxB = DbContextFactory.Create(dbName);
        var stale = await new SubscriptionRepository(ctxA).GetByStallionAndSeasonAsync(stallionId, seasonId);

        var other = await ctxB.StallionSeasonSubscriptions.SingleAsync(s => s.Id == id);
        other.Status = SubscriptionStatus.Paid;
        other.PaymentMethod = SubscriptionPaymentMethod.Card;
        other.PaymentReference = "INV-9";
        other.PaidAt = DateTime.UtcNow;
        await ctxB.SaveChangesAsync();

        var expires = DateTime.UtcNow.AddHours(1);
        await new SubscriptionRepository(ctxA).SetPendingCheckoutAsync(stale!, "https://pay/x", expires);

        await using var fresh = DbContextFactory.Create(dbName);
        var saved = await fresh.StallionSeasonSubscriptions.SingleAsync(s => s.Id == id);
        saved.Status.Should().Be(SubscriptionStatus.Paid);
        saved.PaymentReference.Should().Be("INV-9");
        saved.PaymentMethod.Should().Be(SubscriptionPaymentMethod.Card);
        saved.PaidAt.Should().NotBeNull();
        saved.PendingCheckoutUrl.Should().Be("https://pay/x");
        saved.PendingCheckoutExpiresAt.Should().BeCloseTo(expires, TimeSpan.FromSeconds(1));
    }
}
