# Staff Admin Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `/staff/*` section of the Blazor WASM app so Stallions Australia staff can verify buyers, onboard stud farms, oversee all listings, view transactions, and generate per-farm remittance invoices.

**Architecture:** New `StaffLayout.razor` with a six-item sidebar mirrors the existing `AdminLayout.razor` pattern but lives at `/staff/*` with `[Authorize(Roles = "Staff")]`. A new `StaffApiService` (authenticated, separate from `AdminApiService`) covers all staff API calls. The server gets four new `AdminController` endpoints backed by three new `AdminService` methods and two new repository methods.

**Tech Stack:** Blazor WASM (.NET 9), ASP.NET Core Web API, Entity Framework Core, xUnit + Moq + FluentAssertions

---

## File Map

| Action | Path | Purpose |
|---|---|---|
| Create | `src/Shared/DTOs/Admin/StudFarmSummaryDto.cs` | Staff view of a stud farm record |
| Create | `src/Shared/DTOs/Admin/CreateStudFarmRequest.cs` | POST body for onboarding a new farm |
| Create | `src/Shared/DTOs/Admin/ForceListingStatusRequest.cs` | POST body for status override |
| Create | `src/Shared/DTOs/Admin/ListingStaffSummaryDto.cs` | Staff all-listings row |
| Modify | `src/Server/Data/Repositories/IStudFarmRepository.cs` | Add `GetAllAsync()` |
| Modify | `src/Server/Data/Repositories/StudFarmRepository.cs` | Implement `GetAllAsync()` |
| Modify | `src/Server/Data/Repositories/IListingRepository.cs` | Add `GetAllStaffAsync()` |
| Modify | `src/Server/Data/Repositories/ListingRepository.cs` | Implement `GetAllStaffAsync()` |
| Modify | `src/Server/Services/IAdminService.cs` | Add 3 new method signatures |
| Modify | `src/Server/Services/AdminService.cs` | Implement 3 new methods; add `IStudFarmRepository` ctor param |
| Modify | `src/Server/Controllers/AdminController.cs` | Add 4 new endpoints |
| Create | `src/Client/Services/StaffApiService.cs` | Authenticated client for all `/api/admin/staff*` calls |
| Modify | `src/Client/Program.cs` | Register `StaffApiService` |
| Create | `src/Client/Layout/StaffLayout.razor` | Sidebar layout for `/staff/*` |
| Create | `src/Client/Layout/StaffLayout.razor.css` | Scoped styles (reuses admin CSS class names) |
| Modify | `src/Client/_Imports.razor` | Add `@using Stallions.Client.Pages.Staff` |
| Create | `src/Client/Pages/Staff/StaffDashboard.razor` | `/staff/dashboard` |
| Create | `src/Client/Pages/Staff/StaffUsers.razor` | `/staff/users` |
| Create | `src/Client/Pages/Staff/StaffUserDetail.razor` | `/staff/users/{id}` |
| Create | `src/Client/Pages/Staff/StaffStudFarms.razor` | `/staff/studfarms` |
| Create | `src/Client/Pages/Staff/StaffStudFarmNew.razor` | `/staff/studfarms/new` |
| Create | `src/Client/Pages/Staff/StaffListings.razor` | `/staff/listings` |
| Create | `src/Client/Pages/Staff/StaffTransactions.razor` | `/staff/transactions` |
| Create | `src/Client/Pages/Staff/StaffInvoices.razor` | `/staff/invoices` |
| Modify | `tests/Server.Tests/Services/AdminServiceTests.cs` | Tests for 3 new service methods |

---

## Task 1: Shared DTOs

**Files:**
- Create: `src/Shared/DTOs/Admin/StudFarmSummaryDto.cs`
- Create: `src/Shared/DTOs/Admin/CreateStudFarmRequest.cs`
- Create: `src/Shared/DTOs/Admin/ForceListingStatusRequest.cs`
- Create: `src/Shared/DTOs/Admin/ListingStaffSummaryDto.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Server.Tests/Shared/AdminDtoTests.cs
using Stallions.Shared.DTOs.Admin;

namespace Stallions.Server.Tests.Shared;

public class AdminDtoTests
{
    [Fact]
    public void StudFarmSummaryDto_DefaultsAreCorrect()
    {
        var dto = new StudFarmSummaryDto();
        Assert.Equal(string.Empty, dto.Name);
        Assert.Equal(string.Empty, dto.LinkedUserDisplayName);
        Assert.Equal(string.Empty, dto.LinkedUserEmail);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public void CreateStudFarmRequest_DefaultsAreCorrect()
    {
        var req = new CreateStudFarmRequest();
        Assert.Equal(Guid.Empty, req.UserId);
        Assert.Equal(string.Empty, req.Name);
    }

    [Fact]
    public void ForceListingStatusRequest_DefaultsAreCorrect()
    {
        var req = new ForceListingStatusRequest();
        Assert.Equal(string.Empty, req.Status);
        Assert.Null(req.Reason);
    }

    [Fact]
    public void ListingStaffSummaryDto_DefaultsAreCorrect()
    {
        var dto = new ListingStaffSummaryDto();
        Assert.Equal(string.Empty, dto.StallionName);
        Assert.Equal(string.Empty, dto.StudFarmName);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test tests/Server.Tests/Server.Tests.csproj --filter "AdminDtoTests" -v n
```

Expected: FAIL — types not yet defined.

- [ ] **Step 3: Create the DTOs**

`src/Shared/DTOs/Admin/StudFarmSummaryDto.cs`:

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
    public DateTime CreatedAt { get; set; }
}
```

`src/Shared/DTOs/Admin/CreateStudFarmRequest.cs`:

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
}
```

`src/Shared/DTOs/Admin/ForceListingStatusRequest.cs`:

```csharp
namespace Stallions.Shared.DTOs.Admin;

public class ForceListingStatusRequest
{
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
```

`src/Shared/DTOs/Admin/ListingStaffSummaryDto.cs`:

```csharp
namespace Stallions.Shared.DTOs.Admin;

public class ListingStaffSummaryDto
{
    public Guid Id { get; set; }
    public string StallionName { get; set; } = string.Empty;
    public string StudFarmName { get; set; } = string.Empty;
    public string ListingType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal? PriceIncGst { get; set; }
    public decimal? PlatformFeePercent { get; set; }
    public DateTime? PublishedAt { get; set; }
}
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet test tests/Server.Tests/Server.Tests.csproj --filter "AdminDtoTests" -v n
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Shared/DTOs/Admin/StudFarmSummaryDto.cs \
        src/Shared/DTOs/Admin/CreateStudFarmRequest.cs \
        src/Shared/DTOs/Admin/ForceListingStatusRequest.cs \
        src/Shared/DTOs/Admin/ListingStaffSummaryDto.cs \
        tests/Server.Tests/Shared/AdminDtoTests.cs
git commit -m "feat: add staff admin shared DTOs (Plan 5)"
```

---

## Task 2: Repository Extensions

**Files:**
- Modify: `src/Server/Data/Repositories/IStudFarmRepository.cs`
- Modify: `src/Server/Data/Repositories/StudFarmRepository.cs`
- Modify: `src/Server/Data/Repositories/IListingRepository.cs`
- Modify: `src/Server/Data/Repositories/ListingRepository.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Server.Tests/Data/Repositories/StudFarmRepositoryGetAllTests.cs
using FluentAssertions;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;

namespace Stallions.Server.Tests.Data.Repositories;

public class StudFarmRepositoryGetAllTests
{
    [Fact]
    public async Task GetAllAsync_ReturnsAllFarms_WithUserNavigation()
    {
        await using var db = DbContextFactory.Create(nameof(GetAllAsync_ReturnsAllFarms_WithUserNavigation));

        var user1 = new User { Id = Guid.NewGuid(), DisplayName = "Alice", Email = "alice@test.com" };
        var user2 = new User { Id = Guid.NewGuid(), DisplayName = "Bob", Email = "bob@test.com" };
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
```

- [ ] **Step 2: Run to verify they fail**

```
dotnet test tests/Server.Tests/Server.Tests.csproj --filter "StudFarmRepositoryGetAllTests" -v n
```

Expected: FAIL — `GetAllAsync` not defined on interface.

- [ ] **Step 3: Add `GetAllAsync()` to the interface and implement it**

`src/Server/Data/Repositories/IStudFarmRepository.cs` — add one method:

```csharp
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IStudFarmRepository
{
    Task<StudFarm?> GetByIdAsync(Guid id);
    Task<StudFarm?> GetByUserIdAsync(Guid userId);
    Task<IReadOnlyList<StudFarm>> GetAllAsync();
    Task<StudFarm> AddAsync(StudFarm studFarm);
    Task UpdateAsync(StudFarm studFarm);
}
```

`src/Server/Data/Repositories/StudFarmRepository.cs` — add the implementation (full file):

```csharp
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class StudFarmRepository : IStudFarmRepository
{
    private readonly AppDbContext _db;
    public StudFarmRepository(AppDbContext db) => _db = db;

    public async Task<StudFarm?> GetByIdAsync(Guid id) =>
        await _db.StudFarms.FindAsync(id);

    public async Task<StudFarm?> GetByUserIdAsync(Guid userId) =>
        await _db.StudFarms.FirstOrDefaultAsync(f => f.UserId == userId);

    public async Task<IReadOnlyList<StudFarm>> GetAllAsync() =>
        await _db.StudFarms
            .Include(f => f.User)
            .OrderBy(f => f.Name)
            .ToListAsync();

    public async Task<StudFarm> AddAsync(StudFarm studFarm)
    {
        _db.StudFarms.Add(studFarm);
        await _db.SaveChangesAsync();
        return studFarm;
    }

    public async Task UpdateAsync(StudFarm studFarm)
    {
        _db.StudFarms.Update(studFarm);
        await _db.SaveChangesAsync();
    }
}
```

`src/Server/Data/Repositories/IListingRepository.cs` — add one method:

```csharp
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public interface IListingRepository
{
    Task<Listing?> GetByIdAsync(Guid id);
    Task<AuctionListing?> GetAuctionByIdAsync(Guid id);
    Task<FixedPriceListing?> GetFixedPriceByIdAsync(Guid id);
    Task<IReadOnlyList<Listing>> GetActiveAsync(Guid? seasonId = null, Guid? studFarmId = null, ListingType? type = null);
    Task<Dictionary<Guid, (int Count, decimal? Highest)>> GetBidAggregatesAsync(IEnumerable<Guid> auctionIds);
    Task<IReadOnlyList<Listing>> GetByStudFarmIdAsync(Guid studFarmId);
    Task<IReadOnlyList<AuctionListing>> GetExpiredAuctionsAsync();
    Task<IReadOnlyList<Listing>> GetAllStaffAsync();
    Task<Listing> AddAsync(Listing listing);
    Task UpdateAsync(Listing listing);
}
```

