using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;

namespace Stallions.Server.Tests.Data.Repositories;

public class StudDirectoryRepositoryTests
{
    [Fact]
    public async Task GetAllAsync_ExcludesInactiveByDefault()
    {
        var db = DbContextFactory.Create(nameof(GetAllAsync_ExcludesInactiveByDefault));
        db.StudDirectories.AddRange(
            new StudDirectory { Name = "Active Farm", IsActive = true },
            new StudDirectory { Name = "Inactive Farm", IsActive = false });
        await db.SaveChangesAsync();
        var repo = new StudDirectoryRepository(db);

        var result = await repo.GetAllAsync();

        Assert.Single(result);
        Assert.Equal("Active Farm", result[0].Name);
    }

    [Fact]
    public async Task GetAllAsync_IncludesInactiveWhenFlagSet()
    {
        var db = DbContextFactory.Create(nameof(GetAllAsync_IncludesInactiveWhenFlagSet));
        db.StudDirectories.AddRange(
            new StudDirectory { Name = "A", IsActive = true },
            new StudDirectory { Name = "B", IsActive = false });
        await db.SaveChangesAsync();
        var repo = new StudDirectoryRepository(db);

        var result = await repo.GetAllAsync(includeInactive: true);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNullForUnknownId()
    {
        var db = DbContextFactory.Create(nameof(GetByIdAsync_ReturnsNullForUnknownId));
        var repo = new StudDirectoryRepository(db);

        var result = await repo.GetByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task AddAsync_PersistsEntry()
    {
        var db = DbContextFactory.Create(nameof(AddAsync_PersistsEntry));
        var repo = new StudDirectoryRepository(db);
        var entry = new StudDirectory { Name = "New Farm", State = "NSW" };

        var added = await repo.AddAsync(entry);

        Assert.NotEqual(Guid.Empty, added.Id);
        Assert.Equal("New Farm", (await db.StudDirectories.FindAsync(added.Id))!.Name);
    }

    [Fact]
    public async Task UpdateAsync_SetsUpdatedAtAndPersists()
    {
        var db = DbContextFactory.Create(nameof(UpdateAsync_SetsUpdatedAtAndPersists));
        var entry = new StudDirectory { Name = "Farm", UpdatedAt = DateTime.UtcNow.AddDays(-1) };
        db.StudDirectories.Add(entry);
        await db.SaveChangesAsync();
        var originalId = entry.Id;
        var oldUpdatedAt = entry.UpdatedAt;

        var repo = new StudDirectoryRepository(db);
        entry.Name = "Farm Updated";
        await repo.UpdateAsync(entry);

        var fetched = await db.StudDirectories.FindAsync(originalId);
        Assert.Equal("Farm Updated", fetched!.Name);
        Assert.True(fetched.UpdatedAt > oldUpdatedAt);
    }

    [Fact]
    public async Task GetByIdAsync_IncludesStallionNavigation()
    {
        var db = DbContextFactory.Create(nameof(GetByIdAsync_IncludesStallionNavigation));
        var studDir = new StudDirectory { Name = "Test Farm" };
        db.StudDirectories.Add(studDir);
        await db.SaveChangesAsync();
        db.StallionDirectories.Add(new StallionDirectory
        {
            Name = "Stallion A",
            StudDirectoryId = studDir.Id,
            StallionId = 1,
            ArionId = 0,
            YearOfBirth = 2018
        });
        await db.SaveChangesAsync();

        var repo = new StudDirectoryRepository(db);
        var result = await repo.GetByIdAsync(studDir.Id);

        Assert.NotNull(result);
        Assert.Single(result!.Stallions);
        Assert.Equal("Stallion A", result.Stallions.First().Name);
    }
}
