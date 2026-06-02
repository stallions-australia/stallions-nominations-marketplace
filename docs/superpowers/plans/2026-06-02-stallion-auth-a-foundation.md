# Stallion Authorization — Plan A: Foundation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `StudDirectory` and `StallionDirectory` entities to the DB, attach FK columns to `StudFarm` and `Stallion`, generate the EF migration, create all shared DTOs, and stand up the four new repositories.

**Architecture:** Two new reference tables are added to the existing Azure SQL DB. `StudFarm.StudDirectoryId` and `Stallion.StallionDirectoryId` are nullable FKs; they remain null for legacy records. All four new repository interfaces follow the existing `IStudFarmRepository` pattern.

**Tech Stack:** EF Core, Azure SQL, ASP.NET Core (no UI in this plan — purely data layer)

**Depends on:** Nothing — this is the base layer. Plans B and C depend on this.

---

### Task 1: New entity classes — `StudDirectory` and `StallionDirectory`

**Files:**
- Create: `src/Server/Data/Entities/StudDirectory.cs`
- Create: `src/Server/Data/Entities/StallionDirectory.cs`

- [ ] **Step 1: Create `StudDirectory.cs`**

```csharp
namespace Stallions.Server.Data.Entities;

public class StudDirectory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int? ArionStudId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Website { get; set; }
    public string? Address { get; set; }
    public string? Town { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<StallionDirectory> Stallions { get; set; } = new List<StallionDirectory>();
}
```

- [ ] **Step 2: Create `StallionDirectory.cs`**

```csharp
namespace Stallions.Server.Data.Entities;

public class StallionDirectory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int StallionId { get; set; }           // original stallionID from external DB
    public int ArionId { get; set; }              // original arionID; 0 when not applicable
    public Guid StudDirectoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int YearOfBirth { get; set; }
    public string? Colour { get; set; }
    public string? Height { get; set; }
    public string? SireName { get; set; }
    public string? DamName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public StudDirectory StudDirectory { get; set; } = null!;
}
```

- [ ] **Step 3: Build to verify no compile errors**

```
dotnet build src/Server
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```
git add src/Server/Data/Entities/StudDirectory.cs src/Server/Data/Entities/StallionDirectory.cs
git commit -m "feat: add StudDirectory and StallionDirectory entities"
```

---

### Task 2: Add FK columns to `StudFarm` and `Stallion`

**Files:**
- Modify: `src/Server/Data/Entities/StudFarm.cs`
- Modify: `src/Server/Data/Entities/Stallion.cs`

- [ ] **Step 1: Update `StudFarm.cs` — add nullable FK and navigation**

Add two lines to the existing `StudFarm` class — a nullable FK column and its navigation property. Place them after the existing `IsActive` property:

```csharp
    public bool IsActive { get; set; } = true;
    public Guid? StudDirectoryId { get; set; }   // null until Staff assigns it
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User User { get; set; } = null!;
    public StudDirectory? StudDirectory { get; set; }
    public ICollection<Stallion> Stallions { get; set; } = new List<Stallion>();
    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
```

The complete updated file:

```csharp
namespace Stallions.Server.Data.Entities;

public class StudFarm
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ABN { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? StudDirectoryId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User User { get; set; } = null!;
    public StudDirectory? StudDirectory { get; set; }
    public ICollection<Stallion> Stallions { get; set; } = new List<Stallion>();
    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
}
```

- [ ] **Step 2: Update `Stallion.cs` — add nullable FK and navigation**

```csharp
namespace Stallions.Server.Data.Entities;

public class Stallion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StudFarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? YearOfBirth { get; set; }
    public string? Colour { get; set; }
    public string? Sire { get; set; }
    public string? Dam { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? StallionDirectoryId { get; set; }   // set when created from directory
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public StudFarm StudFarm { get; set; } = null!;
    public StallionDirectory? StallionDirectory { get; set; }
    public ICollection<StallionImage> Images { get; set; } = new List<StallionImage>();
    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
}
```

- [ ] **Step 3: Build**

```
dotnet build src/Server
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add src/Server/Data/Entities/StudFarm.cs src/Server/Data/Entities/Stallion.cs
git commit -m "feat: add StudDirectoryId FK to StudFarm, StallionDirectoryId FK to Stallion"
```

---

### Task 3: Update `AppDbContext` + generate EF migration

**Files:**
- Modify: `src/Server/Data/AppDbContext.cs`

- [ ] **Step 1: Add DbSets for new entities**

At the top of the DbSets block (after `AuditLogs`), add:

```csharp
    public DbSet<StudDirectory> StudDirectories => Set<StudDirectory>();
    public DbSet<StallionDirectory> StallionDirectories => Set<StallionDirectory>();
