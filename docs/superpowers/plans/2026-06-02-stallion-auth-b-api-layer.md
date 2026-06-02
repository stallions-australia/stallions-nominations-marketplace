# Stallion Authorization — Plan B: API Layer

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the service and controller layer: `DirectoryService` (Staff CRUD for both directory tables), extensions to `StallionService` (authorized list + one-click add + free-form lockout), extensions to `AdminService` (link farm to directory + get single farm), two new controllers, two updated controllers, and `Program.cs` DI registration.

**Architecture:** `DirectoryService` owns Staff CRUD for `StudDirectory` and `StallionDirectory`. `StallionService` gains two new methods and a guard on the existing `CreateAsync`. `AdminService` gains `LinkStudFarmToDirectoryAsync` and `GetStudFarmByIdAsync`. All new routes are keyed off the spec's table: `/api/stud-directory`, `/api/stallion-directory`, `/api/stallions/authorized`, `/api/stallions/add-from-directory/{id}`, `/api/admin/studfarms/{id}/link-directory`, `/api/admin/studfarms/{id}`.

**Tech Stack:** ASP.NET Core API, EF Core, Moq + xUnit for unit tests

**Depends on:** Plan A (entities, DTOs, repositories must all exist)

---

### Task 1: `DirectoryService` — Staff CRUD for both directory tables

**Files:**
- Create: `src/Server/Services/IDirectoryService.cs`
- Create: `src/Server/Services/DirectoryService.cs`
- Create: `tests/Server.Tests/Services/DirectoryServiceTests.cs`

- [ ] **Step 1: Write failing service tests**

```csharp
using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Directory;

namespace Stallions.Server.Tests.Services;

public class DirectoryServiceTests
{
    private readonly Mock<IStudDirectoryRepository> _studRepo = new();
    private readonly Mock<IStallionDirectoryRepository> _stallionRepo = new();

    private DirectoryService CreateSut() => new(_studRepo.Object, _stallionRepo.Object);

    [Fact]
    public async Task GetStudDirectoryAsync_ReturnsNotFound_WhenEntryMissing()
    {
        _studRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((StudDirectory?)null);

        var result = await CreateSut().GetStudDirectoryAsync(Guid.NewGuid());

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetStudDirectoryAsync_ReturnsMappedDto_WhenFound()
    {
        var id = Guid.NewGuid();
        var entry = new StudDirectory
        {
            Id = id,
            Name = "Coolmore",
            State = "NSW",
            IsActive = true,
            Stallions = new List<StallionDirectory>()
        };
        _studRepo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(entry);

        var result = await CreateSut().GetStudDirectoryAsync(id);

        result.Succeeded.Should().BeTrue();
        result.Value!.Name.Should().Be("Coolmore");
        result.Value!.State.Should().Be("NSW");
    }

    [Fact]
    public async Task CreateStudDirectoryAsync_ReturnsCreated_WithCorrectFields()
    {
        var request = new CreateStudDirectoryRequest
        {
            Name = "Darley",
            State = "QLD",
            ArionStudId = 42
        };
        _studRepo.Setup(r => r.AddAsync(It.IsAny<StudDirectory>()))
            .ReturnsAsync((StudDirectory s) => s);

        var result = await CreateSut().CreateStudDirectoryAsync(request);

        result.Succeeded.Should().BeTrue();
        result.Value!.Name.Should().Be("Darley");
        result.Value!.ArionStudId.Should().Be(42);
    }

    [Fact]
    public async Task UpdateStudDirectoryAsync_ReturnsNotFound_WhenEntryMissing()
    {
        _studRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((StudDirectory?)null);

        var result = await CreateSut().UpdateStudDirectoryAsync(Guid.NewGuid(), new UpdateStudDirectoryRequest { Name = "X" });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetStallionDirectoryAsync_ReturnsNotFound_WhenEntryMissing()
    {
        _stallionRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((StallionDirectory?)null);

        var result = await CreateSut().GetStallionDirectoryAsync(Guid.NewGuid());

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task CreateStallionDirectoryAsync_RequiresExistingStud()
    {
        var studId = Guid.NewGuid();
        _studRepo.Setup(r => r.GetByIdAsync(studId)).ReturnsAsync((StudDirectory?)null);

        var result = await CreateSut().CreateStallionDirectoryAsync(new CreateStallionDirectoryRequest
        {
            StudDirectoryId = studId,
            Name = "Frankel",
            YearOfBirth = 2008
        });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(404);
    }
}
```

- [ ] **Step 2: Run test — confirm compile error**

```
dotnet test tests/Server.Tests --filter "DirectoryServiceTests"
```

- [ ] **Step 3: Create `IDirectoryService.cs`**

