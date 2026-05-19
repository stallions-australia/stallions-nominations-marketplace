# Data Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create the .NET solution, all entity classes, EF Core DbContext with TPT inheritance, the initial database migration, and the full repository layer with unit tests — everything the API layer needs to build on.

**Architecture:** ASP.NET Core Web API (`src/Server`) references a shared class library (`src/Shared`) for enums and DTOs. EF Core 9 with Table Per Type (TPT) inheritance maps `AuctionListing` and `FixedPriceListing` as child tables of `Listing`. All data access goes through repository interfaces — no raw DbContext in controllers or services. The `Blazor.Client` project is created as a stub only in this plan.

**Tech Stack:** .NET 9, ASP.NET Core Web API, EF Core 9 (SqlServer + InMemory), xUnit, FluentAssertions, Moq

---

## File Map

```
stallions-nominations-marketplace.sln
src/
  Shared/
    Stallions.Shared.csproj
    Enums/
      UserRole.cs
      UserStatus.cs
      ListingType.cs
      ListingStatus.cs
      BidStatus.cs
      PurchaseStatus.cs
      BindingStatus.cs
  Server/
    Stallions.Server.csproj
    Data/
      AppDbContext.cs
      Entities/
        User.cs
        StudFarm.cs
        Stallion.cs
        StallionImage.cs
        Season.cs
        Listing.cs
        AuctionListing.cs
        FixedPriceListing.cs
        Bid.cs
        Purchase.cs
        NominationBinding.cs
        Enquiry.cs
        EnquiryMessage.cs
        AuditLog.cs
      Repositories/
        IUserRepository.cs
        UserRepository.cs
        IStudFarmRepository.cs
        StudFarmRepository.cs
        IStallionRepository.cs
        StallionRepository.cs
        ISeasonRepository.cs
        SeasonRepository.cs
        IListingRepository.cs
        ListingRepository.cs
        IBidRepository.cs
        BidRepository.cs
        IPurchaseRepository.cs
        PurchaseRepository.cs
        INominationBindingRepository.cs
        NominationBindingRepository.cs
        IEnquiryRepository.cs
        EnquiryRepository.cs
        IAuditLogRepository.cs
        AuditLogRepository.cs
    appsettings.json
    appsettings.Development.json
    Program.cs
  Client/
    Stallions.Client.csproj   (stub only)
functions/
  Stallions.Functions.csproj  (stub only)
tests/
  Server.Tests/
    Stallions.Server.Tests.csproj
    Data/
      Entities/
        UserEntityTests.cs
        ListingEntityTests.cs
      Repositories/
        UserRepositoryTests.cs
        SeasonRepositoryTests.cs
        ListingRepositoryTests.cs
        BidRepositoryTests.cs
        PurchaseRepositoryTests.cs
```

---

### Task 1: Create the .NET solution and project structure

**Files:**
- Create: `stallions-nominations-marketplace.sln`
- Create: `src/Shared/Stallions.Shared.csproj`
- Create: `src/Server/Stallions.Server.csproj`
- Create: `src/Client/Stallions.Client.csproj`
- Create: `functions/Stallions.Functions.csproj`
- Create: `tests/Server.Tests/Stallions.Server.Tests.csproj`

- [ ] **Step 1: Create the solution and all projects**

Run from the repo root (`C:\Users\david\source\repos\stallions-nominations-marketplace`):

```powershell
dotnet new sln -n stallions-nominations-marketplace
dotnet new classlib -n Stallions.Shared -o src/Shared --framework net9.0
dotnet new webapi -n Stallions.Server -o src/Server --framework net9.0 --no-openapi
dotnet new blazorwasm -n Stallions.Client -o src/Client --framework net9.0
dotnet new classlib -n Stallions.Functions -o functions --framework net9.0
dotnet new xunit -n Stallions.Server.Tests -o tests/Server.Tests --framework net9.0
```

- [ ] **Step 2: Add all projects to the solution**

```powershell
dotnet sln add src/Shared/Stallions.Shared.csproj
dotnet sln add src/Server/Stallions.Server.csproj
dotnet sln add src/Client/Stallions.Client.csproj
dotnet sln add functions/Stallions.Functions.csproj
dotnet sln add tests/Server.Tests/Stallions.Server.Tests.csproj
```

- [ ] **Step 3: Add project references**

```powershell
# Server depends on Shared
dotnet add src/Server/Stallions.Server.csproj reference src/Shared/Stallions.Shared.csproj

# Tests depend on Server and Shared
dotnet add tests/Server.Tests/Stallions.Server.Tests.csproj reference src/Server/Stallions.Server.csproj
dotnet add tests/Server.Tests/Stallions.Server.Tests.csproj reference src/Shared/Stallions.Shared.csproj
```

- [ ] **Step 4: Delete the generated boilerplate files from the new projects**

```powershell
Remove-Item src/Shared/Class1.cs
Remove-Item src/Server/WeatherForecast.cs
Remove-Item src/Server/Controllers/WeatherForecastController.cs -ErrorAction SilentlyContinue
Remove-Item tests/Server.Tests/UnitTest1.cs
Remove-Item functions/Class1.cs
```

- [ ] **Step 5: Verify solution builds**

```powershell
dotnet build
```

Expected: `Build succeeded.` with 0 errors (warnings about empty projects are fine).

- [ ] **Step 6: Commit**

```powershell
git add stallions-nominations-marketplace.sln src/ functions/ tests/
git commit -m "chore: scaffold .NET solution with Server, Shared, Client, Functions, and Tests projects"
```

---

### Task 2: Add NuGet packages

**Files:**
- Modify: `src/Server/Stallions.Server.csproj`
- Modify: `tests/Server.Tests/Stallions.Server.Tests.csproj`

- [ ] **Step 1: Add EF Core packages to Server**

```powershell
dotnet add src/Server/Stallions.Server.csproj package Microsoft.EntityFrameworkCore.SqlServer --version 9.*
dotnet add src/Server/Stallions.Server.csproj package Microsoft.EntityFrameworkCore.Design --version 9.*
```

- [ ] **Step 2: Add test packages to Server.Tests**

```powershell
dotnet add tests/Server.Tests/Stallions.Server.Tests.csproj package Microsoft.EntityFrameworkCore.InMemory --version 9.*
dotnet add tests/Server.Tests/Stallions.Server.Tests.csproj package FluentAssertions --version 7.*
dotnet add tests/Server.Tests/Stallions.Server.Tests.csproj package Moq --version 4.*
```

- [ ] **Step 3: Install EF Core tools globally (if not already installed)**

```powershell
dotnet tool install --global dotnet-ef
```

Expected: Tool installed, or message saying it's already installed. Verify with:
```powershell
dotnet ef --version
```
Expected output: `Entity Framework Core .NET Command-line Tools 9.x.x`

- [ ] **Step 4: Verify build**

```powershell
dotnet build
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```powershell
git add src/Server/Stallions.Server.csproj tests/Server.Tests/Stallions.Server.Tests.csproj
git commit -m "chore: add EF Core, FluentAssertions, and Moq NuGet packages"
```

---

### Task 3: Define shared enums

**Files:**
- Create: `src/Shared/Enums/UserRole.cs`
- Create: `src/Shared/Enums/UserStatus.cs`
- Create: `src/Shared/Enums/ListingType.cs`
- Create: `src/Shared/Enums/ListingStatus.cs`
- Create: `src/Shared/Enums/BidStatus.cs`
- Create: `src/Shared/Enums/PurchaseStatus.cs`
- Create: `src/Shared/Enums/BindingStatus.cs`

- [ ] **Step 1: Create the Enums directory and all enum files**

Create `src/Shared/Enums/UserRole.cs`:
```csharp
namespace Stallions.Shared.Enums;