```

- [ ] **Step 2: Add model configuration for `StudDirectory`**

In `OnModelCreating`, after the `// ── AuditLog` section, add:

```csharp
        // ── StudDirectory ────────────────────────────────────────────────────
        modelBuilder.Entity<StudDirectory>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Name).HasMaxLength(200).IsRequired();
            e.Property(d => d.Website).HasMaxLength(500);
            e.Property(d => d.Address).HasMaxLength(500);
            e.Property(d => d.Town).HasMaxLength(200);
            e.Property(d => d.State).HasMaxLength(100);
            e.Property(d => d.Country).HasMaxLength(100);
            e.Property(d => d.Phone).HasMaxLength(50);
            e.Property(d => d.Email).HasMaxLength(256);
            e.Property(d => d.LogoUrl).HasMaxLength(1000);
        });

        // ── StallionDirectory ────────────────────────────────────────────────
        modelBuilder.Entity<StallionDirectory>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Name).HasMaxLength(200).IsRequired();
            e.Property(d => d.Colour).HasMaxLength(50);
            e.Property(d => d.Height).HasMaxLength(20);
            e.Property(d => d.SireName).HasMaxLength(200);
            e.Property(d => d.DamName).HasMaxLength(200);

            e.HasOne(d => d.StudDirectory)
                .WithMany(s => s.Stallions)
                .HasForeignKey(d => d.StudDirectoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });
```

- [ ] **Step 3: Add FK config for `StudFarm.StudDirectoryId`**

Inside the existing `modelBuilder.Entity<StudFarm>(e => { ... })` block, add after the `e.HasOne(f => f.User)` relationship:

```csharp
            e.HasOne(f => f.StudDirectory)
                .WithMany()
                .HasForeignKey(f => f.StudDirectoryId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.NoAction);
```

- [ ] **Step 4: Add FK config for `Stallion.StallionDirectoryId`**

Inside the existing `modelBuilder.Entity<Stallion>(e => { ... })` block, add after the `e.HasOne(s => s.StudFarm)` relationship:

```csharp
            e.HasOne(s => s.StallionDirectory)
                .WithMany()
                .HasForeignKey(s => s.StallionDirectoryId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.NoAction);
```

- [ ] **Step 5: Build to verify DbContext compiles**

```
dotnet build src/Server
```

Expected: 0 errors.

- [ ] **Step 6: Generate EF Core migration**

```
dotnet ef migrations add StallionAuthorization --project src/Server --startup-project src/Server
```

Expected: output ending in `Done.` A new file appears under `src/Server/Data/Migrations/` (e.g. `20260602xxxxxx_StallionAuthorization.cs`).

- [ ] **Step 7: Verify the migration looks correct**

Open the generated migration file. Confirm it contains:
- `CreateTable("StudDirectories", ...)` — all 13 columns including `ArionStudId` (nullable int)
- `CreateTable("StallionDirectories", ...)` — all 13 columns including `StallionId` and `ArionId` (int, not null)
- `AddColumn<Guid>("StudFarms", "StudDirectoryId", nullable: true)`
- `AddColumn<Guid>("Stallions", "StallionDirectoryId", nullable: true)`
- Two `AddForeignKey` calls for the new nullable FKs

If anything is wrong (extra or missing columns), delete the migration file and fix `AppDbContext` before regenerating.

- [ ] **Step 8: Commit**

```
git add src/Server/Data/AppDbContext.cs src/Server/Data/Migrations/
git commit -m "feat: AppDbContext + EF migration for StallionAuthorization"
```

---

### Task 4: Shared DTOs

**Files:**
- Create: `src/Shared/DTOs/Directory/StudDirectorySummaryDto.cs`
- Create: `src/Shared/DTOs/Directory/StudDirectoryDto.cs`
- Create: `src/Shared/DTOs/Directory/CreateStudDirectoryRequest.cs`
- Create: `src/Shared/DTOs/Directory/UpdateStudDirectoryRequest.cs`
- Create: `src/Shared/DTOs/Directory/StallionDirectorySummaryDto.cs`
- Create: `src/Shared/DTOs/Directory/StallionDirectoryDto.cs`
- Create: `src/Shared/DTOs/Directory/CreateStallionDirectoryRequest.cs`
- Create: `src/Shared/DTOs/Directory/UpdateStallionDirectoryRequest.cs`
- Create: `src/Shared/DTOs/Directory/AuthorizedStallionsDto.cs`
- Create: `src/Shared/DTOs/Admin/LinkStudDirectoryRequest.cs`
- Modify: `src/Shared/DTOs/Stallions/StallionDto.cs`
- Modify: `src/Shared/DTOs/Admin/StudFarmSummaryDto.cs`
- Modify: `src/Shared/DTOs/Admin/CreateStudFarmRequest.cs`