```csharp
using Stallions.Shared.DTOs.Directory;

namespace Stallions.Server.Services;

public interface IDirectoryService
{
    // Stud Directory
    Task<ServiceResult<IReadOnlyList<StudDirectorySummaryDto>>> GetStudDirectoriesAsync(bool includeInactive = false);
    Task<ServiceResult<StudDirectoryDto>> GetStudDirectoryAsync(Guid id);
    Task<ServiceResult<StudDirectoryDto>> CreateStudDirectoryAsync(CreateStudDirectoryRequest request);
    Task<ServiceResult<StudDirectoryDto>> UpdateStudDirectoryAsync(Guid id, UpdateStudDirectoryRequest request);

    // Stallion Directory
    Task<ServiceResult<IReadOnlyList<StallionDirectorySummaryDto>>> GetStallionDirectoriesAsync(bool includeInactive = false, Guid? studDirectoryId = null);
    Task<ServiceResult<StallionDirectoryDto>> GetStallionDirectoryAsync(Guid id);
    Task<ServiceResult<StallionDirectoryDto>> CreateStallionDirectoryAsync(CreateStallionDirectoryRequest request);
    Task<ServiceResult<StallionDirectoryDto>> UpdateStallionDirectoryAsync(Guid id, UpdateStallionDirectoryRequest request);
}
```

- [ ] **Step 4: Create `DirectoryService.cs`**