public enum UserRole
{
    Buyer,
    StudFarmAdmin,
    Staff
}
```

Create `src/Shared/Enums/UserStatus.cs`:
```csharp
namespace Stallions.Shared.Enums;

public enum UserStatus
{
    PendingVerification,
    Active,
    Suspended
}
```

Create `src/Shared/Enums/ListingType.cs`:
```csharp
namespace Stallions.Shared.Enums;

public enum ListingType
{
    FixedPrice,
    Auction
}
```

Create `src/Shared/Enums/ListingStatus.cs`:
```csharp
namespace Stallions.Shared.Enums;

public enum ListingStatus
{
    Draft,
    Active,
    Sold,
    Expired,
    Cancelled
}
```

Create `src/Shared/Enums/BidStatus.cs`:
```csharp
namespace Stallions.Shared.Enums;

public enum BidStatus
{
    Active,
    Outbid,
    Won,
    SecondChance,
    Declined,
    Expired
}
```

Create `src/Shared/Enums/PurchaseStatus.cs`:
```csharp
namespace Stallions.Shared.Enums;

public enum PurchaseStatus
{
    Pending,
    Completed,
    Refunded
}
```

Create `src/Shared/Enums/BindingStatus.cs`:
```csharp
namespace Stallions.Shared.Enums;

public enum BindingStatus
{
    PendingAcknowledgement,
    Acknowledged,
    PdfGenerated,
    AwaitingSignatures,
    BuyerSigned,
    FarmSigned,
    Complete,
    Disputed
}
```

- [ ] **Step 2: Verify build**

```powershell
dotnet build src/Shared/Stallions.Shared.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```powershell
git add src/Shared/Enums/
git commit -m "feat(shared): add domain enums for user roles, listing types, bids, purchases, and bindings"
```

---

### Task 4: Entity classes — Identity domain

**Files:**
- Create: `src/Server/Data/Entities/User.cs`
- Create: `src/Server/Data/Entities/StudFarm.cs`
- Create: `tests/Server.Tests/Data/Entities/UserEntityTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Server.Tests/Data/Entities/UserEntityTests.cs`:
```csharp
using FluentAssertions;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data.Entities;

public class UserEntityTests
{
    [Fact]
    public void User_DefaultStatus_IsPendingVerification()
    {
        var user = new User();
        user.Status.Should().Be(UserStatus.PendingVerification);
    }

    [Fact]
    public void User_DefaultId_IsNotEmpty()
    {
        var user = new User();
        user.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void StudFarm_DefaultIsActive_IsTrue()
    {
        var farm = new StudFarm();
        farm.IsActive.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test tests/Server.Tests/ --filter "UserEntityTests"
```

Expected: FAIL — `Stallions.Server.Data.Entities` namespace not found.

- [ ] **Step 3: Create User entity**

Create `src/Server/Data/Entities/User.cs`:
```csharp
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EntraObjectId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public UserStatus Status { get; set; } = UserStatus.PendingVerification;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? VerifiedAt { get; set; }
    public Guid? VerifiedByUserId { get; set; }

    // Navigation properties
    public User? VerifiedBy { get; set; }
    public StudFarm? StudFarm { get; set; }
    public ICollection<Bid> Bids { get; set; } = new List<Bid>();
    public ICollection<Purchase> Purchases { get; set; } = new List<Purchase>();
    public ICollection<Enquiry> Enquiries { get; set; } = new List<Enquiry>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
```

- [ ] **Step 4: Create StudFarm entity**

Create `src/Server/Data/Entities/StudFarm.cs`:
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public User User { get; set; } = null!;
    public ICollection<Stallion> Stallions { get; set; } = new List<Stallion>();
    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
}
```

- [ ] **Step 5: Run tests to verify they pass**

```powershell
dotnet test tests/Server.Tests/ --filter "UserEntityTests"
```

Expected: PASS — 3 tests passed.

- [ ] **Step 6: Commit**

```powershell
git add src/Server/Data/Entities/User.cs src/Server/Data/Entities/StudFarm.cs tests/Server.Tests/Data/Entities/UserEntityTests.cs
git commit -m "feat(entities): add User and StudFarm entity classes"
```

---

### Task 5: Entity classes — Catalogue domain

**Files:**
- Create: `src/Server/Data/Entities/Stallion.cs`
- Create: `src/Server/Data/Entities/StallionImage.cs`
- Create: `src/Server/Data/Entities/Season.cs`

- [ ] **Step 1: Create Stallion entity**

Create `src/Server/Data/Entities/Stallion.cs`:
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public StudFarm StudFarm { get; set; } = null!;
    public ICollection<StallionImage> Images { get; set; } = new List<StallionImage>();
    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
}
```

- [ ] **Step 2: Create StallionImage entity**

Create `src/Server/Data/Entities/StallionImage.cs`:
```csharp
namespace Stallions.Server.Data.Entities;

public class StallionImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StallionId { get; set; }
    public string BlobPath { get; set; } = string.Empty;
    public bool IsPrimary { get; set; } = false;
    public int DisplayOrder { get; set; } = 0;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Stallion Stallion { get; set; } = null!;
}
```

- [ ] **Step 3: Create Season entity**

Create `src/Server/Data/Entities/Season.cs`:
```csharp
namespace Stallions.Server.Data.Entities;

public class Season
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsOpen { get; set; } = false;
    public DateTime? OpenedAt { get; set; }
    public Guid? OpenedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User? OpenedBy { get; set; }
    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
}
```

- [ ] **Step 4: Verify build**

```powershell
dotnet build src/Server/Stallions.Server.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```powershell
git add src/Server/Data/Entities/Stallion.cs src/Server/Data/Entities/StallionImage.cs src/Server/Data/Entities/Season.cs
git commit -m "feat(entities): add Stallion, StallionImage, and Season entity classes"
```

---

### Task 6: Entity classes — Listings domain (TPT)

**Files:**
- Create: `src/Server/Data/Entities/Listing.cs`
- Create: `src/Server/Data/Entities/AuctionListing.cs`
- Create: `src/Server/Data/Entities/FixedPriceListing.cs`
- Create: `tests/Server.Tests/Data/Entities/ListingEntityTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Server.Tests/Data/Entities/ListingEntityTests.cs`:
```csharp
using FluentAssertions;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data.Entities;

public class ListingEntityTests
{
    [Fact]
    public void Listing_DefaultStatus_IsDraft()
    {
        var listing = new Listing();
        listing.Status.Should().Be(ListingStatus.Draft);
    }

    [Fact]
    public void AuctionListing_DefaultMinimumBidIncrement_Is25()
    {
        var listing = new AuctionListing();
        listing.MinimumBidIncrement.Should().Be(25m);
    }

    [Fact]
    public void AuctionListing_DefaultIsNoReserve_IsFalse()
    {
        var listing = new AuctionListing();
        listing.IsNoReserve.Should().BeFalse();
    }

