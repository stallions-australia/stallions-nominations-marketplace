using FluentAssertions;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;

namespace Stallions.Server.Tests.Data.Repositories;

public class PlatformSettingsRepositoryTests
{
    [Fact]
    public async Task GetAsync_ReturnsSeededDefaults()
    {
        await using var db = DbContextFactory.Create(nameof(GetAsync_ReturnsSeededDefaults));
        await db.Database.EnsureCreatedAsync(); // applies HasData seed

        var settings = await new PlatformSettingsRepository(db).GetAsync();

        settings.Id.Should().Be(PlatformSettings.SingletonId);
        settings.BuyerFeeIncGst.Should().Be(150m);
        settings.StandardListingFeeIncGst.Should().Be(990m);
        settings.MinimumBidIncrement.Should().Be(25m);
        settings.ChargeGracePeriodHours.Should().Be(2);
        settings.OfferExpiryDays.Should().Be(7);
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges()
    {
        var name = nameof(UpdateAsync_PersistsChanges);
        await using (var db = DbContextFactory.Create(name))
        {
            await db.Database.EnsureCreatedAsync();
            var repo = new PlatformSettingsRepository(db);
            var settings = await repo.GetAsync();
            settings.BuyerFeeIncGst = 175m;
            await repo.UpdateAsync(settings);
        }

        await using var fresh = DbContextFactory.Create(name);
        (await new PlatformSettingsRepository(fresh).GetAsync()).BuyerFeeIncGst.Should().Be(175m);
    }
}
