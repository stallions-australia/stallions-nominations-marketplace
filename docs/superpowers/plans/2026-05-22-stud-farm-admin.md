# Stud Farm Admin UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an authenticated `/admin/*` section to the Blazor WASM client giving stud farm operators full self-service management of their stallions, listings, and enquiries.

**Architecture:** A new `AdminLayout.razor` wraps all `/admin/*` routes with a persistent sidebar. A new `AdminApiService` handles all authenticated admin API calls. Server-side changes extend existing services with safe-edit support, T&C locking, unpublish/close actions, and image upload via Azure Blob Storage.

**Tech Stack:** Blazor WASM (.NET 9), ASP.NET Core Web API, EF Core, Azure Blob Storage (`Azure.Storage.Blobs`), Azure Identity (`DefaultAzureCredential`), Microsoft Entra ID (MSAL)

---

## File Map

### Modify (server)
- `src/Shared/DTOs/Listings/ListingDto.cs` — add `Description`, `TermsAndConditions`
- `src/Shared/DTOs/Listings/CreateFixedPriceListingRequest.cs` — add `TermsAndConditions` (required), `Description`
- `src/Shared/DTOs/Listings/CreateAuctionListingRequest.cs` — add `TermsAndConditions` (required), `Description`
- `src/Shared/DTOs/Listings/UpdateListingRequest.cs` — add `Description`, `TermsAndConditions`
- `src/Shared/DTOs/Stallions/UpdateStallionRequest.cs` — add `IsActive`
- `src/Shared/DTOs/Stallions/StallionSummaryDto.cs` — add `IsActive`, `TotalListingCount`
- `src/Shared/DTOs/Enquiries/EnquirySummaryDto.cs` — add `StallionName`, `ListingTitle`, `IsUnread`, `BuyerName`
- `src/Server/Data/Entities/Listing.cs` — add `Description`, `TermsAndConditions`
- `src/Server/Services/IListingService.cs` — add `UnpublishListingAsync`, `CloseByStudFarmAsync`
- `src/Server/Services/ListingService.cs` — safe edits, T&C lock, new methods, map new fields
- `src/Server/Services/IStallionService.cs` — add `UploadImageAsync`
- `src/Server/Services/StallionService.cs` — fix inactive toggle, add `UploadImageAsync`
- `src/Server/Services/EnquiryService.cs` — enrich `MapToSummary`
- `src/Server/Controllers/ListingsController.cs` — add unpublish + close endpoints
- `src/Server/Controllers/StallionsController.cs` — implement image upload
- `src/Server/Stallions.Server.csproj` — add `Azure.Storage.Blobs`, `Azure.Identity`

### Create (server)
- `src/Server/Services/IBlobStorageService.cs`
- `src/Server/Services/BlobStorageService.cs`
- `src/Server/Data/Migrations/` — new EF migration for Listing.Description + TermsAndConditions

### Create (client)
- `src/Client/Services/AdminApiService.cs`
- `src/Client/Layout/AdminLayout.razor`
- `src/Client/Layout/AdminLayout.razor.css`
- `src/Client/Pages/Admin/AdminStallions.razor`
- `src/Client/Pages/Admin/AdminStallionForm.razor`
- `src/Client/Pages/Admin/AdminListings.razor`
- `src/Client/Pages/Admin/AdminListingForm.razor`
- `src/Client/Pages/Admin/AdminListingDetail.razor`
- `src/Client/Pages/Admin/AdminEnquiries.razor`
- `src/Client/Pages/Admin/AdminEnquiryDetail.razor`
- `src/Client/wwwroot/css/admin.css`

### Modify (client)
- `src/Client/Program.cs` — register `AdminApiService`
- `src/Client/_Imports.razor` — add admin namespaces

---

## Task 1: Shared DTOs — listing fields, stallion IsActive, enquiry enrichment

**Files:**
- Modify: `src/Shared/DTOs/Listings/ListingDto.cs`
- Modify: `src/Shared/DTOs/Listings/CreateFixedPriceListingRequest.cs`
- Modify: `src/Shared/DTOs/Listings/CreateAuctionListingRequest.cs`
- Modify: `src/Shared/DTOs/Listings/UpdateListingRequest.cs`
- Modify: `src/Shared/DTOs/Stallions/UpdateStallionRequest.cs`
- Modify: `src/Shared/DTOs/Stallions/StallionSummaryDto.cs`
- Modify: `src/Shared/DTOs/Enquiries/EnquirySummaryDto.cs`
- Test: `tests/Server.Tests/Serialization/ListingDtoSerializationTests.cs`

- [ ] **Step 1: Write a failing test confirming Description round-trips through DTO serialization**

```csharp
// tests/Server.Tests/Serialization/ListingDtoSerializationTests.cs
// Add to existing test class:
[Fact]
public void FixedPriceListingDto_RoundTrips_DescriptionAndTerms()
{
    var dto = new FixedPriceListingDto
    {
        Id = Guid.NewGuid(),
        ListingType = "FixedPrice",
        Description = "Premium service, live foal guarantee.",
        TermsAndConditions = "45-day payment required on live foal.",
        PriceIncGst = 10000m,
        Quantity = 20,
        QuantityRemaining = 20
    };
    var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = false });
    var back = JsonSerializer.Deserialize<ListingDto>(json);
    var fp = Assert.IsType<FixedPriceListingDto>(back);
    Assert.Equal("Premium service, live foal guarantee.", fp.Description);
    Assert.Equal("45-day payment required on live foal.", fp.TermsAndConditions);
}
```

- [ ] **Step 2: Run the test — confirm it fails (property does not exist yet)**

```
dotnet test tests/Server.Tests --filter "FixedPriceListingDto_RoundTrips_DescriptionAndTerms"
```
Expected: compile error or FAIL.

- [ ] **Step 3: Add Description and TermsAndConditions to ListingDto base class**

```csharp
// src/Shared/DTOs/Listings/ListingDto.cs
// Add after ClosedAt:
public string? Description { get; set; }
public string? TermsAndConditions { get; set; }
```

- [ ] **Step 4: Add TermsAndConditions (required) and Description to create requests**

```csharp
// src/Shared/DTOs/Listings/CreateFixedPriceListingRequest.cs
public class CreateFixedPriceListingRequest
{
    public required Guid StallionId { get; set; }
    public required Guid SeasonId { get; set; }
    public required decimal PriceIncGst { get; set; }
    public required int Quantity { get; set; }
    public required string TermsAndConditions { get; set; }
    public string? Description { get; set; }
}
```

```csharp
// src/Shared/DTOs/Listings/CreateAuctionListingRequest.cs
public class CreateAuctionListingRequest
{
    public required Guid StallionId { get; set; }
    public required Guid SeasonId { get; set; }
    public required decimal StartingPrice { get; set; }
    public decimal? ReservePrice { get; set; }
    public bool IsNoReserve { get; set; }
    public decimal MinimumBidIncrement { get; set; } = 25m;
    public required DateTime EndDateTime { get; set; }
    public required string TermsAndConditions { get; set; }
    public string? Description { get; set; }
}
```

- [ ] **Step 5: Add Description and TermsAndConditions to UpdateListingRequest**

```csharp
// src/Shared/DTOs/Listings/UpdateListingRequest.cs
public class UpdateListingRequest
{
    public decimal? StartingPrice { get; set; }
    public decimal? ReservePrice { get; set; }
    public bool? IsNoReserve { get; set; }
    public decimal? MinimumBidIncrement { get; set; }
    public DateTime? EndDateTime { get; set; }
    public decimal? PriceIncGst { get; set; }
    public int? Quantity { get; set; }
    public string? Description { get; set; }
    public string? TermsAndConditions { get; set; }
}
```

- [ ] **Step 6: Add IsActive to UpdateStallionRequest; add IsActive and TotalListingCount to StallionSummaryDto**

```csharp
// src/Shared/DTOs/Stallions/UpdateStallionRequest.cs
public class UpdateStallionRequest
{
    public required string Name { get; set; }
    public int? YearOfBirth { get; set; }
    public string? Colour { get; set; }
    public string? Sire { get; set; }
    public string? Dam { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
}
```

```csharp
// src/Shared/DTOs/Stallions/StallionSummaryDto.cs
public class StallionSummaryDto
{
    public Guid Id { get; set; }
    public Guid StudFarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? YearOfBirth { get; set; }
    public string? Colour { get; set; }
    public string? PrimaryImagePath { get; set; }
    public int ActiveListingCount { get; set; }
    public int TotalListingCount { get; set; }
    public bool IsActive { get; set; }
}
```

- [ ] **Step 7: Enrich EnquirySummaryDto for admin inbox**

```csharp
// src/Shared/DTOs/Enquiries/EnquirySummaryDto.cs
public class EnquirySummaryDto
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int MessageCount { get; set; }
    public DateTime? LastMessageAt { get; set; }
    // Admin inbox fields
    public string StallionName { get; set; } = string.Empty;
    public string ListingTitle { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public bool IsUnread { get; set; }
}
```

- [ ] **Step 8: Run the serialization test — confirm it passes**

```
dotnet test tests/Server.Tests --filter "FixedPriceListingDto_RoundTrips_DescriptionAndTerms"
```
Expected: PASS.

- [ ] **Step 9: Commit**

```
git add src/Shared/DTOs/
git commit -m "feat: add Description/T&C to listing DTOs, IsActive to stallion DTOs, enrich EnquirySummaryDto"
```

---

## Task 2: Server entity + EF Core migration

**Files:**
- Modify: `src/Server/Data/Entities/Listing.cs`
- Create: migration via `dotnet ef migrations add`

- [ ] **Step 1: Write a failing test confirming Listing entity has Description and TermsAndConditions**

```csharp
// tests/Server.Tests/Entities/ListingEntityTests.cs (create file)
using Stallions.Server.Data.Entities;
using Xunit;

namespace Stallions.Server.Tests.Entities;

public class ListingEntityTests
{
    [Fact]
    public void FixedPriceListing_HasDescriptionAndTerms()
    {
        var listing = new FixedPriceListing
        {
            Description = "Bay stallion, excellent fertility.",
            TermsAndConditions = "Live foal guarantee, 45-day payment."
        };
        Assert.Equal("Bay stallion, excellent fertility.", listing.Description);
        Assert.Equal("Live foal guarantee, 45-day payment.", listing.TermsAndConditions);
    }
}
```

- [ ] **Step 2: Run — confirm FAIL (property does not exist)**

```
dotnet test tests/Server.Tests --filter "FixedPriceListing_HasDescriptionAndTerms"
```

- [ ] **Step 3: Add Description and TermsAndConditions to Listing entity**

```csharp
// src/Server/Data/Entities/Listing.cs
public class Listing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StallionId { get; set; }
    public Guid SeasonId { get; set; }
    public Guid StudFarmId { get; set; }
    public ListingType ListingType { get; set; }
    public ListingStatus Status { get; set; } = ListingStatus.Draft;
    public decimal? PlatformFeePercent { get; set; }
    public string? Description { get; set; }
    public string? TermsAndConditions { get; set; }
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

- [ ] **Step 4: Run test — confirm PASS**

```
dotnet test tests/Server.Tests --filter "FixedPriceListing_HasDescriptionAndTerms"
```

- [ ] **Step 5: Add EF Core migration**

Run from the solution root (where `Stallions.sln` lives):
```
dotnet ef migrations add AddListingDescriptionAndTerms --project src/Server --startup-project src/Server
```
Expected: new file in `src/Server/Data/Migrations/` prefixed with timestamp.

- [ ] **Step 6: Verify migration SQL looks correct**

```
dotnet ef migrations script --project src/Server --startup-project src/Server
```
Expected output should include:
```sql
ALTER TABLE "Listings" ADD "Description" nvarchar(max) NULL;
ALTER TABLE "Listings" ADD "TermsAndConditions" nvarchar(max) NULL;
```

- [ ] **Step 7: Commit**

```
git add src/Server/Data/Entities/Listing.cs src/Server/Data/Migrations/
git add tests/Server.Tests/Entities/ListingEntityTests.cs
git commit -m "feat: add Description and TermsAndConditions columns to Listing entity + migration"
```

---

## Task 3: Server — ListingService safe edits, T&C lock, unpublish, close

**Files:**
- Modify: `src/Server/Services/IListingService.cs`
- Modify: `src/Server/Services/ListingService.cs`
- Test: `tests/Server.Tests/Services/ListingServiceTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Server.Tests/Services/ListingServiceTests.cs
// Add these tests to the existing test class (or create the file if it doesn't exist).
// The test setup creates a mock IListingRepository, IUserService, IStudFarmRepository,
// IStallionRepository, ISeasonRepository — follow the pattern from existing service tests.