    [Fact]
    public void FixedPriceListing_OnCreation_QuantityRemainingEqualsQuantity()
    {
        var listing = new FixedPriceListing { Quantity = 5, QuantityRemaining = 5 };
        listing.QuantityRemaining.Should().Be(listing.Quantity);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test tests/Server.Tests/ --filter "ListingEntityTests"
```

Expected: FAIL — entity classes not found.

- [ ] **Step 3: Create Listing base entity**

Create `src/Server/Data/Entities/Listing.cs`:
```csharp
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

public class Listing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StallionId { get; set; }
    public Guid SeasonId { get; set; }
    public Guid StudFarmId { get; set; }
    public ListingType ListingType { get; set; }
    public ListingStatus Status { get; set; } = ListingStatus.Draft;
    public decimal? PlatformFeePercent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    // Navigation properties
    public Stallion Stallion { get; set; } = null!;
    public Season Season { get; set; } = null!;
    public StudFarm StudFarm { get; set; } = null!;
    public ICollection<Purchase> Purchases { get; set; } = new List<Purchase>();
    public ICollection<Enquiry> Enquiries { get; set; } = new List<Enquiry>();
}
```

- [ ] **Step 4: Create AuctionListing entity**

Create `src/Server/Data/Entities/AuctionListing.cs`:
```csharp
namespace Stallions.Server.Data.Entities;

public class AuctionListing : Listing
{
    public decimal StartingPrice { get; set; }
    public decimal? ReservePrice { get; set; }
    public bool IsNoReserve { get; set; } = false;
    public decimal MinimumBidIncrement { get; set; } = 25m;
    public DateTime EndDateTime { get; set; }
    public Guid? WinningBidId { get; set; }

    // Navigation properties
    public Bid? WinningBid { get; set; }
    public ICollection<Bid> Bids { get; set; } = new List<Bid>();
}
```

- [ ] **Step 5: Create FixedPriceListing entity**

Create `src/Server/Data/Entities/FixedPriceListing.cs`:
```csharp
namespace Stallions.Server.Data.Entities;

public class FixedPriceListing : Listing
{
    public decimal PriceIncGst { get; set; }
    public int Quantity { get; set; }
    public int QuantityRemaining { get; set; }
}
```

- [ ] **Step 6: Run tests to verify they pass**

```powershell
dotnet test tests/Server.Tests/ --filter "ListingEntityTests"
```

Expected: PASS — 4 tests passed.

- [ ] **Step 7: Commit**

```powershell
git add src/Server/Data/Entities/Listing.cs src/Server/Data/Entities/AuctionListing.cs src/Server/Data/Entities/FixedPriceListing.cs tests/Server.Tests/Data/Entities/ListingEntityTests.cs
git commit -m "feat(entities): add Listing (TPT base), AuctionListing, and FixedPriceListing entity classes"
```

---

### Task 7: Entity classes — Transactions domain

**Files:**
- Create: `src/Server/Data/Entities/Bid.cs`
- Create: `src/Server/Data/Entities/Purchase.cs`
- Create: `src/Server/Data/Entities/NominationBinding.cs`

- [ ] **Step 1: Create Bid entity**

Create `src/Server/Data/Entities/Bid.cs`:
```csharp
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

public class Bid
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AuctionListingId { get; set; }
    public Guid BuyerUserId { get; set; }
    public decimal AmountIncGst { get; set; }
    public DateTime PlacedAt { get; set; } = DateTime.UtcNow;
    public BidStatus Status { get; set; } = BidStatus.Active;

    // Navigation properties
    public AuctionListing AuctionListing { get; set; } = null!;
    public User Buyer { get; set; } = null!;
}
```

- [ ] **Step 2: Create Purchase entity**

Create `src/Server/Data/Entities/Purchase.cs`:
```csharp
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

public class Purchase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public Guid BuyerUserId { get; set; }
    public Guid? BidId { get; set; }
    public decimal TotalPriceIncGst { get; set; }
    public decimal PlatformFeeIncGst { get; set; }
    public decimal PlatformFeeExGst { get; set; }
    public decimal PlatformFeeGst { get; set; }
    public string MareName { get; set; } = string.Empty;
    public string? MareRegistration { get; set; }
    public string? MareBreed { get; set; }
    public string? PaymentProvider { get; set; }
    public string? PaymentReference { get; set; }
    public DateTime? PaidAt { get; set; }
    public PurchaseStatus Status { get; set; } = PurchaseStatus.Pending;
    public decimal? RefundAmount { get; set; }
    public DateTime? RefundedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Listing Listing { get; set; } = null!;
    public User Buyer { get; set; } = null!;
    public Bid? Bid { get; set; }
    public NominationBinding? NominationBinding { get; set; }
}
```

- [ ] **Step 3: Create NominationBinding entity**

Create `src/Server/Data/Entities/NominationBinding.cs`:
```csharp
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

public class NominationBinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PurchaseId { get; set; }
    public BindingStatus Status { get; set; } = BindingStatus.PendingAcknowledgement;
    public string? PdfBlobPath { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public Guid? AcknowledgedByUserId { get; set; }
    public DateTime? BuyerSignedAt { get; set; }
    public DateTime? FarmSignedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Navigation properties
    public Purchase Purchase { get; set; } = null!;
    public User? AcknowledgedBy { get; set; }
}
```

- [ ] **Step 4: Verify build**

```powershell
dotnet build src/Server/Stallions.Server.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```powershell
git add src/Server/Data/Entities/Bid.cs src/Server/Data/Entities/Purchase.cs src/Server/Data/Entities/NominationBinding.cs
git commit -m "feat(entities): add Bid, Purchase, and NominationBinding entity classes"
```

---

### Task 8: Entity classes — Communication domain

**Files:**
- Create: `src/Server/Data/Entities/Enquiry.cs`
- Create: `src/Server/Data/Entities/EnquiryMessage.cs`
- Create: `src/Server/Data/Entities/AuditLog.cs`

- [ ] **Step 1: Create Enquiry entity**

Create `src/Server/Data/Entities/Enquiry.cs`:
```csharp
namespace Stallions.Server.Data.Entities;

public class Enquiry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public Guid BuyerUserId { get; set; }
    public Guid StudFarmUserId { get; set; }
    public string Status { get; set; } = "Open";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }

    // Navigation properties
    public Listing Listing { get; set; } = null!;
    public User Buyer { get; set; } = null!;
    public User StudFarmUser { get; set; } = null!;
    public ICollection<EnquiryMessage> Messages { get; set; } = new List<EnquiryMessage>();
}
```

- [ ] **Step 2: Create EnquiryMessage entity**

Create `src/Server/Data/Entities/EnquiryMessage.cs`:
```csharp
namespace Stallions.Server.Data.Entities;

public class EnquiryMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EnquiryId { get; set; }
    public Guid SenderUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public bool IsReadByRecipient { get; set; } = false;

    // Navigation properties
    public Enquiry Enquiry { get; set; } = null!;
    public User Sender { get; set; } = null!;
}
```

- [ ] **Step 3: Create AuditLog entity**

Create `src/Server/Data/Entities/AuditLog.cs`:
```csharp
namespace Stallions.Server.Data.Entities;

public class AuditLog
{
    public long Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string? Details { get; set; }

    // Navigation properties
    public User? User { get; set; }
}
```