`src/Server/Data/Repositories/ListingRepository.cs` — add `GetAllStaffAsync()` before `AddAsync`. Insert after `GetExpiredAuctionsAsync`:

```csharp
    public async Task<IReadOnlyList<Listing>> GetAllStaffAsync() =>
        await _db.Listings
            .Include(l => l.Stallion)
            .Include(l => l.StudFarm)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
```

- [ ] **Step 4: Run all tests to verify they pass**

```
dotnet test tests/Server.Tests/Server.Tests.csproj --filter "StudFarmRepositoryGetAllTests" -v n
```

Expected: PASS. Then confirm no regressions:

```
dotnet test tests/Server.Tests/Server.Tests.csproj -v n
```

Expected: all previously passing tests still pass.

- [ ] **Step 5: Commit**

```bash
git add src/Server/Data/Repositories/IStudFarmRepository.cs \
        src/Server/Data/Repositories/StudFarmRepository.cs \
        src/Server/Data/Repositories/IListingRepository.cs \
        src/Server/Data/Repositories/ListingRepository.cs \
        tests/Server.Tests/Data/Repositories/StudFarmRepositoryGetAllTests.cs
git commit -m "feat: add GetAllAsync to StudFarmRepository and GetAllStaffAsync to ListingRepository (Plan 5)"
```

---

## Task 3: AdminService Extensions

**Files:**
- Modify: `src/Server/Services/IAdminService.cs`
- Modify: `src/Server/Services/AdminService.cs`
- Modify: `tests/Server.Tests/Services/AdminServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `tests/Server.Tests/Services/AdminServiceTests.cs`:

```csharp
// Add these fields to the AdminServiceTests class:
//   private readonly Mock<IStudFarmRepository> _studFarmRepoMock = new();

// Update CreateSut() to pass _studFarmRepoMock.Object as a new first argument
// (see Step 3 below for the updated constructor signature).

// Add these test methods inside the AdminServiceTests class:

[Fact]
public async Task GetAllStudFarmsAsync_ReturnsMappedDtos()
{
    var user = new User { Id = Guid.NewGuid(), DisplayName = "Alice", Email = "alice@test.com" };
    var farm = new StudFarm
    {
        Id = Guid.NewGuid(), Name = "Alpha Stud", ABN = "123", ContactEmail = "farm@test.com",
        UserId = user.Id, User = user, IsActive = true, CreatedAt = DateTime.UtcNow
    };
    _studFarmRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<StudFarm> { farm });

    var result = await CreateSut().GetAllStudFarmsAsync();

    result.Succeeded.Should().BeTrue();
    result.Value.Should().HaveCount(1);
    result.Value![0].Name.Should().Be("Alpha Stud");
    result.Value![0].LinkedUserDisplayName.Should().Be("Alice");
    result.Value![0].LinkedUserEmail.Should().Be("alice@test.com");
}

[Fact]
public async Task CreateStudFarmAsync_WhenUserNotFound_ReturnsNotFound()
{
    _userRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

    var result = await CreateSut().CreateStudFarmAsync(new CreateStudFarmRequest
    {
        UserId = Guid.NewGuid(), Name = "New Farm"
    });

    result.Succeeded.Should().BeFalse();
    result.HttpStatusCode.Should().Be(404);
}

[Fact]
public async Task CreateStudFarmAsync_WhenUserIsNotStudFarmAdmin_ReturnsBadRequest()
{
    var user = new User { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.Active };
    _userRepoMock.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

    var result = await CreateSut().CreateStudFarmAsync(new CreateStudFarmRequest
    {
        UserId = user.Id, Name = "New Farm"
    });

    result.Succeeded.Should().BeFalse();
    result.HttpStatusCode.Should().Be(400);
}

[Fact]
public async Task CreateStudFarmAsync_WhenUserAlreadyHasFarm_ReturnsBadRequest()
{
    var user = new User { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };
    _userRepoMock.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
    _studFarmRepoMock.Setup(r => r.GetByUserIdAsync(user.Id))
        .ReturnsAsync(new StudFarm { Id = Guid.NewGuid(), UserId = user.Id, Name = "Existing" });

    var result = await CreateSut().CreateStudFarmAsync(new CreateStudFarmRequest
    {
        UserId = user.Id, Name = "New Farm"
    });

    result.Succeeded.Should().BeFalse();
    result.HttpStatusCode.Should().Be(400);
}

[Fact]
public async Task CreateStudFarmAsync_WhenValid_CreatesFarmAndAuditLogs()
{
    var user = new User
    {
        Id = Guid.NewGuid(), DisplayName = "Alice", Email = "alice@test.com",
        Role = UserRole.StudFarmAdmin, Status = UserStatus.Active
    };
    _userRepoMock.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
    _studFarmRepoMock.Setup(r => r.GetByUserIdAsync(user.Id)).ReturnsAsync((StudFarm?)null);
    _studFarmRepoMock.Setup(r => r.AddAsync(It.IsAny<StudFarm>()))
        .ReturnsAsync((StudFarm f) => f);
    _userServiceMock.Setup(u => u.GetOrCreateCurrentUserAsync())
        .ReturnsAsync(new User { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active });

    var result = await CreateSut().CreateStudFarmAsync(new CreateStudFarmRequest
    {
        UserId = user.Id, Name = "Alpha Stud", ABN = "123456789"
    });

    result.Succeeded.Should().BeTrue();
    result.Value!.Name.Should().Be("Alpha Stud");
    _auditRepoMock.Verify(r => r.LogAsync("StudFarm", It.IsAny<Guid>(), "CreateStudFarm",
        It.IsAny<Guid?>(), It.IsAny<string?>()), Times.Once);
}

[Fact]
public async Task ForceListingStatusAsync_WhenListingNotFound_ReturnsNotFound()
{
    _listingRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Listing?)null);
    _userServiceMock.Setup(u => u.GetOrCreateCurrentUserAsync())
        .ReturnsAsync(new User { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active });

    var result = await CreateSut().ForceListingStatusAsync(Guid.NewGuid(),
        new ForceListingStatusRequest { Status = "Closed" });

    result.Succeeded.Should().BeFalse();
    result.HttpStatusCode.Should().Be(404);
}

[Fact]
public async Task ForceListingStatusAsync_WhenInvalidStatus_ReturnsBadRequest()
{
    var listing = new AuctionListing { Id = Guid.NewGuid(), Status = ListingStatus.Draft };
    _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
    _userServiceMock.Setup(u => u.GetOrCreateCurrentUserAsync())
        .ReturnsAsync(new User { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active });

    var result = await CreateSut().ForceListingStatusAsync(listing.Id,
        new ForceListingStatusRequest { Status = "NotAStatus" });

    result.Succeeded.Should().BeFalse();
    result.HttpStatusCode.Should().Be(400);
}

[Fact]
public async Task ForceListingStatusAsync_WhenValid_SetsStatusAndAuditLogs()
{
    var listing = new AuctionListing { Id = Guid.NewGuid(), Status = ListingStatus.Draft };
    _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
    _userServiceMock.Setup(u => u.GetOrCreateCurrentUserAsync())
        .ReturnsAsync(new User { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active });

    var result = await CreateSut().ForceListingStatusAsync(listing.Id,
        new ForceListingStatusRequest { Status = "Closed", Reason = "Quality issue" });

    result.Succeeded.Should().BeTrue();
    listing.Status.Should().Be(ListingStatus.Closed);
    _auditRepoMock.Verify(r => r.LogAsync("Listing", listing.Id, "ForceListingStatus",
        It.IsAny<Guid?>(), It.Is<string?>(s => s!.Contains("Quality issue"))), Times.Once);
}
```

- [ ] **Step 2: Run to verify they fail**

```
dotnet test tests/Server.Tests/Server.Tests.csproj --filter "AdminServiceTests" -v n
```

Expected: FAIL — new methods not on the service yet.

- [ ] **Step 3: Update `IAdminService` with the three new method signatures**

Full `src/Server/Services/IAdminService.cs`:

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
    Task<ServiceResult<StudFarmSummaryDto>> CreateStudFarmAsync(CreateStudFarmRequest request);
    Task<ServiceResult<IReadOnlyList<ListingStaffSummaryDto>>> GetAllListingsStaffAsync();
    Task<ServiceResult> ForceListingStatusAsync(Guid listingId, ForceListingStatusRequest request);
}
```

- [ ] **Step 4: Implement the three new methods in `AdminService`**

Full `src/Server/Services/AdminService.cs`:

```csharp
using Stallions.Server.Auth;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.Admin;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public class AdminService : IAdminService
{
    private readonly IListingRepository _listingRepo;
    private readonly IPurchaseRepository _purchaseRepo;
    private readonly IUserRepository _userRepo;
    private readonly IStudFarmRepository _studFarmRepo;
    private readonly IAuditLogRepository _auditRepo;
    private readonly ICurrentUserService _currentUser;
    private readonly IUserService _users;

    public AdminService(
        IListingRepository listingRepo,
        IPurchaseRepository purchaseRepo,
        IUserRepository userRepo,
        IStudFarmRepository studFarmRepo,
        IAuditLogRepository auditRepo,
        ICurrentUserService currentUser,
        IUserService users)
    {
        _listingRepo = listingRepo;
        _purchaseRepo = purchaseRepo;
        _userRepo = userRepo;
        _studFarmRepo = studFarmRepo;
        _auditRepo = auditRepo;
        _currentUser = currentUser;
        _users = users;
    }

    public async Task<ServiceResult<DashboardDto>> GetDashboardAsync()
    {
        var activeListings = await _listingRepo.GetActiveAsync();
        var allPurchases = await _purchaseRepo.GetAllAsync();
        var pendingUsers = await _userRepo.GetAllAsync(status: UserStatus.PendingVerification);

        var cutoff = DateTime.UtcNow.AddDays(-30);
        var recentCompleted = allPurchases
            .Where(p => p.Status == PurchaseStatus.Completed && p.PaidAt >= cutoff)
            .ToList();

        var dto = new DashboardDto
        {
            ActiveListingCount = activeListings.Count,
            AuctionListingCount = activeListings.Count(l => l.ListingType == ListingType.Auction),
            FixedPriceListingCount = activeListings.Count(l => l.ListingType == ListingType.FixedPrice),
            RecentPurchaseCount = recentCompleted.Count,
            RecentFeeRevenueIncGst = recentCompleted.Sum(p => p.PlatformFeeIncGst),
            PendingVerificationCount = pendingUsers.Count
        };
        return ServiceResult<DashboardDto>.Ok(dto);
    }

    public async Task<ServiceResult<IReadOnlyList<TransactionDto>>> GetTransactionsAsync()
    {
        var purchases = await _purchaseRepo.GetAllAsync();
        var dtos = purchases.Select(p => new TransactionDto
        {
            PurchaseId = p.Id,
            StallionName = p.Listing?.Stallion?.Name ?? string.Empty,
            BuyerDisplayName = p.Buyer?.DisplayName ?? string.Empty,
            StudFarmName = p.Listing?.StudFarm?.Name ?? string.Empty,
            TotalPriceIncGst = p.TotalPriceIncGst,
            PlatformFeeIncGst = p.PlatformFeeIncGst,
            PlatformFeeExGst = p.PlatformFeeExGst,
            PlatformFeeGst = p.PlatformFeeGst,
            PaidAt = p.PaidAt,
            Status = p.Status.ToString()
        }).ToList();
        return ServiceResult<IReadOnlyList<TransactionDto>>.Ok(dtos);
    }

    public async Task<ServiceResult<IReadOnlyList<InvoiceDto>>> GetInvoicesAsync()
    {
        var purchases = await _purchaseRepo.GetAllAsync();
        var completed = purchases
            .Where(p => p.Status == PurchaseStatus.Completed && p.PaidAt.HasValue)
            .ToList();

        var invoices = completed
            .GroupBy(p => p.Listing?.StudFarmId ?? Guid.Empty)
            .Select(g => new InvoiceDto
            {
                StudFarmId = g.Key,
                StudFarmName = g.First().Listing?.StudFarm?.Name ?? string.Empty,
                Lines = g.Select(p => new InvoiceLineDto
                {
                    PurchaseId = p.Id,
                    StallionName = p.Listing?.Stallion?.Name ?? string.Empty,
                    SalePriceIncGst = p.TotalPriceIncGst,
                    PlatformFeeIncGst = p.PlatformFeeIncGst,
                    RemittanceAmount = p.TotalPriceIncGst - p.PlatformFeeIncGst,
                    PaidAt = p.PaidAt!.Value
                }).ToList(),
                TotalSalesIncGst = g.Sum(p => p.TotalPriceIncGst),
                TotalPlatformFeesIncGst = g.Sum(p => p.PlatformFeeIncGst),
                TotalRemittance = g.Sum(p => p.TotalPriceIncGst - p.PlatformFeeIncGst)
            }).ToList();

        return ServiceResult<IReadOnlyList<InvoiceDto>>.Ok(invoices);
    }

    public async Task<ServiceResult> SetListingFeeAsync(Guid listingId, SetListingFeeRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult.Forbidden();

        if (request.PlatformFeePercent < 0 || request.PlatformFeePercent > 100)
            return ServiceResult.BadRequest("Fee percent must be between 0 and 100.");

        var listing = await _listingRepo.GetByIdAsync(listingId);
        if (listing == null) return ServiceResult.NotFound("Listing not found.");

        var previousFee = listing.PlatformFeePercent;
        listing.PlatformFeePercent = request.PlatformFeePercent;
        await _listingRepo.UpdateAsync(listing);

        await _auditRepo.LogAsync(
            "Listing",
            listingId,
            "SetListingFee",
            caller.Id,
            $"Fee changed from {previousFee?.ToString() ?? "unset"} to {request.PlatformFeePercent}");

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<IReadOnlyList<StudFarmSummaryDto>>> GetAllStudFarmsAsync()
    {
        var farms = await _studFarmRepo.GetAllAsync();
        var dtos = farms.Select(f => new StudFarmSummaryDto
        {
            Id = f.Id,
            Name = f.Name,
            ABN = f.ABN,
            ContactEmail = f.ContactEmail,
            LinkedUserDisplayName = f.User?.DisplayName ?? string.Empty,
            LinkedUserEmail = f.User?.Email ?? string.Empty,
            IsActive = f.IsActive,
            CreatedAt = f.CreatedAt
        }).ToList();
        return ServiceResult<IReadOnlyList<StudFarmSummaryDto>>.Ok(dtos);
    }

    public async Task<ServiceResult<StudFarmSummaryDto>> CreateStudFarmAsync(CreateStudFarmRequest request)
    {
        var user = await _userRepo.GetByIdAsync(request.UserId);
        if (user == null)
            return ServiceResult<StudFarmSummaryDto>.NotFound("User not found.");

        if (user.Role != UserRole.StudFarmAdmin)
            return ServiceResult<StudFarmSummaryDto>.BadRequest(
                "User must have the StudFarmAdmin role.");

        var existing = await _studFarmRepo.GetByUserIdAsync(request.UserId);
        if (existing != null)
            return ServiceResult<StudFarmSummaryDto>.BadRequest(
                "This user already has a stud farm linked to their account.");

        var caller = await _users.GetOrCreateCurrentUserAsync();

        var farm = new StudFarm
        {
            UserId = request.UserId,
            Name = request.Name,
            ABN = request.ABN,
            ContactPhone = request.ContactPhone,
            ContactEmail = request.ContactEmail,
            Address = request.Address
        };

        farm = await _studFarmRepo.AddAsync(farm);

        await _auditRepo.LogAsync(
            "StudFarm",
            farm.Id,
            "CreateStudFarm",
            caller?.Id,
            $"Farm '{farm.Name}' created and linked to user {user.Email}");

        var dto = new StudFarmSummaryDto
        {
            Id = farm.Id,
            Name = farm.Name,
            ABN = farm.ABN,
            ContactEmail = farm.ContactEmail,
            LinkedUserDisplayName = user.DisplayName,
            LinkedUserEmail = user.Email,
            IsActive = farm.IsActive,
            CreatedAt = farm.CreatedAt
        };
        return ServiceResult<StudFarmSummaryDto>.Ok(dto);
    }

    public async Task<ServiceResult<IReadOnlyList<ListingStaffSummaryDto>>> GetAllListingsStaffAsync()
    {
        var listings = await _listingRepo.GetAllStaffAsync();
        var dtos = listings.Select(l =>
        {
            decimal? price = l switch
            {
                FixedPriceListing fp => fp.PriceIncGst,
                AuctionListing al => al.StartingPrice,
                _ => null
            };
            return new ListingStaffSummaryDto
            {
                Id = l.Id,
                StallionName = l.Stallion?.Name ?? string.Empty,
                StudFarmName = l.StudFarm?.Name ?? string.Empty,
                ListingType = l.ListingType.ToString(),
                Status = l.Status.ToString(),
                PriceIncGst = price,
                PlatformFeePercent = l.PlatformFeePercent,
                PublishedAt = l.PublishedAt
            };
        }).ToList();
        return ServiceResult<IReadOnlyList<ListingStaffSummaryDto>>.Ok(dtos);
    }

    public async Task<ServiceResult> ForceListingStatusAsync(Guid listingId, ForceListingStatusRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();

        var listing = await _listingRepo.GetByIdAsync(listingId);
        if (listing == null) return ServiceResult.NotFound("Listing not found.");

        if (!Enum.TryParse<ListingStatus>(request.Status, ignoreCase: true, out var newStatus))
            return ServiceResult.BadRequest($"'{request.Status}' is not a valid listing status.");

        var previousStatus = listing.Status;
        listing.Status = newStatus;
        await _listingRepo.UpdateAsync(listing);

        await _auditRepo.LogAsync(
            "Listing",
            listingId,
            "ForceListingStatus",
            caller?.Id,
            $"Status forced from {previousStatus} to {newStatus}. Reason: {request.Reason ?? "none"}");

        return ServiceResult.Ok();
    }
}
```

- [ ] **Step 5: Update the test class to add the new mock and updated `CreateSut()`**

Replace the top of `tests/Server.Tests/Services/AdminServiceTests.cs` class body:

```csharp
public class AdminServiceTests
{
    private readonly Mock<IListingRepository> _listingRepoMock = new();
    private readonly Mock<IPurchaseRepository> _purchaseRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IStudFarmRepository> _studFarmRepoMock = new();
    private readonly Mock<IAuditLogRepository> _auditRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<IUserService> _userServiceMock = new();

    private AdminService CreateSut() => new(
        _listingRepoMock.Object,
        _purchaseRepoMock.Object,
        _userRepoMock.Object,
        _studFarmRepoMock.Object,
        _auditRepoMock.Object,
        _currentUserMock.Object,
        _userServiceMock.Object);

    // ... existing and new test methods ...
}
```

- [ ] **Step 6: Run all admin service tests to verify they pass**

```
dotnet test tests/Server.Tests/Server.Tests.csproj --filter "AdminServiceTests" -v n
```

Expected: all tests PASS (old + new).

- [ ] **Step 7: Commit**

```bash
git add src/Server/Services/IAdminService.cs \
        src/Server/Services/AdminService.cs \
        tests/Server.Tests/Services/AdminServiceTests.cs
git commit -m "feat: extend AdminService with GetAllStudFarms, CreateStudFarm, ForceListingStatus (Plan 5)"
```

---

## Task 4: AdminController New Endpoints

**Files:**
- Modify: `src/Server/Controllers/AdminController.cs`

The controller is thin — it delegates to the service. No additional tests are needed beyond the service tests already written (controller integration tests for existing endpoints were established in Plan 2).

- [ ] **Step 1: Add four endpoints to `AdminController`**

Full `src/Server/Controllers/AdminController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Admin;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Staff")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _admin;
    public AdminController(IAdminService admin) => _admin = admin;

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        var r = await _admin.GetDashboardAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactions()
    {
        var r = await _admin.GetTransactionsAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("invoices")]
    public async Task<IActionResult> GetInvoices()
    {
        var r = await _admin.GetInvoicesAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPut("listings/{id:guid}/fee")]
    public async Task<IActionResult> SetListingFee(Guid id, [FromBody] SetListingFeeRequest request)
    {
        var r = await _admin.SetListingFeeAsync(id, request);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("studfarms")]
    public async Task<IActionResult> GetStudFarms()
    {
        var r = await _admin.GetAllStudFarmsAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("studfarms")]
    public async Task<IActionResult> CreateStudFarm([FromBody] CreateStudFarmRequest request)
    {
        var r = await _admin.CreateStudFarmAsync(request);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("listings")]
    public async Task<IActionResult> GetListings()
    {
        var r = await _admin.GetAllListingsStaffAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("listings/{id:guid}/force-status")]
    public async Task<IActionResult> ForceListingStatus(Guid id, [FromBody] ForceListingStatusRequest request)
    {
        var r = await _admin.ForceListingStatusAsync(id, request);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }
}
```