```csharp
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.Directory;

namespace Stallions.Server.Services;

public class DirectoryService : IDirectoryService
{
    private readonly IStudDirectoryRepository _studRepo;
    private readonly IStallionDirectoryRepository _stallionRepo;

    public DirectoryService(
        IStudDirectoryRepository studRepo,
        IStallionDirectoryRepository stallionRepo)
    {
        _studRepo = studRepo;
        _stallionRepo = stallionRepo;
    }

    // ── Stud Directory ────────────────────────────────────────────────────────

    public async Task<ServiceResult<IReadOnlyList<StudDirectorySummaryDto>>> GetStudDirectoriesAsync(
        bool includeInactive = false)
    {
        var entries = await _studRepo.GetAllAsync(includeInactive);
        var dtos = entries.Select(MapToStudSummary).ToList();
        return ServiceResult<IReadOnlyList<StudDirectorySummaryDto>>.Ok(dtos);
    }

    public async Task<ServiceResult<StudDirectoryDto>> GetStudDirectoryAsync(Guid id)
    {
        var entry = await _studRepo.GetByIdAsync(id);
        if (entry == null)
            return ServiceResult<StudDirectoryDto>.NotFound("Stud directory entry not found.");
        return ServiceResult<StudDirectoryDto>.Ok(MapToStudDto(entry));
    }

    public async Task<ServiceResult<StudDirectoryDto>> CreateStudDirectoryAsync(
        CreateStudDirectoryRequest request)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrEmpty(name))
            return ServiceResult<StudDirectoryDto>.BadRequest("Name is required.");

        var entry = new StudDirectory
        {
            Name = name,
            ArionStudId = request.ArionStudId,
            Website = request.Website,
            Address = request.Address,
            Town = request.Town,
            State = request.State,
            Country = request.Country,
            Phone = request.Phone,
            Email = request.Email,
            LogoUrl = request.LogoUrl,
            IsActive = request.IsActive
        };

        var created = await _studRepo.AddAsync(entry);
        return ServiceResult<StudDirectoryDto>.Created(MapToStudDto(created));
    }

    public async Task<ServiceResult<StudDirectoryDto>> UpdateStudDirectoryAsync(
        Guid id, UpdateStudDirectoryRequest request)
    {
        var entry = await _studRepo.GetByIdAsync(id);
        if (entry == null)
            return ServiceResult<StudDirectoryDto>.NotFound("Stud directory entry not found.");

        var name = request.Name.Trim();
        if (string.IsNullOrEmpty(name))
            return ServiceResult<StudDirectoryDto>.BadRequest("Name is required.");

        entry.Name = name;
        entry.ArionStudId = request.ArionStudId;
        entry.Website = request.Website;
        entry.Address = request.Address;
        entry.Town = request.Town;
        entry.State = request.State;
        entry.Country = request.Country;
        entry.Phone = request.Phone;
        entry.Email = request.Email;
        entry.LogoUrl = request.LogoUrl;
        entry.IsActive = request.IsActive;

        await _studRepo.UpdateAsync(entry);
        return ServiceResult<StudDirectoryDto>.Ok(MapToStudDto(entry));
    }

    // ── Stallion Directory ───────────────────────────────────────────────────

    public async Task<ServiceResult<IReadOnlyList<StallionDirectorySummaryDto>>> GetStallionDirectoriesAsync(
        bool includeInactive = false, Guid? studDirectoryId = null)
    {
        var entries = await _stallionRepo.GetAllAsync(includeInactive, studDirectoryId);
        var dtos = entries.Select(MapToStallionSummary).ToList();
        return ServiceResult<IReadOnlyList<StallionDirectorySummaryDto>>.Ok(dtos);
    }

    public async Task<ServiceResult<StallionDirectoryDto>> GetStallionDirectoryAsync(Guid id)
    {
        var entry = await _stallionRepo.GetByIdAsync(id);
        if (entry == null)
            return ServiceResult<StallionDirectoryDto>.NotFound("Stallion directory entry not found.");
        return ServiceResult<StallionDirectoryDto>.Ok(MapToStallionDto(entry));
    }

    public async Task<ServiceResult<StallionDirectoryDto>> CreateStallionDirectoryAsync(
        CreateStallionDirectoryRequest request)
    {
        var stud = await _studRepo.GetByIdAsync(request.StudDirectoryId);
        if (stud == null)
            return ServiceResult<StallionDirectoryDto>.NotFound("Stud directory entry not found.");

        var name = request.Name.Trim();
        if (string.IsNullOrEmpty(name))
            return ServiceResult<StallionDirectoryDto>.BadRequest("Name is required.");

        var entry = new StallionDirectory
        {
            StudDirectoryId = request.StudDirectoryId,
            StallionId = request.StallionId,
            ArionId = request.ArionId,
            Name = name,
            YearOfBirth = request.YearOfBirth,
            Colour = request.Colour,
            Height = request.Height,
            SireName = request.SireName,
            DamName = request.DamName,
            IsActive = request.IsActive,
            StudDirectory = stud
        };

        var created = await _stallionRepo.AddAsync(entry);
        return ServiceResult<StallionDirectoryDto>.Created(MapToStallionDto(created));
    }

    public async Task<ServiceResult<StallionDirectoryDto>> UpdateStallionDirectoryAsync(
        Guid id, UpdateStallionDirectoryRequest request)
    {
        var entry = await _stallionRepo.GetByIdAsync(id);
        if (entry == null)
            return ServiceResult<StallionDirectoryDto>.NotFound("Stallion directory entry not found.");

        // Validate the target stud exists if it changed
        if (entry.StudDirectoryId != request.StudDirectoryId)
        {
            var stud = await _studRepo.GetByIdAsync(request.StudDirectoryId);
            if (stud == null)
                return ServiceResult<StallionDirectoryDto>.NotFound("Target stud directory entry not found.");
        }

        var name = request.Name.Trim();
        if (string.IsNullOrEmpty(name))
            return ServiceResult<StallionDirectoryDto>.BadRequest("Name is required.");

        entry.StudDirectoryId = request.StudDirectoryId;
        entry.StallionId = request.StallionId;
        entry.ArionId = request.ArionId;
        entry.Name = name;
        entry.YearOfBirth = request.YearOfBirth;
        entry.Colour = request.Colour;
        entry.Height = request.Height;
        entry.SireName = request.SireName;
        entry.DamName = request.DamName;
        entry.IsActive = request.IsActive;

        await _stallionRepo.UpdateAsync(entry);
        return ServiceResult<StallionDirectoryDto>.Ok(MapToStallionDto(entry));
    }

    // ── Mapping ──────────────────────────────────────────────────────────────

    private static StudDirectorySummaryDto MapToStudSummary(StudDirectory d) => new()
    {
        Id = d.Id,
        ArionStudId = d.ArionStudId,
        Name = d.Name,
        State = d.State,
        Website = d.Website,
        IsActive = d.IsActive,
        StallionCount = d.Stallions.Count
    };

    private static StudDirectoryDto MapToStudDto(StudDirectory d) => new()
    {
        Id = d.Id,
        ArionStudId = d.ArionStudId,
        Name = d.Name,
        Website = d.Website,
        Address = d.Address,
        Town = d.Town,
        State = d.State,
        Country = d.Country,
        Phone = d.Phone,
        Email = d.Email,
        LogoUrl = d.LogoUrl,
        IsActive = d.IsActive,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
        Stallions = d.Stallions.Select(MapToStallionSummary).ToList()
    };

    private static StallionDirectorySummaryDto MapToStallionSummary(StallionDirectory d) => new()
    {
        Id = d.Id,
        StallionId = d.StallionId,
        ArionId = d.ArionId,
        StudDirectoryId = d.StudDirectoryId,
        StudName = d.StudDirectory?.Name ?? string.Empty,
        Name = d.Name,
        YearOfBirth = d.YearOfBirth,
        Colour = d.Colour,
        IsActive = d.IsActive
    };

    private static StallionDirectoryDto MapToStallionDto(StallionDirectory d) => new()
    {
        Id = d.Id,
        StallionId = d.StallionId,
        ArionId = d.ArionId,
        StudDirectoryId = d.StudDirectoryId,
        StudName = d.StudDirectory?.Name ?? string.Empty,
        Name = d.Name,
        YearOfBirth = d.YearOfBirth,
        Colour = d.Colour,
        Height = d.Height,
        SireName = d.SireName,
        DamName = d.DamName,
        IsActive = d.IsActive,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt
    };
}
```

