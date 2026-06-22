# Buyer T&C Onboarding + First-Bid Confirmation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Require verified buyers to accept versioned, staff-editable Terms & Conditions before bidding, and show a suppressible "legally binding" confirmation on each bid.

**Architecture:** A new `TermsDocument` entity (one row per published version) backs staff-editable T&C. Three new `User` fields track acceptance + confirmation suppression. The server enforces acceptance in `BidService` (defense in depth); the client gates verified buyers with a blocking modal in `MainLayout` and a per-bid confirmation modal on the listing page. T&C body is Markdown, rendered client-side with Markdig.

**Tech Stack:** ASP.NET Core 9 Web API, EF Core (SQL Server), Blazor WASM, xUnit + Moq + FluentAssertions, Markdig.

Spec: `docs/superpowers/specs/2026-06-22-buyer-terms-and-bid-confirmation-design.md`

---

## File Structure

**Shared (`src/Shared`)**
- Create: `DTOs/Terms/TermsDocumentDto.cs`, `DTOs/Terms/PublishTermsRequest.cs`, `DTOs/Terms/AcceptTermsRequest.cs`
- Modify: `DTOs/Users/UserDto.cs` (add `AcceptedTermsVersion`, `SuppressBidConfirmation`)

**Server (`src/Server`)**
- Create: `Data/Entities/TermsDocument.cs`, `Data/Repositories/ITermsRepository.cs`, `Data/Repositories/TermsRepository.cs`, `Services/ITermsService.cs`, `Services/TermsService.cs`, `Controllers/TermsController.cs`
- Modify: `Data/Entities/User.cs`, `Data/AppDbContext.cs`, `Services/UserService.cs`, `Services/IUserService.cs`, `Controllers/UsersController.cs`, `Services/BidService.cs`, `Program.cs`
- Migration: `Data/Migrations/*_AddTermsDocumentAndUserAcceptance.cs` (generated)

**Client (`src/Client`)**
- Create: `Services/TermsApiService.cs`, `Components/Buyer/TermsAgreementModal.razor`, `Components/Buyer/BidConfirmationModal.razor`, `Pages/Staff/StaffTerms.razor`
- Modify: `Stallions.Client.csproj` (Markdig), `Services/UserStateService.cs`, `Services/UserApiService.cs`, `Layout/MainLayout.razor`, `Layout/StaffLayout.razor`, `Pages/ListingDetail.razor`, `Program.cs`

**Tests (`tests/Server.Tests`)**
- Create: `Services/TermsServiceTests.cs`, `Services/UserServiceTermsTests.cs`
- Modify: `Services/BidServiceTests.cs` (constructor change + new T&C-guard tests)

---

## Task 1: Shared DTOs and UserDto fields

**Files:**
- Create: `src/Shared/DTOs/Terms/TermsDocumentDto.cs`
- Create: `src/Shared/DTOs/Terms/PublishTermsRequest.cs`
- Create: `src/Shared/DTOs/Terms/AcceptTermsRequest.cs`
- Modify: `src/Shared/DTOs/Users/UserDto.cs`

- [ ] **Step 1: Create the DTOs**

`src/Shared/DTOs/Terms/TermsDocumentDto.cs`:
```csharp
namespace Stallions.Shared.DTOs.Terms;

public class TermsDocumentDto
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
```

`src/Shared/DTOs/Terms/PublishTermsRequest.cs`:
```csharp
namespace Stallions.Shared.DTOs.Terms;

public class PublishTermsRequest
{
    public string Body { get; set; } = string.Empty;
}
```

`src/Shared/DTOs/Terms/AcceptTermsRequest.cs`:
```csharp
namespace Stallions.Shared.DTOs.Terms;

public class AcceptTermsRequest
{
    public int Version { get; set; }
}
```

- [ ] **Step 2: Add fields to UserDto**

Modify `src/Shared/DTOs/Users/UserDto.cs` to add two properties after `VerifiedAt`:
```csharp
namespace Stallions.Shared.DTOs.Users;

public class UserDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public int? AcceptedTermsVersion { get; set; }
    public bool SuppressBidConfirmation { get; set; }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Shared/Stallions.Shared.csproj -v q`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/Shared/DTOs/Terms src/Shared/DTOs/Users/UserDto.cs
git commit -m "feat: shared DTOs for terms documents and user acceptance fields"
```

---

## Task 2: TermsDocument entity, User fields, DbContext config, migration

**Files:**
- Create: `src/Server/Data/Entities/TermsDocument.cs`
- Modify: `src/Server/Data/Entities/User.cs`
- Modify: `src/Server/Data/AppDbContext.cs`

- [ ] **Step 1: Create the entity**

`src/Server/Data/Entities/TermsDocument.cs`:
```csharp
namespace Stallions.Server.Data.Entities;

public class TermsDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Version { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedByUserId { get; set; }

    public User? CreatedBy { get; set; }
}
```

- [ ] **Step 2: Add fields to User entity**

Modify `src/Server/Data/Entities/User.cs` — add three properties after `VerifiedByUserId`:
```csharp
    public Guid? VerifiedByUserId { get; set; }

    // T&C acceptance + bid-confirmation onboarding
    public int? AcceptedTermsVersion { get; set; }
    public DateTime? AcceptedTermsAt { get; set; }
    public bool SuppressBidConfirmation { get; set; }
```

- [ ] **Step 3: Register DbSet + config in AppDbContext**

In `src/Server/Data/AppDbContext.cs`, add the DbSet after the `StallionDirectories` line (currently line 26):
```csharp
    public DbSet<TermsDocument> TermsDocuments => Set<TermsDocument>();