- [ ] **Step 2: Register `IStudFarmRepository` in DI (check Program.cs)**

Open `src/Server/Program.cs` and verify `IStudFarmRepository` / `StudFarmRepository` is already registered. It was added in Plan 2. If missing, add:

```csharp
builder.Services.AddScoped<IStudFarmRepository, StudFarmRepository>();
```

Also add `IStudFarmRepository` to the `AdminService` DI registration. Look for the line that registers `AdminService` (it may use `AddScoped<IAdminService, AdminService>()`) — the DI container auto-resolves constructor parameters, so no change is needed as long as `IStudFarmRepository` is registered.

- [ ] **Step 3: Build the server to confirm no compile errors**

```
dotnet build src/Server/Server.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run full test suite**

```
dotnet test tests/Server.Tests/Server.Tests.csproj -v n
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Server/Controllers/AdminController.cs
git commit -m "feat: add studfarms, listings, and force-status endpoints to AdminController (Plan 5)"
```

---

## Task 5: StaffApiService + Program.cs Registration

**Files:**
- Create: `src/Client/Services/StaffApiService.cs`
- Modify: `src/Client/Program.cs`

- [ ] **Step 1: Create `StaffApiService.cs`**

```csharp
// src/Client/Services/StaffApiService.cs
using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Admin;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Services;

/// <summary>
/// Authenticated service for all Stallions Australia staff operations.
/// Uses BaseAddressAuthorizationMessageHandler — always requires a Bearer token.
/// Kept completely separate from AdminApiService (stud farm admin) and public browse services.
/// </summary>
public class StaffApiService
{
    private readonly HttpClient _http;
    public StaffApiService(HttpClient http) => _http = http;

    // ── Dashboard ──────────────────────────────────────────────────────────