- [ ] **Step 1: Write failing DTO shape test**

Create `tests/Server.Tests/Shared/DirectoryDtoTests.cs`:

```csharp
using Stallions.Shared.DTOs.Directory;

namespace Stallions.Server.Tests.Shared;

public class DirectoryDtoTests
{
    [Fact]
    public void StudDirectorySummaryDto_DefaultsAreCorrect()
    {
        var dto = new StudDirectorySummaryDto();
        Assert.Equal(string.Empty, dto.Name);
        Assert.True(dto.IsActive);
        Assert.Equal(0, dto.StallionCount);
    }

    [Fact]
    public void AuthorizedStallionsDto_DefaultIsNotLinked()
    {
        var dto = new AuthorizedStallionsDto();
        Assert.False(dto.IsLinked);
        Assert.Empty(dto.Available);
    }
}
```

- [ ] **Step 2: Run test — confirm it fails (types not found)**

```
dotnet test tests/Server.Tests --filter "DirectoryDtoTests"
```

Expected: compile error — `DirectoryDtoTests` references missing types.

- [ ] **Step 3: Create `StudDirectorySummaryDto.cs`**

```csharp
namespace Stallions.Shared.DTOs.Directory;

public class StudDirectorySummaryDto
{
    public Guid Id { get; set; }
    public int? ArionStudId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? State { get; set; }
    public string? Website { get; set; }
    public bool IsActive { get; set; } = true;
    public int StallionCount { get; set; }
}
```

- [ ] **Step 4: Create `StudDirectoryDto.cs`**

```csharp
namespace Stallions.Shared.DTOs.Directory;

public class StudDirectoryDto
{
    public Guid Id { get; set; }
    public int? ArionStudId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Website { get; set; }
    public string? Address { get; set; }
    public string? Town { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<StallionDirectorySummaryDto> Stallions { get; set; } = new();
}
```

- [ ] **Step 5: Create `CreateStudDirectoryRequest.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Stallions.Shared.DTOs.Directory;

public class CreateStudDirectoryRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;
    public int? ArionStudId { get; set; }
    public string? Website { get; set; }
    public string? Address { get; set; }
    public string? Town { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; } = true;
}
```

- [ ] **Step 6: Create `UpdateStudDirectoryRequest.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Stallions.Shared.DTOs.Directory;

public class UpdateStudDirectoryRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;
    public int? ArionStudId { get; set; }
    public string? Website { get; set; }
    public string? Address { get; set; }
    public string? Town { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; } = true;
}
```

- [ ] **Step 7: Create `StallionDirectorySummaryDto.cs`**

```csharp
namespace Stallions.Shared.DTOs.Directory;

public class StallionDirectorySummaryDto
{
    public Guid Id { get; set; }
    public int StallionId { get; set; }
    public int ArionId { get; set; }
    public Guid StudDirectoryId { get; set; }
    public string StudName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int YearOfBirth { get; set; }
    public string? Colour { get; set; }
    public bool IsActive { get; set; }
}
```

- [ ] **Step 8: Create `StallionDirectoryDto.cs`**

```csharp
namespace Stallions.Shared.DTOs.Directory;

public class StallionDirectoryDto
{
    public Guid Id { get; set; }
    public int StallionId { get; set; }
    public int ArionId { get; set; }
    public Guid StudDirectoryId { get; set; }
    public string StudName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int YearOfBirth { get; set; }
    public string? Colour { get; set; }
    public string? Height { get; set; }
    public string? SireName { get; set; }
    public string? DamName { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

- [ ] **Step 9: Create `CreateStallionDirectoryRequest.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Stallions.Shared.DTOs.Directory;

public class CreateStallionDirectoryRequest
{
    public Guid StudDirectoryId { get; set; }
    public int StallionId { get; set; }
    public int ArionId { get; set; }
    [Required]
    public string Name { get; set; } = string.Empty;
    public int YearOfBirth { get; set; }
    public string? Colour { get; set; }
    public string? Height { get; set; }
    public string? SireName { get; set; }
    public string? DamName { get; set; }
    public bool IsActive { get; set; } = true;
}
```

- [ ] **Step 10: Create `UpdateStallionDirectoryRequest.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Stallions.Shared.DTOs.Directory;

