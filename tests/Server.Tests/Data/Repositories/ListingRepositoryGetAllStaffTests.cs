using FluentAssertions;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data.Repositories;

public class ListingRepositoryGetAllStaffTests
{
    private static (User user, StudFarm farm, Stallion stallion, Season season) SeedCommon(
        Stallions.Server.Data.AppDbContext db, string suffix)
    {
        var user = new User
        {
            Id = Guid.NewGuid(), EntraObjectId = $"oid-{suffix}",
            DisplayName = $"Admin {suffix}", Email = $"admin{suffix}@test.com",
            Role = UserRole.StudFarmAdmin, Status = UserStatus.Active
        };
        var farm = new StudFarm { Id = Guid.NewGuid(), UserId = user.Id, Name = $"Farm {suffix}" };
        var stallion = new Stallion { Id = Guid.NewGuid(), StudFarmId = farm.Id, Name = $"Stallion {suffix}" };
        var season = new Season
        {
            Id = Guid.NewGuid(), Name = $"Season {suffix}",
            StartDate = new DateOnly(2025, 8, 1), EndDate = new DateOnly(2026, 3, 31)
        };
        db.Users.Add(user);
        db.StudFarms.Add(farm);
        db.Stallions.Add(stallion);
        db.Seasons.Add(season);
        return (user, farm, stallion, season);
    }

    [Fact]
    public async Task GetAllStaffAsync_ReturnsBothFixedPriceAndAuctionListings_WithNavigations()
    {
        await using var db = DbContextFactory.Create(
            nameof(GetAllStaffAsync_ReturnsBothFixedPriceAndAuctionListings_WithNavigations));

        var (_, farm1, stallion1, season1) = SeedCommon(db, "A");
        var (_, farm2, stallion2, season2) = SeedCommon(db, "B");

        var fixedListing = new FixedPriceListing
        {
            Id = Guid.NewGuid(),
            StallionId = stallion1.Id,
            SeasonId = season1.Id,
            StudFarmId = farm1.Id,
            ListingType = ListingType.FixedPrice,
            Status = ListingStatus.Active,
            PriceIncGst = 8000m,
            Quantity = 10,
            QuantityRemaining = 10
        };
        var auctionListing = new AuctionListing
        {
            Id = Guid.NewGuid(),
            StallionId = stallion2.Id,
            SeasonId = season2.Id,
            StudFarmId = farm2.Id,
            ListingType = ListingType.Auction,
            Status = ListingStatus.Active,
            StartingPrice = 5000m,
            EndDateTime = DateTime.UtcNow.AddDays(7)
        };

        db.FixedPriceListings.Add(fixedListing);
        db.AuctionListings.Add(auctionListing);
        await db.SaveChangesAsync();

        var repo = new ListingRepository(db);
        var result = await repo.GetAllStaffAsync();

        result.Should().HaveCount(2);

        var fp = result.Should().ContainSingle(l => l.Id == fixedListing.Id).Subject;
        fp.Stallion.Should().NotBeNull();
        fp.Stallion.Name.Should().Be("Stallion A");
        fp.StudFarm.Should().NotBeNull();
        fp.StudFarm.Name.Should().Be("Farm A");

        var au = result.Should().ContainSingle(l => l.Id == auctionListing.Id).Subject;
        au.Stallion.Should().NotBeNull();
        au.Stallion.Name.Should().Be("Stallion B");
        au.StudFarm.Should().NotBeNull();
        au.StudFarm.Name.Should().Be("Farm B");
    }

    [Fact]
    public async Task GetAllStaffAsync_WhenNoListingsExist_ReturnsEmptyList()
    {
        await using var db = DbContextFactory.Create(
            nameof(GetAllStaffAsync_WhenNoListingsExist_ReturnsEmptyList));

        var repo = new ListingRepository(db);
        var result = await repo.GetAllStaffAsync();

        result.Should().BeEmpty();
    }
}