- [ ] **Step 4: Verify full build**

```powershell
dotnet build
```

Expected: `Build succeeded.` All 5 projects build cleanly.

- [ ] **Step 5: Commit**

```powershell
git add src/Server/Data/Entities/Enquiry.cs src/Server/Data/Entities/EnquiryMessage.cs src/Server/Data/Entities/AuditLog.cs
git commit -m "feat(entities): add Enquiry, EnquiryMessage, and AuditLog entity classes"
```

---

### Task 9: Configure AppDbContext

**Files:**
- Create: `src/Server/Data/AppDbContext.cs`

- [ ] **Step 1: Create AppDbContext**

Create `src/Server/Data/AppDbContext.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<StudFarm> StudFarms => Set<StudFarm>();
    public DbSet<Stallion> Stallions => Set<Stallion>();
    public DbSet<StallionImage> StallionImages => Set<StallionImage>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<AuctionListing> AuctionListings => Set<AuctionListing>();
    public DbSet<FixedPriceListing> FixedPriceListings => Set<FixedPriceListing>();
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<NominationBinding> NominationBindings => Set<NominationBinding>();
    public DbSet<Enquiry> Enquiries => Set<Enquiry>();
    public DbSet<EnquiryMessage> EnquiryMessages => Set<EnquiryMessage>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Users ────────────────────────────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.EntraObjectId).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(u => u.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(u => u.Email).HasMaxLength(256).IsRequired();
            e.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(u => u.EntraObjectId).HasMaxLength(36).IsRequired();

            e.HasOne(u => u.VerifiedBy)
                .WithMany()
                .HasForeignKey(u => u.VerifiedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ── StudFarms ────────────────────────────────────────────────────────
        modelBuilder.Entity<StudFarm>(e =>
        {
            e.HasKey(f => f.Id);
            e.HasIndex(f => f.UserId).IsUnique();
            e.Property(f => f.Name).HasMaxLength(200).IsRequired();
            e.Property(f => f.ABN).HasMaxLength(14);
            e.Property(f => f.ContactPhone).HasMaxLength(20);
            e.Property(f => f.ContactEmail).HasMaxLength(256);
            e.Property(f => f.Address).HasMaxLength(500);

            e.HasOne(f => f.User)
                .WithOne(u => u.StudFarm)
                .HasForeignKey<StudFarm>(f => f.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Stallions ────────────────────────────────────────────────────────
        modelBuilder.Entity<Stallion>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).HasMaxLength(200).IsRequired();
            e.Property(s => s.Colour).HasMaxLength(50);
            e.Property(s => s.Sire).HasMaxLength(200);
            e.Property(s => s.Dam).HasMaxLength(200);
            e.Property(s => s.RegistrationNumber).HasMaxLength(100);

            e.HasOne(s => s.StudFarm)
                .WithMany(f => f.Stallions)
                .HasForeignKey(s => s.StudFarmId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── StallionImages ───────────────────────────────────────────────────
        modelBuilder.Entity<StallionImage>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.BlobPath).HasMaxLength(500).IsRequired();

            e.HasOne(i => i.Stallion)
                .WithMany(s => s.Images)
                .HasForeignKey(i => i.StallionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Seasons ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Season>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).HasMaxLength(100).IsRequired();

            e.HasOne(s => s.OpenedBy)
                .WithMany()
                .HasForeignKey(s => s.OpenedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ── Listings (TPT base) ──────────────────────────────────────────────
        modelBuilder.Entity<Listing>(e =>
        {
            e.ToTable("Listings");
            e.HasKey(l => l.Id);
            e.Property(l => l.ListingType).HasConversion<string>().HasMaxLength(20);
            e.Property(l => l.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(l => l.PlatformFeePercent).HasPrecision(5, 2);

            e.HasIndex(l => new { l.Status, l.SeasonId });
            e.HasIndex(l => new { l.StudFarmId, l.Status });

            e.HasOne(l => l.Stallion)
                .WithMany(s => s.Listings)
                .HasForeignKey(l => l.StallionId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(l => l.Season)
                .WithMany(s => s.Listings)
                .HasForeignKey(l => l.SeasonId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(l => l.StudFarm)
                .WithMany(f => f.Listings)
                .HasForeignKey(l => l.StudFarmId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── AuctionListings (TPT child) ──────────────────────────────────────
        modelBuilder.Entity<AuctionListing>(e =>
        {
            e.ToTable("AuctionListings");
            e.Property(a => a.StartingPrice).HasPrecision(12, 2);
            e.Property(a => a.ReservePrice).HasPrecision(12, 2);
            e.Property(a => a.MinimumBidIncrement).HasPrecision(12, 2);

            e.HasOne(a => a.WinningBid)
                .WithMany()
                .HasForeignKey(a => a.WinningBidId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ── FixedPriceListings (TPT child) ───────────────────────────────────
        modelBuilder.Entity<FixedPriceListing>(e =>
        {
            e.ToTable("FixedPriceListings");
            e.Property(f => f.PriceIncGst).HasPrecision(12, 2);
        });

        // ── Bids ─────────────────────────────────────────────────────────────
        modelBuilder.Entity<Bid>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.AmountIncGst).HasPrecision(12, 2);
            e.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);

            e.HasIndex(b => new { b.AuctionListingId, b.AmountIncGst });

            e.HasOne(b => b.AuctionListing)
                .WithMany(a => a.Bids)
                .HasForeignKey(b => b.AuctionListingId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(b => b.Buyer)
                .WithMany(u => u.Bids)
                .HasForeignKey(b => b.BuyerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Purchases ────────────────────────────────────────────────────────
        modelBuilder.Entity<Purchase>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.TotalPriceIncGst).HasPrecision(12, 2);
            e.Property(p => p.PlatformFeeIncGst).HasPrecision(12, 2);
            e.Property(p => p.PlatformFeeExGst).HasPrecision(12, 2);
            e.Property(p => p.PlatformFeeGst).HasPrecision(12, 2);
            e.Property(p => p.RefundAmount).HasPrecision(12, 2);
            e.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.MareName).HasMaxLength(200).IsRequired();
            e.Property(p => p.MareRegistration).HasMaxLength(100);
            e.Property(p => p.MareBreed).HasMaxLength(100);
            e.Property(p => p.PaymentProvider).HasMaxLength(50);
            e.Property(p => p.PaymentReference).HasMaxLength(200);

            e.HasOne(p => p.Listing)
                .WithMany(l => l.Purchases)
                .HasForeignKey(p => p.ListingId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(p => p.Buyer)
                .WithMany(u => u.Purchases)
                .HasForeignKey(p => p.BuyerUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(p => p.Bid)
                .WithMany()
                .HasForeignKey(p => p.BidId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ── NominationBindings ───────────────────────────────────────────────
        modelBuilder.Entity<NominationBinding>(e =>
        {
            e.HasKey(n => n.Id);
            e.HasIndex(n => n.PurchaseId).IsUnique();
            e.Property(n => n.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(n => n.PdfBlobPath).HasMaxLength(500);

            e.HasOne(n => n.Purchase)
                .WithOne(p => p.NominationBinding)
                .HasForeignKey<NominationBinding>(n => n.PurchaseId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(n => n.AcknowledgedBy)
                .WithMany()
                .HasForeignKey(n => n.AcknowledgedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ── Enquiries ────────────────────────────────────────────────────────
        modelBuilder.Entity<Enquiry>(e =>
        {
            e.HasKey(eq => eq.Id);
            e.Property(eq => eq.Status).HasMaxLength(20);

            e.HasOne(eq => eq.Listing)
                .WithMany(l => l.Enquiries)
                .HasForeignKey(eq => eq.ListingId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(eq => eq.Buyer)
                .WithMany(u => u.Enquiries)
                .HasForeignKey(eq => eq.BuyerUserId)
                .OnDelete(DeleteBehavior.NoAction);

            e.HasOne(eq => eq.StudFarmUser)
                .WithMany()
                .HasForeignKey(eq => eq.StudFarmUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ── EnquiryMessages ──────────────────────────────────────────────────
        modelBuilder.Entity<EnquiryMessage>(e =>
        {
            e.HasKey(m => m.Id);

            e.HasOne(m => m.Enquiry)
                .WithMany(eq => eq.Messages)
                .HasForeignKey(m => m.EnquiryId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(m => m.Sender)
                .WithMany()
                .HasForeignKey(m => m.SenderUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ── AuditLog ─────────────────────────────────────────────────────────
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).ValueGeneratedOnAdd();
            e.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
            e.Property(a => a.Action).HasMaxLength(100).IsRequired();

            e.HasIndex(a => new { a.EntityType, a.EntityId });
            e.HasIndex(a => a.OccurredAt);

            e.HasOne(a => a.User)
                .WithMany(u => u.AuditLogs)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
```