- [ ] **Step 5: Run tests**

```
dotnet test tests/Server.Tests --filter "DirectoryServiceTests"
```

Expected: 6 tests PASS.

- [ ] **Step 6: Commit**

```
git add src/Server/Services/IDirectoryService.cs src/Server/Services/DirectoryService.cs tests/Server.Tests/Services/DirectoryServiceTests.cs
git commit -m "feat: DirectoryService — Staff CRUD for StudDirectory and StallionDirectory"
```

---

### Task 2: `StallionService` extensions — authorized list, add-from-directory, free-form lockout

**Files:**
- Modify: `src/Server/Services/IStallionService.cs`
- Modify: `src/Server/Services/StallionService.cs`
- Create: `tests/Server.Tests/Services/StallionServiceDirectoryTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Stallions;

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
}
```

- [ ] **Step 2: Run — confirm compile error**

```
dotnet test tests/Server.Tests --filter "StallionServiceDirectoryTests"
```

- [ ] **Step 3: Update `IStallionService.cs` — add two new method signatures**

```csharp
using Microsoft.AspNetCore.Http;
using Stallions.Shared.DTOs.Directory;
using Stallions.Shared.DTOs.Stallions;

namespace Stallions.Server.Services;

public interface IStallionService
{
    Task<ServiceResult<IReadOnlyList<StallionSummaryDto>>> GetAllWithActiveListingsAsync();
    Task<ServiceResult<IReadOnlyList<StallionSummaryDto>>> GetByStudFarmAsync();
    Task<ServiceResult<StallionDto>> GetByIdAsync(Guid id, bool isStaff);
    Task<ServiceResult<StallionDto>> CreateAsync(CreateStallionRequest request);
    Task<ServiceResult<StallionDto>> UpdateAsync(Guid id, UpdateStallionRequest request);
    Task<ServiceResult<StallionDto>> UploadImageAsync(Guid stallionId, IFormFile file);
    Task<ServiceResult<StallionDto>> SetPrimaryImageAsync(Guid stallionId, Guid imageId);
    Task<ServiceResult> DeleteImageAsync(Guid stallionId, Guid imageId);
    // Directory-gated methods
    Task<ServiceResult<AuthorizedStallionsDto>> GetAuthorizedAsync();
    Task<ServiceResult<StallionDto>> AddFromDirectoryAsync(Guid directoryId);
}
```

- [ ] **Step 4: Update `StallionService.cs`**

**4a — Add `IStallionDirectoryRepository` dependency**

Change the constructor:

```csharp
    private readonly IStallionRepository _repo;
    private readonly IStudFarmRepository _farmRepo;
    private readonly IStallionDirectoryRepository _directoryRepo;
    private readonly IUserService _users;
    private readonly IBlobStorageService _blobs;

    public StallionService(
        IStallionRepository repo,
        IStudFarmRepository farmRepo,
        IStallionDirectoryRepository directoryRepo,
        IUserService users,
        IBlobStorageService blobs)
    {
        _repo = repo;
        _farmRepo = farmRepo;
        _directoryRepo = directoryRepo;
        _users = users;
        _blobs = blobs;
    }
```

**4b — Add lockout guard to `CreateAsync`**

After the `farm == null` check and before creating the stallion object, add:

```csharp
        // Directory-managed farms must use AddFromDirectoryAsync instead.
        if (farm.StudDirectoryId.HasValue)
            return ServiceResult<StallionDto>.Forbidden(
                "This farm is directory-managed. Use the 'Add from directory' flow to add stallions.");
```

**4c — Add `GetAuthorizedAsync`**

```csharp
    public async Task<ServiceResult<AuthorizedStallionsDto>> GetAuthorizedAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<AuthorizedStallionsDto>.Forbidden("Caller identity could not be resolved.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult<AuthorizedStallionsDto>.NotFound("No stud farm found for the current user.");

        if (!farm.StudDirectoryId.HasValue)
        {
            return ServiceResult<AuthorizedStallionsDto>.Ok(new AuthorizedStallionsDto
            {
                IsLinked = false,
                FarmName = farm.Name
            });
        }

        var dirEntries = await _directoryRepo.GetByStudDirectoryIdAsync(farm.StudDirectoryId.Value, activeOnly: true);
        var existingStallions = await _repo.GetByStudFarmIdAsync(farm.Id);

        var addedDirectoryIds = existingStallions
            .Where(s => s.StallionDirectoryId.HasValue)
            .Select(s => s.StallionDirectoryId!.Value)
            .ToHashSet();

        var available = dirEntries
            .Where(d => !addedDirectoryIds.Contains(d.Id))
            .Select(d => new StallionDirectorySummaryDto
            {
                Id = d.Id,
                StallionId = d.StallionId,
                ArionId = d.ArionId,
                StudDirectoryId = d.StudDirectoryId,
                StudName = d.StudDirectory?.Name ?? string.Empty,
                Name = d.Name,
                YearOfBirth = d.YearOfBirth,
                Colour = d.Colour,
                IsActive = d.IsActive
            })
            .ToList();

        return ServiceResult<AuthorizedStallionsDto>.Ok(new AuthorizedStallionsDto
        {
            IsLinked = true,
            FarmName = farm.Name,
            Available = available
        });
    }
```

