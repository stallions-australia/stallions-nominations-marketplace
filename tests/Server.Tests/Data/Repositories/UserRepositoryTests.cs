using FluentAssertions;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data.Repositories;

public class UserRepositoryTests
{
    [Fact]
    public async Task AddAsync_ThenGetByEntraObjectId_ReturnsUser()
    {
        using var db = DbContextFactory.Create(nameof(AddAsync_ThenGetByEntraObjectId_ReturnsUser));
        var repo = new UserRepository(db);
        var user = new User
        {
            EntraObjectId = "oid-123",
            Email = "buyer@test.com",
            DisplayName = "Test Buyer",
            Role = UserRole.Buyer
        };

        await repo.AddAsync(user);
        var found = await repo.GetByEntraObjectIdAsync("oid-123");

        found.Should().NotBeNull();
        found!.Email.Should().Be("buyer@test.com");
    }

    [Fact]
    public async Task GetAllAsync_FilterByRole_ReturnsOnlyMatchingUsers()
    {
        using var db = DbContextFactory.Create(nameof(GetAllAsync_FilterByRole_ReturnsOnlyMatchingUsers));
        var repo = new UserRepository(db);
        await repo.AddAsync(new User { EntraObjectId = "a", Email = "buyer@test.com", DisplayName = "B", Role = UserRole.Buyer });
        await repo.AddAsync(new User { EntraObjectId = "b", Email = "farm@test.com", DisplayName = "F", Role = UserRole.StudFarmAdmin });

        var buyers = await repo.GetAllAsync(role: UserRole.Buyer);

        buyers.Should().HaveCount(1);
        buyers[0].Role.Should().Be(UserRole.Buyer);
    }
}