- [ ] **Step 2: Verify build**

```powershell
dotnet build src/Server/Stallions.Server.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```powershell
git add src/Server/Data/AppDbContext.cs
git commit -m "feat(data): configure AppDbContext with TPT inheritance, indexes, and all FK constraints"
```

---

### Task 10: Wire DbContext into the application and create migration

**Files:**
- Modify: `src/Server/Program.cs`
- Create: `src/Server/appsettings.Development.json`
- Create: `src/Server/Migrations/` (generated by EF Core)

- [ ] **Step 1: Update appsettings.json with connection string placeholder**

Replace the contents of `src/Server/appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 2: Create appsettings.Development.json with local connection string**

Create `src/Server/appsettings.Development.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=StallionsNomsDev;Trusted_Connection=True;MultipleActiveResultSets=true"
  }
}
```

> **Note:** This file uses LocalDB, which ships with Visual Studio. If you have SQL Server locally, replace with `Server=localhost;Database=StallionsNomsDev;Trusted_Connection=True;`. This file must NOT be committed — add it to `.gitignore`.

- [ ] **Step 3: Add appsettings.Development.json to .gitignore**

Add to the repo root `.gitignore`:
```
src/Server/appsettings.Development.json
```

- [ ] **Step 4: Register DbContext in Program.cs**

Replace the contents of `src/Server/Program.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

- [ ] **Step 5: Verify build**

```powershell
dotnet build src/Server/Stallions.Server.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 6: Create the initial EF Core migration**

Run from the repo root:
```powershell
dotnet ef migrations add InitialCreate --project src/Server/Stallions.Server.csproj --startup-project src/Server/Stallions.Server.csproj --output-dir Data/Migrations
```

Expected: `Build started... Done. To undo this action, use 'ef migrations remove'`

This generates `src/Server/Data/Migrations/` with three files:
- `{timestamp}_InitialCreate.cs` — the migration
- `{timestamp}_InitialCreate.Designer.cs` — snapshot metadata
- `AppDbContextModelSnapshot.cs` — model snapshot

- [ ] **Step 7: Apply the migration to the local database**

```powershell
dotnet ef database update --project src/Server/Stallions.Server.csproj --startup-project src/Server/Stallions.Server.csproj
```

Expected output ends with: `Done.`

- [ ] **Step 8: Verify the schema in SQL Server Object Explorer (Visual Studio) or via query**

```powershell
dotnet ef dbcontext info --project src/Server/Stallions.Server.csproj --startup-project src/Server/Stallions.Server.csproj
```

Expected: Lists the database provider and connection string in use.

Verify these tables exist in the database: `Users`, `StudFarms`, `Stallions`, `StallionImages`, `Seasons`, `Listings`, `AuctionListings`, `FixedPriceListings`, `Bids`, `Purchases`, `NominationBindings`, `Enquiries`, `EnquiryMessages`, `AuditLogs`.

- [ ] **Step 9: Commit**

```powershell
git add src/Server/Program.cs src/Server/appsettings.json src/Server/Data/Migrations/ .gitignore
git commit -m "feat(data): register AppDbContext in DI, add initial EF Core migration"
```

---

### Task 11: Repository interfaces

**Files:**
- Create: `src/Server/Data/Repositories/IUserRepository.cs`
- Create: `src/Server/Data/Repositories/IStudFarmRepository.cs`
- Create: `src/Server/Data/Repositories/IStallionRepository.cs`
- Create: `src/Server/Data/Repositories/ISeasonRepository.cs`
- Create: `src/Server/Data/Repositories/IListingRepository.cs`
- Create: `src/Server/Data/Repositories/IBidRepository.cs`
- Create: `src/Server/Data/Repositories/IPurchaseRepository.cs`
- Create: `src/Server/Data/Repositories/INominationBindingRepository.cs`
- Create: `src/Server/Data/Repositories/IEnquiryRepository.cs`
- Create: `src/Server/Data/Repositories/IAuditLogRepository.cs`

- [ ] **Step 1: Create IUserRepository**

Create `src/Server/Data/Repositories/IUserRepository.cs`:
```csharp
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id);
    Task<User?> GetByEntraObjectIdAsync(string entraObjectId);
    Task<IReadOnlyList<User>> GetAllAsync(UserRole? role = null, UserStatus? status = null);
    Task<User> AddAsync(User user);
    Task UpdateAsync(User user);
}
```

- [ ] **Step 2: Create IStudFarmRepository**

Create `src/Server/Data/Repositories/IStudFarmRepository.cs`:
```csharp
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IStudFarmRepository
{
    Task<StudFarm?> GetByIdAsync(Guid id);
    Task<StudFarm?> GetByUserIdAsync(Guid userId);
    Task<StudFarm> AddAsync(StudFarm studFarm);
    Task UpdateAsync(StudFarm studFarm);
}
```

- [ ] **Step 3: Create IStallionRepository**

Create `src/Server/Data/Repositories/IStallionRepository.cs`:
```csharp
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IStallionRepository
{
    Task<Stallion?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<Stallion>> GetByStudFarmIdAsync(Guid studFarmId);
    Task<IReadOnlyList<Stallion>> GetWithActiveListingsAsync();
    Task<Stallion> AddAsync(Stallion stallion);
    Task UpdateAsync(Stallion stallion);
}
```

- [ ] **Step 4: Create ISeasonRepository**

Create `src/Server/Data/Repositories/ISeasonRepository.cs`:
```csharp
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface ISeasonRepository
{
    Task<Season?> GetByIdAsync(Guid id);
    Task<Season?> GetCurrentOpenSeasonAsync();
    Task<IReadOnlyList<Season>> GetAllAsync();
    Task<Season> AddAsync(Season season);
    Task UpdateAsync(Season season);
}
```