**4d — Add `AddFromDirectoryAsync`**

```csharp
    public async Task<ServiceResult<StallionDto>> AddFromDirectoryAsync(Guid directoryId)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<StallionDto>.Forbidden("Caller identity could not be resolved.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult<StallionDto>.NotFound("No stud farm found for the current user.");

        if (!farm.StudDirectoryId.HasValue)
            return ServiceResult<StallionDto>.Forbidden("Your farm is not linked to the stallion directory.");

        var entry = await _directoryRepo.GetByIdAsync(directoryId);
        if (entry == null)
            return ServiceResult<StallionDto>.NotFound("Stallion directory entry not found.");

        // Security: the directory entry must belong to this farm's linked stud.
        if (entry.StudDirectoryId != farm.StudDirectoryId.Value)
            return ServiceResult<StallionDto>.Forbidden(
                "This stallion does not belong to your stud's directory.");

        // Prevent duplicate additions.
        var existingStallions = await _repo.GetByStudFarmIdAsync(farm.Id);
        var alreadyAdded = existingStallions.Any(s => s.StallionDirectoryId == directoryId);
        if (alreadyAdded)
            return ServiceResult<StallionDto>.Conflict("This stallion has already been added to your stable.");

        // Snapshot the directory data — decoupled from this point on.
        var stallion = new Stallion
        {
            StudFarmId = farm.Id,
            StallionDirectoryId = entry.Id,
            Name = entry.Name,
            YearOfBirth = entry.YearOfBirth,
            Colour = entry.Colour,
            Sire = entry.SireName,
            Dam = entry.DamName
        };

        var created = await _repo.AddAsync(stallion);
        return ServiceResult<StallionDto>.Created(MapToDto(created));
    }
```

**4e — Update `MapToDto` to include `StallionDirectoryId`**

```csharp
    private static StallionDto MapToDto(Stallion s) => new()
    {
        Id = s.Id,
        StudFarmId = s.StudFarmId,
        Name = s.Name,
        YearOfBirth = s.YearOfBirth,
        Colour = s.Colour,
        Sire = s.Sire,
        Dam = s.Dam,
        RegistrationNumber = s.RegistrationNumber,
        Description = s.Description,
        IsActive = s.IsActive,
        StallionDirectoryId = s.StallionDirectoryId,
        CreatedAt = s.CreatedAt,
        Images = s.Images.Select(img => new StallionImageDto
        {
            Id = img.Id,
            BlobPath = img.BlobPath,
            IsPrimary = img.IsPrimary,
            DisplayOrder = img.DisplayOrder
        }).ToList()
    };
```

Also add the missing using at the top of `StallionService.cs`:

```csharp
using Stallions.Shared.DTOs.Directory;
```

- [ ] **Step 5: Run tests**

```
dotnet test tests/Server.Tests --filter "StallionServiceDirectoryTests"
```

Expected: 5 tests PASS.

- [ ] **Step 6: Run existing StallionService tests to confirm no regressions**

```
dotnet test tests/Server.Tests --filter "StallionServiceTests"
```

Expected: All existing tests PASS.

- [ ] **Step 7: Commit**

```
git add src/Server/Services/IStallionService.cs src/Server/Services/StallionService.cs tests/Server.Tests/Services/StallionServiceDirectoryTests.cs
git commit -m "feat: StallionService — authorized list, add-from-directory, free-form lockout"
```

---

### Task 3: `AdminService` extensions — link farm, get farm by ID, update CreateStudFarm

**Files:**
- Modify: `src/Server/Services/IAdminService.cs`
- Modify: `src/Server/Services/AdminService.cs`
- Modify: `tests/Server.Tests/Services/AdminServiceTests.cs`

- [ ] **Step 1: Add failing tests to `AdminServiceTests.cs`**

Append these tests to the existing file (do not remove existing tests):

