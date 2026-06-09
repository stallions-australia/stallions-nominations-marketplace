using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;

namespace Stallions.Server.Tests.Data.Repositories;

public class StallionDirectoryRepositoryTests
{
    private static StudDirectory MakeStud(string name = "Test Stud") =>
        new() { Name = name, IsActive = true };

    [Fact]
    public async Task GetByStudDirectoryIdAsync_ReturnsOnlyActiveEntriesForThatStud()
    {
        var db = DbContextFactory.Create(nameof(GetByStudDirectoryIdAsync_ReturnsOnlyActiveEntriesForThatStud));
        var stud = MakeStud();
        var otherStud = MakeStud("Other Stud");
        db.StudDirectories.AddRange(stud, otherStud);
        await db.SaveChangesAsync();

        db.StallionDirectories.AddRange(
            new StallionDirectory { StudDirectoryId = stud.Id, Name = "Active", StallionId = 1, ArionId = 0, YearOfBirth = 2018, IsActive = true },
            new StallionDirectory { StudDirectoryId = stud.Id, Name = "Inactive", StallionId = 2, ArionId = 0, YearOfBirth = 2019, IsActive = false },
            new StallionDirectory { StudDirectoryId = otherStud.Id, Name = "Other", StallionId = 3, ArionId = 0, YearOfBirth = 2020, IsActive = true });
        await db.SaveChangesAsync();

        var repo = new StallionDirectoryRepository(db);
        var result = await repo.GetByStudDirectoryIdAsync(stud.Id, activeOnly: true);

        Assert.Single(result);
        Assert.Equal("Active", result[0].Name);
    }

    [Fact]
    public async Task GetAllAsync_FiltersByStudDirectoryId_WhenProvided()
    {
        var db = DbContextFactory.Create(nameof(GetAllAsync_FiltersByStudDirectoryId_WhenProvided));
        var stud = MakeStud();
        db.StudDirectories.Add(stud);
        await db.SaveChangesAsync();

        db.StallionDirectories.AddRange(
            new StallionDirectory { StudDirectoryId = stud.Id, Name = "S1", StallionId = 1, ArionId = 0, YearOfBirth = 2018, IsActive = true },
            new StallionDirectory { StudDirectoryId = Guid.NewGuid(), Name = "S2", StallionId = 2, ArionId = 0, YearOfBirth = 2019, IsActive = true });
        await db.SaveChangesAsync();

        var repo = new StallionDirectoryRepository(db);
        var result = await repo.GetAllAsync(studDirectoryId: stud.Id);

        Assert.Single(result);
        Assert.Equal("S1", result[0].Name);
    }

    [Fact]
    public async Task AddAsync_PersistsEntry()
    {
        var db = DbContextFactory.Create(nameof(AddAsync_PersistsEntry));
        var stud = MakeStud();
        db.StudDirectories.Add(stud);
        await db.SaveChangesAsync();

        var repo = new StallionDirectoryRepository(db);
        var entry = new StallionDirectory
        {
            StudDirectoryId = stud.Id,
            Name = "Stallion X",
            StallionId = 42,
            ArionId = 0,
            YearOfBirth = 2018
        };

        var added = await repo.AddAsync(entry);

        Assert.NotEqual(Guid.Empty, added.Id);
    }
}