- [ ] **Step 5: Create IListingRepository**

Create `src/Server/Data/Repositories/IListingRepository.cs`:
```csharp
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public interface IListingRepository
{
    Task<Listing?> GetByIdAsync(Guid id);
    Task<AuctionListing?> GetAuctionByIdAsync(Guid id);
    Task<FixedPriceListing?> GetFixedPriceByIdAsync(Guid id);
    Task<IReadOnlyList<Listing>> GetActiveAsync(Guid? seasonId = null, ListingType? type = null);
    Task<IReadOnlyList<Listing>> GetByStudFarmIdAsync(Guid studFarmId);
    Task<IReadOnlyList<AuctionListing>> GetExpiredAuctionsAsync();
    Task<Listing> AddAsync(Listing listing);
    Task UpdateAsync(Listing listing);
}
```

- [ ] **Step 6: Create IBidRepository**

Create `src/Server/Data/Repositories/IBidRepository.cs`:
```csharp
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IBidRepository
{
    Task<Bid?> GetByIdAsync(Guid id);
    Task<Bid?> GetHighestBidAsync(Guid auctionListingId);
    Task<Bid?> GetSecondHighestBidAsync(Guid auctionListingId);
    Task<IReadOnlyList<Bid>> GetByAuctionListingIdAsync(Guid auctionListingId);
    Task<IReadOnlyList<Bid>> GetByBuyerIdAsync(Guid buyerUserId);
    Task<Bid> AddAsync(Bid bid);
    Task UpdateAsync(Bid bid);
    Task UpdateRangeAsync(IEnumerable<Bid> bids);
}
```

- [ ] **Step 7: Create IPurchaseRepository**

Create `src/Server/Data/Repositories/IPurchaseRepository.cs`:
```csharp
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IPurchaseRepository
{
    Task<Purchase?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<Purchase>> GetByBuyerIdAsync(Guid buyerUserId);
    Task<IReadOnlyList<Purchase>> GetAllAsync();
    Task<Purchase> AddAsync(Purchase purchase);
    Task UpdateAsync(Purchase purchase);
    Task DeleteAsync(Purchase purchase);
}
```

- [ ] **Step 8: Create INominationBindingRepository**

Create `src/Server/Data/Repositories/INominationBindingRepository.cs`:
```csharp
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface INominationBindingRepository
{
    Task<NominationBinding?> GetByIdAsync(Guid id);
    Task<NominationBinding?> GetByPurchaseIdAsync(Guid purchaseId);
    Task<NominationBinding> AddAsync(NominationBinding binding);
    Task UpdateAsync(NominationBinding binding);
}
```

- [ ] **Step 9: Create IEnquiryRepository**

Create `src/Server/Data/Repositories/IEnquiryRepository.cs`:
```csharp
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IEnquiryRepository
{
    Task<Enquiry?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<Enquiry>> GetByBuyerIdAsync(Guid buyerUserId);
    Task<IReadOnlyList<Enquiry>> GetByStudFarmUserIdAsync(Guid studFarmUserId);
    Task<IReadOnlyList<Enquiry>> GetAllAsync();
    Task<Enquiry> AddAsync(Enquiry enquiry);
    Task UpdateAsync(Enquiry enquiry);
    Task AddMessageAsync(EnquiryMessage message);
}
```

- [ ] **Step 10: Create IAuditLogRepository**

Create `src/Server/Data/Repositories/IAuditLogRepository.cs`:
```csharp
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IAuditLogRepository
{
    Task LogAsync(string entityType, Guid entityId, string action, Guid? userId, string? details = null);
    Task<IReadOnlyList<AuditLog>> GetByEntityAsync(string entityType, Guid entityId);
}
```

- [ ] **Step 11: Verify build**

```powershell
dotnet build
```

Expected: `Build succeeded.`

- [ ] **Step 12: Commit**

```powershell
git add src/Server/Data/Repositories/I*.cs
git commit -m "feat(repositories): add all repository interfaces"
```

---

### Task 12: Repository implementations and integration tests

**Files:**
- Create: `src/Server/Data/Repositories/UserRepository.cs`
- Create: `src/Server/Data/Repositories/SeasonRepository.cs`
- Create: `src/Server/Data/Repositories/ListingRepository.cs`
- Create: `src/Server/Data/Repositories/BidRepository.cs`
- Create: `src/Server/Data/Repositories/PurchaseRepository.cs`
- Create: `src/Server/Data/Repositories/StudFarmRepository.cs`
- Create: `src/Server/Data/Repositories/StallionRepository.cs`
- Create: `src/Server/Data/Repositories/NominationBindingRepository.cs`
- Create: `src/Server/Data/Repositories/EnquiryRepository.cs`
- Create: `src/Server/Data/Repositories/AuditLogRepository.cs`
- Create: `tests/Server.Tests/Data/Repositories/UserRepositoryTests.cs`
- Create: `tests/Server.Tests/Data/Repositories/SeasonRepositoryTests.cs`
- Create: `tests/Server.Tests/Data/Repositories/ListingRepositoryTests.cs`
- Create: `tests/Server.Tests/Data/Repositories/BidRepositoryTests.cs`
- Create: `tests/Server.Tests/Data/Repositories/PurchaseRepositoryTests.cs`

- [ ] **Step 1: Create a shared test helper for building an in-memory DbContext**

Create `tests/Server.Tests/Helpers/DbContextFactory.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data;

namespace Stallions.Server.Tests.Helpers;

public static class DbContextFactory
{
    public static AppDbContext Create(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new AppDbContext(options);
    }
}
```

- [ ] **Step 2: Write failing tests for UserRepository**

Create `tests/Server.Tests/Data/Repositories/UserRepositoryTests.cs`:
```csharp
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
```

- [ ] **Step 3: Run failing tests**

```powershell
dotnet test tests/Server.Tests/ --filter "UserRepositoryTests"
```

Expected: FAIL — `UserRepository` not found.

- [ ] **Step 4: Implement UserRepository**