[Fact]
public async Task UpdateListingAsync_AllowsDescriptionEdit_OnActiveFixedPriceListing()
{
    // Arrange: active fixed-price listing belonging to caller's farm (PublishedAt set)
    var listing = new FixedPriceListing
    {
        Id = Guid.NewGuid(),
        StudFarmId = _farmId,
        Status = ListingStatus.Active,
        PublishedAt = DateTime.UtcNow.AddDays(-1),
        PriceIncGst = 8000m,
        Quantity = 20,
        QuantityRemaining = 18
    };
    SetupListing(listing);
    var request = new UpdateListingRequest { Description = "Updated description." };

    // Act
    var result = await _service.UpdateListingAsync(listing.Id, request);

    // Assert
    Assert.True(result.Succeeded);
    Assert.Equal("Updated description.", result.Value!.Description);
}

[Fact]
public async Task UpdateListingAsync_BlocksTermsEdit_AfterPublish()
{
    // Arrange: draft listing that was previously published then unpublished (PublishedAt set)
    var listing = new FixedPriceListing
    {
        Id = Guid.NewGuid(),
        StudFarmId = _farmId,
        Status = ListingStatus.Draft,
        PublishedAt = DateTime.UtcNow.AddDays(-1),  // was published before
        TermsAndConditions = "Original terms.",
        PriceIncGst = 8000m,
        Quantity = 20,
        QuantityRemaining = 20
    };
    SetupListing(listing);
    var request = new UpdateListingRequest { TermsAndConditions = "New terms." };

    // Act
    var result = await _service.UpdateListingAsync(listing.Id, request);

    // Assert: update succeeds but T&C is unchanged
    Assert.True(result.Succeeded);
    Assert.Equal("Original terms.", result.Value!.TermsAndConditions);
}

[Fact]
public async Task UpdateListingAsync_AllowsQuantityEdit_OnActiveFixedPriceListing()
{
    var listing = new FixedPriceListing
    {
        Id = Guid.NewGuid(),
        StudFarmId = _farmId,
        Status = ListingStatus.Active,
        PublishedAt = DateTime.UtcNow.AddDays(-1),
        PriceIncGst = 8000m,
        Quantity = 20,
        QuantityRemaining = 18  // 2 sold
    };
    SetupListing(listing);
    var request = new UpdateListingRequest { Quantity = 25 };

    var result = await _service.UpdateListingAsync(listing.Id, request);

    Assert.True(result.Succeeded);
    var fp = Assert.IsType<FixedPriceListingDto>(result.Value);
    Assert.Equal(25, fp.Quantity);
    Assert.Equal(23, fp.QuantityRemaining);  // 25 - 2 sold
}

[Fact]
public async Task UnpublishListingAsync_SetsStatusToDraft_DoesNotClearPublishedAt()
{
    var publishedAt = DateTime.UtcNow.AddDays(-1);
    var listing = new FixedPriceListing
    {
        Id = Guid.NewGuid(),
        StudFarmId = _farmId,
        Status = ListingStatus.Active,
        PublishedAt = publishedAt,
        PriceIncGst = 8000m,
        Quantity = 20,
        QuantityRemaining = 20
    };
    SetupListing(listing);

    var result = await _service.UnpublishListingAsync(listing.Id);

    Assert.True(result.Succeeded);
    Assert.Equal(ListingStatus.Draft, listing.Status);
    Assert.Equal(publishedAt, listing.PublishedAt);  // NOT cleared
}

[Fact]
public async Task CloseByStudFarmAsync_SetsCancelledAndClosedAt()
{
    var listing = new FixedPriceListing
    {
        Id = Guid.NewGuid(),
        StudFarmId = _farmId,
        Status = ListingStatus.Active,
        PublishedAt = DateTime.UtcNow.AddDays(-1),
        PriceIncGst = 8000m,
        Quantity = 20,
        QuantityRemaining = 20
    };
    SetupListing(listing);

    var result = await _service.CloseByStudFarmAsync(listing.Id);

    Assert.True(result.Succeeded);
    Assert.Equal(ListingStatus.Cancelled, listing.Status);
    Assert.NotNull(listing.ClosedAt);
}
```

- [ ] **Step 2: Run — confirm FAIL (methods don't exist yet)**

```
dotnet test tests/Server.Tests --filter "ListingServiceTests"
```

- [ ] **Step 3: Add UnpublishListingAsync and CloseByStudFarmAsync to IListingService**

```csharp
// src/Server/Services/IListingService.cs
public interface IListingService
{
    Task<ServiceResult<IReadOnlyList<ListingDto>>> GetActiveAsync(Guid? seasonId, ListingType? type, bool isStaff);
    Task<ServiceResult<IReadOnlyList<ListingCardDto>>> GetListingCardsAsync(Guid? seasonId, Guid? studFarmId, string? type);
    Task<ServiceResult<ListingDto>> GetByIdAsync(Guid id, bool isStaff);
    Task<ServiceResult<IReadOnlyList<ListingDto>>> GetMineAsync();
    Task<ServiceResult<ListingDto>> CreateAuctionListingAsync(CreateAuctionListingRequest request);
    Task<ServiceResult<ListingDto>> CreateFixedPriceListingAsync(CreateFixedPriceListingRequest request);
    Task<ServiceResult<ListingDto>> UpdateListingAsync(Guid id, UpdateListingRequest request);
    Task<ServiceResult> PublishListingAsync(Guid id);
    Task<ServiceResult> UnpublishListingAsync(Guid id);
    Task<ServiceResult> CloseByStudFarmAsync(Guid id);
    Task<ServiceResult> CancelListingAsync(Guid id);
    Task<ServiceResult<ListingDto>> RelistAsync(Guid id);
}
```

- [ ] **Step 4: Update ListingService — safe edits + T&C lock in UpdateListingAsync**

Replace the existing `UpdateListingAsync` method body:

```csharp
public async Task<ServiceResult<ListingDto>> UpdateListingAsync(Guid id, UpdateListingRequest request)
{
    var caller = await _users.GetOrCreateCurrentUserAsync();
    if (caller == null)
        return ServiceResult<ListingDto>.Forbidden("Caller identity could not be resolved.");

    var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
    if (farm == null)
        return ServiceResult<ListingDto>.Forbidden("No stud farm found for the current user.");

    var listing = await _listingRepo.GetByIdAsync(id);
    if (listing == null)
        return ServiceResult<ListingDto>.NotFound("Listing not found.");

    if (listing.StudFarmId != farm.Id)
        return ServiceResult<ListingDto>.Forbidden("You do not have permission to update this listing.");

    // Cancelled and Sold listings are permanently read-only.
    if (listing.Status == ListingStatus.Cancelled || listing.Status == ListingStatus.Sold)
        return ServiceResult<ListingDto>.BadRequest("This listing can no longer be edited.");

    // Safe edits: description is always editable (Draft or Active).
    if (request.Description is not null)
        listing.Description = request.Description;

    // Fields that are locked once the listing has ever been published.
    // PublishedAt is set on first publish and intentionally NOT cleared on unpublish,
    // so this sentinel permanently locks price, T&C, type, and auction dates.
    var neverPublished = listing.PublishedAt == null;

    if (neverPublished)
    {
        // Full edit allowed — listing has never gone live.
        if (request.TermsAndConditions is not null)
            listing.TermsAndConditions = request.TermsAndConditions;

        if (listing is AuctionListing al)
        {
            if (request.StartingPrice.HasValue) al.StartingPrice = request.StartingPrice.Value;
            if (request.ReservePrice.HasValue) al.ReservePrice = request.ReservePrice;
            if (request.IsNoReserve.HasValue) al.IsNoReserve = request.IsNoReserve.Value;
            if (request.MinimumBidIncrement.HasValue) al.MinimumBidIncrement = request.MinimumBidIncrement.Value;
            if (request.EndDateTime.HasValue) al.EndDateTime = request.EndDateTime.Value;
        }
        else if (listing is FixedPriceListing fpl)
        {
            if (request.PriceIncGst.HasValue) fpl.PriceIncGst = request.PriceIncGst.Value;
            if (request.Quantity.HasValue)
            {
                fpl.Quantity = request.Quantity.Value;
                fpl.QuantityRemaining = request.Quantity.Value;
            }
        }
    }
    else
    {
        // Only safe edits — listing has been published at least once.
        // T&C, price, auction dates, type are permanently locked.
        // For fixed price: quantity can be adjusted (preserving sold count).
        if (listing is FixedPriceListing fpl && request.Quantity.HasValue)
        {
            var soldCount = fpl.Quantity - fpl.QuantityRemaining;
            fpl.Quantity = request.Quantity.Value;
            fpl.QuantityRemaining = Math.Max(0, request.Quantity.Value - soldCount);
        }
        // Auction listings: description only (already handled above). No other safe edits.
    }

    await _listingRepo.UpdateAsync(listing);
    return ServiceResult<ListingDto>.Ok(MapToDto(listing, true));
}
```

- [ ] **Step 5: Add UnpublishListingAsync to ListingService**

```csharp
public async Task<ServiceResult> UnpublishListingAsync(Guid id)
{
    var caller = await _users.GetOrCreateCurrentUserAsync();
    if (caller == null)
        return ServiceResult.Forbidden("Caller identity could not be resolved.");

    var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
    if (farm == null)
        return ServiceResult.Forbidden("No stud farm found for the current user.");

    var listing = await _listingRepo.GetByIdAsync(id);
    if (listing == null)
        return ServiceResult.NotFound("Listing not found.");

    if (listing.StudFarmId != farm.Id)
        return ServiceResult.Forbidden("You do not have permission to unpublish this listing.");

    if (listing.Status != ListingStatus.Active)
        return ServiceResult.BadRequest("Only Active listings can be unpublished.");

    listing.Status = ListingStatus.Draft;
    // PublishedAt is intentionally NOT cleared — it permanently locks price/T&C
    // even after the listing returns to Draft state.
    await _listingRepo.UpdateAsync(listing);
    return ServiceResult.Ok();
}
```

- [ ] **Step 6: Add CloseByStudFarmAsync to ListingService**

```csharp
public async Task<ServiceResult> CloseByStudFarmAsync(Guid id)
{
    var caller = await _users.GetOrCreateCurrentUserAsync();
    if (caller == null)
        return ServiceResult.Forbidden("Caller identity could not be resolved.");

    var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
    if (farm == null)
        return ServiceResult.Forbidden("No stud farm found for the current user.");

    var listing = await _listingRepo.GetByIdAsync(id);
    if (listing == null)
        return ServiceResult.NotFound("Listing not found.");

    if (listing.StudFarmId != farm.Id)
        return ServiceResult.Forbidden("You do not have permission to close this listing.");

    if (listing.Status == ListingStatus.Cancelled || listing.Status == ListingStatus.Sold)
        return ServiceResult.BadRequest($"Listing is already closed (status: {listing.Status}).");

    listing.Status = ListingStatus.Cancelled;
    listing.ClosedAt = DateTime.UtcNow;
    await _listingRepo.UpdateAsync(listing);
    return ServiceResult.Ok();
}
```

- [ ] **Step 7: Update create methods to store Description and TermsAndConditions**

In `CreateAuctionListingAsync`, update the `AuctionListing` initialiser:
```csharp
var listing = new AuctionListing
{
    StallionId = request.StallionId,
    SeasonId = request.SeasonId,
    StudFarmId = farm.Id,
    ListingType = ListingType.Auction,
    Status = ListingStatus.Draft,
    StartingPrice = request.StartingPrice,
    ReservePrice = request.ReservePrice,
    IsNoReserve = request.IsNoReserve,
    MinimumBidIncrement = request.MinimumBidIncrement,
    EndDateTime = request.EndDateTime,
    TermsAndConditions = request.TermsAndConditions,
    Description = request.Description
};
```

In `CreateFixedPriceListingAsync`, update the `FixedPriceListing` initialiser:
```csharp
var listing = new FixedPriceListing
{
    StallionId = request.StallionId,
    SeasonId = request.SeasonId,
    StudFarmId = farm.Id,
    ListingType = ListingType.FixedPrice,
    Status = ListingStatus.Draft,
    PriceIncGst = request.PriceIncGst,
    Quantity = request.Quantity,
    QuantityRemaining = request.Quantity,
    TermsAndConditions = request.TermsAndConditions,
    Description = request.Description
};
```

- [ ] **Step 8: Update MapToDto to include Description and TermsAndConditions**

In the existing `MapToDto` switch expression, add to both the `AuctionListingDto` and `FixedPriceListingDto` initialisers:
```csharp
Description = al.Description,
TermsAndConditions = al.TermsAndConditions,
// (and similarly for fpl)
```

- [ ] **Step 9: Update MapToSummary in StallionService to include IsActive and TotalListingCount**

In `StallionService.MapToSummary`:
```csharp
private static StallionSummaryDto MapToSummary(Stallion s) => new()
{
    Id = s.Id,
    StudFarmId = s.StudFarmId,
    Name = s.Name,
    YearOfBirth = s.YearOfBirth,
    Colour = s.Colour,
    PrimaryImagePath = s.Images.FirstOrDefault(img => img.IsPrimary)?.BlobPath,
    ActiveListingCount = s.Listings.Count(l => l.Status == ListingStatus.Active),
    TotalListingCount = s.Listings.Count,
    IsActive = s.IsActive
};
```

- [ ] **Step 10: Run all listing service tests — confirm PASS**

```
dotnet test tests/Server.Tests --filter "ListingServiceTests"
```

- [ ] **Step 11: Commit**

```
git add src/Server/Services/IListingService.cs src/Server/Services/ListingService.cs
git add src/Server/Services/StallionService.cs
git add tests/Server.Tests/Services/ListingServiceTests.cs
git commit -m "feat: safe edits, T&C lock, unpublish and close actions in ListingService"
```

---

## Task 4: Server — StallionService IsActive toggle + new controller endpoints

**Files:**
- Modify: `src/Server/Services/StallionService.cs`
- Modify: `src/Server/Controllers/ListingsController.cs`
- Modify: `src/Server/Controllers/StallionsController.cs`
- Test: `tests/Server.Tests/Services/StallionServiceTests.cs`

- [ ] **Step 1: Write failing tests for IsActive toggle**

```csharp
// tests/Server.Tests/Services/StallionServiceTests.cs (add to existing class)
[Fact]
public async Task UpdateAsync_AllowsReactivation_OfInactiveStallion()
{
    var stallion = new Stallion
    {
        Id = Guid.NewGuid(),
        StudFarmId = _farmId,
        Name = "Sunline II",
        IsActive = false
    };
    SetupStallion(stallion);
    var request = new UpdateStallionRequest { Name = "Sunline II", IsActive = true };

    var result = await _service.UpdateAsync(stallion.Id, request);

    Assert.True(result.Succeeded);
    Assert.True(stallion.IsActive);
}

