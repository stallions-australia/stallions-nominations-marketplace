using FluentAssertions;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;

namespace Stallions.Server.Tests.Data.Repositories;

public class SeasonRepositoryTests
{
    [Fact]
    public async Task GetCurrentOpenSeasonAsync_WhenOneIsOpen_ReturnsThatSeason()
    {
        using var db = DbContextFactory.Create(nameof(GetCurrentOpenSeasonAsync_WhenOneIsOpen_ReturnsThatSeason));
        var repo = new SeasonRepository(db);
        await repo.AddAsync(new Season { Name = "2025 Season", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 1, 31), IsOpen = false });
        await repo.AddAsync(new Season { Name = "2026 Season", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 1, 31), IsOpen = true });

        var current = await repo.GetCurrentOpenSeasonAsync();

        current.Should().NotBeNull();
        current!.Name.Should().Be("2026 Season");
    }

    [Fact]
    public async Task GetCurrentOpenSeasonAsync_WhenNoneIsOpen_ReturnsNull()
    {
        using var db = DbContextFactory.Create(nameof(GetCurrentOpenSeasonAsync_WhenNoneIsOpen_ReturnsNull));
        var repo = new SeasonRepository(db);
        await repo.AddAsync(new Season { Name = "2025 Season", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 1, 31), IsOpen = false });

        var current = await repo.GetCurrentOpenSeasonAsync();

        current.Should().BeNull();
    }
}