Create `src/Server/Data/Repositories/UserRepository.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;
    public UserRepository(AppDbContext db) => _db = db;

    public async Task<User?> GetByIdAsync(Guid id) =>
        await _db.Users.FindAsync(id);

    public async Task<User?> GetByEntraObjectIdAsync(string entraObjectId) =>
        await _db.Users.FirstOrDefaultAsync(u => u.EntraObjectId == entraObjectId);

    public async Task<IReadOnlyList<User>> GetAllAsync(UserRole? role = null, UserStatus? status = null)
    {
        var query = _db.Users.AsQueryable();
        if (role.HasValue) query = query.Where(u => u.Role == role.Value);
        if (status.HasValue) query = query.Where(u => u.Status == status.Value);
        return await query.OrderBy(u => u.DisplayName).ToListAsync();
    }

    public async Task<User> AddAsync(User user)
    {
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task UpdateAsync(User user)
    {
        _db.Users.Update(user);
        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: Run tests — verify UserRepository passes**

```powershell
dotnet test tests/Server.Tests/ --filter "UserRepositoryTests"
```

Expected: PASS — 2 tests passed.

- [ ] **Step 6: Implement SeasonRepository with tests**

Create `tests/Server.Tests/Data/Repositories/SeasonRepositoryTests.cs`:
```csharp
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
```

Create `src/Server/Data/Repositories/SeasonRepository.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class SeasonRepository : ISeasonRepository
{
    private readonly AppDbContext _db;
    public SeasonRepository(AppDbContext db) => _db = db;

    public async Task<Season?> GetByIdAsync(Guid id) =>
        await _db.Seasons.FindAsync(id);

    public async Task<Season?> GetCurrentOpenSeasonAsync() =>
        await _db.Seasons.FirstOrDefaultAsync(s => s.IsOpen);

    public async Task<IReadOnlyList<Season>> GetAllAsync() =>
        await _db.Seasons.OrderByDescending(s => s.StartDate).ToListAsync();

    public async Task<Season> AddAsync(Season season)
    {
        _db.Seasons.Add(season);
        await _db.SaveChangesAsync();
        return season;
    }

    public async Task UpdateAsync(Season season)
    {
        _db.Seasons.Update(season);
        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 7: Implement BidRepository with tests**

Create `tests/Server.Tests/Data/Repositories/BidRepositoryTests.cs`:
```csharp
using FluentAssertions;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data.Repositories;

public class BidRepositoryTests
{
    [Fact]
    public async Task GetHighestBidAsync_ReturnsActiveBidWithHighestAmount()
    {
        using var db = DbContextFactory.Create(nameof(GetHighestBidAsync_ReturnsActiveBidWithHighestAmount));
        var repo = new BidRepository(db);
        var listingId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        await repo.AddAsync(new Bid { AuctionListingId = listingId, BuyerUserId = buyerId, AmountIncGst = 5000m, Status = BidStatus.Outbid });
        await repo.AddAsync(new Bid { AuctionListingId = listingId, BuyerUserId = buyerId, AmountIncGst = 7500m, Status = BidStatus.Active });

        var highest = await repo.GetHighestBidAsync(listingId);

        highest.Should().NotBeNull();
        highest!.AmountIncGst.Should().Be(7500m);
    }

    [Fact]
    public async Task GetSecondHighestBidAsync_ReturnsHighestOutbidBid()
    {
        using var db = DbContextFactory.Create(nameof(GetSecondHighestBidAsync_ReturnsHighestOutbidBid));
        var repo = new BidRepository(db);
        var listingId = Guid.NewGuid();
        var buyerA = Guid.NewGuid();
        var buyerB = Guid.NewGuid();
        await repo.AddAsync(new Bid { AuctionListingId = listingId, BuyerUserId = buyerA, AmountIncGst = 5000m, Status = BidStatus.Outbid });
        await repo.AddAsync(new Bid { AuctionListingId = listingId, BuyerUserId = buyerB, AmountIncGst = 7500m, Status = BidStatus.Active });

        var second = await repo.GetSecondHighestBidAsync(listingId);

        second.Should().NotBeNull();
        second!.AmountIncGst.Should().Be(5000m);
    }
}
```

Create `src/Server/Data/Repositories/BidRepository.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public class BidRepository : IBidRepository
{
    private readonly AppDbContext _db;
    public BidRepository(AppDbContext db) => _db = db;

    public async Task<Bid?> GetByIdAsync(Guid id) =>
        await _db.Bids.FindAsync(id);

    public async Task<Bid?> GetHighestBidAsync(Guid auctionListingId) =>
        await _db.Bids
            .Where(b => b.AuctionListingId == auctionListingId && b.Status == BidStatus.Active)
            .OrderByDescending(b => b.AmountIncGst)
            .FirstOrDefaultAsync();

    public async Task<Bid?> GetSecondHighestBidAsync(Guid auctionListingId) =>
        await _db.Bids
            .Where(b => b.AuctionListingId == auctionListingId && b.Status == BidStatus.Outbid)
            .OrderByDescending(b => b.AmountIncGst)
            .FirstOrDefaultAsync();

    public async Task<IReadOnlyList<Bid>> GetByAuctionListingIdAsync(Guid auctionListingId) =>
        await _db.Bids
            .Where(b => b.AuctionListingId == auctionListingId)
            .OrderByDescending(b => b.AmountIncGst)
            .ToListAsync();

    public async Task<IReadOnlyList<Bid>> GetByBuyerIdAsync(Guid buyerUserId) =>
        await _db.Bids
            .Where(b => b.BuyerUserId == buyerUserId)
            .OrderByDescending(b => b.PlacedAt)
            .ToListAsync();

    public async Task<Bid> AddAsync(Bid bid)
    {
        _db.Bids.Add(bid);
        await _db.SaveChangesAsync();
        return bid;
    }

    public async Task UpdateAsync(Bid bid)
    {
        _db.Bids.Update(bid);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateRangeAsync(IEnumerable<Bid> bids)
    {
        _db.Bids.UpdateRange(bids);
        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 8: Implement remaining repositories (no tests — pattern is identical to above)**

Create `src/Server/Data/Repositories/StudFarmRepository.cs`:
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

Create `src/Server/Data/Repositories/StallionRepository.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class StallionRepository : IStallionRepository
{
    private readonly AppDbContext _db;
    public StallionRepository(AppDbContext db) => _db = db;

    public async Task<Stallion?> GetByIdAsync(Guid id) =>
        await _db.Stallions.Include(s => s.Images).FirstOrDefaultAsync(s => s.Id == id);

    public async Task<IReadOnlyList<Stallion>> GetByStudFarmIdAsync(Guid studFarmId) =>
        await _db.Stallions.Where(s => s.StudFarmId == studFarmId && s.IsActive)
            .Include(s => s.Images).ToListAsync();

    public async Task<IReadOnlyList<Stallion>> GetWithActiveListingsAsync() =>
        await _db.Stallions.Where(s => s.IsActive && s.Listings.Any(l => l.Status == Stallions.Shared.Enums.ListingStatus.Active))
            .Include(s => s.Images).ToListAsync();

    public async Task<Stallion> AddAsync(Stallion stallion)
    {
        _db.Stallions.Add(stallion);
        await _db.SaveChangesAsync();
        return stallion;
    }

    public async Task UpdateAsync(Stallion stallion)
    {
        _db.Stallions.Update(stallion);
        await _db.SaveChangesAsync();
    }
}
```

Create `src/Server/Data/Repositories/ListingRepository.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public class ListingRepository : IListingRepository
{
    private readonly AppDbContext _db;
    public ListingRepository(AppDbContext db) => _db = db;

    public async Task<Listing?> GetByIdAsync(Guid id) =>
        await _db.Listings.Include(l => l.Stallion).ThenInclude(s => s.Images)
            .FirstOrDefaultAsync(l => l.Id == id);

    public async Task<AuctionListing?> GetAuctionByIdAsync(Guid id) =>
        await _db.AuctionListings.Include(l => l.Stallion).ThenInclude(s => s.Images)
            .FirstOrDefaultAsync(l => l.Id == id);

    public async Task<FixedPriceListing?> GetFixedPriceByIdAsync(Guid id) =>
        await _db.FixedPriceListings.Include(l => l.Stallion).ThenInclude(s => s.Images)
            .FirstOrDefaultAsync(l => l.Id == id);

    public async Task<IReadOnlyList<Listing>> GetActiveAsync(Guid? seasonId = null, ListingType? type = null)
    {
        var query = _db.Listings.Where(l => l.Status == ListingStatus.Active)
            .Include(l => l.Stallion).ThenInclude(s => s.Images)
            .Include(l => l.Season).AsQueryable();
        if (seasonId.HasValue) query = query.Where(l => l.SeasonId == seasonId.Value);
        if (type.HasValue) query = query.Where(l => l.ListingType == type.Value);
        return await query.OrderByDescending(l => l.PublishedAt).ToListAsync();
    }

    public async Task<IReadOnlyList<Listing>> GetByStudFarmIdAsync(Guid studFarmId) =>
        await _db.Listings.Where(l => l.StudFarmId == studFarmId)
            .Include(l => l.Stallion).OrderByDescending(l => l.CreatedAt).ToListAsync();

    public async Task<IReadOnlyList<AuctionListing>> GetExpiredAuctionsAsync() =>
        await _db.AuctionListings
            .Where(a => a.EndDateTime <= DateTime.UtcNow && a.Status == ListingStatus.Active)
            .ToListAsync();

    public async Task<Listing> AddAsync(Listing listing)
    {
        _db.Listings.Add(listing);
        await _db.SaveChangesAsync();
        return listing;
    }

    public async Task UpdateAsync(Listing listing)
    {
        _db.Listings.Update(listing);
        await _db.SaveChangesAsync();
    }
}
```

Create `src/Server/Data/Repositories/PurchaseRepository.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class PurchaseRepository : IPurchaseRepository
{
    private readonly AppDbContext _db;
    public PurchaseRepository(AppDbContext db) => _db = db;

    public async Task<Purchase?> GetByIdAsync(Guid id) =>
        await _db.Purchases.Include(p => p.Listing).Include(p => p.Buyer)
            .FirstOrDefaultAsync(p => p.Id == id);

    public async Task<IReadOnlyList<Purchase>> GetByBuyerIdAsync(Guid buyerUserId) =>
        await _db.Purchases.Where(p => p.BuyerUserId == buyerUserId)
            .OrderByDescending(p => p.CreatedAt).ToListAsync();

    public async Task<IReadOnlyList<Purchase>> GetAllAsync() =>
        await _db.Purchases.Include(p => p.Buyer).Include(p => p.Listing)
            .OrderByDescending(p => p.CreatedAt).ToListAsync();

    public async Task<Purchase> AddAsync(Purchase purchase)
    {
        _db.Purchases.Add(purchase);
        await _db.SaveChangesAsync();
        return purchase;
    }

    public async Task UpdateAsync(Purchase purchase)
    {
        _db.Purchases.Update(purchase);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Purchase purchase)
    {
        _db.Purchases.Remove(purchase);
        await _db.SaveChangesAsync();
    }
}
```

Create `src/Server/Data/Repositories/NominationBindingRepository.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class NominationBindingRepository : INominationBindingRepository
{
    private readonly AppDbContext _db;
    public NominationBindingRepository(AppDbContext db) => _db = db;

    public async Task<NominationBinding?> GetByIdAsync(Guid id) =>
        await _db.NominationBindings.Include(n => n.Purchase).FirstOrDefaultAsync(n => n.Id == id);

    public async Task<NominationBinding?> GetByPurchaseIdAsync(Guid purchaseId) =>
        await _db.NominationBindings.FirstOrDefaultAsync(n => n.PurchaseId == purchaseId);

    public async Task<NominationBinding> AddAsync(NominationBinding binding)
    {
        _db.NominationBindings.Add(binding);
        await _db.SaveChangesAsync();
        return binding;
    }

    public async Task UpdateAsync(NominationBinding binding)
    {
        _db.NominationBindings.Update(binding);
        await _db.SaveChangesAsync();
    }
}
```

Create `src/Server/Data/Repositories/EnquiryRepository.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class EnquiryRepository : IEnquiryRepository
{
    private readonly AppDbContext _db;
    public EnquiryRepository(AppDbContext db) => _db = db;

    public async Task<Enquiry?> GetByIdAsync(Guid id) =>
        await _db.Enquiries.Include(e => e.Messages.OrderBy(m => m.SentAt))
            .FirstOrDefaultAsync(e => e.Id == id);

    public async Task<IReadOnlyList<Enquiry>> GetByBuyerIdAsync(Guid buyerUserId) =>
        await _db.Enquiries.Where(e => e.BuyerUserId == buyerUserId)
            .OrderByDescending(e => e.CreatedAt).ToListAsync();

    public async Task<IReadOnlyList<Enquiry>> GetByStudFarmUserIdAsync(Guid studFarmUserId) =>
        await _db.Enquiries.Where(e => e.StudFarmUserId == studFarmUserId)
            .OrderByDescending(e => e.CreatedAt).ToListAsync();

    public async Task<IReadOnlyList<Enquiry>> GetAllAsync() =>
        await _db.Enquiries.Include(e => e.Buyer)
            .OrderByDescending(e => e.CreatedAt).ToListAsync();

    public async Task<Enquiry> AddAsync(Enquiry enquiry)
    {
        _db.Enquiries.Add(enquiry);
        await _db.SaveChangesAsync();
        return enquiry;
    }

    public async Task UpdateAsync(Enquiry enquiry)
    {
        _db.Enquiries.Update(enquiry);
        await _db.SaveChangesAsync();
    }

    public async Task AddMessageAsync(EnquiryMessage message)
    {
        _db.EnquiryMessages.Add(message);
        await _db.SaveChangesAsync();
    }
}
```

Create `src/Server/Data/Repositories/AuditLogRepository.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly AppDbContext _db;
    public AuditLogRepository(AppDbContext db) => _db = db;

    public async Task LogAsync(string entityType, Guid entityId, string action, Guid? userId, string? details = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            UserId = userId,
            Details = details,
            OccurredAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<AuditLog>> GetByEntityAsync(string entityType, Guid entityId) =>
        await _db.AuditLogs
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .OrderByDescending(a => a.OccurredAt)
            .ToListAsync();
}
```

- [ ] **Step 9: Run all tests**

```powershell
dotnet test tests/Server.Tests/ -v normal
```

Expected: All tests pass. Output shows number of tests passed.

- [ ] **Step 10: Register all repositories in Program.cs**

Add to `src/Server/Program.cs` after the `AddDbContext` call:
```csharp
// Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IStudFarmRepository, StudFarmRepository>();
builder.Services.AddScoped<IStallionRepository, StallionRepository>();
builder.Services.AddScoped<ISeasonRepository, SeasonRepository>();
builder.Services.AddScoped<IListingRepository, ListingRepository>();
builder.Services.AddScoped<IBidRepository, BidRepository>();
builder.Services.AddScoped<IPurchaseRepository, PurchaseRepository>();
builder.Services.AddScoped<INominationBindingRepository, NominationBindingRepository>();
builder.Services.AddScoped<IEnquiryRepository, EnquiryRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
```

Add the required using statements at the top of `Program.cs`:
```csharp
using Stallions.Server.Data.Repositories;
```

- [ ] **Step 11: Final build and test run**

```powershell
dotnet build
dotnet test tests/Server.Tests/
```

Expected: Build succeeded, all tests pass.

- [ ] **Step 12: Commit**

```powershell
git add src/Server/Data/Repositories/ tests/Server.Tests/Data/ tests/Server.Tests/Helpers/ src/Server/Program.cs
git commit -m "feat(repositories): implement all repositories and register in DI — data layer complete"
```