```

Then add an entity configuration inside `OnModelCreating`, after the `StallionDirectory` block:
```csharp
        modelBuilder.Entity<TermsDocument>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Body).IsRequired();
            e.HasIndex(t => t.Version).IsUnique();
            e.HasOne(t => t.CreatedBy)
                .WithMany()
                .HasForeignKey(t => t.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Server/Stallions.Server.csproj -v q`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Create the EF migration**

Run:
```bash
dotnet ef migrations add AddTermsDocumentAndUserAcceptance --project src/Server/Stallions.Server.csproj
```
Expected: a new file under `src/Server/Data/Migrations/`. Open it and confirm it creates the `TermsDocuments` table (with a unique index on `Version`) and adds `AcceptedTermsVersion`, `AcceptedTermsAt`, `SuppressBidConfirmation` columns to `Users`.

- [ ] **Step 6: Commit**

```bash
git add src/Server/Data/Entities/TermsDocument.cs src/Server/Data/Entities/User.cs src/Server/Data/AppDbContext.cs src/Server/Data/Migrations
git commit -m "feat: TermsDocument entity, user acceptance fields, EF migration"
```

---

## Task 3: ITermsRepository + TermsRepository

**Files:**
- Create: `src/Server/Data/Repositories/ITermsRepository.cs`
- Create: `src/Server/Data/Repositories/TermsRepository.cs`

- [ ] **Step 1: Create the interface**

`src/Server/Data/Repositories/ITermsRepository.cs`:
```csharp
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface ITermsRepository
{
    /// <summary>The current published T&C = the row with the highest Version, or null if none.</summary>
    Task<TermsDocument?> GetCurrentAsync();
    Task<IReadOnlyList<TermsDocument>> GetHistoryAsync();
    Task<TermsDocument> AddAsync(TermsDocument document);
}
```

- [ ] **Step 2: Create the implementation**

`src/Server/Data/Repositories/TermsRepository.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class TermsRepository : ITermsRepository
{
    private readonly AppDbContext _db;
    public TermsRepository(AppDbContext db) => _db = db;

    public async Task<TermsDocument?> GetCurrentAsync() =>
        await _db.TermsDocuments
            .OrderByDescending(t => t.Version)
            .FirstOrDefaultAsync();

    public async Task<IReadOnlyList<TermsDocument>> GetHistoryAsync() =>
        await _db.TermsDocuments
            .OrderByDescending(t => t.Version)
            .ToListAsync();

    public async Task<TermsDocument> AddAsync(TermsDocument document)
    {
        _db.TermsDocuments.Add(document);
        await _db.SaveChangesAsync();
        return document;
    }
}
```

- [ ] **Step 3: Register in Program.cs**

In `src/Server/Program.cs`, add after the `IStallionDirectoryRepository` registration (line ~104):
```csharp
builder.Services.AddScoped<ITermsRepository, TermsRepository>();
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Server/Stallions.Server.csproj -v q`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/Server/Data/Repositories/ITermsRepository.cs src/Server/Data/Repositories/TermsRepository.cs src/Server/Program.cs
git commit -m "feat: TermsRepository for versioned T&C storage"
```

---

## Task 4: ITermsService + TermsService (+ tests)

**Files:**
- Create: `src/Server/Services/ITermsService.cs`
- Create: `src/Server/Services/TermsService.cs`
- Create: `tests/Server.Tests/Services/TermsServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/Server.Tests/Services/TermsServiceTests.cs`:
```csharp
using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Terms;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class TermsServiceTests
{
    private readonly Mock<ITermsRepository> _repo = new();
    private readonly Mock<IUserService> _users = new();

    private TermsService CreateSut() => new(_repo.Object, _users.Object);

    private static User Staff() => new() { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active };

    [Fact]
    public async Task GetCurrent_WhenNonePublished_ReturnsNotFound()
    {
        _repo.Setup(r => r.GetCurrentAsync()).ReturnsAsync((TermsDocument?)null);
        var result = await CreateSut().GetCurrentAsync();
        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetCurrent_WhenPublished_ReturnsDto()
    {
        _repo.Setup(r => r.GetCurrentAsync())
            .ReturnsAsync(new TermsDocument { Id = Guid.NewGuid(), Version = 3, Body = "Hi" });
        var result = await CreateSut().GetCurrentAsync();
        result.Succeeded.Should().BeTrue();
        result.Value!.Version.Should().Be(3);
        result.Value.Body.Should().Be("Hi");
    }

    [Fact]
    public async Task Publish_WhenNoExisting_CreatesVersion1()
    {
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(Staff());
        _repo.Setup(r => r.GetCurrentAsync()).ReturnsAsync((TermsDocument?)null);
        TermsDocument? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<TermsDocument>()))
            .ReturnsAsync((TermsDocument d) => { added = d; return d; });

        var result = await CreateSut().PublishAsync(new PublishTermsRequest { Body = "Terms v1" });

        result.Succeeded.Should().BeTrue();
        added!.Version.Should().Be(1);
        added.Body.Should().Be("Terms v1");
    }

    [Fact]
    public async Task Publish_WhenExistingV2_CreatesVersion3()
    {
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(Staff());
        _repo.Setup(r => r.GetCurrentAsync())
            .ReturnsAsync(new TermsDocument { Version = 2, Body = "old" });
        TermsDocument? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<TermsDocument>()))
            .ReturnsAsync((TermsDocument d) => { added = d; return d; });

        var result = await CreateSut().PublishAsync(new PublishTermsRequest { Body = "Terms v3" });

        result.Succeeded.Should().BeTrue();
        added!.Version.Should().Be(3);
    }

    [Fact]
    public async Task Publish_WhenBodyEmpty_ReturnsBadRequest()
    {
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(Staff());
        var result = await CreateSut().PublishAsync(new PublishTermsRequest { Body = "   " });
        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Server.Tests/Stallions.Server.Tests.csproj --filter "FullyQualifiedName~TermsServiceTests" -v q`
Expected: FAIL — `TermsService` / `ITermsService` do not exist (compile error).

- [ ] **Step 3: Create the interface**

`src/Server/Services/ITermsService.cs`:
```csharp
using Stallions.Shared.DTOs.Terms;

namespace Stallions.Server.Services;

public interface ITermsService
{
    Task<ServiceResult<TermsDocumentDto>> GetCurrentAsync();
    Task<ServiceResult<TermsDocumentDto>> PublishAsync(PublishTermsRequest request);
    Task<ServiceResult<IReadOnlyList<TermsDocumentDto>>> GetHistoryAsync();
}
```

- [ ] **Step 4: Create the implementation**

`src/Server/Services/TermsService.cs`:
```csharp
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.Terms;

namespace Stallions.Server.Services;

public class TermsService : ITermsService
{
    private readonly ITermsRepository _repo;
    private readonly IUserService _users;

    public TermsService(ITermsRepository repo, IUserService users)
    {
        _repo = repo;
        _users = users;
    }

    public async Task<ServiceResult<TermsDocumentDto>> GetCurrentAsync()
    {
        var current = await _repo.GetCurrentAsync();
        return current == null
            ? ServiceResult<TermsDocumentDto>.NotFound("No Terms & Conditions have been published yet.")
            : ServiceResult<TermsDocumentDto>.Ok(MapToDto(current));
    }

    public async Task<ServiceResult<TermsDocumentDto>> PublishAsync(PublishTermsRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<TermsDocumentDto>.Forbidden();

        if (string.IsNullOrWhiteSpace(request.Body))
            return ServiceResult<TermsDocumentDto>.BadRequest("Terms body cannot be empty.");

        var current = await _repo.GetCurrentAsync();
        var nextVersion = (current?.Version ?? 0) + 1;

        var created = await _repo.AddAsync(new TermsDocument
        {
            Version = nextVersion,
            Body = request.Body.Trim(),
            CreatedByUserId = caller.Id
        });

        return ServiceResult<TermsDocumentDto>.Created(MapToDto(created));
    }

    public async Task<ServiceResult<IReadOnlyList<TermsDocumentDto>>> GetHistoryAsync()
    {
        var docs = await _repo.GetHistoryAsync();
        return ServiceResult<IReadOnlyList<TermsDocumentDto>>.Ok(docs.Select(MapToDto).ToList());
    }

    private static TermsDocumentDto MapToDto(TermsDocument t) => new()
    {
        Id = t.Id,
        Version = t.Version,
        Body = t.Body,
        CreatedAt = t.CreatedAt
    };
}
```

> Note: If `ServiceResult<T>.Created` does not exist, use `.Ok` instead — check `src/Server/Services/ServiceResult.cs` and match the existing API. The other listing/directory services use `.Created` for POST-create results, so it should exist.

- [ ] **Step 5: Register in Program.cs**

In `src/Server/Program.cs`, add after `IDirectoryService` registration (line ~119):
```csharp
builder.Services.AddScoped<ITermsService, TermsService>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Server.Tests/Stallions.Server.Tests.csproj --filter "FullyQualifiedName~TermsServiceTests" -v q`
Expected: PASS — 5 passed.

- [ ] **Step 7: Commit**

```bash
git add src/Server/Services/ITermsService.cs src/Server/Services/TermsService.cs src/Server/Program.cs tests/Server.Tests/Services/TermsServiceTests.cs
git commit -m "feat: TermsService with versioned publish + tests"
```

---

## Task 5: TermsController

**Files:**
- Create: `src/Server/Controllers/TermsController.cs`

- [ ] **Step 1: Create the controller**

`src/Server/Controllers/TermsController.cs`:
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Terms;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/terms")]
public class TermsController : ControllerBase
{
    private readonly ITermsService _terms;
    public TermsController(ITermsService terms) => _terms = terms;

    // Any authenticated user can read the current T&C (buyers need it to accept).
    [HttpGet("current")]
    [Authorize]
    public async Task<IActionResult> GetCurrent()
    {
        var r = await _terms.GetCurrentAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> Publish([FromBody] PublishTermsRequest request)
    {
        var r = await _terms.PublishAsync(request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("history")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> GetHistory()
    {
        var r = await _terms.GetHistoryAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Server/Stallions.Server.csproj -v q`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/Server/Controllers/TermsController.cs
git commit -m "feat: TermsController endpoints (current/publish/history)"
```

---

## Task 6: UserService accept-terms + suppress-bid-confirmation (+ tests)

**Files:**
- Modify: `src/Server/Services/IUserService.cs`
- Modify: `src/Server/Services/UserService.cs`
- Modify: `src/Server/Controllers/UsersController.cs`
- Create: `tests/Server.Tests/Services/UserServiceTermsTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/Server.Tests/Services/UserServiceTermsTests.cs`:
```csharp
using FluentAssertions;
using Moq;
using Stallions.Server.Auth;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Terms;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class UserServiceTermsTests
{
    private readonly Mock<IUserRepository> _repo = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<IAuditLogRepository> _audit = new();
    private readonly Mock<ITermsRepository> _terms = new();

    private UserService CreateSut() =>
        new(_repo.Object, _currentUser.Object, _audit.Object, _terms.Object);

    private User SetupActiveBuyer(int? acceptedVersion = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(), ObjectId = "oid-1", Email = "b@x.com",
            Role = UserRole.Buyer, Status = UserStatus.Active,
            AcceptedTermsVersion = acceptedVersion
        };
        _currentUser.Setup(c => c.ObjectId).Returns("oid-1");
        _currentUser.Setup(c => c.IsAuthenticated).Returns(true);
        _repo.Setup(r => r.GetByObjectIdAsync("oid-1")).ReturnsAsync(user);
        return user;
    }

    [Fact]
    public async Task AcceptTerms_WhenVersionMatchesCurrent_SetsFields()
    {
        var user = SetupActiveBuyer();
        _terms.Setup(t => t.GetCurrentAsync())
            .ReturnsAsync(new TermsDocument { Version = 4, Body = "x" });

        var result = await CreateSut().AcceptTermsAsync(new AcceptTermsRequest { Version = 4 });

        result.Succeeded.Should().BeTrue();
        user.AcceptedTermsVersion.Should().Be(4);
        user.AcceptedTermsAt.Should().NotBeNull();
        _repo.Verify(r => r.UpdateAsync(user), Times.Once);
        _audit.Verify(a => a.LogAsync("User", user.Id, "TermsAccepted", user.Id,
            It.Is<string>(s => s.Contains("4"))), Times.Once);
    }

    [Fact]
    public async Task AcceptTerms_WhenVersionStale_ReturnsBadRequest()
    {
        SetupActiveBuyer();
        _terms.Setup(t => t.GetCurrentAsync())
            .ReturnsAsync(new TermsDocument { Version = 5, Body = "x" });

        var result = await CreateSut().AcceptTermsAsync(new AcceptTermsRequest { Version = 4 });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task AcceptTerms_WhenNoTermsPublished_ReturnsBadRequest()
    {
        SetupActiveBuyer();
        _terms.Setup(t => t.GetCurrentAsync()).ReturnsAsync((TermsDocument?)null);

        var result = await CreateSut().AcceptTermsAsync(new AcceptTermsRequest { Version = 1 });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task SuppressBidConfirmation_SetsFlag()
    {
        var user = SetupActiveBuyer();

        var result = await CreateSut().SuppressBidConfirmationAsync();

        result.Succeeded.Should().BeTrue();
        user.SuppressBidConfirmation.Should().BeTrue();
        _repo.Verify(r => r.UpdateAsync(user), Times.Once);
    }
}
```

> Note: this test calls the `UserService` constructor with a 4th argument `_terms.Object`. The current constructor has three parameters; Step 4 adds the fourth. Confirm the existing constructor parameter order in `src/Server/Services/UserService.cs` (it is `repo, currentUser, auditRepo`) and append `ITermsRepository terms` last.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Server.Tests/Stallions.Server.Tests.csproj --filter "FullyQualifiedName~UserServiceTermsTests" -v q`
Expected: FAIL — `AcceptTermsAsync` / `SuppressBidConfirmationAsync` and the 4-arg constructor do not exist (compile error).

- [ ] **Step 3: Add interface methods**

In `src/Server/Services/IUserService.cs`, add `using Stallions.Shared.DTOs.Terms;` at the top, and these two methods to the interface:
```csharp
    Task<ServiceResult> AcceptTermsAsync(AcceptTermsRequest request);
    Task<ServiceResult> SuppressBidConfirmationAsync();
```

- [ ] **Step 4: Update UserService constructor and add methods**

In `src/Server/Services/UserService.cs`:

Add `using Stallions.Shared.DTOs.Terms;` at the top.

Add a field and extend the constructor (current fields are `_repo`, `_currentUser`, `_auditRepo`):
```csharp
    private readonly ITermsRepository _terms;

    public UserService(IUserRepository repo, ICurrentUserService currentUser,
        IAuditLogRepository auditRepo, ITermsRepository terms)
    {
        _repo = repo;
        _currentUser = currentUser;
        _auditRepo = auditRepo;
        _terms = terms;
    }
```

Add the two methods (place them after `SuspendUserAsync`):
```csharp
    public async Task<ServiceResult> AcceptTermsAsync(AcceptTermsRequest request)
    {
        var user = await GetOrCreateCurrentUserAsync();
        if (user == null) return ServiceResult.Forbidden();

        var current = await _terms.GetCurrentAsync();
        if (current == null)
            return ServiceResult.BadRequest("No Terms & Conditions are currently published.");
        if (request.Version != current.Version)
            return ServiceResult.BadRequest("The Terms & Conditions have changed. Please review the latest version.");

        user.AcceptedTermsVersion = current.Version;
        user.AcceptedTermsAt = DateTime.UtcNow;
        await _repo.UpdateAsync(user);
        await _auditRepo.LogAsync("User", user.Id, "TermsAccepted", user.Id,
            $"{{\"Version\":{current.Version}}}");
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> SuppressBidConfirmationAsync()
    {
        var user = await GetOrCreateCurrentUserAsync();
        if (user == null) return ServiceResult.Forbidden();
        user.SuppressBidConfirmation = true;
        await _repo.UpdateAsync(user);
        return ServiceResult.Ok();
    }
```

- [ ] **Step 5: Extend MapToDto**

In `src/Server/Services/UserService.cs`, update the `MapToDto` method to include the two new fields:
```csharp
    private static UserDto MapToDto(User u) => new()
    {
        Id = u.Id,
        DisplayName = u.DisplayName,
        Email = u.Email,
        Role = u.Role.ToString(),
        Status = u.Status.ToString(),
        CreatedAt = u.CreatedAt,
        VerifiedAt = u.VerifiedAt,
        AcceptedTermsVersion = u.AcceptedTermsVersion,
        SuppressBidConfirmation = u.SuppressBidConfirmation
    };
```

- [ ] **Step 6: Add controller endpoints**

In `src/Server/Controllers/UsersController.cs`, add `using Stallions.Shared.DTOs.Terms;` and these two actions (after `UpdateMe`):
```csharp
    [HttpPost("me/accept-terms")]
    [Authorize]
    public async Task<IActionResult> AcceptTerms([FromBody] AcceptTermsRequest request)
    {
        var r = await _users.AcceptTermsAsync(request);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("me/suppress-bid-confirmation")]
    [Authorize]
    public async Task<IActionResult> SuppressBidConfirmation()
    {
        var r = await _users.SuppressBidConfirmationAsync();
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Server.Tests/Stallions.Server.Tests.csproj --filter "FullyQualifiedName~UserServiceTermsTests" -v q`
Expected: PASS — 4 passed.

- [ ] **Step 8: Run the full server test suite to catch constructor-change fallout**

Run: `dotnet test tests/Server.Tests/Stallions.Server.Tests.csproj -v q`
Expected: PASS. If any existing `UserService` test fails to compile because it used the 3-arg constructor, update those call sites to pass a `Mock<ITermsRepository>().Object` as the 4th argument.

- [ ] **Step 9: Commit**

```bash
git add src/Server/Services/IUserService.cs src/Server/Services/UserService.cs src/Server/Controllers/UsersController.cs tests/Server.Tests/Services/UserServiceTermsTests.cs
git commit -m "feat: accept-terms and suppress-bid-confirmation user endpoints + tests"
```

---

## Task 7: BidService T&C enforcement guard (+ tests)

**Files:**
- Modify: `src/Server/Services/BidService.cs`
- Modify: `tests/Server.Tests/Services/BidServiceTests.cs`

- [ ] **Step 1: Update the test helper and add failing tests**

In `tests/Server.Tests/Services/BidServiceTests.cs`:

Add a terms-repo mock field and update `CreateSut` (the constructor gains a 5th argument):
```csharp
    private readonly Mock<ITermsRepository> _termsRepoMock = new();

    private BidService CreateSut() =>
        new(_bidRepoMock.Object, _listingRepoMock.Object, _usersMock.Object, CreateInMemoryDb(), _termsRepoMock.Object);
```

By default `_termsRepoMock.GetCurrentAsync()` returns null (no T&C) so all existing tests still pass — the guard is skipped. Now add two new tests:
```csharp
    [Fact]
    public async Task PlaceBid_WhenTermsPublishedAndBuyerHasNotAccepted_ReturnsBadRequest()
    {
        var buyer = ActiveBuyer();                       // AcceptedTermsVersion = null
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var auction = OpenAuction();
        _listingRepoMock.Setup(r => r.GetAuctionByIdAsync(auction.Id)).ReturnsAsync(auction);
        _termsRepoMock.Setup(t => t.GetCurrentAsync())
            .ReturnsAsync(new TermsDocument { Version = 2, Body = "x" });

        var result = await CreateSut().PlaceBidAsync(auction.Id, new PlaceBidRequest { AmountIncGst = 1000m });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Contain("Terms");
    }

    [Fact]
    public async Task PlaceBid_WhenBuyerAcceptedCurrentTerms_Succeeds()
    {
        var buyer = ActiveBuyer();
        buyer.AcceptedTermsVersion = 2;
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var auction = OpenAuction(startingPrice: 1000m);
        _listingRepoMock.Setup(r => r.GetAuctionByIdAsync(auction.Id)).ReturnsAsync(auction);
        _bidRepoMock.Setup(r => r.GetHighestBidAsync(auction.Id)).ReturnsAsync((Bid?)null);
        _bidRepoMock.Setup(r => r.AddAsync(It.IsAny<Bid>())).ReturnsAsync((Bid b) => b);
        _termsRepoMock.Setup(t => t.GetCurrentAsync())
            .ReturnsAsync(new TermsDocument { Version = 2, Body = "x" });

        var result = await CreateSut().PlaceBidAsync(auction.Id, new PlaceBidRequest { AmountIncGst = 1000m });

        result.Succeeded.Should().BeTrue();
    }
```

Add `using Stallions.Server.Data.Entities;` if not already present (it is — `User`, `Bid`, `AuctionListing` are used).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Server.Tests/Stallions.Server.Tests.csproj --filter "FullyQualifiedName~BidServiceTests" -v q`
Expected: FAIL — the 5-arg constructor does not exist yet (compile error).

- [ ] **Step 3: Add the guard to BidService**

In `src/Server/Services/BidService.cs`:

Add the field + constructor parameter (current params: `bidRepo, listingRepo, users, db`):
```csharp
    private readonly ITermsRepository _termsRepo;

    public BidService(IBidRepository bidRepo, IListingRepository listingRepo,
        IUserService users, AppDbContext db, ITermsRepository termsRepo)
    {
        _bidRepo = bidRepo;
        _listingRepo = listingRepo;
        _users = users;
        _db = db;
        _termsRepo = termsRepo;
    }
```

Add `using Stallions.Server.Data.Repositories;` if not present (it is — repos are used).

In `PlaceBidAsync`, after the existing `if (caller.Status != UserStatus.Active)` guard and before loading the listing, insert:
```csharp
        // Buyer must have accepted the current T&C. If none is published, the gate is skipped.
        var currentTerms = await _termsRepo.GetCurrentAsync();
        if (currentTerms != null && caller.AcceptedTermsVersion != currentTerms.Version)
            return ServiceResult<BidDto>.BadRequest(
                "You must accept the current Terms & Conditions before placing a bid.");
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Server.Tests/Stallions.Server.Tests.csproj --filter "FullyQualifiedName~BidServiceTests" -v q`
Expected: PASS — all bid tests (existing 19 + 2 new = 21) pass.

- [ ] **Step 5: Commit**

```bash
git add src/Server/Services/BidService.cs tests/Server.Tests/Services/BidServiceTests.cs
git commit -m "feat: enforce T&C acceptance in BidService before allowing a bid"
```

---

## Task 8: Client — Markdig, TermsApiService, user API additions, DI

**Files:**
- Modify: `src/Client/Stallions.Client.csproj`
- Create: `src/Client/Services/TermsApiService.cs`
- Modify: `src/Client/Services/UserApiService.cs`
- Modify: `src/Client/Program.cs`

- [ ] **Step 1: Add the Markdig package**

Run:
```bash
dotnet add src/Client/Stallions.Client.csproj package Markdig
```
Expected: `Markdig` added to `Stallions.Client.csproj` `<PackageReference>`s.

- [ ] **Step 2: Create TermsApiService**

`src/Client/Services/TermsApiService.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Terms;

namespace Stallions.Client.Services;

/// <summary>Authenticated access to the platform Terms & Conditions endpoints.</summary>
public class TermsApiService
{
    private readonly HttpClient _http;
    public TermsApiService(HttpClient http) => _http = http;

    /// <summary>Returns the current published T&C, or null if none has been published.</summary>
    public virtual async Task<TermsDocumentDto?> GetCurrentAsync()
    {
        var r = await _http.GetAsync("api/terms/current");
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load Terms & Conditions.");
        return await r.Content.ReadFromJsonAsync<TermsDocumentDto>();
    }

    public virtual async Task<TermsDocumentDto> PublishAsync(string body)
    {
        var r = await _http.PostAsJsonAsync("api/terms", new PublishTermsRequest { Body = body });
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ReadError(r, "Failed to publish Terms & Conditions."));
        return await r.Content.ReadFromJsonAsync<TermsDocumentDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<List<TermsDocumentDto>> GetHistoryAsync()
    {
        var r = await _http.GetAsync("api/terms/history");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load Terms history.");
        return await r.Content.ReadFromJsonAsync<List<TermsDocumentDto>>() ?? [];
    }

    private static async Task<string> ReadError(HttpResponseMessage r, string fallback)
    {
        var body = await r.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(body) ? fallback : body.Trim('"');
    }
}
```

> Note: confirm `ApiException` lives in the `Stallions.Client.Services` namespace (it is used by `BidApiService` / `AdminApiService` without an extra `using`). If it is elsewhere, add the matching `using`.

- [ ] **Step 3: Add accept-terms + suppress methods to UserApiService**

In `src/Client/Services/UserApiService.cs`, add `using Stallions.Shared.DTOs.Terms;` and two methods (match the existing class style — it already has `GetMeAsync`):
```csharp
    public virtual async Task AcceptTermsAsync(int version)
    {
        var r = await _http.PostAsJsonAsync("api/users/me/accept-terms",
            new AcceptTermsRequest { Version = version });
        if (!r.IsSuccessStatusCode)
        {
            var body = await r.Content.ReadAsStringAsync();
            throw new ApiException((int)r.StatusCode,
                string.IsNullOrWhiteSpace(body) ? "Failed to accept Terms & Conditions." : body.Trim('"'));
        }
    }

    public virtual async Task SuppressBidConfirmationAsync()
    {
        var r = await _http.PostAsync("api/users/me/suppress-bid-confirmation", null);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to save your preference.");
    }
```

> Note: confirm `UserApiService` has an `HttpClient _http` field. If the field is named differently, match it.

- [ ] **Step 4: Register TermsApiService in client Program.cs**

In `src/Client/Program.cs`, register `TermsApiService` as an authenticated typed client next to `StaffApiService` (which uses `BaseAddressAuthorizationMessageHandler`). Add after the `StaffApiService` registration (line ~52):
```csharp
builder.Services.AddHttpClient<TermsApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
```

> Note: match the exact handler-registration style used by `StaffApiService`/`DirectoryApiService` in the same file (copy that line and swap the type name).

- [ ] **Step 5: Build**

Run: `dotnet build src/Client/Stallions.Client.csproj -v q`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add src/Client/Stallions.Client.csproj src/Client/Services/TermsApiService.cs src/Client/Services/UserApiService.cs src/Client/Program.cs
git commit -m "feat: client Markdig, TermsApiService, accept/suppress user API calls"
```

---

## Task 9: Client — UserStateService acceptance state

**Files:**
- Modify: `src/Client/Services/UserStateService.cs`

- [ ] **Step 1: Expose acceptance state + local mutators**

In `src/Client/Services/UserStateService.cs`, add these members after the existing `IsVerified`/`IsPendingVerification` properties:
```csharp
    public int? AcceptedTermsVersion => CurrentUser?.AcceptedTermsVersion;
    public bool SuppressBidConfirmation => CurrentUser?.SuppressBidConfirmation ?? false;

    /// <summary>True when this verified buyer must accept a newer T&C version.</summary>
    public bool NeedsTermsAcceptance(int currentVersion) =>
        IsBuyer && IsVerified && (AcceptedTermsVersion is null || AcceptedTermsVersion < currentVersion);

    /// <summary>Update cached state after a successful accept-terms call (avoids a full reload).</summary>
    public void MarkTermsAccepted(int version)
    {
        if (CurrentUser is not null)
        {
            CurrentUser.AcceptedTermsVersion = version;
            OnChange?.Invoke();
        }
    }

    /// <summary>Update cached state after a successful suppress-bid-confirmation call.</summary>
    public void MarkBidConfirmationSuppressed()
    {
        if (CurrentUser is not null)
        {
            CurrentUser.SuppressBidConfirmation = true;
            OnChange?.Invoke();
        }
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Client/Stallions.Client.csproj -v q`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/Client/Services/UserStateService.cs
git commit -m "feat: UserStateService exposes T&C acceptance + suppression state"
```

---

## Task 10: Client — TermsAgreementModal (scroll-to-bottom to enable agree)

**Files:**
- Create: `src/Client/Components/Buyer/TermsAgreementModal.razor`

- [ ] **Step 1: Create the component**

`src/Client/Components/Buyer/TermsAgreementModal.razor`:
```razor
@using Stallions.Shared.DTOs.Terms
@inject IJSRuntime JS

<Modal IsOpen="true" Title="Welcome — Terms & Conditions" CloseOnBackdrop="false" OnClose="NoOp">
    <div class="terms-welcome">
        <p>Before you can bid on a nomination, please read and agree to the Stallions Australia
           Terms &amp; Conditions below. Scroll to the bottom to continue.</p>
    </div>
    <div class="terms-body" @ref="_scrollRef" @onscroll="OnScroll">
        @((MarkupString)_html)
    </div>
    <label class="terms-agree">
        <input type="checkbox" @bind="_checked" disabled="@(!_scrolledToEnd)" />
        <span>I have read and agree to the Terms &amp; Conditions@(_scrolledToEnd ? "" : " (scroll to the end to enable)").</span>
    </label>
    @if (_error is not null) { <ErrorMessage Message="@_error" /> }
    <div class="terms-actions">
        <button class="btn btn-gold" disabled="@(!_checked || _busy)" @onclick="Agree">
            @(_busy ? "Saving…" : "Agree and continue")
        </button>
    </div>
</Modal>

@code {
    [Parameter, EditorRequired] public TermsDocumentDto Terms { get; set; } = default!;
    [Parameter] public EventCallback OnAccepted { get; set; }

    private ElementReference _scrollRef;
    private string _html = string.Empty;
    private bool _scrolledToEnd;
    private bool _checked;
    private bool _busy;
    private string? _error;

    protected override void OnParametersSet() =>
        _html = Markdig.Markdown.ToHtml(Terms.Body ?? string.Empty);

    private async Task OnScroll()
    {
        // Detect scroll-to-bottom (within a small tolerance) via JS measurement.
        try
        {
            _scrolledToEnd = await JS.InvokeAsync<bool>("termsScroll.atBottom", _scrollRef);
            StateHasChanged();
        }
        catch { _scrolledToEnd = true; /* fail open in environments without JS */ }
    }

    private async Task Agree()
    {
        _busy = true; _error = null;
        try
        {
            await OnAccepted.InvokeAsync();
        }
        catch (ApiException ex) { _error = ex.Message; }
        finally { _busy = false; }
    }

    private void NoOp() { /* blocking modal — no dismiss without agreeing */ }
}
```

- [ ] **Step 2: Add the scroll-detection JS helper**

In `src/Client/wwwroot/index.html`, add a small inline script (next to the existing `modalFocus` helper that `Modal.razor` uses). If `modalFocus` is defined in a separate `.js`, add this beside it:
```html
<script>
  window.termsScroll = {
    atBottom: function (el) {
      if (!el) return true;
      return el.scrollTop + el.clientHeight >= el.scrollHeight - 8;
    }
  };
</script>
```

> Note: find where `modalFocus.trap` is defined (search `modalFocus` under `src/Client/wwwroot`) and colocate `termsScroll` there so it loads identically.

- [ ] **Step 3: Add component styles**

Create `src/Client/Components/Buyer/TermsAgreementModal.razor.css`:
```css
.terms-welcome { margin-bottom: var(--space-4); }

.terms-body {
    max-height: 45vh;
    overflow-y: auto;
    border: 1px solid var(--border, #e5e7eb);
    border-radius: var(--radius-md);
    padding: var(--space-4);
    margin-bottom: var(--space-4);
}

.terms-agree {
    display: flex;
    gap: var(--space-2);
    align-items: flex-start;
    margin-bottom: var(--space-4);
    font-size: var(--font-size-sm);
}

.terms-actions { display: flex; justify-content: flex-end; }
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Client/Stallions.Client.csproj -v q`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/Client/Components/Buyer/TermsAgreementModal.razor src/Client/Components/Buyer/TermsAgreementModal.razor.css src/Client/wwwroot/index.html
git commit -m "feat: TermsAgreementModal with scroll-to-bottom gate and Markdown rendering"
```

---

## Task 11: Client — BidConfirmationModal

**Files:**
- Create: `src/Client/Components/Buyer/BidConfirmationModal.razor`

- [ ] **Step 1: Create the component**

`src/Client/Components/Buyer/BidConfirmationModal.razor`:
```razor
<Modal IsOpen="true" Title="Confirm your bid" CloseOnBackdrop="false" OnClose="Cancel">
    <div class="bid-confirm-body">
        <p><strong>This bid is legally binding.</strong> By confirming, you commit to purchasing
           this nomination at your bid price if you win the auction. Winning bidders must provide
           mare details and pay the platform fee to complete the purchase.</p>
        <label class="bid-confirm-skip">
            <input type="checkbox" @bind="_dontShowAgain" />
            <span>Don't show this message again.</span>
        </label>
    </div>
    <div class="bid-confirm-actions">
        <button class="btn btn-outline" @onclick="Cancel" disabled="@_busy">Cancel</button>
        <button class="btn btn-gold" @onclick="Confirm" disabled="@_busy">
            @(_busy ? "Placing…" : "Confirm bid")
        </button>
    </div>
</Modal>

@code {
    /// <summary>Invoked when the buyer confirms; argument = whether to suppress future confirmations.</summary>
    [Parameter] public EventCallback<bool> OnConfirm { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private bool _dontShowAgain;
    private bool _busy;

    private async Task Confirm()
    {
        _busy = true;
        await OnConfirm.InvokeAsync(_dontShowAgain);
        _busy = false;
    }

    private async Task Cancel() => await OnCancel.InvokeAsync();
}
```

- [ ] **Step 2: Add styles**

Create `src/Client/Components/Buyer/BidConfirmationModal.razor.css`:
```css
.bid-confirm-body { margin-bottom: var(--space-4); }

.bid-confirm-skip {
    display: flex;
    gap: var(--space-2);
    align-items: center;
    margin-top: var(--space-4);
    font-size: var(--font-size-sm);
}

.bid-confirm-actions {
    display: flex;
    gap: var(--space-3);
    justify-content: flex-end;
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Client/Stallions.Client.csproj -v q`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/Client/Components/Buyer/BidConfirmationModal.razor src/Client/Components/Buyer/BidConfirmationModal.razor.css
git commit -m "feat: BidConfirmationModal with legally-binding notice and opt-out"
```

---

## Task 12: Client — MainLayout T&C gate

**Files:**
- Modify: `src/Client/Layout/MainLayout.razor`

- [ ] **Step 1: Wire the gate into MainLayout**

In `src/Client/Layout/MainLayout.razor`:

Add injections near the top (match existing `@inject` style):
```razor
@using Stallions.Shared.DTOs.Terms
@using Stallions.Client.Components.Buyer
@inject TermsApiService TermsApi
@inject UserApiService UserApi
```

Render the gate modal somewhere inside the layout markup (e.g. just before `@Body` or at the end of the shell), so it overlays whatever page is shown:
```razor
@if (_termsToAccept is not null)
{
    <TermsAgreementModal Terms="_termsToAccept" OnAccepted="HandleTermsAccepted" />
}
```

In the `@code` block, after the existing `UserState.LoadAsync()` call in the auth-state handler, add a check. Add these members:
```csharp
    private TermsDocumentDto? _termsToAccept;

    private async Task CheckTermsGateAsync()
    {
        _termsToAccept = null;
        if (!UserState.IsBuyer || !UserState.IsVerified) return;

        var current = await TermsApi.GetCurrentAsync();
        if (current is null) return;   // none published → no gate
        if (UserState.NeedsTermsAcceptance(current.Version))
            _termsToAccept = current;
        StateHasChanged();
    }

    private async Task HandleTermsAccepted()
    {
        if (_termsToAccept is null) return;
        await UserApi.AcceptTermsAsync(_termsToAccept.Version);
        UserState.MarkTermsAccepted(_termsToAccept.Version);
        _termsToAccept = null;
        StateHasChanged();
    }
```

Call `await CheckTermsGateAsync();` immediately after `await UserState.LoadAsync();` in whatever method currently loads user state (the auth-state-changed handler / `OnInitializedAsync`).

> Note: read the current `MainLayout.razor` `@code` block first and insert the `CheckTermsGateAsync()` call at the existing point where `UserState.LoadAsync()` is awaited. Do not duplicate the load.

- [ ] **Step 2: Build**

Run: `dotnet build src/Client/Stallions.Client.csproj -v q`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/Client/Layout/MainLayout.razor
git commit -m "feat: MainLayout gates verified buyers with the T&C agreement modal"
```

---

## Task 13: Client — ListingDetail bid confirmation + fallback

**Files:**
- Modify: `src/Client/Pages/ListingDetail.razor`

- [ ] **Step 1: Add the confirmation modal + gate the PlaceBid handler**

In `src/Client/Pages/ListingDetail.razor`:

Add `@using Stallions.Client.Components.Buyer` and inject `UserApi`:
```razor
@using Stallions.Client.Components.Buyer
@inject UserApiService UserApi
```

Render the confirmation modal near the bid form markup:
```razor
@if (_showBidConfirm)
{
    <BidConfirmationModal OnConfirm="ConfirmBidAndPlace" OnCancel="() => _showBidConfirm = false" />
}
```

Rename the current `PlaceBid` button-click handler path so the button first runs a gate. Change the bid button to call `StartBid` instead of `PlaceBid`. Add these members to the `@code` block (keep the existing `PlaceBid` body as the actual bid call, renamed `DoPlaceBidAsync`):
```csharp
    private bool _showBidConfirm;

    // Called by the "Place bid" button.
    private void StartBid()
    {
        _bidError = null;
        if (UserState.SuppressBidConfirmation)
        {
            _ = DoPlaceBidAsync();
        }
        else
        {
            _showBidConfirm = true;
        }
    }

    private async Task ConfirmBidAndPlace(bool dontShowAgain)
    {
        _showBidConfirm = false;
        if (dontShowAgain)
        {
            try
            {
                await UserApi.SuppressBidConfirmationAsync();
                UserState.MarkBidConfirmationSuppressed();
            }
            catch { /* non-fatal — proceed with the bid regardless */ }
        }
        await DoPlaceBidAsync();
    }
```

Rename the existing `PlaceBid()` method to `DoPlaceBidAsync()` and keep its body, but make the T&C-rejection case open the terms gate. Inside the `catch (ApiException ex)` block, add a check before assigning `_bidError`:
```csharp
        catch (ApiException ex)
        {
            if (ex.Message.Contains("Terms & Conditions"))
            {
                // Server rejected because a newer T&C needs acceptance — trigger the layout gate on next load.
                _bidError = "Please accept the latest Terms & Conditions, then try again.";
                await UserState.LoadAsync();   // refresh acceptance state
            }
            else
            {
                _bidError = ex.Message;
            }
        }
```

Update the bid button in the markup to call `StartBid`:
```razor
<button class="btn btn-gold" @onclick="StartBid" disabled="@_bidBusy">
    @(_bidBusy ? "Placing…" : "Place bid")
</button>
```

> Note: read the current `@code` block of `ListingDetail.razor` first. The existing handler is named `PlaceBid` (per the bid form). Rename carefully and ensure no other markup references the old name.

- [ ] **Step 2: Build**

Run: `dotnet build src/Client/Stallions.Client.csproj -v q`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/Client/Pages/ListingDetail.razor
git commit -m "feat: first-bid confirmation modal + T&C fallback on the listing page"
```

---

## Task 14: Client — Staff Terms editor page + nav

**Files:**
- Create: `src/Client/Pages/Staff/StaffTerms.razor`
- Modify: `src/Client/Layout/StaffLayout.razor`

- [ ] **Step 1: Create the staff page**

`src/Client/Pages/Staff/StaffTerms.razor`:
```razor
@page "/staff/terms"
@layout StaffLayout
@attribute [Authorize]
@using Stallions.Shared.DTOs.Terms
@inject TermsApiService TermsApi
@inject UserStateService UserState
@inject NavigationManager Nav

<PageTitle>Terms &amp; Conditions — Staff Admin</PageTitle>

<div class="admin-page-header">
    <h1 class="admin-page-title">Terms &amp; Conditions</h1>
</div>

@if (_loading)
{
    <p>Loading…</p>
}
else
{
    <p class="text-secondary text-sm">
        Current version: <strong>@(_current?.Version.ToString() ?? "none published")</strong>
        @if (_current is not null) { <text> · published @_current.CreatedAt.ToString("d MMM yyyy")</text> }
    </p>

    <div class="terms-editor">
        <label class="form-label">Body (Markdown)</label>
        <textarea class="form-control" rows="16" @bind="_body" @bind:event="oninput"></textarea>
    </div>

    <div class="terms-preview">
        <label class="form-label">Preview</label>
        <div class="terms-preview-box">@((MarkupString)_preview)</div>
    </div>

    @if (_error is not null) { <div class="alert alert-danger">@_error</div> }
    @if (_published) { <div class="alert alert-success">New version published.</div> }

    <button class="btn btn-gold" @onclick="Publish" disabled="@_busy">
        @(_busy ? "Publishing…" : "Publish new version")
    </button>
}

@code {
    private TermsDocumentDto? _current;
    private string _body = string.Empty;
    private string _preview = string.Empty;
    private bool _loading = true;
    private bool _busy;
    private bool _published;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        await UserState.LoadAsync();
        if (!UserState.IsStaff) { Nav.NavigateTo("/", replace: true); return; }
        await Load();
    }

    private async Task Load()
    {
        _loading = true; _error = null;
        try
        {
            _current = await TermsApi.GetCurrentAsync();
            _body = _current?.Body ?? string.Empty;
            UpdatePreview();
        }
        catch (Exception ex) { _error = ex is ApiException ae ? ae.Message : "Failed to load Terms."; }
        finally { _loading = false; }
    }

    protected override void OnParametersSet() => UpdatePreview();

    private void UpdatePreview() => _preview = Markdig.Markdown.ToHtml(_body ?? string.Empty);

    private async Task Publish()
    {
        _busy = true; _error = null; _published = false;
        try
        {
            _current = await TermsApi.PublishAsync(_body);
            _published = true;
        }
        catch (Exception ex) { _error = ex is ApiException ae ? ae.Message : "Failed to publish."; }
        finally { _busy = false; }
    }
}
```

> Note: the textarea uses `@bind:event="oninput"`, but the Markdown preview only refreshes on re-render. To make the preview live, call `UpdatePreview()` from an `oninput` handler instead of relying on `OnParametersSet`. If a live preview is desired, change the textarea to `@oninput="(e) => { _body = e.Value?.ToString() ?? string.Empty; UpdatePreview(); }"`. A non-live preview (updates on publish/load) is acceptable for a first pass.

- [ ] **Step 2: Add the nav link**

In `src/Client/Layout/StaffLayout.razor`, add a nav link after the Invoices link (line ~34):
```razor
            <NavLink href="/staff/terms" class="admin-nav-link" Match="NavLinkMatch.Prefix">
                <span class="admin-nav-icon">📜</span> Terms &amp; Conditions
            </NavLink>
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Client/Stallions.Client.csproj -v q`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/Client/Pages/Staff/StaffTerms.razor src/Client/Layout/StaffLayout.razor
git commit -m "feat: Staff Terms & Conditions editor page + nav link"
```

---

## Task 15: Full build, test, migration apply, manual smoke test

**Files:** none (verification only)

- [ ] **Step 1: Full solution build**

Run: `dotnet build -v q`
Expected: `Build succeeded. 0 Error(s)` across all projects.

- [ ] **Step 2: Full server test suite**

Run: `dotnet test tests/Server.Tests/Stallions.Server.Tests.csproj -v q`
Expected: PASS — all tests green (existing + new TermsService/UserServiceTerms/BidService tests).

- [ ] **Step 3: Apply the migration locally (optional sanity)**

The app applies migrations on startup (`db.Database.Migrate()` in `Program.cs`). For a local DB check:
Run: `dotnet ef database update --project src/Server/Stallions.Server.csproj`
Expected: the `AddTermsDocumentAndUserAcceptance` migration applies cleanly.

- [ ] **Step 4: Manual smoke test (record results)**

Run the app and verify the full path:
1. As **Staff**, go to `/staff/terms`, enter Markdown, click "Publish new version" → version becomes 1.
2. As a **verified buyer** who has not accepted, log in → the blocking `TermsAgreementModal` appears; the Agree button is disabled until you scroll to the bottom; agreeing dismisses it.
3. As that buyer, place a bid → the `BidConfirmationModal` appears; confirm (without ticking) → bid succeeds; place another bid → modal appears again. Tick "Don't show again" + confirm → subsequent bids skip the modal.
4. As Staff, publish a new T&C version → the same buyer is re-prompted on next page load before they can bid again.
5. Confirm a buyer who hasn't accepted is rejected server-side if they bypass the UI (the bid API returns 400 with the T&C message).

- [ ] **Step 5: Commit any smoke-test fixes, then finish**

Use `superpowers:finishing-a-development-branch` to complete the work (this branch is `feature/stallion-authorization`; the user deploys to the `dev` azd environment).

---

## Self-Review Notes

- **Spec coverage:** versioned T&C store (Tasks 2–5) ✓; staff editor (Task 14) ✓; User acceptance fields + audit (Tasks 2, 6) ✓; server bid guard incl. no-T&C skip (Task 7) ✓; MainLayout gate after verification (Task 12) ✓; scroll-to-bottom (Task 10) ✓; per-bid confirmation + permanent opt-out (Tasks 6, 11, 13) ✓; Markdown rendering (Tasks 8, 10, 14) ✓; buyers-only / re-prompt on version bump (Tasks 9, 12) ✓; tests (Tasks 4, 6, 7) ✓.
- **Type consistency:** `TermsDocumentDto { Id, Version, Body, CreatedAt }`, `AcceptTermsRequest { Version }`, `PublishTermsRequest { Body }`, `UserDto.AcceptedTermsVersion`/`SuppressBidConfirmation`, `ITermsRepository.GetCurrentAsync/GetHistoryAsync/AddAsync`, `IUserService.AcceptTermsAsync/SuppressBidConfirmationAsync`, `BidService` 5-arg constructor — all consistent across tasks.
- **Constructor-change risk flagged:** `UserService` (3→4 args, Task 6 Step 8) and `BidService` (4→5 args, Task 7 Step 1) both change constructors; the plan updates the affected test helpers and runs the full suite to catch fallout.