```csharp
    // ── LinkStudFarmToDirectoryAsync ─────────────────────────────────────────

    [Fact]
    public async Task LinkStudFarmToDirectoryAsync_ReturnsNotFound_WhenFarmMissing()
    {
        // AdminServiceTests already has field declarations — add these tests in the class body
        _studFarmRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((StudFarm?)null);

        var result = await CreateSut().LinkStudFarmToDirectoryAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.HttpStatusCode);
    }

    [Fact]
    public async Task LinkStudFarmToDirectoryAsync_SetsStudDirectoryId_AndAudits()
    {
        var farm = new StudFarm { Id = Guid.NewGuid(), Name = "Oak", StudDirectoryId = null };
        var studDirId = Guid.NewGuid();
        _studFarmRepoMock.Setup(r => r.GetByIdAsync(farm.Id)).ReturnsAsync(farm);
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(new User { Id = Guid.NewGuid() });

        var result = await CreateSut().LinkStudFarmToDirectoryAsync(farm.Id, studDirId);

        Assert.True(result.Succeeded);
        Assert.Equal(studDirId, farm.StudDirectoryId);
        _studFarmRepoMock.Verify(r => r.UpdateAsync(farm), Times.Once);
    }

    // ── CreateStudFarm with StudDirectoryId ──────────────────────────────────

    [Fact]
    public async Task CreateStudFarmAsync_SetsStudDirectoryId_WhenProvided()
    {
        var userId = Guid.NewGuid();
        var studDirId = Guid.NewGuid();
        var user = new User { Id = userId, Role = UserRole.StudFarmAdmin };
        _userRepoMock.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(user);
        _studFarmRepoMock.Setup(r => r.GetByUserIdAsync(userId)).ReturnsAsync((StudFarm?)null);
        _studFarmRepoMock.Setup(r => r.AddAsync(It.IsAny<StudFarm>()))
            .ReturnsAsync((StudFarm f) => f);
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(new User { Id = Guid.NewGuid() });

        var request = new CreateStudFarmRequest
        {
            UserId = userId,
            Name = "New Farm",
            StudDirectoryId = studDirId
        };

        var result = await CreateSut().CreateStudFarmAsync(request);

        Assert.True(result.Succeeded);
        _studFarmRepoMock.Verify(
            r => r.AddAsync(It.Is<StudFarm>(f => f.StudDirectoryId == studDirId)),
            Times.Once);
    }
```

**Note:** `AdminServiceTests` already declares `_studFarmRepoMock` and `_usersMock` and `_userRepoMock`. If the test file uses a different field name pattern, adjust accordingly by reading the existing test file.

- [ ] **Step 2: Run — confirm new tests fail**

```
dotnet test tests/Server.Tests --filter "AdminServiceTests"
```

- [ ] **Step 3: Update `IAdminService.cs` — add two signatures**

Add to the interface (alongside existing methods):

```csharp
    Task<ServiceResult<StudFarmSummaryDto>> GetStudFarmByIdAsync(Guid id);
    Task<ServiceResult> LinkStudFarmToDirectoryAsync(Guid farmId, Guid studDirectoryId);
```

Full updated interface (keep all existing methods):

```csharp
using Stallions.Shared.DTOs.Admin;

namespace Stallions.Server.Services;

public interface IAdminService
{
    Task<ServiceResult<DashboardDto>> GetDashboardAsync();
    Task<ServiceResult<IReadOnlyList<TransactionDto>>> GetTransactionsAsync();
    Task<ServiceResult<IReadOnlyList<InvoiceDto>>> GetInvoicesAsync();
    Task<ServiceResult> SetListingFeeAsync(Guid listingId, SetListingFeeRequest request);
    Task<ServiceResult<IReadOnlyList<StudFarmSummaryDto>>> GetAllStudFarmsAsync();
    Task<ServiceResult<StudFarmSummaryDto>> GetStudFarmByIdAsync(Guid id);
    Task<ServiceResult<StudFarmSummaryDto>> CreateStudFarmAsync(CreateStudFarmRequest request);
    Task<ServiceResult> LinkStudFarmToDirectoryAsync(Guid farmId, Guid studDirectoryId);
    Task<ServiceResult<IReadOnlyList<ListingStaffSummaryDto>>> GetAllListingsStaffAsync();
    Task<ServiceResult> ForceListingStatusAsync(Guid listingId, ForceListingStatusRequest request);
    Task<ServiceResult> SetUserRoleAsync(Guid userId, SetUserRoleRequest request);
}
```

- [ ] **Step 4: Implement `GetStudFarmByIdAsync` in `AdminService.cs`**

Add after `GetAllStudFarmsAsync`:

```csharp
    public async Task<ServiceResult<StudFarmSummaryDto>> GetStudFarmByIdAsync(Guid id)
    {
        var farm = await _studFarmRepo.GetByIdAsync(id);
        if (farm == null)
            return ServiceResult<StudFarmSummaryDto>.NotFound("Stud farm not found.");

        // Load the user manually (GetByIdAsync doesn't Include User)
        var user = await _userRepo.GetByIdAsync(farm.UserId);

        var dto = new StudFarmSummaryDto
        {
            Id = farm.Id,
            Name = farm.Name,
            ABN = farm.ABN,
            ContactEmail = farm.ContactEmail,
            LinkedUserDisplayName = user?.DisplayName ?? string.Empty,
            LinkedUserEmail = user?.Email ?? string.Empty,
            IsActive = farm.IsActive,
            StudDirectoryId = farm.StudDirectoryId,
            CreatedAt = farm.CreatedAt
        };
        return ServiceResult<StudFarmSummaryDto>.Ok(dto);
    }
```