public class UpdateStallionDirectoryRequest
{
    public Guid StudDirectoryId { get; set; }
    public int StallionId { get; set; }
    public int ArionId { get; set; }
    [Required]
    public string Name { get; set; } = string.Empty;
    public int YearOfBirth { get; set; }
    public string? Colour { get; set; }
    public string? Height { get; set; }
    public string? SireName { get; set; }
    public string? DamName { get; set; }
    public bool IsActive { get; set; } = true;
}
```

- [ ] **Step 11: Create `AuthorizedStallionsDto.cs`**

```csharp
namespace Stallions.Shared.DTOs.Directory;

/// <summary>
/// Returned by GET /api/stallions/authorized.
/// IsLinked = false means the farm has no StudDirectoryId set; the UI shows an unlinked notice.
/// Available = directory entries that haven't yet been added to the farm's stable.
/// </summary>
public class AuthorizedStallionsDto
{
    public bool IsLinked { get; set; }
    public string FarmName { get; set; } = string.Empty;
    public List<StallionDirectorySummaryDto> Available { get; set; } = new();
}
```

- [ ] **Step 12: Create `LinkStudDirectoryRequest.cs`**

```csharp
namespace Stallions.Shared.DTOs.Admin;

public class LinkStudDirectoryRequest
{
    public Guid StudDirectoryId { get; set; }
}
```

- [ ] **Step 13: Update `StallionDto.cs` — add `StallionDirectoryId` and `IsDirectoryManaged`**

Replace the file contents:

```csharp
namespace Stallions.Shared.DTOs.Stallions;

