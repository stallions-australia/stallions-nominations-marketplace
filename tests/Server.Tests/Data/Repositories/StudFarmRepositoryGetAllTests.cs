using FluentAssertions;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data.Repositories;

public class StudFarmRepositoryGetAllTests
{
    [Fact]
    public async Task GetAllAsync_ReturnsAllFarms_WithUserNavigation()
    {
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_ReturnsAllFarms_WithUserNavigation));

        var user1 = new User { Id = Guid.NewGuid(), EntraObjectId = "oid-1", DisplayName = "Alice", Email = "alice@test.com", Role = UserRole.StudFarmAdmin };
        var user2 = new User { Id = Guid.NewGuid(), EntraObjectId = "oid-2", DisplayName = "Bob", Email = "bob@test.com", Role = UserRole.StudFarmAdmin };
        db.Users.AddRange(user1, user2);

        var farm1 = new StudFarm { Id = Guid.NewGuid(), UserId = user1.Id, Name = "Alpha Stud" };
        var farm2 = new StudFarm { Id = Guid.NewGuid(), UserId = user2.Id, Name = "Beta Stud" };
        db.StudFarms.AddRange(farm1, farm2);
        await db.SaveChangesAsync();

        var repo = new StudFarmRepository(db);
        var result = await repo.GetAllAsync();

        result.Should().HaveCount(2);
        result.Should().Contain(f => f.Name == "Alpha Stud" && f.User.DisplayName == "Alice");
        result.Should().Contain(f => f.Name == "Beta Stud" && f.User.DisplayName == "Bob");
    }

    [Fact]
    public async Task GetAllAsync_WhenEmpty_ReturnsEmptyList()
    {
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_WhenEmpty_ReturnsEmptyList));
        var repo = new StudFarmRepository(db);
        var result = await repo.GetAllAsync();
        result.Should().BeEmpty();
    }
}