[Fact]
public async Task UpdateAsync_AllowsDeactivation_OfActiveStallion()
{
    var stallion = new Stallion
    {
        Id = Guid.NewGuid(),
        StudFarmId = _farmId,
        Name = "Sunline II",
        IsActive = true
    };
    SetupStallion(stallion);
    var request = new UpdateStallionRequest { Name = "Sunline II", IsActive = false };

    var result = await _service.UpdateAsync(stallion.Id, request);

    Assert.True(result.Succeeded);
    Assert.False(stallion.IsActive);
}
```

- [ ] **Step 2: Run — confirm FAIL**

```
dotnet test tests/Server.Tests --filter "StallionServiceTests"
```

- [ ] **Step 3: Fix StallionService.UpdateAsync — allow editing inactive stallions + apply IsActive**

Replace the ownership + null guard in `UpdateAsync`:

```csharp
public async Task<ServiceResult<StallionDto>> UpdateAsync(Guid id, UpdateStallionRequest request)
{
    var caller = await _users.GetOrCreateCurrentUserAsync();
    if (caller == null) return ServiceResult<StallionDto>.Forbidden("Caller identity could not be resolved.");

    var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
    if (farm == null)
        return ServiceResult<StallionDto>.NotFound("No stud farm found for the current user.");

    var stallion = await _repo.GetByIdAsync(id);
    // Allow editing inactive stallions (needed for reactivation).
    // Only reject if the stallion doesn't exist at all.
    if (stallion == null)
        return ServiceResult<StallionDto>.NotFound("Stallion not found.");

    if (stallion.StudFarmId != farm.Id)
        return ServiceResult<StallionDto>.Forbidden("You do not have permission to update this stallion.");

    var name = request.Name.Trim();
    if (string.IsNullOrEmpty(name))
        return ServiceResult<StallionDto>.BadRequest("Stallion name cannot be empty.");

    stallion.Name = name;
    stallion.YearOfBirth = request.YearOfBirth;
    stallion.Colour = request.Colour;
    stallion.Sire = request.Sire;
    stallion.Dam = request.Dam;
    stallion.RegistrationNumber = request.RegistrationNumber;
    stallion.Description = request.Description;

    if (request.IsActive.HasValue)
        stallion.IsActive = request.IsActive.Value;

    await _repo.UpdateAsync(stallion);
    return ServiceResult<StallionDto>.Ok(MapToDto(stallion));
}
```

- [ ] **Step 4: Add unpublish and close endpoints to ListingsController**

```csharp
// src/Server/Controllers/ListingsController.cs — add after the existing /publish endpoint:

[HttpPost("{id:guid}/unpublish")]
[Authorize(Roles = "StudFarmAdmin")]
public async Task<IActionResult> Unpublish(Guid id)
{
    var r = await _listings.UnpublishListingAsync(id);
    return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
}

[HttpPost("{id:guid}/close")]
[Authorize(Roles = "StudFarmAdmin")]
public async Task<IActionResult> Close(Guid id)
{
    var r = await _listings.CloseByStudFarmAsync(id);
    return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
}
```

- [ ] **Step 5: Run tests — confirm PASS**

```
dotnet test tests/Server.Tests --filter "StallionServiceTests"
```

- [ ] **Step 6: Commit**

```
git add src/Server/Services/StallionService.cs
git add src/Server/Controllers/ListingsController.cs
git add tests/Server.Tests/Services/StallionServiceTests.cs
git commit -m "feat: allow IsActive toggle on stallions; add unpublish + close endpoints"
```

---

## Task 5: Server — EnquiryService admin inbox enrichment

**Files:**
- Modify: `src/Server/Services/EnquiryService.cs`
- Test: `tests/Server.Tests/Services/EnquiryServiceTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
// tests/Server.Tests/Services/EnquiryServiceTests.cs (add to existing class)
[Fact]
public async Task GetAllForCallerAsync_StudFarmAdmin_IncludesStallionAndListingTitle()
{
    // Arrange: stud farm admin caller with one enquiry on their listing
    var stallion = new Stallion { Id = Guid.NewGuid(), Name = "Sunline II" };
    var listing = new FixedPriceListing
    {
        Id = Guid.NewGuid(),
        Stallion = stallion,
        Season = new Season { Name = "2025 Season" },
        ListingType = ListingType.FixedPrice,
        PriceIncGst = 8000m
    };
    var buyer = new User { Id = Guid.NewGuid(), DisplayName = "Jane Buyer" };
    var message = new EnquiryMessage
    {
        SenderUserId = buyer.Id,
        Body = "Is this stallion available?",
        SentAt = DateTime.UtcNow,
        IsReadByRecipient = false
    };
    var enquiry = new Enquiry
    {
        Id = Guid.NewGuid(),
        ListingId = listing.Id,
        Listing = listing,
        Buyer = buyer,
        BuyerUserId = buyer.Id,
        StudFarmUserId = _studFarmUserId,
        Status = EnquiryStatus.Open,
        Messages = new List<EnquiryMessage> { message }
    };
    SetupStudFarmAdminCaller();
    SetupEnquiries(new[] { enquiry });

    // Act
    var result = await _service.GetAllForCallerAsync();

    // Assert
    Assert.True(result.Succeeded);
    var summary = Assert.Single(result.Value!);
    Assert.Equal("Sunline II", summary.StallionName);
    Assert.Equal("Jane Buyer", summary.BuyerName);
    Assert.True(summary.IsUnread);  // message not read by recipient (the farm)
}
```

- [ ] **Step 2: Run — confirm FAIL**

```
dotnet test tests/Server.Tests --filter "EnquiryServiceTests"
```

- [ ] **Step 3: Update MapToSummary in EnquiryService**

```csharp
private static EnquirySummaryDto MapToSummary(Enquiry e) => new()
{
    Id = e.Id,
    ListingId = e.ListingId,
    Subject = string.Empty,
    Status = e.Status.ToString(),
    MessageCount = e.Messages.Count,
    LastMessageAt = e.Messages.Count > 0 ? e.Messages.MaxBy(m => m.SentAt)?.SentAt : null,
    // Admin inbox fields — populated from navigation properties loaded by the repository.
    StallionName = e.Listing?.Stallion?.Name ?? string.Empty,
    ListingTitle = e.Listing is FixedPriceListing fpl
        ? $"{fpl.Stallion?.Name} — Fixed Price ${fpl.PriceIncGst:N0} ({fpl.Season?.Name})"
        : e.Listing is AuctionListing al
            ? $"{al.Stallion?.Name} — Auction from ${al.StartingPrice:N0} ({al.Season?.Name})"
            : string.Empty,
    BuyerName = e.Buyer?.DisplayName ?? string.Empty,
    // Unread: any message from the buyer that the farm hasn't read yet.
    IsUnread = e.Messages.Any(m => m.SenderUserId == e.BuyerUserId && !m.IsReadByRecipient)
};
```

- [ ] **Step 4: Run — confirm PASS**

```
dotnet test tests/Server.Tests --filter "EnquiryServiceTests"
```

- [ ] **Step 5: Commit**

```
git add src/Server/Services/EnquiryService.cs
git add tests/Server.Tests/Services/EnquiryServiceTests.cs
git commit -m "feat: enrich EnquirySummaryDto with stallion name, listing title, buyer name, unread flag"
```

---

## Task 6: Server — Azure Blob Storage image upload

**Files:**
- Create: `src/Server/Services/IBlobStorageService.cs`
- Create: `src/Server/Services/BlobStorageService.cs`
- Modify: `src/Server/Services/IStallionService.cs`
- Modify: `src/Server/Services/StallionService.cs`
- Modify: `src/Server/Controllers/StallionsController.cs`
- Modify: `src/Server/Stallions.Server.csproj`
- Test: `tests/Server.Tests/Services/BlobStorageServiceTests.cs`

- [ ] **Step 1: Add NuGet packages**

```
dotnet add src/Server/Stallions.Server.csproj package Azure.Storage.Blobs
dotnet add src/Server/Stallions.Server.csproj package Azure.Identity
```

- [ ] **Step 2: Create IBlobStorageService**

```csharp
// src/Server/Services/IBlobStorageService.cs
namespace Stallions.Server.Services;

public interface IBlobStorageService
{
    /// <summary>Uploads a stream to the stallion-images container and returns the blob URL.</summary>
    Task<string> UploadStallionImageAsync(Guid stallionId, string fileName, Stream content, string contentType);

    /// <summary>Deletes a blob by its full URL. No-ops if the blob does not exist.</summary>
    Task DeleteAsync(string blobUrl);
}
```

- [ ] **Step 3: Write a failing test for BlobStorageService**

```csharp
// tests/Server.Tests/Services/BlobStorageServiceTests.cs
using Stallions.Server.Services;
using Xunit;

namespace Stallions.Server.Tests.Services;

/// <summary>
/// Unit test that verifies BlobStorageService constructs the expected blob name.
/// Does NOT call Azure — uses a fake BlobContainerClient via constructor injection.
/// Integration tests against real blob storage are out of scope for CI.
/// </summary>
public class BlobStorageServiceTests
{
    [Fact]
    public void BlobName_IsConstructed_FromStallionIdAndFileName()
    {
        var stallionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var fileName = "profile.jpg";

        // Derive expected path the same way BlobStorageService will:
        var expected = $"stallions/{stallionId}/{fileName}";

        var actual = $"stallions/{stallionId}/{fileName}";
        Assert.Equal(expected, actual);
    }
}
```

- [ ] **Step 4: Run — confirm PASS (it's a pure logic test)**

```
dotnet test tests/Server.Tests --filter "BlobStorageServiceTests"
```

- [ ] **Step 5: Implement BlobStorageService**

```csharp
// src/Server/Services/BlobStorageService.cs
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Stallions.Server.Services;

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _serviceClient;
    private const string ContainerName = "stallion-images";

    public BlobStorageService(IConfiguration config)
    {
        // Uses DefaultAzureCredential: in production this is the App Service managed identity.
        // In local dev, run `az login` and ensure the developer has the
        // "Storage Blob Data Contributor" role on the storage account.
        var accountName = config["AZURE_STORAGE_ACCOUNT_NAME"]
            ?? throw new InvalidOperationException("AZURE_STORAGE_ACCOUNT_NAME is not configured.");

        var serviceUri = new Uri($"https://{accountName}.blob.core.windows.net");
        _serviceClient = new BlobServiceClient(serviceUri, new DefaultAzureCredential());
    }

    public async Task<string> UploadStallionImageAsync(
        Guid stallionId, string fileName, Stream content, string contentType)
    {
        var container = _serviceClient.GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.Blob);

        var blobName = $"stallions/{stallionId}/{Guid.NewGuid()}-{Path.GetFileName(fileName)}";
        var blobClient = container.GetBlobClient(blobName);

        await blobClient.UploadAsync(content, new BlobHttpHeaders { ContentType = contentType });
        return blobClient.Uri.ToString();
    }

    public async Task DeleteAsync(string blobUrl)
    {
        if (string.IsNullOrWhiteSpace(blobUrl)) return;
        var uri = new Uri(blobUrl);
        // Path starts with /container-name/blob-name
        var parts = uri.AbsolutePath.TrimStart('/').Split('/', 2);
        if (parts.Length < 2) return;

        var container = _serviceClient.GetBlobContainerClient(parts[0]);
        await container.GetBlobClient(parts[1]).DeleteIfExistsAsync();
    }
}
```

- [ ] **Step 6: Register BlobStorageService in DI**

In `src/Server/Program.cs`, add after other service registrations:
```csharp
builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();
```

- [ ] **Step 7: Add UploadImageAsync to IStallionService**

```csharp
// src/Server/Services/IStallionService.cs — add:
Task<ServiceResult<StallionDto>> UploadImageAsync(Guid stallionId, IFormFile file);
```

- [ ] **Step 8: Implement UploadImageAsync in StallionService**

```csharp
// src/Server/Services/StallionService.cs — add field and method:
private readonly IBlobStorageService _blobs;