public class StallionDto
{
    public Guid Id { get; set; }
    public Guid StudFarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? YearOfBirth { get; set; }
    public string? Colour { get; set; }
    public string? Sire { get; set; }
    public string? Dam { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public Guid? StallionDirectoryId { get; set; }
    public bool IsDirectoryManaged => StallionDirectoryId.HasValue;
    public DateTime CreatedAt { get; set; }
    public List<StallionImageDto> Images { get; set; } = new();
}
```

- [ ] **Step 14: Update `StudFarmSummaryDto.cs` — add directory fields**

```csharp
namespace Stallions.Shared.DTOs.Admin;

public class StudFarmSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ABN { get; set; }
    public string? ContactEmail { get; set; }
    public string LinkedUserDisplayName { get; set; } = string.Empty;
    public string LinkedUserEmail { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public Guid? StudDirectoryId { get; set; }
    public string? StudDirectoryName { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 15: Update `CreateStudFarmRequest.cs` — add optional `StudDirectoryId`**

```csharp
namespace Stallions.Shared.DTOs.Admin;

public class CreateStudFarmRequest
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ABN { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string? Address { get; set; }
    public Guid? StudDirectoryId { get; set; }
}
```

- [ ] **Step 16: Run tests**

```
dotnet test tests/Server.Tests --filter "DirectoryDtoTests"
```

Expected: 2 tests PASS.

- [ ] **Step 17: Build full solution**

```
dotnet build
```

Expected: 0 errors.

- [ ] **Step 18: Commit**

```
git add src/Shared/DTOs/ tests/Server.Tests/Shared/DirectoryDtoTests.cs
git commit -m "feat: shared DTOs for stallion authorization feature"
```

---

### Task 5: `StudDirectory` repository

**Files:**
- Create: `src/Server/Data/Repositories/IStudDirectoryRepository.cs`
- Create: `src/Server/Data/Repositories/StudDirectoryRepository.cs`
- Create: `tests/Server.Tests/Data/Repositories/StudDirectoryRepositoryTests.cs`

- [ ] **Step 1: Write failing repository tests**

```csharp
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
}
```

- [ ] **Step 2: Run tests — confirm they fail (types not found)**

```
dotnet test tests/Server.Tests --filter "StudDirectoryRepositoryTests"
```

Expected: compile error.

- [ ] **Step 3: Create `IStudDirectoryRepository.cs`**

```csharp
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IStudDirectoryRepository
{
    Task<StudDirectory?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<StudDirectory>> GetAllAsync(bool includeInactive = false);
    Task<StudDirectory> AddAsync(StudDirectory entry);
    Task UpdateAsync(StudDirectory entry);
}
```

- [ ] **Step 4: Create `StudDirectoryRepository.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class StudDirectoryRepository : IStudDirectoryRepository
{
    private readonly AppDbContext _db;
    public StudDirectoryRepository(AppDbContext db) => _db = db;

    public async Task<StudDirectory?> GetByIdAsync(Guid id) =>
        await _db.StudDirectories
            .Include(d => d.Stallions)
            .FirstOrDefaultAsync(d => d.Id == id);

    public async Task<IReadOnlyList<StudDirectory>> GetAllAsync(bool includeInactive = false) =>
        await _db.StudDirectories
            .Include(d => d.Stallions)
            .Where(d => includeInactive || d.IsActive)
            .OrderBy(d => d.Name)
            .ToListAsync();

    public async Task<StudDirectory> AddAsync(StudDirectory entry)
    {
        _db.StudDirectories.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    public async Task UpdateAsync(StudDirectory entry)
    {
        entry.UpdatedAt = DateTime.UtcNow;
        _db.StudDirectories.Update(entry);
        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: Run tests**

```
dotnet test tests/Server.Tests --filter "StudDirectoryRepositoryTests"
```

Expected: 4 tests PASS.

- [ ] **Step 6: Commit**

```
git add src/Server/Data/Repositories/IStudDirectoryRepository.cs src/Server/Data/Repositories/StudDirectoryRepository.cs tests/Server.Tests/Data/Repositories/StudDirectoryRepositoryTests.cs
git commit -m "feat: StudDirectory repository + tests"
```

---

### Task 6: `StallionDirectory` repository

**Files:**
- Create: `src/Server/Data/Repositories/IStallionDirectoryRepository.cs`
- Create: `src/Server/Data/Repositories/StallionDirectoryRepository.cs`
- Create: `tests/Server.Tests/Data/Repositories/StallionDirectoryRepositoryTests.cs`

- [ ] **Step 1: Write failing repository tests**

```csharp
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
```

- [ ] **Step 2: Run tests — confirm compile error**

```
dotnet test tests/Server.Tests --filter "StallionDirectoryRepositoryTests"
```

- [ ] **Step 3: Create `IStallionDirectoryRepository.cs`**

```csharp
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IStallionDirectoryRepository
{
    Task<StallionDirectory?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<StallionDirectory>> GetAllAsync(bool includeInactive = false, Guid? studDirectoryId = null);
    Task<IReadOnlyList<StallionDirectory>> GetByStudDirectoryIdAsync(Guid studDirectoryId, bool activeOnly = true);
    Task<StallionDirectory> AddAsync(StallionDirectory entry);
    Task UpdateAsync(StallionDirectory entry);
}
```

- [ ] **Step 4: Create `StallionDirectoryRepository.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class StallionDirectoryRepository : IStallionDirectoryRepository
{
    private readonly AppDbContext _db;
    public StallionDirectoryRepository(AppDbContext db) => _db = db;

    public async Task<StallionDirectory?> GetByIdAsync(Guid id) =>
        await _db.StallionDirectories
            .Include(d => d.StudDirectory)
            .FirstOrDefaultAsync(d => d.Id == id);

    public async Task<IReadOnlyList<StallionDirectory>> GetAllAsync(
        bool includeInactive = false, Guid? studDirectoryId = null) =>
        await _db.StallionDirectories
            .Include(d => d.StudDirectory)
            .Where(d => includeInactive || d.IsActive)
            .Where(d => studDirectoryId == null || d.StudDirectoryId == studDirectoryId)
            .OrderBy(d => d.Name)
            .ToListAsync();

    public async Task<IReadOnlyList<StallionDirectory>> GetByStudDirectoryIdAsync(
        Guid studDirectoryId, bool activeOnly = true) =>
        await _db.StallionDirectories
            .Include(d => d.StudDirectory)
            .Where(d => d.StudDirectoryId == studDirectoryId)
            .Where(d => !activeOnly || d.IsActive)
            .OrderBy(d => d.Name)
            .ToListAsync();

    public async Task<StallionDirectory> AddAsync(StallionDirectory entry)
    {
        _db.StallionDirectories.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    public async Task UpdateAsync(StallionDirectory entry)
    {
        entry.UpdatedAt = DateTime.UtcNow;
        _db.StallionDirectories.Update(entry);
        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: Run tests**

```
dotnet test tests/Server.Tests --filter "StallionDirectoryRepositoryTests"
```

Expected: 3 tests PASS.

- [ ] **Step 6: Run full test suite**

```
dotnet test
```

Expected: All existing tests still pass.

- [ ] **Step 7: Commit**

```
git add src/Server/Data/Repositories/IStallionDirectoryRepository.cs src/Server/Data/Repositories/StallionDirectoryRepository.cs tests/Server.Tests/Data/Repositories/StallionDirectoryRepositoryTests.cs
git commit -m "feat: StallionDirectory repository + tests — Plan A complete"
```

---

## ✅ Plan A Complete

The DB schema is updated (migration generated), all DTOs exist, and all four repositories are wired up and tested. Proceed to **Plan B (API layer)** to build the service and controller layer on top of this foundation.
