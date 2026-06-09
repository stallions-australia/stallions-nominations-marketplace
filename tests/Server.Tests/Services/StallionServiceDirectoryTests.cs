using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Stallions;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class StallionServiceDirectoryTests
{
    private readonly Mock<IStallionRepository> _stallionRepo = new();
    private readonly Mock<IStudFarmRepository> _farmRepo = new();
    private readonly Mock<IStallionDirectoryRepository> _directoryRepo = new();
    private readonly Mock<IUserService> _users = new();
    private readonly Mock<IBlobStorageService> _blobs = new();

    private StallionService CreateSut() =>
        new(_stallionRepo.Object, _farmRepo.Object, _directoryRepo.Object, _users.Object, _blobs.Object);

    // ── CreateAsync lockout ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ReturnsForbidden_WhenFarmIsDirectoryManaged()
    {
        var caller = new User { Id = Guid.NewGuid() };
        var farm = new StudFarm { Id = Guid.NewGuid(), UserId = caller.Id, StudDirectoryId = Guid.NewGuid() };
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepo.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);

        var result = await CreateSut().CreateAsync(new CreateStallionRequest { Name = "Flash" });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task CreateAsync_Succeeds_WhenFarmHasNoDirectoryId()
    {
        var caller = new User { Id = Guid.NewGuid() };
        var farm = new StudFarm { Id = Guid.NewGuid(), UserId = caller.Id, StudDirectoryId = null };
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepo.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        _stallionRepo.Setup(r => r.AddAsync(It.IsAny<Stallion>()))
            .ReturnsAsync((Stallion s) => s);

        var result = await CreateSut().CreateAsync(new CreateStallionRequest { Name = "Legacy" });

        result.Succeeded.Should().BeTrue();
    }

    // ── GetAuthorizedAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetAuthorizedAsync_ReturnsIsLinkedFalse_WhenFarmHasNoDirectoryId()
    {
        var caller = new User { Id = Guid.NewGuid() };
        var farm = new StudFarm { Id = Guid.NewGuid(), Name = "Oaklands", UserId = caller.Id, StudDirectoryId = null };
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepo.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);

        var result = await CreateSut().GetAuthorizedAsync();

        result.Succeeded.Should().BeTrue();
        result.Value!.IsLinked.Should().BeFalse();
        result.Value!.Available.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAuthorizedAsync_ExcludesAlreadyAddedStallions()
    {
        var caller = new User { Id = Guid.NewGuid() };
        var studDirId = Guid.NewGuid();
        var dirEntryId = Guid.NewGuid();
        var farm = new StudFarm { Id = Guid.NewGuid(), Name = "Coolmore", UserId = caller.Id, StudDirectoryId = studDirId };

        var dirEntries = new List<StallionDirectory>
        {
            new() { Id = dirEntryId, Name = "Already Added", StallionId = 1, ArionId = 0, YearOfBirth = 2018, StudDirectoryId = studDirId, StudDirectory = new StudDirectory { Name = "Coolmore" } },
            new() { Id = Guid.NewGuid(), Name = "Available", StallionId = 2, ArionId = 0, YearOfBirth = 2019, StudDirectoryId = studDirId, StudDirectory = new StudDirectory { Name = "Coolmore" } }
        };
        var existingStallions = new List<Stallion>
        {
            new() { Id = Guid.NewGuid(), StudFarmId = farm.Id, Name = "Already Added", StallionDirectoryId = dirEntryId, Images = new List<StallionImage>(), Listings = new List<Listing>() }
        };

        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepo.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        _directoryRepo.Setup(r => r.GetByStudDirectoryIdAsync(studDirId, true)).ReturnsAsync(dirEntries);
        _stallionRepo.Setup(r => r.GetByStudFarmIdAsync(farm.Id)).ReturnsAsync(existingStallions);

        var result = await CreateSut().GetAuthorizedAsync();

        result.Succeeded.Should().BeTrue();
        result.Value!.IsLinked.Should().BeTrue();
        result.Value!.Available.Should().HaveCount(1);
        result.Value!.Available[0].Name.Should().Be("Available");
    }

    // ── AddFromDirectoryAsync ────────────────────────────────────────────────

    [Fact]
    public async Task AddFromDirectoryAsync_ReturnsConflict_WhenAlreadyAdded()
    {
        var caller = new User { Id = Guid.NewGuid() };
        var studDirId = Guid.NewGuid();
        var dirEntryId = Guid.NewGuid();
        var farm = new StudFarm { Id = Guid.NewGuid(), Name = "X", UserId = caller.Id, StudDirectoryId = studDirId };
        var dirEntry = new StallionDirectory
        {
            Id = dirEntryId, StudDirectoryId = studDirId,
            Name = "Flash", StallionId = 1, ArionId = 0, YearOfBirth = 2018,
            StudDirectory = new StudDirectory { Name = "X" }
        };
        var existing = new List<Stallion>
        {
            new() { StudFarmId = farm.Id, Name = "Flash", StallionDirectoryId = dirEntryId, Images = new List<StallionImage>(), Listings = new List<Listing>() }
        };

        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepo.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        _directoryRepo.Setup(r => r.GetByIdAsync(dirEntryId)).ReturnsAsync(dirEntry);
        _stallionRepo.Setup(r => r.GetByStudFarmIdAsync(farm.Id)).ReturnsAsync(existing);

        var result = await CreateSut().AddFromDirectoryAsync(dirEntryId);

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(409);
    }

    [Fact]
    public async Task AddFromDirectoryAsync_ReturnsNotFound_WhenDirectoryEntryIsInactive()
    {
        var caller = new User { Id = Guid.NewGuid() };
        var studDirId = Guid.NewGuid();
        var farm = new StudFarm { Id = Guid.NewGuid(), Name = "X", UserId = caller.Id, StudDirectoryId = studDirId };
        var dirEntry = new StallionDirectory
        {
            Id = Guid.NewGuid(),
            StudDirectoryId = studDirId,
            Name = "Retired", StallionId = 1, ArionId = 0, YearOfBirth = 2010,
            IsActive = false,
            StudDirectory = new StudDirectory { Name = "X" }
        };

        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepo.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        _directoryRepo.Setup(r => r.GetByIdAsync(dirEntry.Id)).ReturnsAsync(dirEntry);
        _stallionRepo.Setup(r => r.GetByStudFarmIdAsync(farm.Id)).ReturnsAsync(new List<Stallion>());

        var result = await CreateSut().AddFromDirectoryAsync(dirEntry.Id);

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task AddFromDirectoryAsync_ReturnsForbidden_WhenDirectoryEntryBelongsToDifferentStud()
    {
        var caller = new User { Id = Guid.NewGuid() };
        var farm = new StudFarm { Id = Guid.NewGuid(), Name = "X", UserId = caller.Id, StudDirectoryId = Guid.NewGuid() };
        var dirEntry = new StallionDirectory
        {
            Id = Guid.NewGuid(),
            StudDirectoryId = Guid.NewGuid(), // different stud
            Name = "Flash", StallionId = 1, ArionId = 0, YearOfBirth = 2018,
            StudDirectory = new StudDirectory { Name = "Other Stud" }
        };

        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepo.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        _directoryRepo.Setup(r => r.GetByIdAsync(dirEntry.Id)).ReturnsAsync(dirEntry);
        _stallionRepo.Setup(r => r.GetByStudFarmIdAsync(farm.Id)).ReturnsAsync(new List<Stallion>());

        var result = await CreateSut().AddFromDirectoryAsync(dirEntry.Id);

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(403);
    }

    // ── UpdateAsync directory guard ──────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_WhenStallionIsDirectoryManaged_DoesNotOverrideCoreFields()
    {
        var caller = new User { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };
        var farm = new StudFarm { Id = Guid.NewGuid(), UserId = caller.Id };
        var directoryId = Guid.NewGuid();
        var stallion = new Stallion
        {
            Id = Guid.NewGuid(),
            StudFarmId = farm.Id,
            StallionDirectoryId = directoryId,
            Name = "OriginalName",
            YearOfBirth = 2018,
            Colour = "Bay",
            Sire = "OriginalSire",
            Dam = "OriginalDam"
        };

        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepo.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        _stallionRepo.Setup(r => r.GetByIdAsync(stallion.Id)).ReturnsAsync(stallion);

        var request = new UpdateStallionRequest
        {
            Name = "HackedName",
            YearOfBirth = 2000,
            Colour = "Grey",
            Sire = "HackedSire",
            Dam = "HackedDam"
        };

        var result = await CreateSut().UpdateAsync(stallion.Id, request);

        result.Succeeded.Should().BeTrue();
        stallion.Name.Should().Be("OriginalName");
        stallion.YearOfBirth.Should().Be(2018);
        stallion.Colour.Should().Be("Bay");
        stallion.Sire.Should().Be("OriginalSire");
        stallion.Dam.Should().Be("OriginalDam");
    }

    [Fact]
    public async Task UpdateAsync_WhenStallionIsNotDirectoryManaged_UpdatesCoreFields()
    {
        var caller = new User { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };
        var farm = new StudFarm { Id = Guid.NewGuid(), UserId = caller.Id };
        var stallion = new Stallion
        {
            Id = Guid.NewGuid(),
            StudFarmId = farm.Id,
            StallionDirectoryId = null, // not directory-managed
            Name = "OldName",
            YearOfBirth = 2018,
            Colour = "Bay"
        };

        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepo.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        _stallionRepo.Setup(r => r.GetByIdAsync(stallion.Id)).ReturnsAsync(stallion);

        var request = new UpdateStallionRequest
        {
            Name = "NewName",
            YearOfBirth = 2020,
            Colour = "Chestnut"
        };

        var result = await CreateSut().UpdateAsync(stallion.Id, request);

        result.Succeeded.Should().BeTrue();
        stallion.Name.Should().Be("NewName");
        stallion.YearOfBirth.Should().Be(2020);
        stallion.Colour.Should().Be("Chestnut");
    }
}