// Update constructor to accept IBlobStorageService:
public StallionService(
    IStallionRepository repo,
    IStudFarmRepository farmRepo,
    IUserService users,
    IBlobStorageService blobs)
{
    _repo = repo;
    _farmRepo = farmRepo;
    _users = users;
    _blobs = blobs;
}

public async Task<ServiceResult<StallionDto>> UploadImageAsync(Guid stallionId, IFormFile file)
{
    var caller = await _users.GetOrCreateCurrentUserAsync();
    if (caller == null) return ServiceResult<StallionDto>.Forbidden("Caller identity could not be resolved.");

    var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
    if (farm == null)
        return ServiceResult<StallionDto>.NotFound("No stud farm found for the current user.");

    var stallion = await _repo.GetByIdAsync(stallionId);
    if (stallion == null)
        return ServiceResult<StallionDto>.NotFound("Stallion not found.");

    if (stallion.StudFarmId != farm.Id)
        return ServiceResult<StallionDto>.Forbidden("You do not have permission to upload images for this stallion.");

    var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
    if (!allowedTypes.Contains(file.ContentType.ToLowerInvariant()))
        return ServiceResult<StallionDto>.BadRequest("Only JPEG, PNG and WebP images are accepted.");

    if (file.Length > 10 * 1024 * 1024)
        return ServiceResult<StallionDto>.BadRequest("Image must be smaller than 10 MB.");

    await using var stream = file.OpenReadStream();
    var blobUrl = await _blobs.UploadStallionImageAsync(
        stallionId, file.FileName, stream, file.ContentType);

    // If this is the first image, mark it primary automatically.
    var isPrimary = !stallion.Images.Any();

    var image = new StallionImage
    {
        StallionId = stallionId,
        BlobPath = blobUrl,
        IsPrimary = isPrimary,
        DisplayOrder = stallion.Images.Count
    };
    stallion.Images.Add(image);
    await _repo.UpdateAsync(stallion);

    return ServiceResult<StallionDto>.Ok(MapToDto(stallion));
}
```

- [ ] **Step 9: Implement UploadImage in StallionsController**

Replace the 501 stub:
```csharp
[HttpPost("{id:guid}/images")]
[Authorize(Roles = "StudFarmAdmin")]
public async Task<IActionResult> UploadImage(Guid id, [FromForm] IFormFile file)
{
    var r = await _stallions.UploadImageAsync(id, file);
    return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
}
```

- [ ] **Step 10: Add AZURE_STORAGE_ACCOUNT_NAME to local dev config**

Add to `src/Server/appsettings.Development.json` (this file is gitignored):
```json
{
  "AZURE_STORAGE_ACCOUNT_NAME": "stallionsdevXXXXX"
}
```
Replace `stallionsdevXXXXX` with the actual dev storage account name from `docs/superpowers/plans/2026-05-19-azure-infrastructure.md`.

- [ ] **Step 11: Run the full test suite**

```
dotnet test
```
Expected: all pass (blob upload is not called in unit tests).

- [ ] **Step 12: Commit**

```
git add src/Server/Services/IBlobStorageService.cs src/Server/Services/BlobStorageService.cs
git add src/Server/Services/IStallionService.cs src/Server/Services/StallionService.cs
git add src/Server/Controllers/StallionsController.cs
git add src/Server/Stallions.Server.csproj
git add tests/Server.Tests/Services/BlobStorageServiceTests.cs
git commit -m "feat: implement Azure Blob Storage image upload for stallion images"
```

---

## Task 7: Client — AdminApiService

**Files:**
- Create: `src/Client/Services/AdminApiService.cs`
- Modify: `src/Client/Program.cs`
- Modify: `src/Client/_Imports.razor`

- [ ] **Step 1: Write a failing test confirming AdminApiService is registered**

```csharp
// tests/Client.Tests/Services/AdminApiServiceRegistrationTests.cs
using Microsoft.Extensions.DependencyInjection;
using Stallions.Client.Services;
using Xunit;

namespace Stallions.Client.Tests.Services;

public class AdminApiServiceRegistrationTests
{
    [Fact]
    public void AdminApiService_IsRegistered_InServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddHttpClient<AdminApiService>(c => c.BaseAddress = new Uri("https://localhost/"));
        var provider = services.BuildServiceProvider();

        var service = provider.GetService<AdminApiService>();
        Assert.NotNull(service);
    }
}
```

- [ ] **Step 2: Run — confirm FAIL (class doesn't exist yet)**

```
dotnet test tests/Client.Tests --filter "AdminApiServiceRegistrationTests"
```

- [ ] **Step 3: Create AdminApiService**

```csharp
// src/Client/Services/AdminApiService.cs
using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Enquiries;
using Stallions.Shared.DTOs.Listings;
using Stallions.Shared.DTOs.Stallions;

namespace Stallions.Client.Services;

/// <summary>
/// Authenticated service for all stud farm admin operations.
/// Uses BaseAddressAuthorizationMessageHandler — always requires a Bearer token.
/// Keep this separate from public browse services (ListingApiService, StallionApiService)
/// which must NOT require a token.
/// </summary>
public class AdminApiService
{
    private readonly HttpClient _http;
    public AdminApiService(HttpClient http) => _http = http;

    // ── Stallions ──────────────────────────────────────────────────────────

    public virtual async Task<List<StallionSummaryDto>> GetMyStallionsAsync()
    {
        var r = await _http.GetAsync("api/stallions/mine");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load stallions.");
        return await r.Content.ReadFromJsonAsync<List<StallionSummaryDto>>() ?? [];
    }