**Note:** `GetByIdAsync` on `StudFarmRepository` calls `_db.StudFarms.FindAsync(id)` which doesn't include the `StudDirectory` navigation. That's fine — for this method we only need the `StudDirectoryId` Guid, which is a column (not a navigation).

- [ ] **Step 5: Implement `LinkStudFarmToDirectoryAsync` in `AdminService.cs`**

Add after `GetStudFarmByIdAsync`:

```csharp
    public async Task<ServiceResult> LinkStudFarmToDirectoryAsync(Guid farmId, Guid studDirectoryId)
    {
        var farm = await _studFarmRepo.GetByIdAsync(farmId);
        if (farm == null) return ServiceResult.NotFound("Stud farm not found.");

        var caller = await _users.GetOrCreateCurrentUserAsync();
        var previous = farm.StudDirectoryId?.ToString() ?? "none";
        farm.StudDirectoryId = studDirectoryId;
        await _studFarmRepo.UpdateAsync(farm);

        await _auditRepo.LogAsync(
            "StudFarm",
            farmId,
            "LinkStudDirectory",
            caller?.Id,
            $"StudDirectoryId changed from {previous} to {studDirectoryId}");

        return ServiceResult.Ok();
    }
```

- [ ] **Step 6: Update `CreateStudFarmAsync` — set `StudDirectoryId` from request**

In the existing `CreateStudFarmAsync` method, update the `StudFarm` initializer to include the new field:

```csharp
        var farm = new StudFarm
        {
            UserId = request.UserId,
            Name = request.Name,
            ABN = request.ABN,
            ContactPhone = request.ContactPhone,
            ContactEmail = request.ContactEmail,
            Address = request.Address,
            StudDirectoryId = request.StudDirectoryId   // null if not provided
        };
```

Also update the returned DTO mapping in `CreateStudFarmAsync` to include `StudDirectoryId`:

```csharp
        var dto = new StudFarmSummaryDto
        {
            Id = farm.Id,
            Name = farm.Name,
            ABN = farm.ABN,
            ContactEmail = farm.ContactEmail,
            LinkedUserDisplayName = user.DisplayName,
            LinkedUserEmail = user.Email,
            IsActive = farm.IsActive,
            StudDirectoryId = farm.StudDirectoryId,
            CreatedAt = farm.CreatedAt
        };
```

And update the `GetAllStudFarmsAsync` mapping too so it includes `StudDirectoryId` and `StudDirectoryName`:

```csharp
        var dtos = farms.Select(f => new StudFarmSummaryDto
        {
            Id = f.Id,
            Name = f.Name,
            ABN = f.ABN,
            ContactEmail = f.ContactEmail,
            LinkedUserDisplayName = f.User?.DisplayName ?? string.Empty,
            LinkedUserEmail = f.User?.Email ?? string.Empty,
            IsActive = f.IsActive,
            StudDirectoryId = f.StudDirectoryId,
            StudDirectoryName = f.StudDirectory?.Name,
            CreatedAt = f.CreatedAt
        }).ToList();
```

**Note:** `GetAllAsync` on `StudFarmRepository` already does `Include(f => f.User)` but does NOT include `StudDirectory`. Update `StudFarmRepository.GetAllAsync` to also include it:

```csharp
    public async Task<IReadOnlyList<StudFarm>> GetAllAsync() =>
        await _db.StudFarms
            .Include(f => f.User)
            .Include(f => f.StudDirectory)
            .OrderBy(f => f.Name)
            .ToListAsync();
```

- [ ] **Step 7: Run tests**

```
dotnet test tests/Server.Tests --filter "AdminServiceTests"
```

Expected: All tests including the new ones PASS.

- [ ] **Step 8: Commit**

```
git add src/Server/Services/IAdminService.cs src/Server/Services/AdminService.cs src/Server/Data/Repositories/StudFarmRepository.cs tests/Server.Tests/Services/AdminServiceTests.cs
git commit -m "feat: AdminService — LinkStudFarmToDirectory, GetStudFarmById, update CreateStudFarm"
```

---

### Task 4: New controllers — `StudDirectoryController` and `StallionDirectoryController`

**Files:**
- Create: `src/Server/Controllers/StudDirectoryController.cs`
- Create: `src/Server/Controllers/StallionDirectoryController.cs`