    public virtual async Task<DashboardDto> GetDashboardAsync()
    {
        var r = await _http.GetAsync("api/admin/dashboard");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load dashboard.");
        return await r.Content.ReadFromJsonAsync<DashboardDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    // ── Users ──────────────────────────────────────────────────────────────

    public virtual async Task<List<UserDto>> GetUsersAsync()
    {
        var r = await _http.GetAsync("api/users");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load users.");
        return await r.Content.ReadFromJsonAsync<List<UserDto>>() ?? [];
    }

    public virtual async Task<UserDto> GetUserAsync(Guid id)
    {
        var r = await _http.GetAsync($"api/users/{id}");
        if (r.StatusCode == HttpStatusCode.NotFound)
            throw new ApiException(404, "User not found.");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load user.");
        return await r.Content.ReadFromJsonAsync<UserDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task VerifyUserAsync(Guid id)
    {
        var r = await _http.PostAsync($"api/users/{id}/verify", null);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to verify user.");
    }

    public virtual async Task SuspendUserAsync(Guid id)
    {
        var r = await _http.PostAsync($"api/users/{id}/suspend", null);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to suspend user.");
    }

    // ── Stud Farms ─────────────────────────────────────────────────────────

    public virtual async Task<List<StudFarmSummaryDto>> GetStudFarmsAsync()
    {
        var r = await _http.GetAsync("api/admin/studfarms");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load stud farms.");
        return await r.Content.ReadFromJsonAsync<List<StudFarmSummaryDto>>() ?? [];
    }

    public virtual async Task<StudFarmSummaryDto> CreateStudFarmAsync(CreateStudFarmRequest request)
    {
        var r = await _http.PostAsJsonAsync("api/admin/studfarms", request);
        if (!r.IsSuccessStatusCode)
        {
            var msg = await r.Content.ReadAsStringAsync();
            throw new ApiException((int)r.StatusCode, string.IsNullOrWhiteSpace(msg)
                ? "Failed to create stud farm." : msg);
        }
        return await r.Content.ReadFromJsonAsync<StudFarmSummaryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    // ── Listings ───────────────────────────────────────────────────────────

    public virtual async Task<List<ListingStaffSummaryDto>> GetAllListingsAsync()
    {
        var r = await _http.GetAsync("api/admin/listings");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load listings.");
        return await r.Content.ReadFromJsonAsync<List<ListingStaffSummaryDto>>() ?? [];
    }

    public virtual async Task SetListingFeeAsync(Guid id, decimal feePercent)
    {
        var r = await _http.PutAsJsonAsync($"api/admin/listings/{id}/fee",
            new { PlatformFeePercent = feePercent });
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to set listing fee.");
    }

    public virtual async Task ForceListingStatusAsync(Guid id, ForceListingStatusRequest request)
    {
        var r = await _http.PostAsJsonAsync($"api/admin/listings/{id}/force-status", request);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to override listing status.");
    }

    // ── Transactions ───────────────────────────────────────────────────────

    public virtual async Task<List<TransactionDto>> GetTransactionsAsync()
    {
        var r = await _http.GetAsync("api/admin/transactions");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load transactions.");
        return await r.Content.ReadFromJsonAsync<List<TransactionDto>>() ?? [];
    }

    // ── Invoices ───────────────────────────────────────────────────────────

    public virtual async Task<List<InvoiceDto>> GetInvoicesAsync()
    {
        var r = await _http.GetAsync("api/admin/invoices");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load invoices.");
        return await r.Content.ReadFromJsonAsync<List<InvoiceDto>>() ?? [];
    }

    // ── Users for stud farm onboarding dropdown ────────────────────────────

    /// <summary>
    /// Returns StudFarmAdmin users who have no existing farm linked.
    /// Used to populate the User dropdown on the new stud farm form.
    /// </summary>
    public virtual async Task<List<UserDto>> GetUnlinkedStudFarmAdminsAsync()
    {
        var all = await GetUsersAsync();
        return all.Where(u => u.Role == "StudFarmAdmin").ToList();
        // Filtering out already-linked admins happens server-side via CreateStudFarmAsync validation.
        // Here we show all StudFarmAdmins; the form submission will fail gracefully if one is already linked.
    }
}
```

- [ ] **Step 2: Register `StaffApiService` in `Program.cs`**

In `src/Client/Program.cs`, add after the `AdminApiService` registration:

```csharp
builder.Services.AddHttpClient<StaffApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
```

- [ ] **Step 3: Build the client to confirm no compile errors**

```
dotnet build src/Client/Client.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/Client/Services/StaffApiService.cs src/Client/Program.cs
git commit -m "feat: add StaffApiService and register in Program.cs (Plan 5)"
```

---

## Task 6: StaffLayout + _Imports Update

**Files:**
- Create: `src/Client/Layout/StaffLayout.razor`
- Create: `src/Client/Layout/StaffLayout.razor.css`
- Modify: `src/Client/_Imports.razor`

- [ ] **Step 1: Create `StaffLayout.razor`**

```razor
@* src/Client/Layout/StaffLayout.razor *@
@inherits LayoutComponentBase
@inject NavigationManager Nav

<div class="admin-shell">
    <aside class="admin-sidebar">
        <div class="admin-sidebar-header">
            <a href="/" class="admin-brand">Stallions Australia</a>
            <span class="admin-badge staff-badge">Staff</span>
        </div>
        <nav class="admin-nav">
            <NavLink href="/staff/dashboard" class="admin-nav-link" Match="NavLinkMatch.Prefix">
                <span class="admin-nav-icon">📊</span> Dashboard
            </NavLink>
            <NavLink href="/staff/users" class="admin-nav-link" Match="NavLinkMatch.Prefix">
                <span class="admin-nav-icon">👤</span> Users
            </NavLink>
            <NavLink href="/staff/studfarms" class="admin-nav-link" Match="NavLinkMatch.Prefix">
                <span class="admin-nav-icon">🏡</span> Stud Farms
            </NavLink>
            <NavLink href="/staff/listings" class="admin-nav-link" Match="NavLinkMatch.Prefix">
                <span class="admin-nav-icon">📋</span> Listings
            </NavLink>
            <NavLink href="/staff/transactions" class="admin-nav-link" Match="NavLinkMatch.Prefix">
                <span class="admin-nav-icon">💰</span> Transactions
            </NavLink>
            <NavLink href="/staff/invoices" class="admin-nav-link" Match="NavLinkMatch.Prefix">
                <span class="admin-nav-icon">🧾</span> Invoices
            </NavLink>
        </nav>
        <div class="admin-sidebar-footer">
            <a href="/" class="admin-nav-link">← Back to marketplace</a>
        </div>
    </aside>

    <main class="admin-content">
        <ErrorBoundary>
            <ChildContent>
                @Body
            </ChildContent>
            <ErrorContent Context="ex">
                <div class="admin-error">
                    <h3>Something went wrong</h3>
                    <p>An unexpected error occurred. <a href="/staff/dashboard">Return to Dashboard</a>.</p>
                </div>
            </ErrorContent>
        </ErrorBoundary>
    </main>
</div>
```

- [ ] **Step 2: Create `StaffLayout.razor.css`**

The layout reuses `AdminLayout`'s CSS class names (which live in `AdminLayout.razor.css` and are already in scope globally because Blazor scoped CSS for layouts applies to child content). We only need to override the badge colour so staff can be distinguished from stud farm admin.

```css
/* src/Client/Layout/StaffLayout.razor.css */

/* Gold badge is the default from AdminLayout.razor.css; staff gets a distinct amber-gold treatment */
.staff-badge {
    background: #c4993a;
    color: #fff;
    letter-spacing: 0.08em;
}
```

**Important note:** The `.admin-shell`, `.admin-sidebar`, `.admin-nav-link`, `.admin-content`, etc. styles come from `AdminLayout.razor.css`. Blazor scoped CSS does NOT leak between sibling layout files, so those classes will not be automatically in scope for `StaffLayout`. 

**Fix:** Extract the shared admin layout styles into a global CSS file. Add the following to `src/Client/wwwroot/css/app.css`, placing it after the existing `.btn-*` rules:

```css
/* ── Shared admin/staff shell layout ─────────────────────────────── */
.admin-shell {
    display: flex;
    min-height: 100vh;
}

.admin-sidebar {
    width: 240px;
    flex-shrink: 0;
    background: var(--navy);
    color: var(--white);
    display: flex;
    flex-direction: column;
    position: sticky;
    top: 0;
    height: 100vh;
    overflow-y: auto;
}

.admin-sidebar-header {
    padding: var(--space-6) var(--space-5);
    border-bottom: 1px solid rgba(255,255,255,0.1);
}

.admin-brand {
    display: block;
    color: var(--white);
    font-weight: 700;
    font-size: var(--font-size-sm);
    text-decoration: none;
    margin-bottom: var(--space-1);
}

.admin-badge {
    display: inline-block;
    background: var(--gold);
    color: var(--navy);
    font-size: 10px;
    font-weight: 700;
    padding: 2px 6px;
    border-radius: 3px;
    text-transform: uppercase;
    letter-spacing: 0.05em;
}

.admin-nav {
    flex: 1;
    padding: var(--space-4) 0;
}

.admin-nav-link {
    display: flex;
    align-items: center;
    gap: var(--space-3);
    padding: var(--space-3) var(--space-5);
    color: rgba(255,255,255,0.7);
    text-decoration: none;
    font-size: var(--font-size-sm);
    transition: background 0.15s, color 0.15s;
    position: relative;
}

.admin-nav-link:hover,
.admin-nav-link.active {
    background: rgba(255,255,255,0.1);
    color: var(--white);
}

.admin-nav-icon {
    font-size: 16px;
    width: 20px;
    text-align: center;
}

.admin-unread-badge {
    margin-left: auto;
    background: var(--gold);
    color: var(--navy);
    font-size: 11px;
    font-weight: 700;
    min-width: 20px;
    height: 20px;
    border-radius: 10px;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 0 5px;
}

.admin-sidebar-footer {
    padding: var(--space-4) 0;
    border-top: 1px solid rgba(255,255,255,0.1);
}

.admin-content {
    flex: 1;
    padding: var(--space-8);
    background: var(--cream);
    min-width: 0;
    overflow-x: auto;
}

.admin-error {
    padding: var(--space-8);
    text-align: center;
}
```

Then remove the duplicate rules from `AdminLayout.razor.css` — replace the entire file with just a comment:

```css
/* admin-shell layout styles have been moved to app.css so StaffLayout can share them. */
```

- [ ] **Step 3: Add `@using Stallions.Client.Pages.Staff` to `_Imports.razor`**

Append to `src/Client/_Imports.razor`:

```razor
@using Stallions.Shared.DTOs.Admin
@using Stallions.Client.Pages.Staff
```

(Add `@using Stallions.Shared.DTOs.Admin` only if it isn't already present — check the file first.)

- [ ] **Step 4: Build to confirm no compile errors**

```
dotnet build src/Client/Client.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Client/Layout/StaffLayout.razor \
        src/Client/Layout/StaffLayout.razor.css \
        src/Client/Layout/AdminLayout.razor.css \
        src/Client/wwwroot/css/app.css \
        src/Client/_Imports.razor
git commit -m "feat: add StaffLayout and move shared admin CSS to app.css (Plan 5)"
```

---

## Task 7: Staff Dashboard Page

**Files:**
- Create: `src/Client/Pages/Staff/StaffDashboard.razor`

- [ ] **Step 1: Create the page**

```razor
@* src/Client/Pages/Staff/StaffDashboard.razor *@
@page "/staff/dashboard"
@layout StaffLayout
@attribute [Authorize(Roles = "Staff")]
@inject StaffApiService StaffApi

<PageTitle>Staff Dashboard — Stallions Australia</PageTitle>

<div class="admin-page-header">
    <h1 class="admin-page-title">Dashboard</h1>
</div>

@if (_loading)
{
    <p>Loading…</p>
}
else if (_error != null)
{
    <div class="alert alert-danger">@_error</div>
}
else if (_dto != null)
{
    <div class="staff-stats-grid">
        <div class="stat-card">
            <div class="stat-value">@_dto.ActiveListingCount</div>
            <div class="stat-label">Active Listings</div>
        </div>
        <div class="stat-card">
            <div class="stat-value">@_dto.AuctionListingCount / @_dto.FixedPriceListingCount</div>
            <div class="stat-label">Auctions / Fixed Price</div>
        </div>
        <div class="stat-card">
            <div class="stat-value">@_dto.RecentFeeRevenueIncGst.ToString("C")</div>
            <div class="stat-label">Fee Revenue (30 days)</div>
        </div>
        <div class="stat-card @(_dto.PendingVerificationCount > 0 ? "stat-card--alert" : string.Empty)">
            <div class="stat-value">@_dto.PendingVerificationCount</div>
            <div class="stat-label">
                Pending Verifications
                @if (_dto.PendingVerificationCount > 0)
                {
                    <a href="/staff/users?status=PendingVerification" class="stat-action-link">Review →</a>
                }
            </div>
        </div>
    </div>
}

<style>
    .staff-stats-grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
        gap: var(--space-6);
        margin-top: var(--space-6);
    }
    .stat-card {
        background: var(--white);
        border-radius: 8px;
        padding: var(--space-6);
        border: 1px solid var(--border-light);
    }
    .stat-card--alert {
        border-color: #f59e0b;
        background: #fffbeb;
    }
    .stat-value {
        font-size: 2rem;
        font-weight: 700;
        color: var(--navy);
        line-height: 1;
        margin-bottom: var(--space-2);
    }
    .stat-label {
        font-size: var(--font-size-sm);
        color: var(--text-muted);
    }
    .stat-action-link {
        display: block;
        margin-top: var(--space-1);
        color: var(--gold);
        font-weight: 600;
    }
</style>

@code {
    private DashboardDto? _dto;
    private bool _loading = true;
    private string? _error;

    protected override async Task OnInitializedAsync() => await Load();

    private async Task Load()
    {
        _loading = true;
        _error = null;
        try
        {
            _dto = await StaffApi.GetDashboardAsync();
        }
        catch (Exception ex)
        {
            _error = ex is ApiException ae ? ae.Message : "Failed to load dashboard. Please try again.";
        }
        finally
        {
            _loading = false;
        }
    }
}
```

- [ ] **Step 2: Build to confirm no compile errors**

```
dotnet build src/Client/Client.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Client/Pages/Staff/StaffDashboard.razor
git commit -m "feat: add Staff Dashboard page at /staff/dashboard (Plan 5)"
```

---

## Task 8: Staff Users Pages

**Files:**
- Create: `src/Client/Pages/Staff/StaffUsers.razor`
- Create: `src/Client/Pages/Staff/StaffUserDetail.razor`

- [ ] **Step 1: Create `StaffUsers.razor`**

```razor
@* src/Client/Pages/Staff/StaffUsers.razor *@
@page "/staff/users"
@layout StaffLayout
@attribute [Authorize(Roles = "Staff")]
@inject StaffApiService StaffApi
@inject NavigationManager Nav

<PageTitle>Users — Staff Admin</PageTitle>

<div class="admin-page-header">
    <h1 class="admin-page-title">Users</h1>
</div>

@if (_loading)
{
    <p>Loading…</p>
}
else if (_error != null)
{
    <div class="alert alert-danger">@_error</div>
}
else
{
    <div class="admin-filters" style="display:flex;gap:var(--space-4);margin-bottom:var(--space-5)">
        <select class="form-select" style="width:180px" @bind="_roleFilter">
            <option value="">All Roles</option>
            <option value="Buyer">Buyer</option>
            <option value="StudFarmAdmin">Stud Farm Admin</option>
            <option value="Staff">Staff</option>
        </select>
        <select class="form-select" style="width:200px" @bind="_statusFilter">
            <option value="">All Statuses</option>
            <option value="PendingVerification">Pending Verification</option>
            <option value="Active">Active</option>
            <option value="Suspended">Suspended</option>
        </select>
    </div>

    <table class="admin-table">
        <thead>
            <tr>
                <th>Name</th>
                <th>Email</th>
                <th>Role</th>
                <th>Status</th>
                <th>Joined</th>
                <th>Actions</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var u in Filtered)
            {
                <tr>
                    <td>
                        <a href="/staff/users/@u.Id">@u.DisplayName</a>
                    </td>
                    <td>@u.Email</td>
                    <td><span class="badge badge-role">@u.Role</span></td>
                    <td><span class="badge badge-status-@u.Status.ToLowerInvariant()">@u.Status</span></td>
                    <td>@u.CreatedAt.ToString("d MMM yyyy")</td>
                    <td>
                        @if (u.Status == "PendingVerification")
                        {
                            <button class="btn btn-sm btn-success" @onclick="() => VerifyAsync(u.Id)"
                                    disabled="@_busy">Verify</button>
                        }
                        else if (u.Status == "Active")
                        {
                            <button class="btn btn-sm btn-outline" @onclick="() => SuspendAsync(u.Id)"
                                    disabled="@_busy">Suspend</button>
                        }
                    </td>
                </tr>
            }
            @if (!Filtered.Any())
            {
                <tr><td colspan="6" style="text-align:center;color:var(--text-muted)">No users match the selected filters.</td></tr>
            }
        </tbody>
    </table>

    @if (_actionError != null)
    {
        <div class="alert alert-danger" style="margin-top:var(--space-4)">@_actionError</div>
    }
}

@code {
    [SupplyParameterFromQuery] public string? status { get; set; }

    private List<UserDto> _users = [];
    private bool _loading = true;
    private string? _error;
    private string? _actionError;
    private bool _busy;
    private string _roleFilter = string.Empty;
    private string _statusFilter = string.Empty;

    private IEnumerable<UserDto> Filtered => _users
        .Where(u => string.IsNullOrEmpty(_roleFilter) || u.Role == _roleFilter)
        .Where(u => string.IsNullOrEmpty(_statusFilter) || u.Status == _statusFilter);

    protected override async Task OnInitializedAsync()
    {
        if (!string.IsNullOrWhiteSpace(status))
            _statusFilter = status;
        await Load();
    }

    private async Task Load()
    {
        _loading = true;
        _error = null;
        try
        {
            _users = await StaffApi.GetUsersAsync();
        }
        catch (Exception ex)
        {
            _error = ex is ApiException ae ? ae.Message : "Failed to load users.";
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task VerifyAsync(Guid id)
    {
        _busy = true;
        _actionError = null;
        try
        {
            await StaffApi.VerifyUserAsync(id);
            await Load();
        }
        catch (Exception ex)
        {
            _actionError = ex is ApiException ae ? ae.Message : "Failed to verify user.";
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task SuspendAsync(Guid id)
    {
        _busy = true;
        _actionError = null;
        try
        {
            await StaffApi.SuspendUserAsync(id);
            await Load();
        }
        catch (Exception ex)
        {
            _actionError = ex is ApiException ae ? ae.Message : "Failed to suspend user.";
        }
        finally
        {
            _busy = false;
        }
    }
}
```

- [ ] **Step 2: Create `StaffUserDetail.razor`**

```razor
@* src/Client/Pages/Staff/StaffUserDetail.razor *@
@page "/staff/users/{Id:guid}"
@layout StaffLayout
@attribute [Authorize(Roles = "Staff")]
@inject StaffApiService StaffApi
@inject NavigationManager Nav

<PageTitle>User Detail — Staff Admin</PageTitle>

@if (_loading)
{
    <p>Loading…</p>
}
else if (_error != null)
{
    <div class="alert alert-danger">@_error</div>
}
else if (_user != null)
{
    <div class="admin-page-header">
        <div>
            <h1 class="admin-page-title">@_user.DisplayName</h1>
            <p style="color:var(--text-muted);margin:0">@_user.Email</p>
        </div>
        <a href="/staff/users" class="btn btn-outline btn-sm">← Back to Users</a>
    </div>

    <div class="admin-detail-card">
        <dl class="admin-dl">
            <dt>Role</dt>
            <dd><span class="badge badge-role">@_user.Role</span></dd>
            <dt>Status</dt>
            <dd><span class="badge badge-status-@_user.Status.ToLowerInvariant()">@_user.Status</span></dd>
            <dt>Joined</dt>
            <dd>@_user.CreatedAt.ToString("d MMMM yyyy")</dd>
            @if (_user.VerifiedAt.HasValue)
            {
                <dt>Verified At</dt>
                <dd>@_user.VerifiedAt.Value.ToString("d MMMM yyyy HH:mm") UTC</dd>
            }
        </dl>

        @if (_user.Status == "PendingVerification")
        {
            <div style="margin-top:var(--space-5)">
                <button class="btn btn-success" @onclick="VerifyAsync" disabled="@_busy">
                    Verify User
                </button>
            </div>
        }
        else if (_user.Status == "Active")
        {
            <div style="margin-top:var(--space-5)">
                <button class="btn btn-outline" @onclick="SuspendAsync" disabled="@_busy">
                    Suspend User
                </button>
            </div>
        }

        @if (_actionError != null)
        {
            <div class="alert alert-danger" style="margin-top:var(--space-4)">@_actionError</div>
        }
    </div>
}

<style>
    .admin-detail-card {
        background: var(--white);
        border-radius: 8px;
        padding: var(--space-6);
        border: 1px solid var(--border-light);
        max-width: 520px;
        margin-top: var(--space-6);
    }
    .admin-dl {
        display: grid;
        grid-template-columns: 140px 1fr;
        gap: var(--space-3) var(--space-4);
        margin: 0;
    }
    .admin-dl dt { font-weight: 600; color: var(--text-muted); font-size: var(--font-size-sm); }
    .admin-dl dd { margin: 0; }
</style>

@code {
    [Parameter] public Guid Id { get; set; }

    private UserDto? _user;
    private bool _loading = true;
    private string? _error;
    private string? _actionError;
    private bool _busy;

    protected override async Task OnInitializedAsync() => await Load();

    private async Task Load()
    {
        _loading = true;
        _error = null;
        try
        {
            _user = await StaffApi.GetUserAsync(Id);
        }
        catch (Exception ex)
        {
            _error = ex is ApiException ae ? ae.Message : "Failed to load user.";
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task VerifyAsync()
    {
        _busy = true;
        _actionError = null;
        try
        {
            await StaffApi.VerifyUserAsync(Id);
            await Load();
        }
        catch (Exception ex)
        {
            _actionError = ex is ApiException ae ? ae.Message : "Failed to verify user.";
        }
        finally { _busy = false; }
    }

    private async Task SuspendAsync()
    {
        _busy = true;
        _actionError = null;
        try
        {
            await StaffApi.SuspendUserAsync(Id);
            await Load();
        }
        catch (Exception ex)
        {
            _actionError = ex is ApiException ae ? ae.Message : "Failed to suspend user.";
        }
        finally { _busy = false; }
    }
}
```

- [ ] **Step 3: Build to confirm no compile errors**

```
dotnet build src/Client/Client.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/Client/Pages/Staff/StaffUsers.razor \
        src/Client/Pages/Staff/StaffUserDetail.razor
git commit -m "feat: add Staff Users and User Detail pages (Plan 5)"
```

---

## Task 9: Staff Stud Farms Pages

**Files:**
- Create: `src/Client/Pages/Staff/StaffStudFarms.razor`
- Create: `src/Client/Pages/Staff/StaffStudFarmNew.razor`

- [ ] **Step 1: Create `StaffStudFarms.razor`**

```razor
@* src/Client/Pages/Staff/StaffStudFarms.razor *@
@page "/staff/studfarms"
@layout StaffLayout
@attribute [Authorize(Roles = "Staff")]
@inject StaffApiService StaffApi
@inject NavigationManager Nav

<PageTitle>Stud Farms — Staff Admin</PageTitle>

<div class="admin-page-header">
    <h1 class="admin-page-title">Stud Farms</h1>
    <a href="/staff/studfarms/new" class="btn btn-gold btn-sm">+ Onboard New Farm</a>
</div>

@if (_loading)
{
    <p>Loading…</p>
}
else if (_error != null)
{
    <div class="alert alert-danger">@_error</div>
}
else if (!_farms.Any())
{
    <div class="admin-empty-state">
        <p>No stud farms have been onboarded yet.</p>
        <a href="/staff/studfarms/new" class="btn btn-gold">Onboard First Farm</a>
    </div>
}
else
{
    <table class="admin-table">
        <thead>
            <tr>
                <th>Farm Name</th>
                <th>ABN</th>
                <th>Contact Email</th>
                <th>Linked User</th>
                <th>Active</th>
                <th>Created</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var f in _farms)
            {
                <tr>
                    <td>@f.Name</td>
                    <td>@(f.ABN ?? "—")</td>
                    <td>@(f.ContactEmail ?? "—")</td>
                    <td>
                        <div>@f.LinkedUserDisplayName</div>
                        <div style="font-size:var(--font-size-sm);color:var(--text-muted)">@f.LinkedUserEmail</div>
                    </td>
                    <td>
                        <span class="badge @(f.IsActive ? "badge-status-active" : "badge-status-suspended")">
                            @(f.IsActive ? "Active" : "Inactive")
                        </span>
                    </td>
                    <td>@f.CreatedAt.ToString("d MMM yyyy")</td>
                </tr>
            }
        </tbody>
    </table>
}

@code {
    private List<StudFarmSummaryDto> _farms = [];
    private bool _loading = true;
    private string? _error;

    protected override async Task OnInitializedAsync() => await Load();

    private async Task Load()
    {
        _loading = true;
        _error = null;
        try
        {
            _farms = await StaffApi.GetStudFarmsAsync();
        }
        catch (Exception ex)
        {
            _error = ex is ApiException ae ? ae.Message : "Failed to load stud farms.";
        }
        finally
        {
            _loading = false;
        }
    }
}
```

- [ ] **Step 2: Create `StaffStudFarmNew.razor`**

```razor
@* src/Client/Pages/Staff/StaffStudFarmNew.razor *@
@page "/staff/studfarms/new"
@layout StaffLayout
@attribute [Authorize(Roles = "Staff")]
@inject StaffApiService StaffApi
@inject NavigationManager Nav

<PageTitle>Onboard New Stud Farm — Staff Admin</PageTitle>

<div class="admin-page-header">
    <h1 class="admin-page-title">Onboard New Stud Farm</h1>
    <a href="/staff/studfarms" class="btn btn-outline btn-sm">← Back to Stud Farms</a>
</div>

<div class="admin-form-card">
    <EditForm Model="_request" OnValidSubmit="SubmitAsync">
        <DataAnnotationsValidator />

        <div class="form-group">
            <label class="form-label">Linked User <span class="required">*</span></label>
            @if (_loadingUsers)
            {
                <p style="font-size:var(--font-size-sm);color:var(--text-muted)">Loading users…</p>
            }
            else if (!_studFarmAdmins.Any())
            {
                <div class="alert alert-warning">
                    No users with the StudFarmAdmin role found. Ask the farm contact to register, then assign them the StudFarmAdmin role in Entra ID before onboarding.
                </div>
            }
            else
            {
                <select class="form-select" @bind="_request.UserId">
                    <option value="@Guid.Empty">— Select a user —</option>
                    @foreach (var u in _studFarmAdmins)
                    {
                        <option value="@u.Id">@u.DisplayName (@u.Email)</option>
                    }
                </select>
            }
        </div>

        <div class="form-group">
            <label class="form-label">Farm Name <span class="required">*</span></label>
            <InputText class="form-control" @bind-Value="_request.Name" placeholder="e.g. Coolmore Australia" />
            <ValidationMessage For="() => _request.Name" />
        </div>

        <div class="form-group">
            <label class="form-label">ABN <span class="required">*</span></label>
            <InputText class="form-control" @bind-Value="_request.ABN" placeholder="11 digit ABN" />
        </div>

        <div class="form-group">
            <label class="form-label">Contact Phone</label>
            <InputText class="form-control" @bind-Value="_request.ContactPhone" placeholder="+61 2 0000 0000" />
        </div>

        <div class="form-group">
            <label class="form-label">Contact Email</label>
            <InputText class="form-control" @bind-Value="_request.ContactEmail" placeholder="nominations@farm.com.au" />
        </div>

        <div class="form-group">
            <label class="form-label">Address</label>
            <InputTextArea class="form-control" @bind-Value="_request.Address" rows="3"
                           placeholder="Street, Town, State, Postcode" />
        </div>

        @if (_error != null)
        {
            <div class="alert alert-danger">@_error</div>
        }

        <div style="margin-top:var(--space-6);display:flex;gap:var(--space-3)">
            <button type="submit" class="btn btn-gold" disabled="@_submitting">
                @(_submitting ? "Creating…" : "Create Stud Farm")
            </button>
            <a href="/staff/studfarms" class="btn btn-outline">Cancel</a>
        </div>
    </EditForm>
</div>

<style>
    .admin-form-card {
        background: var(--white);
        border-radius: 8px;
        padding: var(--space-8);
        border: 1px solid var(--border-light);
        max-width: 560px;
        margin-top: var(--space-6);
    }
    .required { color: #dc2626; }
</style>

@code {
    private readonly CreateStudFarmRequest _request = new();
    private List<UserDto> _studFarmAdmins = [];
    private bool _loadingUsers = true;
    private bool _submitting;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _studFarmAdmins = await StaffApi.GetUnlinkedStudFarmAdminsAsync();
        }
        catch
        {
            // Non-fatal — page will show warning state
        }
        finally
        {
            _loadingUsers = false;
        }
    }

    private async Task SubmitAsync()
    {
        if (_request.UserId == Guid.Empty)
        {
            _error = "Please select a user to link to this farm.";
            return;
        }
        if (string.IsNullOrWhiteSpace(_request.Name))
        {
            _error = "Farm Name is required.";
            return;
        }

        _submitting = true;
        _error = null;
        try
        {
            await StaffApi.CreateStudFarmAsync(_request);
            Nav.NavigateTo("/staff/studfarms");
        }
        catch (Exception ex)
        {
            _error = ex is ApiException ae ? ae.Message : "Failed to create stud farm. Please try again.";
        }
        finally
        {
            _submitting = false;
        }
    }
}
```

- [ ] **Step 3: Build to confirm no compile errors**

```
dotnet build src/Client/Client.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/Client/Pages/Staff/StaffStudFarms.razor \
        src/Client/Pages/Staff/StaffStudFarmNew.razor
git commit -m "feat: add Staff Stud Farms list and onboarding form (Plan 5)"
```

---

## Task 10: Staff Listings Page

**Files:**
- Create: `src/Client/Pages/Staff/StaffListings.razor`

This page has two interactive features beyond basic load/display: inline fee editing and a force-status modal.

- [ ] **Step 1: Create `StaffListings.razor`**

```razor
@* src/Client/Pages/Staff/StaffListings.razor *@
@page "/staff/listings"
@layout StaffLayout
@attribute [Authorize(Roles = "Staff")]
@inject StaffApiService StaffApi

<PageTitle>Listings — Staff Admin</PageTitle>

<div class="admin-page-header">
    <h1 class="admin-page-title">All Listings</h1>
</div>

@if (_loading)
{
    <p>Loading…</p>
}
else if (_error != null)
{
    <div class="alert alert-danger">@_error</div>
}
else if (!_listings.Any())
{
    <div class="admin-empty-state">
        <p>No listings yet. <a href="/staff/studfarms/new">Onboard a stud farm</a> first.</p>
    </div>
}
else
{
    <table class="admin-table">
        <thead>
            <tr>
                <th>Stallion</th>
                <th>Farm</th>
                <th>Type</th>
                <th>Status</th>
                <th>Price (inc. GST)</th>
                <th>Fee %</th>
                <th>Published</th>
                <th>Actions</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var l in _listings)
            {
                <tr>
                    <td>@l.StallionName</td>
                    <td>@l.StudFarmName</td>
                    <td><span class="badge badge-type-@l.ListingType.ToLowerInvariant()">@l.ListingType</span></td>
                    <td><span class="badge badge-status-@l.Status.ToLowerInvariant()">@l.Status</span></td>
                    <td>@(l.PriceIncGst.HasValue ? l.PriceIncGst.Value.ToString("C") : "—")</td>
                    <td>
                        @if (_editingFeeId == l.Id)
                        {
                            <div style="display:flex;gap:var(--space-2);align-items:center">
                                <input type="number" class="form-control" style="width:80px"
                                       @bind="_editingFeeValue" min="0" max="100" step="0.1" />
                                <button class="btn btn-sm btn-gold" @onclick="() => SaveFeeAsync(l.Id)"
                                        disabled="@_busy">✓</button>
                                <button class="btn btn-sm btn-outline" @onclick="CancelFeeEdit">✕</button>
                            </div>
                        }
                        else
                        {
                            <span style="cursor:pointer;text-decoration:underline dotted"
                                  @onclick="() => StartFeeEdit(l)">
                                @(l.PlatformFeePercent.HasValue ? $"{l.PlatformFeePercent:0.##}%" : "—")
                            </span>
                        }
                    </td>
                    <td>@(l.PublishedAt.HasValue ? l.PublishedAt.Value.ToString("d MMM yyyy") : "—")</td>
                    <td>
                        <button class="btn btn-sm btn-outline"
                                @onclick="() => OpenForceStatusModal(l)">
                            Override Status
                        </button>
                    </td>
                </tr>
            }
        </tbody>
    </table>
}

@if (_actionError != null)
{
    <div class="alert alert-danger" style="margin-top:var(--space-4)">@_actionError</div>
}

@* ── Force Status Modal ──────────────────────────────────────────── *@
@if (_modalListing != null)
{
    <div class="modal-backdrop" @onclick="CloseModal"></div>
    <div class="modal-dialog" role="dialog" aria-modal="true">
        <div class="modal-header">
            <h2>Override Status — @_modalListing.StallionName</h2>
        </div>
        <div class="modal-body">
            <div class="form-group">
                <label class="form-label">New Status</label>
                <select class="form-select" @bind="_modalStatus">
                    <option value="Draft">Draft</option>
                    <option value="Active">Active</option>
                    <option value="Closed">Closed</option>
                    <option value="Unsold">Unsold</option>
                </select>
            </div>
            <div class="form-group">
                <label class="form-label">Reason (optional)</label>
                <textarea class="form-control" rows="2" @bind="_modalReason"
                          placeholder="Briefly explain the reason for this override"></textarea>
            </div>
            @if (_modalError != null)
            {
                <div class="alert alert-danger">@_modalError</div>
            }
        </div>
        <div class="modal-footer">
            <button class="btn btn-gold" @onclick="ConfirmForceStatusAsync" disabled="@_busy">Confirm</button>
            <button class="btn btn-outline" @onclick="CloseModal" disabled="@_busy">Cancel</button>
        </div>
    </div>
}

<style>
    .modal-backdrop {
        position: fixed; inset: 0; background: rgba(0,0,0,0.4); z-index: 100;
    }
    .modal-dialog {
        position: fixed; top: 50%; left: 50%; transform: translate(-50%,-50%);
        background: var(--white); border-radius: 8px; padding: var(--space-6);
        width: 440px; max-width: 95vw; z-index: 101; box-shadow: 0 20px 60px rgba(0,0,0,0.2);
    }
    .modal-header { margin-bottom: var(--space-5); }
    .modal-header h2 { font-size: var(--font-size-lg); margin: 0; }
    .modal-footer { display: flex; gap: var(--space-3); margin-top: var(--space-5); }
</style>

@code {
    private List<ListingStaffSummaryDto> _listings = [];
    private bool _loading = true;
    private string? _error;
    private string? _actionError;
    private bool _busy;

    // Inline fee edit state
    private Guid? _editingFeeId;
    private decimal _editingFeeValue;

    // Force status modal state
    private ListingStaffSummaryDto? _modalListing;
    private string _modalStatus = "Draft";
    private string _modalReason = string.Empty;
    private string? _modalError;

    protected override async Task OnInitializedAsync() => await Load();

    private async Task Load()
    {
        _loading = true;
        _error = null;
        try
        {
            _listings = await StaffApi.GetAllListingsAsync();
        }
        catch (Exception ex)
        {
            _error = ex is ApiException ae ? ae.Message : "Failed to load listings.";
        }
        finally
        {
            _loading = false;
        }
    }

    private void StartFeeEdit(ListingStaffSummaryDto listing)
    {
        _editingFeeId = listing.Id;
        _editingFeeValue = listing.PlatformFeePercent ?? 0;
        _actionError = null;
    }

    private void CancelFeeEdit()
    {
        _editingFeeId = null;
    }

    private async Task SaveFeeAsync(Guid id)
    {
        _busy = true;
        _actionError = null;
        try
        {
            await StaffApi.SetListingFeeAsync(id, _editingFeeValue);
            _editingFeeId = null;
            await Load();
        }
        catch (Exception ex)
        {
            _actionError = ex is ApiException ae ? ae.Message : "Failed to save fee.";
        }
        finally
        {
            _busy = false;
        }
    }

    private void OpenForceStatusModal(ListingStaffSummaryDto listing)
    {
        _modalListing = listing;
        _modalStatus = listing.Status;
        _modalReason = string.Empty;
        _modalError = null;
    }

    private void CloseModal()
    {
        _modalListing = null;
        _modalError = null;
    }

    private async Task ConfirmForceStatusAsync()
    {
        if (_modalListing == null) return;
        _busy = true;
        _modalError = null;
        try
        {
            await StaffApi.ForceListingStatusAsync(_modalListing.Id, new ForceListingStatusRequest
            {
                Status = _modalStatus,
                Reason = string.IsNullOrWhiteSpace(_modalReason) ? null : _modalReason
            });
            CloseModal();
            await Load();
        }
        catch (Exception ex)
        {
            _modalError = ex is ApiException ae ? ae.Message : "Failed to override status.";
        }
        finally
        {
            _busy = false;
        }
    }
}
```

- [ ] **Step 2: Build to confirm no compile errors**

```
dotnet build src/Client/Client.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Client/Pages/Staff/StaffListings.razor
git commit -m "feat: add Staff Listings page with inline fee edit and force-status modal (Plan 5)"
```

---

## Task 11: Staff Transactions Page

**Files:**
- Create: `src/Client/Pages/Staff/StaffTransactions.razor`

- [ ] **Step 1: Create `StaffTransactions.razor`**

```razor
@* src/Client/Pages/Staff/StaffTransactions.razor *@
@page "/staff/transactions"
@layout StaffLayout
@attribute [Authorize(Roles = "Staff")]
@inject StaffApiService StaffApi

<PageTitle>Transactions — Staff Admin</PageTitle>

<div class="admin-page-header">
    <h1 class="admin-page-title">Transactions</h1>
</div>

@if (_loading)
{
    <p>Loading…</p>
}
else if (_error != null)
{
    <div class="alert alert-danger">@_error</div>
}
else if (!_transactions.Any())
{
    <div class="admin-empty-state">
        <p>No transactions recorded yet.</p>
    </div>
}
else
{
    <table class="admin-table">
        <thead>
            <tr>
                <th>Date</th>
                <th>Stallion</th>
                <th>Farm</th>
                <th>Buyer</th>
                <th>Sale Price (inc. GST)</th>
                <th>Platform Fee (inc. GST)</th>
                <th>Fee ex GST</th>
                <th>GST Amount</th>
                <th>Status</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var t in _transactions)
            {
                <tr>
                    <td>@(t.PaidAt.HasValue ? t.PaidAt.Value.ToString("d MMM yyyy") : "—")</td>
                    <td>@t.StallionName</td>
                    <td>@t.StudFarmName</td>
                    <td>@t.BuyerDisplayName</td>
                    <td>@t.TotalPriceIncGst.ToString("C")</td>
                    <td>@t.PlatformFeeIncGst.ToString("C")</td>
                    <td>@t.PlatformFeeExGst.ToString("C")</td>
                    <td>@t.PlatformFeeGst.ToString("C")</td>
                    <td><span class="badge badge-status-@t.Status.ToLowerInvariant()">@t.Status</span></td>
                </tr>
            }
        </tbody>
    </table>
}

@code {
    private List<TransactionDto> _transactions = [];
    private bool _loading = true;
    private string? _error;

    protected override async Task OnInitializedAsync() => await Load();

    private async Task Load()
    {
        _loading = true;
        _error = null;
        try
        {
            _transactions = await StaffApi.GetTransactionsAsync();
        }
        catch (Exception ex)
        {
            _error = ex is ApiException ae ? ae.Message : "Failed to load transactions.";
        }
        finally
        {
            _loading = false;
        }
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build src/Client/Client.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Client/Pages/Staff/StaffTransactions.razor
git commit -m "feat: add Staff Transactions page (Plan 5)"
```

---

## Task 12: Staff Invoices Page

**Files:**
- Create: `src/Client/Pages/Staff/StaffInvoices.razor`

- [ ] **Step 1: Create `StaffInvoices.razor`**

```razor
@* src/Client/Pages/Staff/StaffInvoices.razor *@
@page "/staff/invoices"
@layout StaffLayout
@attribute [Authorize(Roles = "Staff")]
@inject StaffApiService StaffApi

<PageTitle>Invoices — Staff Admin</PageTitle>

<div class="admin-page-header">
    <h1 class="admin-page-title">Remittance Invoices</h1>
</div>

@if (_loading)
{
    <p>Loading…</p>
}
else if (_error != null)
{
    <div class="alert alert-danger">@_error</div>
}
else if (!_invoices.Any())
{
    <div class="admin-empty-state">
        <p>No completed sales to invoice yet.</p>
    </div>
}
else
{
    @foreach (var inv in _invoices)
    {
        <details class="invoice-section">
            <summary class="invoice-summary">
                <div class="invoice-farm-name">@inv.StudFarmName</div>
                <div class="invoice-totals">
                    <span>Sales: <strong>@inv.TotalSalesIncGst.ToString("C")</strong></span>
                    <span>Fees Retained: <strong>@inv.TotalPlatformFeesIncGst.ToString("C")</strong></span>
                    <span>Remittance Due: <strong class="remittance-amount">@inv.TotalRemittance.ToString("C")</strong></span>
                </div>
            </summary>
            <div class="invoice-lines">
                <table class="admin-table">
                    <thead>
                        <tr>
                            <th>Stallion</th>
                            <th>Sale Price (inc. GST)</th>
                            <th>Platform Fee (inc. GST)</th>
                            <th>Remittance Amount</th>
                            <th>Sale Date</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var line in inv.Lines)
                        {
                            <tr>
                                <td>@line.StallionName</td>
                                <td>@line.SalePriceIncGst.ToString("C")</td>
                                <td>@line.PlatformFeeIncGst.ToString("C")</td>
                                <td>@line.RemittanceAmount.ToString("C")</td>
                                <td>@line.PaidAt.ToString("d MMM yyyy")</td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
        </details>
    }
}

<style>
    .invoice-section {
        background: var(--white);
        border: 1px solid var(--border-light);
        border-radius: 8px;
        margin-bottom: var(--space-4);
        overflow: hidden;
    }
    .invoice-summary {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: var(--space-5) var(--space-6);
        cursor: pointer;
        list-style: none;
        gap: var(--space-4);
    }
    .invoice-summary::-webkit-details-marker { display: none; }
    .invoice-farm-name {
        font-weight: 700;
        font-size: var(--font-size-lg);
        color: var(--navy);
    }
    .invoice-totals {
        display: flex;
        gap: var(--space-6);
        font-size: var(--font-size-sm);
        color: var(--text-muted);
    }
    .remittance-amount { color: var(--navy); font-size: 1.05em; }
    .invoice-lines {
        padding: 0 var(--space-6) var(--space-5);
        border-top: 1px solid var(--border-light);
    }
</style>

@code {
    private List<InvoiceDto> _invoices = [];
    private bool _loading = true;
    private string? _error;

    protected override async Task OnInitializedAsync() => await Load();

    private async Task Load()
    {
        _loading = true;
        _error = null;
        try
        {
            _invoices = await StaffApi.GetInvoicesAsync();
        }
        catch (Exception ex)
        {
            _error = ex is ApiException ae ? ae.Message : "Failed to load invoices.";
        }
        finally
        {
            _loading = false;
        }
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build src/Client/Client.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Client/Pages/Staff/StaffInvoices.razor
git commit -m "feat: add Staff Invoices page with collapsible per-farm sections (Plan 5)"
```

---

## Task 13: Wire Up, Add CSS Helpers, Smoke Test

**Files:**
- Modify: `src/Client/wwwroot/css/app.css` — add table and badge helpers used across all staff pages
- Smoke test in browser

- [ ] **Step 1: Add shared CSS utilities to `app.css`**

These classes are referenced across multiple staff pages (`admin-table`, `admin-page-header`, `admin-empty-state`, `badge-*`, etc.). Add after the shared admin-shell layout block:

```css
/* ── Admin / Staff shared page utilities ──────────────────────────── */
.admin-page-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-bottom: var(--space-6);
    gap: var(--space-4);
}

.admin-page-title {
    font-size: var(--font-size-2xl);
    font-weight: 700;
    color: var(--navy);
    margin: 0;
}

.admin-table {
    width: 100%;
    border-collapse: collapse;
    font-size: var(--font-size-sm);
}

.admin-table th {
    text-align: left;
    padding: var(--space-3) var(--space-4);
    background: var(--cream);
    border-bottom: 2px solid var(--border-light);
    font-weight: 600;
    color: var(--text-muted);
    white-space: nowrap;
}

.admin-table td {
    padding: var(--space-3) var(--space-4);
    border-bottom: 1px solid var(--border-light);
    vertical-align: middle;
}

.admin-table tr:last-child td {
    border-bottom: none;
}

.admin-table tr:hover td {
    background: rgba(196,153,58,0.04);
}

.admin-empty-state {
    text-align: center;
    padding: var(--space-12) var(--space-6);
    color: var(--text-muted);
}

/* Status badges */
.badge {
    display: inline-block;
    padding: 2px 8px;
    border-radius: 12px;
    font-size: 11px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.04em;
}
.badge-status-active       { background: #d1fae5; color: #065f46; }
.badge-status-draft        { background: #f3f4f6; color: #374151; }
.badge-status-closed       { background: #fee2e2; color: #991b1b; }
.badge-status-unsold       { background: #fef3c7; color: #92400e; }
.badge-status-suspended    { background: #fee2e2; color: #991b1b; }
.badge-status-pendingverification { background: #fef3c7; color: #92400e; }
.badge-type-auction        { background: #ede9fe; color: #5b21b6; }
.badge-type-fixedprice     { background: #dbeafe; color: #1e40af; }
.badge-role                { background: #f3f4f6; color: #374151; }

/* Utility */
.btn-success {
    background: #059669;
    color: var(--white);
    border: none;
    padding: var(--space-2) var(--space-4);
    border-radius: 4px;
    font-weight: 600;
    cursor: pointer;
}
.btn-success:hover:not(:disabled) { background: #047857; }
```

- [ ] **Step 2: Full build**

```
dotnet build src/Client/Client.csproj && dotnet build src/Server/Server.csproj
```

Expected: Both build succeeded, 0 errors.

- [ ] **Step 3: Run all server tests**

```
dotnet test tests/Server.Tests/Server.Tests.csproj -v n
```

Expected: all tests pass, no failures.

- [ ] **Step 4: Run the app locally and smoke test each page**

```
dotnet run --project src/Server/Server.csproj
```

Sign in with a Staff-role account, then visit each page and verify:

| URL | Expected |
|---|---|
| `/staff/dashboard` | Four stat cards load (counts show 0 if no data) |
| `/staff/users` | Users table loads; role and status filters work |
| `/staff/users/{id}` | User detail shows; Verify/Suspend buttons visible per status |
| `/staff/studfarms` | Farm list loads or "No stud farms" empty state |
| `/staff/studfarms/new` | Form loads with user dropdown; submit creates farm and redirects |
| `/staff/listings` | Table loads or "No listings" empty state; inline fee edit opens |
| `/staff/transactions` | Table loads or "No transactions" empty state |
| `/staff/invoices` | Per-farm collapsible sections render |

Also verify: navigating to any `/staff/*` URL while signed in as a Buyer or unauthenticated redirects to Entra ID login.

- [ ] **Step 5: Final commit**

```bash
git add src/Client/wwwroot/css/app.css
git commit -m "feat: add shared admin/staff CSS utilities for tables, badges, and empty states (Plan 5)"
```

- [ ] **Step 6: Push**

```bash
git push
```

---

## Self-Review Checklist

**Spec coverage:**
- [x] Dashboard — 4 stat cards (ActiveListingCount, AuctionListingCount/FixedPriceListingCount, RecentFeeRevenueIncGst, PendingVerificationCount) with amber highlight when pending > 0
- [x] Users list — Role and Status filters, Verify/Suspend actions per status
- [x] User detail — same actions, all fields displayed
- [x] Stud Farms list — all columns from spec
- [x] Stud Farm onboarding form — all fields from spec (User dropdown filtered to StudFarmAdmin)
- [x] Listings — all columns, inline fee edit, force-status modal with Status + Reason fields
- [x] Transactions — all GST columns (inc, ex, gst amount)
- [x] Invoices — collapsible per-farm sections with summary header and line table
- [x] Auth guard — `[Authorize(Roles = "Staff")]` on all pages; `StaffLayout` uses `[Authorize]` implicitly via pages
- [x] ErrorBoundary in StaffLayout
- [x] Server: 4 new AdminController endpoints
- [x] Server: 3 new AdminService methods (GetAllStudFarms, CreateStudFarm, ForceListingStatus)
- [x] Server: GetAllAsync on StudFarmRepository, GetAllStaffAsync on ListingRepository
- [x] Audit logging — CreateStudFarm, ForceListingStatus (VerifyUser and SuspendUser already logged in UserService from Plan 2)
- [x] CreateStudFarm validation — user must exist, must be StudFarmAdmin role, must not already have a farm

**No placeholders:** All code steps contain complete, runnable code.

**Type consistency:** `ListingStaffSummaryDto` used in `StaffApiService.GetAllListingsAsync()`, `StaffListings.razor`, and `AdminService.GetAllListingsStaffAsync()`. `StudFarmSummaryDto` flows from server → `StaffApiService` → `StaffStudFarms.razor` and `StaffStudFarmNew.razor`. `ForceListingStatusRequest` used in `StaffApiService.ForceListingStatusAsync()` and `StaffListings.razor`. All consistent.