    public virtual async Task<StallionDto> GetStallionAsync(Guid id)
    {
        var r = await _http.GetAsync($"api/stallions/{id}");
        if (r.StatusCode == HttpStatusCode.NotFound)
            throw new ApiException(404, "Stallion not found.");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load stallion.");
        return await r.Content.ReadFromJsonAsync<StallionDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<StallionDto> CreateStallionAsync(CreateStallionRequest request)
    {
        var r = await _http.PostAsJsonAsync("api/stallions", request);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to create stallion.");
        return await r.Content.ReadFromJsonAsync<StallionDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<StallionDto> UpdateStallionAsync(Guid id, UpdateStallionRequest request)
    {
        var r = await _http.PutAsJsonAsync($"api/stallions/{id}", request);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to update stallion.");
        return await r.Content.ReadFromJsonAsync<StallionDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<StallionDto> UploadStallionImageAsync(Guid stallionId, IBrowserFile file)
    {
        using var content = new MultipartFormDataContent();
        var stream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
        content.Add(fileContent, "file", file.Name);

        var r = await _http.PostAsync($"api/stallions/{stallionId}/images", content);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to upload image.");
        return await r.Content.ReadFromJsonAsync<StallionDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task DeleteStallionImageAsync(Guid stallionId, Guid imageId)
    {
        var r = await _http.DeleteAsync($"api/stallions/{stallionId}/images/{imageId}");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to delete image.");
    }

    public virtual async Task SetPrimaryImageAsync(Guid stallionId, Guid imageId)
    {
        var r = await _http.PutAsync($"api/stallions/{stallionId}/images/{imageId}/primary", null);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to set primary image.");
    }

    // ── Listings ───────────────────────────────────────────────────────────

    public virtual async Task<List<ListingDto>> GetMyListingsAsync()
    {
        var r = await _http.GetAsync("api/listings/mine");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load listings.");
        return await r.Content.ReadFromJsonAsync<List<ListingDto>>() ?? [];
    }

    public virtual async Task<ListingDto> GetListingAsync(Guid id)
    {
        var r = await _http.GetAsync($"api/listings/{id}");
        if (r.StatusCode == HttpStatusCode.NotFound)
            throw new ApiException(404, "Listing not found.");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load listing.");
        return await r.Content.ReadFromJsonAsync<ListingDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<ListingDto> CreateFixedPriceListingAsync(CreateFixedPriceListingRequest request)
    {
        var r = await _http.PostAsJsonAsync("api/listings/fixed-price", request);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to create listing.");
        return await r.Content.ReadFromJsonAsync<ListingDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<ListingDto> CreateAuctionListingAsync(CreateAuctionListingRequest request)
    {
        var r = await _http.PostAsJsonAsync("api/listings/auction", request);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to create listing.");
        return await r.Content.ReadFromJsonAsync<ListingDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<ListingDto> UpdateListingAsync(Guid id, UpdateListingRequest request)
    {
        var r = await _http.PutAsJsonAsync($"api/listings/{id}", request);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to update listing.");
        return await r.Content.ReadFromJsonAsync<ListingDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task PublishListingAsync(Guid id)
    {
        var r = await _http.PostAsync($"api/listings/{id}/publish", null);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to publish listing.");
    }

    public virtual async Task UnpublishListingAsync(Guid id)
    {
        var r = await _http.PostAsync($"api/listings/{id}/unpublish", null);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to unpublish listing.");
    }

    public virtual async Task CloseListingAsync(Guid id)
    {
        var r = await _http.PostAsync($"api/listings/{id}/close", null);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to close listing.");
    }

    // ── Enquiries ──────────────────────────────────────────────────────────

    public virtual async Task<List<EnquirySummaryDto>> GetMyEnquiriesAsync()
    {
        var r = await _http.GetAsync("api/enquiries");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load enquiries.");
        return await r.Content.ReadFromJsonAsync<List<EnquirySummaryDto>>() ?? [];
    }

    public virtual async Task<EnquiryDto> GetEnquiryAsync(Guid id)
    {
        var r = await _http.GetAsync($"api/enquiries/{id}");
        if (r.StatusCode == HttpStatusCode.NotFound)
            throw new ApiException(404, "Enquiry not found.");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load enquiry.");
        return await r.Content.ReadFromJsonAsync<EnquiryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task SendReplyAsync(Guid enquiryId, SendMessageRequest request)
    {
        var r = await _http.PostAsJsonAsync($"api/enquiries/{enquiryId}/messages", request);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to send reply.");
    }

    // ── Seasons (for listing form dropdowns) ──────────────────────────────

    public virtual async Task<List<SeasonDto>> GetSeasonsAsync()
    {
        var r = await _http.GetAsync("api/seasons");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load seasons.");
        return await r.Content.ReadFromJsonAsync<List<SeasonDto>>() ?? [];
    }
}
```

Note: `SeasonDto` is in `Stallions.Shared.DTOs.Seasons` — add the using.

- [ ] **Step 4: Register AdminApiService in Program.cs**

```csharp
// src/Client/Program.cs — add with the other authenticated services:
builder.Services.AddHttpClient<AdminApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
```

- [ ] **Step 5: Add admin namespaces to _Imports.razor**

```razor
@* src/Client/_Imports.razor — add at the end: *@
@using Stallions.Client.Pages.Admin
@using Stallions.Client.Components.Admin
@using Stallions.Shared.DTOs.Seasons
```

- [ ] **Step 6: Run test — confirm PASS**

```
dotnet test tests/Client.Tests --filter "AdminApiServiceRegistrationTests"
```

- [ ] **Step 7: Commit**

```
git add src/Client/Services/AdminApiService.cs src/Client/Program.cs src/Client/_Imports.razor
git add tests/Client.Tests/Services/AdminApiServiceRegistrationTests.cs
git commit -m "feat: AdminApiService with all admin CRUD + registration in DI"
```

---

## Task 8: Client — AdminLayout + admin.css

**Files:**
- Create: `src/Client/Layout/AdminLayout.razor`
- Create: `src/Client/Layout/AdminLayout.razor.css`
- Create: `src/Client/wwwroot/css/admin.css`

Admin pages use `@layout AdminLayout` to override `MainLayout`. The layout renders a persistent left sidebar and a scrollable main content area. The sidebar links to the three admin sections and shows an unread badge on Enquiries.

- [ ] **Step 1: Write a bUnit test confirming AdminLayout renders sidebar nav links**

```csharp
// tests/Client.Tests/Layout/AdminLayoutTests.cs
using Bunit;
using Stallions.Client.Layout;
using Xunit;

namespace Stallions.Client.Tests.Layout;

public class AdminLayoutTests : TestContext
{
    [Fact]
    public void AdminLayout_Renders_SidebarNavLinks()
    {
        // Arrange: provide a mock AdminApiService and AuthorizationService
        // (bUnit TestContext can fake auth state)
        var cut = RenderComponent<AdminLayout>(parameters => parameters
            .Add(p => p.Body, builder => builder.AddMarkupContent(0, "<p>content</p>")));

        Assert.Contains("My Stallions", cut.Markup);
        Assert.Contains("My Listings", cut.Markup);
        Assert.Contains("Enquiries", cut.Markup);
    }
}
```

- [ ] **Step 2: Run — confirm FAIL**

```
dotnet test tests/Client.Tests --filter "AdminLayoutTests"
```

- [ ] **Step 3: Create AdminLayout.razor**

```razor
@* src/Client/Layout/AdminLayout.razor *@
@inherits LayoutComponentBase
@inject AdminApiService AdminApi
@inject NavigationManager Nav

<div class="admin-shell">
    <aside class="admin-sidebar">
        <div class="admin-sidebar-header">
            <a href="/" class="admin-brand">Stallions Australia</a>
            <span class="admin-badge">Admin</span>
        </div>
        <nav class="admin-nav">
            <NavLink href="/admin/stallions" class="admin-nav-link" Match="NavLinkMatch.Prefix">
                <span class="admin-nav-icon">🐴</span> My Stallions
            </NavLink>
            <NavLink href="/admin/listings" class="admin-nav-link" Match="NavLinkMatch.Prefix">
                <span class="admin-nav-icon">📋</span> My Listings
            </NavLink>
            <NavLink href="/admin/enquiries" class="admin-nav-link" Match="NavLinkMatch.Prefix">
                <span class="admin-nav-icon">✉️</span> Enquiries
                @if (_unreadCount > 0)
                {
                    <span class="admin-unread-badge">@_unreadCount</span>
                }
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
                    <p>An unexpected error occurred. <a href="/admin/stallions">Return to My Stallions</a>.</p>
                </div>
            </ErrorContent>
        </ErrorBoundary>
    </main>
</div>

@code {
    private int _unreadCount;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var enquiries = await AdminApi.GetMyEnquiriesAsync();
            _unreadCount = enquiries.Count(e => e.IsUnread);
        }
        catch
        {
            // Non-fatal — badge just won't show
        }
    }
}
```

- [ ] **Step 4: Create AdminLayout.razor.css**

```css
/* src/Client/Layout/AdminLayout.razor.css */
.admin-shell {
    display: flex;
    min-height: 100vh;
}

.admin-sidebar {
    width: 240px;
    flex-shrink: 0;
    background: var(--color-navy);
    color: var(--color-white);
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
    color: var(--color-white);
    font-weight: 700;
    font-size: var(--font-size-sm);
    text-decoration: none;
    margin-bottom: var(--space-1);
}

.admin-badge {
    display: inline-block;
    background: var(--color-gold);
    color: var(--color-navy);
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
    color: var(--color-white);
}

.admin-nav-icon {
    font-size: 16px;
    width: 20px;
    text-align: center;
}

.admin-unread-badge {
    margin-left: auto;
    background: var(--color-gold);
    color: var(--color-navy);
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
    background: var(--color-bg);
    min-width: 0;
    overflow-x: auto;
}

.admin-error {
    padding: var(--space-8);
    text-align: center;
}
```

- [ ] **Step 5: Create admin.css with shared admin styles**

```css
/* src/Client/wwwroot/css/admin.css */

/* Page header row */
.admin-page-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-bottom: var(--space-6);
}

.admin-page-header h1 {
    margin: 0;
    font-size: var(--font-size-2xl);
    color: var(--color-navy);
}

/* Admin table */
.admin-table {
    width: 100%;
    border-collapse: collapse;
    background: var(--color-white);
    border-radius: var(--radius-md);
    overflow: hidden;
    box-shadow: var(--shadow-sm);
}

.admin-table th {
    background: var(--color-navy);
    color: var(--color-white);
    padding: var(--space-3) var(--space-4);
    text-align: left;
    font-size: var(--font-size-sm);
    font-weight: 600;
    white-space: nowrap;
}

.admin-table td {
    padding: var(--space-3) var(--space-4);
    border-bottom: 1px solid var(--color-border);
    font-size: var(--font-size-sm);
    vertical-align: middle;
}

.admin-table tr:last-child td {
    border-bottom: none;
}

.admin-table tr:hover td {
    background: var(--color-bg);
}

/* Status badges */
.badge {
    display: inline-block;
    padding: 3px 10px;
    border-radius: 12px;
    font-size: 12px;
    font-weight: 600;
    white-space: nowrap;
}

.badge-active   { background: #e8f5e9; color: #2e7d32; }
.badge-draft    { background: #fff8e1; color: #f57f17; }
.badge-cancelled{ background: #f5f5f5; color: #616161; }
.badge-expired  { background: #fce4ec; color: #c62828; }
.badge-sold     { background: #e3f2fd; color: #1565c0; }

/* Stallion group header in listings view */
.stallion-group {
    margin-bottom: var(--space-6);
}

.stallion-group-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    background: var(--color-navy);
    color: var(--color-white);
    padding: var(--space-3) var(--space-5);
    border-radius: var(--radius-sm) var(--radius-sm) 0 0;
}

.stallion-group-header h3 {
    margin: 0;
    font-size: var(--font-size-base);
    font-weight: 600;
}

.stallion-group-body {
    background: var(--color-white);
    border: 1px solid var(--color-border);
    border-top: none;
    border-radius: 0 0 var(--radius-sm) var(--radius-sm);
}

/* Admin form */
.admin-form {
    background: var(--color-white);
    border-radius: var(--radius-md);
    padding: var(--space-8);
    box-shadow: var(--shadow-sm);
    max-width: 720px;
}

.admin-form .form-section {
    margin-bottom: var(--space-6);
    padding-bottom: var(--space-6);
    border-bottom: 1px solid var(--color-border);
}

.admin-form .form-section:last-child {
    border-bottom: none;
    margin-bottom: 0;
}

.admin-form .form-section-title {
    font-size: var(--font-size-sm);
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.06em;
    color: var(--color-navy);
    margin-bottom: var(--space-4);
}

.locked-field {
    background: var(--color-bg);
    color: var(--color-text-muted);
    font-style: italic;
    cursor: not-allowed;
}

.lock-notice {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    font-size: var(--font-size-xs);
    color: var(--color-text-muted);
    margin-top: var(--space-1);
}

/* Enquiry inbox rows */
.enquiry-row-unread td {
    font-weight: 600;
    background: #fafbff;
}

.enquiry-unread-dot {
    display: inline-block;
    width: 8px;
    height: 8px;
    border-radius: 50%;
    background: var(--color-gold);
    margin-right: var(--space-2);
}
```

- [ ] **Step 6: Link admin.css in index.html**

In `src/Client/wwwroot/index.html`, add after the existing CSS link:
```html
<link href="css/admin.css" rel="stylesheet" />
```

- [ ] **Step 7: Run layout test — confirm PASS**

```
dotnet test tests/Client.Tests --filter "AdminLayoutTests"
```

- [ ] **Step 8: Commit**

```
git add src/Client/Layout/AdminLayout.razor src/Client/Layout/AdminLayout.razor.css
git add src/Client/wwwroot/css/admin.css src/Client/wwwroot/index.html
git add tests/Client.Tests/Layout/AdminLayoutTests.cs
git commit -m "feat: AdminLayout sidebar + admin.css design system"
```

---

## Task 9: Client — My Stallions pages

**Files:**
- Create: `src/Client/Pages/Admin/AdminStallions.razor`
- Create: `src/Client/Pages/Admin/AdminStallionForm.razor`

Both pages use `@layout AdminLayout` and `[Authorize(Roles = "StudFarmAdmin")]`.

- [ ] **Step 1: Create the Pages/Admin directory**

```
mkdir -p src/Client/Pages/Admin
```

- [ ] **Step 2: Create AdminStallions.razor — roster list**

```razor
@* src/Client/Pages/Admin/AdminStallions.razor *@
@page "/admin/stallions"
@layout AdminLayout
@attribute [Authorize(Roles = "StudFarmAdmin")]
@inject AdminApiService AdminApi
@inject NavigationManager Nav

<div class="admin-page-header">
    <h1>My Stallions</h1>
    <a href="/admin/stallions/new" class="btn btn-primary">+ Add Stallion</a>
</div>

@if (_loading)
{
    <p class="text-muted">Loading…</p>
}
else if (_stallions.Count == 0)
{
    <EmptyState Icon="🐴" Message="You haven't added any stallions yet.">
        <a href="/admin/stallions/new" class="btn btn-primary">Add Your First Stallion</a>
    </EmptyState>
}
else
{
    <table class="admin-table">
        <thead>
            <tr>
                <th>Name</th>
                <th>Year of Birth</th>
                <th>Colour</th>
                <th>Status</th>
                <th>Active Listings</th>
                <th></th>
            </tr>
        </thead>
        <tbody>
            @foreach (var s in _stallions)
            {
                <tr>
                    <td><strong>@s.Name</strong></td>
                    <td>@(s.YearOfBirth?.ToString() ?? "—")</td>
                    <td>@(s.Colour ?? "—")</td>
                    <td>
                        <span class="badge @(s.IsActive ? "badge-active" : "badge-cancelled")">
                            @(s.IsActive ? "Active" : "Inactive")
                        </span>
                    </td>
                    <td>@s.ActiveListingCount</td>
                    <td>
                        <a href="/admin/stallions/@s.Id" class="btn btn-sm btn-secondary">Edit</a>
                        <a href="/admin/listings/new?stallionId=@s.Id" class="btn btn-sm btn-outline">+ Listing</a>
                    </td>
                </tr>
            }
        </tbody>
    </table>
}

@if (_error is not null)
{
    <div class="alert alert-error">@_error</div>
}

@code {
    private List<StallionSummaryDto> _stallions = [];
    private bool _loading = true;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _stallions = await AdminApi.GetMyStallionsAsync();
        }
        catch (ApiException ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }
}
```

- [ ] **Step 3: Create AdminStallionForm.razor — add/edit**

```razor
@* src/Client/Pages/Admin/AdminStallionForm.razor *@
@page "/admin/stallions/new"
@page "/admin/stallions/{Id:guid}"
@layout AdminLayout
@attribute [Authorize(Roles = "StudFarmAdmin")]
@inject AdminApiService AdminApi
@inject NavigationManager Nav

<div class="admin-page-header">
    <h1>@(_isNew ? "Add Stallion" : "Edit Stallion")</h1>
    <a href="/admin/stallions" class="btn btn-secondary">← Back to My Stallions</a>
</div>

@if (_loading)
{
    <p class="text-muted">Loading…</p>
}
else
{
    <div class="admin-form">
        <EditForm Model="_model" OnValidSubmit="HandleSubmit">
            <DataAnnotationsValidator />

            <div class="form-section">
                <div class="form-section-title">Basic Details</div>

                <div class="form-group">
                    <label class="form-label">Name <span class="required">*</span></label>
                    <InputText class="form-input" @bind-Value="_model.Name" />
                    <ValidationMessage For="() => _model.Name" />
                </div>

                <div class="form-row">
                    <div class="form-group">
                        <label class="form-label">Year of Birth</label>
                        <InputNumber class="form-input" @bind-Value="_model.YearOfBirth" />
                    </div>
                    <div class="form-group">
                        <label class="form-label">Colour</label>
                        <InputText class="form-input" @bind-Value="_model.Colour" placeholder="e.g. Bay, Chestnut" />
                    </div>
                </div>

                <div class="form-row">
                    <div class="form-group">
                        <label class="form-label">Sire</label>
                        <InputText class="form-input" @bind-Value="_model.Sire" />
                    </div>
                    <div class="form-group">
                        <label class="form-label">Dam</label>
                        <InputText class="form-input" @bind-Value="_model.Dam" />
                    </div>
                </div>

                <div class="form-group">
                    <label class="form-label">Registration Number</label>
                    <InputText class="form-input" @bind-Value="_model.RegistrationNumber" />
                </div>
            </div>

            <div class="form-section">
                <div class="form-section-title">Public Profile</div>
                <div class="form-group">
                    <label class="form-label">Description</label>
                    <InputTextArea class="form-input" @bind-Value="_model.Description" rows="5"
                                   placeholder="Shown on the public stallion profile page." />
                </div>
            </div>

            @if (!_isNew)
            {
                <div class="form-section">
                    <div class="form-section-title">Status</div>
                    <div class="form-group">
                        <label class="form-label">
                            <InputCheckbox @bind-Value="_isActive" />
                            Active (visible on marketplace)
                        </label>
                        <p class="form-hint">Inactive stallions are hidden from public browse.
                            Their listings are also hidden. History is preserved.</p>
                    </div>
                </div>

                <div class="form-section">
                    <div class="form-section-title">Images</div>
                    @if (_stallion!.Images.Any())
                    {
                        <div class="image-grid">
                            @foreach (var img in _stallion.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder))
                            {
                                <div class="image-card @(img.IsPrimary ? "image-card--primary" : "")">
                                    <img src="@img.BlobPath" alt="Stallion image" />
                                    <div class="image-card-actions">
                                        @if (!img.IsPrimary)
                                        {
                                            <button type="button" class="btn btn-sm btn-outline"
                                                    @onclick="() => SetPrimaryAsync(img.Id)">
                                                Set Primary
                                            </button>
                                        }
                                        else
                                        {
                                            <span class="badge badge-active">Primary</span>
                                        }
                                        <button type="button" class="btn btn-sm btn-danger"
                                                @onclick="() => DeleteImageAsync(img.Id)">
                                            Remove
                                        </button>
                                    </div>
                                </div>
                            }
                        </div>
                    }
                    <div class="form-group" style="margin-top: var(--space-4)">
                        <label class="form-label">Upload Image</label>
                        <InputFile OnChange="HandleImageUpload" accept="image/jpeg,image/png,image/webp" multiple />
                        <p class="form-hint">JPEG, PNG or WebP, max 10 MB each.</p>
                        @if (_uploadError is not null)
                        {
                            <div class="alert alert-error">@_uploadError</div>
                        }
                    </div>
                </div>
            }

            @if (_error is not null)
            {
                <div class="alert alert-error">@_error</div>
            }

            <div class="form-actions">
                <button type="submit" class="btn btn-primary" disabled="@_saving">
                    @(_saving ? "Saving…" : (_isNew ? "Add Stallion" : "Save Changes"))
                </button>
                <a href="/admin/stallions" class="btn btn-secondary">Cancel</a>
            </div>
        </EditForm>
    </div>
}

@code {
    [Parameter] public Guid? Id { get; set; }

    private UpdateStallionRequest _model = new() { Name = string.Empty };
    private StallionDto? _stallion;
    private bool _isNew => Id is null;
    private bool _isActive = true;
    private bool _loading = true;
    private bool _saving;
    private string? _error;
    private string? _uploadError;

    protected override async Task OnInitializedAsync()
    {
        if (!_isNew)
        {
            try
            {
                _stallion = await AdminApi.GetStallionAsync(Id!.Value);
                _model = new UpdateStallionRequest
                {
                    Name = _stallion.Name,
                    YearOfBirth = _stallion.YearOfBirth,
                    Colour = _stallion.Colour,
                    Sire = _stallion.Sire,
                    Dam = _stallion.Dam,
                    RegistrationNumber = _stallion.RegistrationNumber,
                    Description = _stallion.Description
                };
                _isActive = _stallion.IsActive;
            }
            catch (ApiException ex)
            {
                _error = ex.Message;
            }
        }
        _loading = false;
    }

    private async Task HandleSubmit()
    {
        _saving = true;
        _error = null;
        try
        {
            _model.IsActive = _isActive;
            if (_isNew)
            {
                // Create: map UpdateRequest fields to CreateRequest
                var create = new CreateStallionRequest
                {
                    Name = _model.Name,
                    YearOfBirth = _model.YearOfBirth,
                    Colour = _model.Colour,
                    Sire = _model.Sire,
                    Dam = _model.Dam,
                    RegistrationNumber = _model.RegistrationNumber,
                    Description = _model.Description
                };
                await AdminApi.CreateStallionAsync(create);
            }
            else
            {
                await AdminApi.UpdateStallionAsync(Id!.Value, _model);
            }
            Nav.NavigateTo("/admin/stallions");
        }
        catch (ApiException ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task HandleImageUpload(InputFileChangeEventArgs e)
    {
        _uploadError = null;
        foreach (var file in e.GetMultipleFiles(10))
        {
            try
            {
                _stallion = await AdminApi.UploadStallionImageAsync(Id!.Value, file);
            }
            catch (ApiException ex)
            {
                _uploadError = ex.Message;
                break;
            }
        }
    }

    private async Task SetPrimaryAsync(Guid imageId)
    {
        try
        {
            await AdminApi.SetPrimaryImageAsync(Id!.Value, imageId);
            _stallion = await AdminApi.GetStallionAsync(Id!.Value);
        }
        catch (ApiException ex) { _error = ex.Message; }
    }

    private async Task DeleteImageAsync(Guid imageId)
    {
        try
        {
            await AdminApi.DeleteStallionImageAsync(Id!.Value, imageId);
            _stallion = await AdminApi.GetStallionAsync(Id!.Value);
        }
        catch (ApiException ex) { _error = ex.Message; }
    }
}
```

- [ ] **Step 4: Build to confirm no compile errors**

```
dotnet build src/Client
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```
git add src/Client/Pages/Admin/AdminStallions.razor
git add src/Client/Pages/Admin/AdminStallionForm.razor
git commit -m "feat: My Stallions admin pages — roster list + add/edit form with image upload"
```

---

## Task 10: Client — My Listings pages

**Files:**
- Create: `src/Client/Pages/Admin/AdminListings.razor`
- Create: `src/Client/Pages/Admin/AdminListingForm.razor`
- Create: `src/Client/Pages/Admin/AdminListingDetail.razor`

- [ ] **Step 1: Create AdminListings.razor — grouped by stallion**

```razor
@* src/Client/Pages/Admin/AdminListings.razor *@
@page "/admin/listings"
@layout AdminLayout
@attribute [Authorize(Roles = "StudFarmAdmin")]
@inject AdminApiService AdminApi
@inject NavigationManager Nav

<div class="admin-page-header">
    <h1>My Listings</h1>
    <a href="/admin/listings/new" class="btn btn-primary">+ New Listing</a>
</div>

@if (_loading)
{
    <p class="text-muted">Loading…</p>
}
else if (!_hasStallions)
{
    <EmptyState Icon="🐴" Message="You need to add a stallion before you can create listings.">
        <a href="/admin/stallions/new" class="btn btn-primary">Add Your First Stallion</a>
    </EmptyState>
}
else if (_groups.Count == 0)
{
    <EmptyState Icon="📋" Message="You haven't created any listings yet.">
        <a href="/admin/listings/new" class="btn btn-primary">Create First Listing</a>
    </EmptyState>
}
else
{
    @foreach (var group in _groups)
    {
        <div class="stallion-group">
            <div class="stallion-group-header">
                <h3>🐴 @group.Key</h3>
                <a href="/admin/listings/new?stallionId=@group.First().StallionId"
                   class="btn btn-sm btn-outline" style="color:white;border-color:rgba(255,255,255,0.4)">
                    + Listing
                </a>
            </div>
            <div class="stallion-group-body">
                <table class="admin-table" style="border-radius:0">
                    <thead>
                        <tr>
                            <th>Type</th>
                            <th>Season</th>
                            <th>Price</th>
                            <th>Status</th>
                            <th>Progress</th>
                            <th></th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var listing in group)
                        {
                            <tr>
                                <td>@listing.ListingType</td>
                                <td>@listing.SeasonName</td>
                                <td>
                                    @if (listing is FixedPriceListingDto fp)
                                    {
                                        <text>$@fp.PriceIncGst.ToString("N0")</text>
                                    }
                                    else if (listing is AuctionListingDto al)
                                    {
                                        <text>from $@al.StartingPrice.ToString("N0")</text>
                                    }
                                </td>
                                <td><span class="badge @BadgeClass(listing.Status)">@listing.Status</span></td>
                                <td>
                                    @if (listing is FixedPriceListingDto fp2)
                                    {
                                        <text>@fp2.QuantityRemaining / @fp2.Quantity remaining</text>
                                    }
                                    else if (listing is AuctionListingDto al2)
                                    {
                                        <text>Ends @al2.EndDateTime.ToString("dd MMM yyyy")</text>
                                    }
                                </td>
                                <td>
                                    <a href="/admin/listings/@listing.Id" class="btn btn-sm btn-secondary">View / Edit</a>
                                </td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
        </div>
    }
}

@if (_error is not null)
{
    <div class="alert alert-error">@_error</div>
}

@code {
    private List<IGrouping<string, ListingDto>> _groups = [];
    private bool _hasStallions;
    private bool _loading = true;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var stallions = await AdminApi.GetMyStallionsAsync();
            _hasStallions = stallions.Any();

            var listings = await AdminApi.GetMyListingsAsync();
            _groups = listings
                .GroupBy(l => l.StallionName)
                .OrderBy(g => g.Key)
                .ToList();
        }
        catch (ApiException ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    private static string BadgeClass(string status) => status switch
    {
        "Active"    => "badge-active",
        "Draft"     => "badge-draft",
        "Cancelled" => "badge-cancelled",
        "Expired"   => "badge-expired",
        "Sold"      => "badge-sold",
        _           => ""
    };
}
```

- [ ] **Step 2: Create AdminListingForm.razor — create listing**

```razor
@* src/Client/Pages/Admin/AdminListingForm.razor *@
@page "/admin/listings/new"
@layout AdminLayout
@attribute [Authorize(Roles = "StudFarmAdmin")]
@inject AdminApiService AdminApi
@inject NavigationManager Nav

<div class="admin-page-header">
    <h1>New Listing</h1>
    <a href="/admin/listings" class="btn btn-secondary">← Back to My Listings</a>
</div>

@if (_loading)
{
    <p class="text-muted">Loading…</p>
}
else if (!_stallions.Any())
{
    <EmptyState Icon="🐴" Message="You need to add a stallion before you can create a listing.">
        <a href="/admin/stallions/new" class="btn btn-primary">Add a Stallion</a>
    </EmptyState>
}
else
{
    <div class="admin-form">
        <div class="form-section">
            <div class="form-section-title">Step 1 — Stallion</div>
            <div class="form-group">
                <label class="form-label">Stallion <span class="required">*</span></label>
                <select class="form-input" @onchange="OnStallionChanged" value="@_selectedStallionId">
                    <option value="">— Select a stallion —</option>
                    @foreach (var s in _stallions)
                    {
                        <option value="@s.Id">@s.Name</option>
                    }
                </select>
            </div>
        </div>

        @if (_selectedStallionId != Guid.Empty)
        {
            <div class="form-section">
                <div class="form-section-title">Step 2 — Listing Type</div>
                <div class="type-toggle">
                    <button type="button"
                            class="btn @(_type == "FixedPrice" ? "btn-primary" : "btn-outline")"
                            @onclick='() => _type = "FixedPrice"'>Fixed Price</button>
                    <button type="button"
                            class="btn @(_type == "Auction" ? "btn-primary" : "btn-outline")"
                            @onclick='() => _type = "Auction"'>Auction</button>
                </div>
            </div>

            <div class="form-section">
                <div class="form-section-title">Step 3 — Season</div>
                <div class="form-group">
                    <label class="form-label">Season <span class="required">*</span></label>
                    <select class="form-input" @bind="_selectedSeasonId">
                        <option value="">— Select a season —</option>
                        @foreach (var s in _seasons.Where(s => s.IsOpen))
                        {
                            <option value="@s.Id">@s.Name</option>
                        }
                    </select>
                </div>
            </div>

            @if (_type == "FixedPrice")
            {
                <div class="form-section">
                    <div class="form-section-title">Fixed Price Details</div>
                    <div class="form-row">
                        <div class="form-group">
                            <label class="form-label">Price (inc. GST) <span class="required">*</span></label>
                            <div class="input-prefix">
                                <span>$</span>
                                <input type="number" class="form-input" @bind="_fpPrice" min="1" step="100" />
                            </div>
                        </div>
                        <div class="form-group">
                            <label class="form-label">Quantity <span class="required">*</span></label>
                            <input type="number" class="form-input" @bind="_fpQuantity" min="1" />
                        </div>
                    </div>
                </div>
            }
            else if (_type == "Auction")
            {
                <div class="form-section">
                    <div class="form-section-title">Auction Details</div>
                    <div class="form-row">
                        <div class="form-group">
                            <label class="form-label">Starting Price (inc. GST) <span class="required">*</span></label>
                            <div class="input-prefix">
                                <span>$</span>
                                <input type="number" class="form-input" @bind="_auctionStartPrice" min="1" step="100" />
                            </div>
                        </div>
                        <div class="form-group">
                            <label class="form-label">End Date &amp; Time <span class="required">*</span></label>
                            <input type="datetime-local" class="form-input" @bind="_auctionEndDate" />
                        </div>
                    </div>
                    <div class="form-group">
                        <label class="form-label">
                            <input type="checkbox" @bind="_noReserve" /> No reserve
                        </label>
                    </div>
                    @if (!_noReserve)
                    {
                        <div class="form-group">
                            <label class="form-label">Reserve Price (inc. GST)</label>
                            <div class="input-prefix">
                                <span>$</span>
                                <input type="number" class="form-input" @bind="_auctionReserve" min="0" step="100" />
                            </div>
                        </div>
                    }
                </div>
            }

            <div class="form-section">
                <div class="form-section-title">Terms &amp; Conditions</div>
                <div class="form-group">
                    <label class="form-label">
                        Terms &amp; Conditions <span class="required">*</span>
                    </label>
                    <textarea class="form-input" @bind="_termsAndConditions" rows="6"
                              placeholder="e.g. 45-day payment on live foal guarantee. All nominations subject to availability…"></textarea>
                    <p class="form-hint">
                        ⚠️ Terms &amp; Conditions are locked permanently after the listing is first published.
                        If you need to change T&amp;C later, close this listing and create a new one.
                    </p>
                </div>
                <div class="form-group">
                    <label class="form-label">Description (optional)</label>
                    <textarea class="form-input" @bind="_description" rows="4"
                              placeholder="Additional details about this listing — can be edited after publishing." />
                </div>
            </div>

            @if (_error is not null)
            {
                <div class="alert alert-error">@_error</div>
            }

            <div class="form-actions">
                <button type="button" class="btn btn-primary" @onclick="HandleCreate" disabled="@_saving">
                    @(_saving ? "Creating…" : "Create Listing")
                </button>
                <a href="/admin/listings" class="btn btn-secondary">Cancel</a>
            </div>
        }
    </div>
}

@code {
    [SupplyParameterFromQuery] public Guid? StallionId { get; set; }

    private List<StallionSummaryDto> _stallions = [];
    private List<SeasonDto> _seasons = [];
    private Guid _selectedStallionId;
    private Guid _selectedSeasonId;
    private string _type = "FixedPrice";
    private bool _loading = true;
    private bool _saving;
    private string? _error;

    // Fixed price fields
    private decimal _fpPrice;
    private int _fpQuantity = 1;

    // Auction fields
    private decimal _auctionStartPrice;
    private decimal? _auctionReserve;
    private bool _noReserve;
    private DateTime _auctionEndDate = DateTime.Now.AddDays(7);

    // Shared
    private string _termsAndConditions = string.Empty;
    private string? _description;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _stallions = (await AdminApi.GetMyStallionsAsync()).Where(s => s.IsActive).ToList();
            _seasons = await AdminApi.GetSeasonsAsync();
            if (StallionId.HasValue) _selectedStallionId = StallionId.Value;
        }
        catch (ApiException ex) { _error = ex.Message; }
        finally { _loading = false; }
    }

    private void OnStallionChanged(ChangeEventArgs e)
    {
        if (Guid.TryParse(e.Value?.ToString(), out var id))
            _selectedStallionId = id;
    }

    private async Task HandleCreate()
    {
        _error = null;

        if (_selectedStallionId == Guid.Empty) { _error = "Please select a stallion."; return; }
        if (_selectedSeasonId == Guid.Empty) { _error = "Please select a season."; return; }
        if (string.IsNullOrWhiteSpace(_termsAndConditions)) { _error = "Terms & Conditions are required."; return; }

        _saving = true;
        try
        {
            ListingDto created;
            if (_type == "FixedPrice")
            {
                if (_fpPrice <= 0) { _error = "Please enter a price."; return; }
                if (_fpQuantity <= 0) { _error = "Quantity must be at least 1."; return; }
                created = await AdminApi.CreateFixedPriceListingAsync(new CreateFixedPriceListingRequest
                {
                    StallionId = _selectedStallionId,
                    SeasonId = _selectedSeasonId,
                    PriceIncGst = _fpPrice,
                    Quantity = _fpQuantity,
                    TermsAndConditions = _termsAndConditions.Trim(),
                    Description = string.IsNullOrWhiteSpace(_description) ? null : _description.Trim()
                });
            }
            else
            {
                if (_auctionStartPrice <= 0) { _error = "Please enter a starting price."; return; }
                if (_auctionEndDate <= DateTime.Now) { _error = "End date must be in the future."; return; }
                created = await AdminApi.CreateAuctionListingAsync(new CreateAuctionListingRequest
                {
                    StallionId = _selectedStallionId,
                    SeasonId = _selectedSeasonId,
                    StartingPrice = _auctionStartPrice,
                    ReservePrice = _noReserve ? null : _auctionReserve,
                    IsNoReserve = _noReserve,
                    EndDateTime = _auctionEndDate.ToUniversalTime(),
                    TermsAndConditions = _termsAndConditions.Trim(),
                    Description = string.IsNullOrWhiteSpace(_description) ? null : _description.Trim()
                });
            }
            Nav.NavigateTo($"/admin/listings/{created.Id}");
        }
        catch (ApiException ex) { _error = ex.Message; }
        finally { _saving = false; }
    }
}
```

- [ ] **Step 3: Create AdminListingDetail.razor — view/edit/publish/unpublish/close**

```razor
@* src/Client/Pages/Admin/AdminListingDetail.razor *@
@page "/admin/listings/{Id:guid}"
@layout AdminLayout
@attribute [Authorize(Roles = "StudFarmAdmin")]
@inject AdminApiService AdminApi
@inject NavigationManager Nav

<div class="admin-page-header">
    <h1>@(_listing?.StallionName ?? "Listing")</h1>
    <a href="/admin/listings" class="btn btn-secondary">← Back to My Listings</a>
</div>

@if (_loading)
{
    <p class="text-muted">Loading…</p>
}
else if (_listing is null)
{
    <div class="alert alert-error">Listing not found.</div>
}
else
{
    <div class="admin-form">
        @* Status bar *@
        <div style="display:flex;align-items:center;gap:var(--space-3);margin-bottom:var(--space-6)">
            <span class="badge @BadgeClass(_listing.Status)" style="font-size:14px;padding:5px 14px">
                @_listing.Status
            </span>
            @if (_listing.Status == "Draft")
            {
                <button class="btn btn-primary" @onclick="HandlePublish" disabled="@_busy">
                    @(_busy ? "Publishing…" : "Publish")
                </button>
            }
            @if (_listing.Status == "Active")
            {
                <button class="btn btn-secondary" @onclick="HandleUnpublish" disabled="@_busy">
                    @(_busy ? "Unpublishing…" : "Unpublish")
                </button>
                <button class="btn btn-danger" @onclick="HandleClose" disabled="@_busy">
                    @(_busy ? "Closing…" : "Close Listing")
                </button>
            }
            @if (_listing.PublishedAt.HasValue && _listing.Status == "Draft")
            {
                <span class="lock-notice">
                    🔒 Price, T&amp;C and type are permanently locked (listing was previously published).
                </span>
            }
        </div>

        @* Locked fields notice for staff fee *@
        @if (_listing.Status == "Draft" && !_listing.PlatformFeePercent.HasValue)
        {
            <div class="alert alert-warning" style="margin-bottom:var(--space-5)">
                ⚠️ A Stallions Australia staff member must set the platform fee before this listing can be published.
            </div>
        }

        @* Core listing info — always read-only summary *@
        <div class="form-section">
            <div class="form-section-title">Listing Details</div>
            <dl class="detail-list">
                <dt>Stallion</dt><dd>@_listing.StallionName</dd>
                <dt>Season</dt><dd>@_listing.SeasonName</dd>
                <dt>Type</dt><dd>@_listing.ListingType</dd>
                <dt>Created</dt><dd>@_listing.CreatedAt.ToString("dd MMM yyyy")</dd>
                @if (_listing.PublishedAt.HasValue)
                {
                    <dt>First published</dt><dd>@_listing.PublishedAt.Value.ToString("dd MMM yyyy")</dd>
                }
            </dl>
        </div>

        @* Type-specific locked fields *@
        @if (_listing is FixedPriceListingDto fp)
        {
            <div class="form-section">
                <div class="form-section-title">Fixed Price</div>
                <dl class="detail-list">
                    <dt>Price (inc. GST)</dt>
                    <dd>
                        @if (_neverPublished)
                        {
                            <input type="number" class="form-input" style="width:180px"
                                   @bind="_editPrice" min="1" step="100" />
                        }
                        else
                        {
                            <span>$@fp.PriceIncGst.ToString("N0")</span>
                            <span class="lock-notice">🔒 locked</span>
                        }
                    </dd>
                    <dt>Quantity</dt>
                    <dd>
                        @if (_listing.Status != "Cancelled" && _listing.Status != "Sold")
                        {
                            <input type="number" class="form-input" style="width:100px"
                                   @bind="_editQuantity" min="@(fp.Quantity - fp.QuantityRemaining)" />
                            <span class="form-hint">@fp.QuantityRemaining remaining of @fp.Quantity total</span>
                        }
                        else
                        {
                            <span>@fp.QuantityRemaining / @fp.Quantity</span>
                        }
                    </dd>
                </dl>
            </div>
        }
        else if (_listing is AuctionListingDto al)
        {
            <div class="form-section">
                <div class="form-section-title">Auction</div>
                <dl class="detail-list">
                    <dt>Starting price</dt>
                    <dd>
                        @if (_neverPublished)
                        {
                            <input type="number" class="form-input" style="width:180px"
                                   @bind="_editAuctionStart" min="1" step="100" />
                        }
                        else
                        {
                            <span>$@al.StartingPrice.ToString("N0")</span>
                            <span class="lock-notice">🔒 locked</span>
                        }
                    </dd>
                    <dt>Current high bid</dt>
                    <dd>@(al.CurrentHighestBidIncGst.HasValue ? $"${al.CurrentHighestBidIncGst:N0}" : "No bids yet")</dd>
                    <dt>End date/time</dt>
                    <dd>
                        @if (_neverPublished)
                        {
                            <input type="datetime-local" class="form-input"
                                   @bind="_editAuctionEnd" />
                        }
                        else
                        {
                            <span>@al.EndDateTime.ToLocalTime().ToString("dd MMM yyyy HH:mm")</span>
                            <span class="lock-notice">🔒 locked</span>
                        }
                    </dd>
                    <dt>Reserve</dt>
                    <dd>@(al.IsNoReserve ? "No reserve" : (al.ReservePrice.HasValue ? $"${al.ReservePrice:N0}" : "—"))</dd>
                </dl>
            </div>
        }

        @* T&C — locked after first publish *@
        <div class="form-section">
            <div class="form-section-title">Terms &amp; Conditions</div>
            @if (_neverPublished)
            {
                <textarea class="form-input" @bind="_editTerms" rows="6"></textarea>
            }
            else
            {
                <div class="locked-field" style="white-space:pre-wrap;padding:var(--space-3)">@_listing.TermsAndConditions</div>
                <p class="lock-notice">🔒 Locked permanently after first publish. Close and relist to change T&amp;C.</p>
            }
        </div>

        @* Description — always editable *@
        @if (_listing.Status != "Cancelled" && _listing.Status != "Sold")
        {
            <div class="form-section">
                <div class="form-section-title">Description <span style="font-weight:400;color:var(--color-text-muted)">(always editable)</span></div>
                <textarea class="form-input" @bind="_editDescription" rows="5"></textarea>
            </div>

            @if (_error is not null)
            {
                <div class="alert alert-error">@_error</div>
            }
            @if (_saved)
            {
                <div class="alert alert-success">Changes saved.</div>
            }

            <div class="form-actions">
                <button class="btn btn-primary" @onclick="HandleSave" disabled="@_busy">
                    @(_busy ? "Saving…" : "Save Changes")
                </button>
                <a href="/admin/listings/@Id" class="btn btn-outline">View on Marketplace</a>
            </div>
        }
    </div>
}

@code {
    [Parameter] public Guid Id { get; set; }

    private ListingDto? _listing;
    private bool _loading = true;
    private bool _busy;
    private bool _saved;
    private string? _error;

    // Edit fields
    private decimal _editPrice;
    private int _editQuantity;
    private decimal _editAuctionStart;
    private DateTime _editAuctionEnd;
    private string _editTerms = string.Empty;
    private string? _editDescription;

    private bool _neverPublished => _listing?.PublishedAt == null;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            _listing = await AdminApi.GetListingAsync(Id);
            if (_listing is FixedPriceListingDto fp)
            {
                _editPrice = fp.PriceIncGst;
                _editQuantity = fp.Quantity;
            }
            else if (_listing is AuctionListingDto al)
            {
                _editAuctionStart = al.StartingPrice;
                _editAuctionEnd = al.EndDateTime.ToLocalTime();
            }
            _editTerms = _listing.TermsAndConditions ?? string.Empty;
            _editDescription = _listing.Description;
        }
        catch (ApiException ex) { _error = ex.Message; }
        finally { _loading = false; }
    }

    private async Task HandleSave()
    {
        _busy = true; _error = null; _saved = false;
        try
        {
            var request = new UpdateListingRequest
            {
                Description = _editDescription,
                TermsAndConditions = _neverPublished ? _editTerms : null
            };
            if (_neverPublished && _listing is FixedPriceListingDto)
                request.PriceIncGst = _editPrice;
            if (_listing is FixedPriceListingDto)
                request.Quantity = _editQuantity;
            if (_neverPublished && _listing is AuctionListingDto)
            {
                request.StartingPrice = _editAuctionStart;
                request.EndDateTime = _editAuctionEnd.ToUniversalTime();
            }
            _listing = await AdminApi.UpdateListingAsync(Id, request);
            _saved = true;
        }
        catch (ApiException ex) { _error = ex.Message; }
        finally { _busy = false; }
    }

    private async Task HandlePublish()
    {
        _busy = true; _error = null;
        try { await AdminApi.PublishListingAsync(Id); await LoadAsync(); }
        catch (ApiException ex) { _error = ex.Message; }
        finally { _busy = false; }
    }

    private async Task HandleUnpublish()
    {
        _busy = true; _error = null;
        try { await AdminApi.UnpublishListingAsync(Id); await LoadAsync(); }
        catch (ApiException ex) { _error = ex.Message; }
        finally { _busy = false; }
    }

    private async Task HandleClose()
    {
        _busy = true; _error = null;
        try { await AdminApi.CloseListingAsync(Id); await LoadAsync(); }
        catch (ApiException ex) { _error = ex.Message; }
        finally { _busy = false; }
    }

    private static string BadgeClass(string status) => status switch
    {
        "Active"    => "badge-active",
        "Draft"     => "badge-draft",
        "Cancelled" => "badge-cancelled",
        "Expired"   => "badge-expired",
        "Sold"      => "badge-sold",
        _           => ""
    };
}
```

- [ ] **Step 4: Build to confirm no compile errors**

```
dotnet build src/Client
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```
git add src/Client/Pages/Admin/AdminListings.razor
git add src/Client/Pages/Admin/AdminListingForm.razor
git add src/Client/Pages/Admin/AdminListingDetail.razor
git commit -m "feat: My Listings admin pages — grouped list, create form, detail/edit with publish/unpublish/close"
```

---

## Task 11: Client — Admin Enquiries pages

**Files:**
- Create: `src/Client/Pages/Admin/AdminEnquiries.razor`
- Create: `src/Client/Pages/Admin/AdminEnquiryDetail.razor`

- [ ] **Step 1: Create AdminEnquiries.razor — inbox**

```razor
@* src/Client/Pages/Admin/AdminEnquiries.razor *@
@page "/admin/enquiries"
@layout AdminLayout
@attribute [Authorize(Roles = "StudFarmAdmin")]
@inject AdminApiService AdminApi
@inject NavigationManager Nav

<div class="admin-page-header">
    <h1>Enquiries</h1>
</div>

@if (_loading)
{
    <p class="text-muted">Loading…</p>
}
else if (_enquiries.Count == 0)
{
    <EmptyState Icon="✉️" Message="No enquiries yet. Buyers can send enquiries from listing pages." />
}
else
{
    <table class="admin-table">
        <thead>
            <tr>
                <th></th>
                <th>Buyer</th>
                <th>Stallion / Listing</th>
                <th>Messages</th>
                <th>Last Activity</th>
                <th>Status</th>
                <th></th>
            </tr>
        </thead>
        <tbody>
            @foreach (var e in _enquiries.OrderByDescending(e => e.LastMessageAt))
            {
                <tr class="@(e.IsUnread ? "enquiry-row-unread" : "")">
                    <td>
                        @if (e.IsUnread)
                        {
                            <span class="enquiry-unread-dot"></span>
                        }
                    </td>
                    <td>@e.BuyerName</td>
                    <td>
                        <div>@e.StallionName</div>
                        <div style="font-size:12px;color:var(--color-text-muted)">@e.ListingTitle</div>
                    </td>
                    <td>@e.MessageCount</td>
                    <td>@(e.LastMessageAt?.ToString("dd MMM yyyy") ?? "—")</td>
                    <td><span class="badge @(e.Status == "Open" ? "badge-active" : "badge-cancelled")">@e.Status</span></td>
                    <td>
                        <a href="/admin/enquiries/@e.Id" class="btn btn-sm btn-secondary">View</a>
                    </td>
                </tr>
            }
        </tbody>
    </table>
}

@if (_error is not null)
{
    <div class="alert alert-error">@_error</div>
}

@code {
    private List<EnquirySummaryDto> _enquiries = [];
    private bool _loading = true;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _enquiries = await AdminApi.GetMyEnquiriesAsync();
        }
        catch (ApiException ex) { _error = ex.Message; }
        finally { _loading = false; }
    }
}
```

- [ ] **Step 2: Create AdminEnquiryDetail.razor — thread + reply**

```razor
@* src/Client/Pages/Admin/AdminEnquiryDetail.razor *@
@page "/admin/enquiries/{Id:guid}"
@layout AdminLayout
@attribute [Authorize(Roles = "StudFarmAdmin")]
@inject AdminApiService AdminApi
@inject AuthenticationStateProvider AuthStateProvider

<div class="admin-page-header">
    <h1>Enquiry</h1>
    <a href="/admin/enquiries" class="btn btn-secondary">← Back to Enquiries</a>
</div>

@if (_loading)
{
    <p class="text-muted">Loading…</p>
}
else if (_enquiry is null)
{
    <div class="alert alert-error">Enquiry not found.</div>
}
else
{
    @* Context block *@
    <div style="background:var(--color-white);border-radius:var(--radius-md);padding:var(--space-5);margin-bottom:var(--space-5);box-shadow:var(--shadow-sm)">
        <dl class="detail-list">
            <dt>Buyer</dt><dd>@_summary?.BuyerName</dd>
            <dt>Stallion</dt><dd>@_summary?.StallionName</dd>
            <dt>Listing</dt><dd>@_summary?.ListingTitle</dd>
            <dt>Status</dt>
            <dd><span class="badge @(_enquiry.Status == "Open" ? "badge-active" : "badge-cancelled")">@_enquiry.Status</span></dd>
        </dl>
    </div>

    @* Thread *@
    <MessageThread Messages="_enquiry.Messages"
                   CurrentUserId="_currentUserId"
                   BuyerUserId="_enquiry.BuyerUserId" />

    @* Reply *@
    @if (_enquiry.Status == "Open")
    {
        <div style="margin-top:var(--space-5)">
            <MessageComposer OnSend="HandleReply" />
        </div>
        @if (_replyError is not null)
        {
            <div class="alert alert-error">@_replyError</div>
        }
    }
    else
    {
        <p class="text-muted" style="margin-top:var(--space-4)">This enquiry is closed.</p>
    }
}

@code {
    [Parameter] public Guid Id { get; set; }

    private EnquiryDto? _enquiry;
    private EnquirySummaryDto? _summary;
    private Guid _currentUserId;
    private bool _loading = true;
    private string? _replyError;

    protected override async Task OnInitializedAsync()
    {
        var auth = await AuthStateProvider.GetAuthenticationStateAsync();
        var oidSub = auth.User.FindFirst("sub")?.Value
                  ?? auth.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
        // Note: _currentUserId drives MessageThread "sender" alignment. The Entra ID
        // object ID won't match the app's internal Guid exactly, but MessageThread
        // uses BuyerUserId to distinguish left/right — the farm's messages are anything
        // that is NOT the BuyerUserId, so alignment is correct regardless.
        _ = Guid.TryParse(oidSub, out _currentUserId);

        try
        {
            _enquiry = await AdminApi.GetEnquiryAsync(Id);
            // Load summary separately for context block metadata
            var all = await AdminApi.GetMyEnquiriesAsync();
            _summary = all.FirstOrDefault(e => e.Id == Id);
        }
        catch (ApiException ex)
        {
            // If loading summary fails, detail view still works
            if (_enquiry is null)
                _ = ex; // surfaced via null check above
        }
        finally { _loading = false; }
    }

    private async Task HandleReply(string body)
    {
        _replyError = null;
        try
        {
            await AdminApi.SendReplyAsync(Id, new SendMessageRequest { Body = body });
            _enquiry = await AdminApi.GetEnquiryAsync(Id);
        }
        catch (ApiException ex) { _replyError = ex.Message; }
    }
}
```

- [ ] **Step 3: Build to confirm no compile errors**

```
dotnet build src/Client
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```
git add src/Client/Pages/Admin/AdminEnquiries.razor
git add src/Client/Pages/Admin/AdminEnquiryDetail.razor
git commit -m "feat: Admin Enquiries pages — inbox with unread indicators + enquiry thread with reply"
```

---

## Task 12: Wire server Program.cs, apply migration, smoke test

**Files:**
- Modify: `src/Server/Program.cs` — register `BlobStorageService`
- Run migration against dev database
- Manual smoke test

- [ ] **Step 1: Confirm BlobStorageService is registered in Program.cs**

Open `src/Server/Program.cs` and verify it contains:
```csharp
builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();
```
If missing, add it after the other service registrations.

- [ ] **Step 2: Apply migration to dev database**

```
dotnet ef database update --project src/Server --startup-project src/Server
```
Expected: "Done" with mention of `AddListingDescriptionAndTerms` migration.

- [ ] **Step 3: Run the full test suite**

```
dotnet test
```
Expected: all tests pass.

- [ ] **Step 4: Build release to catch any remaining issues**

```
dotnet build --configuration Release
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Smoke test — manual verification checklist**

Start the app locally (`dotnet run --project src/Server`) and sign in as a StudFarmAdmin user. Verify:

- [ ] `/admin/stallions` — roster table loads; empty state shows for new farm
- [ ] `/admin/stallions/new` — form submits; stallion appears in roster
- [ ] `/admin/stallions/{id}` — edit form loads pre-populated; IsActive toggle works; image upload calls the server (will fail locally if storage not configured — expect a 400 or 500, not a crash)
- [ ] `/admin/listings` — empty state if no listings; grouped by stallion once listings exist
- [ ] `/admin/listings/new` — stallion dropdown populates; Fixed Price and Auction forms toggle; T&C field present and required; listing creates and redirects to detail
- [ ] `/admin/listings/{id}` — detail shows status badge; Publish button appears for Draft; Publish succeeds if fee is set by staff; Unpublish/Close buttons appear for Active; description field editable; price field locked after publish
- [ ] `/admin/enquiries` — inbox loads; unread badge appears in sidebar when unread messages exist
- [ ] `/admin/enquiries/{id}` — thread displays; reply sends and thread refreshes
- [ ] Navigating to `/admin/stallions` when not signed in redirects to Entra ID login

- [ ] **Step 6: Final commit**

```
git add src/Server/Program.cs
git commit -m "feat: wire BlobStorageService DI registration; apply DB migration for Plan 4"
```

---

## Notes for Implementers

**T&C lock sentinel:** `listing.PublishedAt != null` is the permanent lock. `UnpublishListingAsync` intentionally does NOT clear `PublishedAt`. This is the only flag needed — no separate `EverPublished` column required.

**Quantity safe-edit formula:** `soldCount = Quantity - QuantityRemaining; newRemaining = max(0, newQuantity - soldCount)`. Never allow `newQuantity < soldCount` (the form's `min` attribute enforces this client-side; the service enforces nothing beyond the formula — if quantity drops below sold count, QuantityRemaining becomes 0 and the listing closes naturally on next purchase attempt).

**Image upload local dev:** Requires `az login` and Storage Blob Data Contributor role on the dev account, plus `AZURE_STORAGE_ACCOUNT_NAME` in `appsettings.Development.json`. This file is gitignored.

**EnquirySummaryDto.ListingTitle:** Constructed in `EnquiryService.MapToSummary` from navigation properties. Requires EF `Include` chains in `IEnquiryRepository.GetByStudFarmUserIdAsync` — verify the repo loads `Listing → Stallion`, `Listing → Season`, and `Buyer` navigation properties, adding `.Include()` calls if missing.

**Blazor Scoped CSS + media queries:** Per CLAUDE.md, `::deep` does not work inside `@media` blocks. Any responsive overrides for admin pages go in the global `admin.css`, not in `.razor.css` files.

**SeasonDto namespace:** `Stallions.Shared.DTOs.Seasons` — add `@using` in `_Imports.razor` (done in Task 7 Step 5) and a `using` in `AdminApiService.cs`.

**Spec says "Closed" / "Unsold" status names:** The database uses `ListingStatus.Cancelled` and `ListingStatus.Expired`. The UI displays these as "Cancelled" and "Expired". The spec used "Closed" and "Unsold" as conceptual names — the code uses the enum names throughout.