- [ ] **Step 1: Create `StudDirectoryController.cs`**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Directory;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/stud-directory")]
[Authorize(Policy = "StaffOnly")]
public class StudDirectoryController : ControllerBase
{
    private readonly IDirectoryService _directory;
    public StudDirectoryController(IDirectoryService directory) => _directory = directory;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool includeInactive = false)
    {
        var r = await _directory.GetStudDirectoriesAsync(includeInactive);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var r = await _directory.GetStudDirectoryAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStudDirectoryRequest request)
    {
        var r = await _directory.CreateStudDirectoryAsync(request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStudDirectoryRequest request)
    {
        var r = await _directory.UpdateStudDirectoryAsync(id, request);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
}
```

- [ ] **Step 2: Create `StallionDirectoryController.cs`**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Directory;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/stallion-directory")]
[Authorize(Policy = "StaffOnly")]
public class StallionDirectoryController : ControllerBase
{
    private readonly IDirectoryService _directory;
    public StallionDirectoryController(IDirectoryService directory) => _directory = directory;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool includeInactive = false,
        [FromQuery] Guid? studDirectoryId = null)
    {
        var r = await _directory.GetStallionDirectoriesAsync(includeInactive, studDirectoryId);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var r = await _directory.GetStallionDirectoryAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStallionDirectoryRequest request)
    {
        var r = await _directory.CreateStallionDirectoryAsync(request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStallionDirectoryRequest request)
    {
        var r = await _directory.UpdateStallionDirectoryAsync(id, request);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
}
```

- [ ] **Step 3: Build**

```
dotnet build src/Server
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add src/Server/Controllers/StudDirectoryController.cs src/Server/Controllers/StallionDirectoryController.cs
git commit -m "feat: StudDirectoryController and StallionDirectoryController"
```

---

### Task 5: Update `StallionsController` and `AdminController`

**Files:**
- Modify: `src/Server/Controllers/StallionsController.cs`
- Modify: `src/Server/Controllers/AdminController.cs`

- [ ] **Step 1: Add two endpoints to `StallionsController.cs`**

Append to the class body (before the closing `}`):

```csharp
    [HttpGet("authorized")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> GetAuthorized()
    {
        var r = await _stallions.GetAuthorizedAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("add-from-directory/{directoryId:guid}")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> AddFromDirectory(Guid directoryId)
    {
        var r = await _stallions.AddFromDirectoryAsync(directoryId);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
```

- [ ] **Step 2: Add two endpoints to `AdminController.cs`**

Append to the class body:

```csharp
    [HttpGet("studfarms/{id:guid}")]
    public async Task<IActionResult> GetStudFarm(Guid id)
    {
        var r = await _admin.GetStudFarmByIdAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPut("studfarms/{id:guid}/link-directory")]
    public async Task<IActionResult> LinkStudFarmToDirectory(Guid id, [FromBody] LinkStudDirectoryRequest request)
    {
        var r = await _admin.LinkStudFarmToDirectoryAsync(id, request.StudDirectoryId);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }
```

Add the using for `LinkStudDirectoryRequest` at the top of `AdminController.cs`:

```csharp
using Stallions.Shared.DTOs.Admin;
```

- [ ] **Step 3: Build**

```
dotnet build src/Server
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add src/Server/Controllers/StallionsController.cs src/Server/Controllers/AdminController.cs
git commit -m "feat: add authorized-stallions and link-directory endpoints"
```

---

### Task 6: `Program.cs` DI registration

**Files:**
- Modify: `src/Server/Program.cs`

- [ ] **Step 1: Register new repositories and service**

In `Program.cs`, in the `// Repositories` block, add after the existing repository registrations:

```csharp
builder.Services.AddScoped<IStudDirectoryRepository, StudDirectoryRepository>();
builder.Services.AddScoped<IStallionDirectoryRepository, StallionDirectoryRepository>();
```

In the `// Services` block, add after `IAdminService`:

```csharp
builder.Services.AddScoped<IDirectoryService, DirectoryService>();
```

- [ ] **Step 2: Build**

```
dotnet build src/Server
```

Expected: 0 errors.

- [ ] **Step 3: Run full test suite**

```
dotnet test
```

Expected: All tests pass.

- [ ] **Step 4: Start the server locally and verify endpoints exist**

```
dotnet run --project src/Server
```

Then test with curl or a REST client:

```bash
# Should return 401 (not 404) — proves routes are wired
curl -i https://localhost:5001/api/stud-directory
curl -i https://localhost:5001/api/stallion-directory
curl -i https://localhost:5001/api/stallions/authorized
```

Expected: `401 Unauthorized` for all three (not 404).

- [ ] **Step 5: Commit**

```
git add src/Server/Program.cs
git commit -m "feat: register DirectoryService and directory repositories in DI — Plan B complete"
```

---

## ✅ Plan B Complete

All server-side logic is in place. The API layer now has working endpoints for Staff directory CRUD and stud admin authorized-stallion access. Proceed to **Plan C (Client UI)** to build the Blazor pages on top of these APIs.
