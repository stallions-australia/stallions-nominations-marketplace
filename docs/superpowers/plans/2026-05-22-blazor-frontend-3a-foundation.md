# Blazor WebAssembly Frontend — Plan 3a: Foundation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the Plan 2 API with the DTOs and endpoints the Blazor client needs, then wire up the Blazor WebAssembly project with MSAL auth, typed HTTP services, and the CSS design token system. At the end of this plan the app compiles, Entra ID auth redirects work, and the design tokens are in place. No pages or UI components yet.

**Architecture:** Four tasks in dependency order — API gaps first (shared DTOs + server fixes), then client NuGet/project setup, then the client bootstrap wiring (Program.cs, App.razor, auth pages), then the CSS system that all components will consume in Plans 3b and 3c.

**Tech Stack:** ASP.NET Core + EF Core (server fixes), Blazor WebAssembly .NET 9, Microsoft.Authentication.WebAssembly.Msal 9.x, bUnit 1.x + xunit 2.x (test project), custom CSS custom properties.

---

### Task 1: Shared DTOs and API server extensions

**Why:** The browse page needs a flat `ListingCardDto` (avoids polymorphic deserialization issues). The stud farm detail page needs a `StudFarmDto`. The existing `GetActiveAsync` doesn't include the `StudFarm` navigation property — so `StudFarmName` is always empty — and never loads bid aggregates. All three gaps are fixed here, on the server, before the client touches any of it.

**Files:**
- Create: `src/Shared/DTOs/Listings/ListingCardDto.cs`
- Create: `src/Shared/DTOs/StudFarms/StudFarmDto.cs`
- Modify: `src/Shared/DTOs/Listings/ListingDto.cs` — add `[JsonPolymorphic]` attrs for client deserialization
- Modify: `src/Server/Data/Repositories/IListingRepository.cs` — add `studFarmId` param + `GetBidAggregatesAsync`
- Modify: `src/Server/Data/Repositories/ListingRepository.cs` — include StudFarm, add `studFarmId` filter, implement `GetBidAggregatesAsync`
- Modify: `src/Server/Services/IListingService.cs` — add `GetListingCardsAsync`
- Modify: `src/Server/Services/ListingService.cs` — implement `GetListingCardsAsync` + `MapToCardDto`
- Modify: `src/Server/Controllers/ListingsController.cs` — update `GetActive` to call `GetListingCardsAsync`, add `studFarmId` param
- Create: `src/Server/Controllers/StudFarmsController.cs`
- Test: `tests/Server.Tests/Services/ListingServiceCardTests.cs`

---

- [ ] **Step 1: Write the failing tests**

Create `tests/Server.Tests/Services/ListingServiceCardTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Listings;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class ListingServiceCardTests
{
    private readonly Mock<IListingRepository> _mockListingRepo = new();
    private readonly Mock<ISeasonRepository> _mockSeasonRepo = new();
    private readonly Mock<IStallionRepository> _mockStallionRepo = new();
    private readonly Mock<IStudFarmRepository> _mockFarmRepo = new();
    private readonly Mock<IUserService> _mockUsers = new();

    private ListingService CreateSut() => new(
        _mockListingRepo.Object,
        _mockSeasonRepo.Object,
        _mockStallionRepo.Object,
        _mockFarmRepo.Object,
        _mockUsers.Object);

    [Fact]
    public async Task GetListingCardsAsync_FixedPrice_PopulatesStudFarmNameAndQuantity()
    {
        var studFarm = new StudFarm { Id = Guid.NewGuid(), Name = "Coolmore Australia" };
        var stallion = new Stallion
        {
            Id = Guid.NewGuid(), Name = "Fastnet Rock",
            Images = new List<StallionImage>()
        };
        var listing = new FixedPriceListing
        {
            Id = Guid.NewGuid(),
            StudFarm = studFarm, StudFarmId = studFarm.Id,
            Stallion = stallion, StallionId = stallion.Id,
            Season = new Season { Name = "2025 Season" },
            PriceIncGst = 8000m, Quantity = 5, QuantityRemaining = 4,
            Status = ListingStatus.Active, ListingType = ListingType.FixedPrice
        };

        _mockListingRepo
            .Setup(r => r.GetActiveAsync(null, null, null))
            .ReturnsAsync(new List<Listing> { listing });

        var result = await CreateSut().GetListingCardsAsync(null, null, null);

        result.Succeeded.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        var card = result.Value![0];
        card.StudFarmName.Should().Be("Coolmore Australia");
        card.QuantityRemaining.Should().Be(4);
        card.PriceIncGst.Should().Be(8000m);
        card.ListingType.Should().Be("FixedPrice");
    }

    [Fact]
    public async Task GetListingCardsAsync_Auction_IncludesBidCountAndHighestBid()
    {
        var studFarm = new StudFarm { Id = Guid.NewGuid(), Name = "Arrowfield Stud" };
        var stallion = new Stallion
        {
            Id = Guid.NewGuid(), Name = "Snitzel",
            Images = new List<StallionImage>()
        };
        var auctionId = Guid.NewGuid();
        var listing = new AuctionListing
        {
            Id = auctionId,
            StudFarm = studFarm, StudFarmId = studFarm.Id,
            Stallion = stallion, StallionId = stallion.Id,
            Season = new Season { Name = "2025 Season" },
            StartingPrice = 5000m, ReservePrice = 8000m, IsNoReserve = false,
            MinimumBidIncrement = 25m,
            EndDateTime = DateTime.UtcNow.AddDays(3),
            Status = ListingStatus.Active, ListingType = ListingType.Auction
        };

        _mockListingRepo
            .Setup(r => r.GetActiveAsync(null, null, null))
            .ReturnsAsync(new List<Listing> { listing });

        _mockListingRepo
            .Setup(r => r.GetBidAggregatesAsync(It.Is<IEnumerable<Guid>>(ids => ids.Contains(auctionId))))
            .ReturnsAsync(new Dictionary<Guid, (int Count, decimal? Highest)>
            {
                { auctionId, (3, 7500m) }
            });

        var result = await CreateSut().GetListingCardsAsync(null, null, null);

        result.Succeeded.Should().BeTrue();
        var card = result.Value![0];
        card.BidCount.Should().Be(3);
        card.CurrentHighestBidIncGst.Should().Be(7500m);
        card.ReserveMet.Should().BeFalse(); // 7500 < 8000 reserve
        card.AuctionClosesAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(3), TimeSpan.FromSeconds(5));
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test tests/Server.Tests --filter "ListingServiceCard" -v minimal
```

Expected: Build error — `GetListingCardsAsync` does not exist yet.

- [ ] **Step 3: Create `ListingCardDto`**

Create `src/Shared/DTOs/Listings/ListingCardDto.cs`:

```csharp
namespace Stallions.Shared.DTOs.Listings;

public class ListingCardDto
{
    public Guid Id { get; set; }
    public string ListingType { get; set; } = string.Empty; // "Auction" | "FixedPrice"
    public Guid StallionId { get; set; }
    public string StallionName { get; set; } = string.Empty;
    public string? PrimaryImagePath { get; set; }
    public Guid StudFarmId { get; set; }
    public string StudFarmName { get; set; } = string.Empty;
    public string? SeasonName { get; set; }

    /// <summary>Starting price for auctions; fixed price for fixed-price listings.</summary>
    public decimal PriceIncGst { get; set; }

    // Auction-specific (null for FixedPrice listings)
    public decimal? CurrentHighestBidIncGst { get; set; }
    public int? BidCount { get; set; }
    public DateTime? AuctionClosesAt { get; set; }
    /// <summary>null = no reserve (IsNoReserve=true); true/false = reserve status.</summary>
    public bool? ReserveMet { get; set; }

    // FixedPrice-specific (null for Auction listings)
    public int? QuantityRemaining { get; set; }
}
```

- [ ] **Step 4: Create `StudFarmDto`**

Create `src/Shared/DTOs/StudFarms/StudFarmDto.cs`:

```csharp
namespace Stallions.Shared.DTOs.StudFarms;

public class StudFarmDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
}
```

- [ ] **Step 5: Add `[JsonPolymorphic]` to `ListingDto`**

This lets the Blazor client correctly deserialize `AuctionListingDto`/`FixedPriceListingDto` from `GET /api/listings/{id}` without writing custom converters.

Edit `src/Shared/DTOs/Listings/ListingDto.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Stallions.Shared.DTOs.Listings;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "listingType")]
[JsonDerivedType(typeof(AuctionListingDto), "Auction")]
[JsonDerivedType(typeof(FixedPriceListingDto), "FixedPrice")]
public class ListingDto
{
    public Guid Id { get; set; }
    public Guid StallionId { get; set; }
    public string StallionName { get; set; } = string.Empty;
    public Guid SeasonId { get; set; }
    public string SeasonName { get; set; } = string.Empty;
    public Guid StudFarmId { get; set; }
    public string StudFarmName { get; set; } = string.Empty;
    public string ListingType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal? PlatformFeePercent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}
```

- [ ] **Step 6: Update `IListingRepository`**

Edit `src/Server/Data/Repositories/IListingRepository.cs` — add `studFarmId` to `GetActiveAsync` and new `GetBidAggregatesAsync`:

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
    Task<Listing> AddAsync(Listing listing);
    Task UpdateAsync(Listing listing);
}
```

- [ ] **Step 7: Update `ListingRepository`**

Edit `src/Server/Data/Repositories/ListingRepository.cs` — add StudFarm include, studFarmId filter, and `GetBidAggregatesAsync`:

```csharp
public async Task<IReadOnlyList<Listing>> GetActiveAsync(
    Guid? seasonId = null, Guid? studFarmId = null, ListingType? type = null)
{
    var query = _db.Listings
        .Where(l => l.Status == ListingStatus.Active)
        .Include(l => l.Stallion).ThenInclude(s => s.Images)
        .Include(l => l.Season)
        .Include(l => l.StudFarm)   // Fix: was missing, caused empty StudFarmName
        .AsQueryable();
    if (seasonId.HasValue)   query = query.Where(l => l.SeasonId == seasonId.Value);
    if (studFarmId.HasValue) query = query.Where(l => l.StudFarmId == studFarmId.Value);
    if (type.HasValue)       query = query.Where(l => l.ListingType == type.Value);
    return await query.OrderByDescending(l => l.PublishedAt).ToListAsync();
}

public async Task<Dictionary<Guid, (int Count, decimal? Highest)>> GetBidAggregatesAsync(
    IEnumerable<Guid> auctionIds)
{
    var ids = auctionIds.ToList();
    var rows = await _db.Bids
        .Where(b => ids.Contains(b.AuctionListingId))
        .GroupBy(b => b.AuctionListingId)
        .Select(g => new
        {
            AuctionListingId = g.Key,
            Count = g.Count(),
            Highest = (decimal?)g.Max(b => b.AmountIncGst)
        })
        .ToListAsync();
    return rows.ToDictionary(x => x.AuctionListingId, x => (x.Count, x.Highest));
}
```

Also update the old `GetActiveAsync` call in the existing `ListingService.GetActiveAsync` method body — it currently passes `(seasonId, type)`. Change to `(seasonId, null, type)` so it compiles with the new signature.

- [ ] **Step 8: Add `GetListingCardsAsync` to `IListingService`**

Edit `src/Server/Services/IListingService.cs` — append new method:

```csharp
Task<ServiceResult<IReadOnlyList<ListingCardDto>>> GetListingCardsAsync(
    Guid? seasonId, Guid? studFarmId, string? type);
```

Add the using at the top: `using Stallions.Shared.DTOs.Listings;`

- [ ] **Step 9: Implement `GetListingCardsAsync` in `ListingService`**

Edit `src/Server/Services/ListingService.cs` — add the method and private helper **after** the existing `MapToDto` switch expression:

```csharp
public async Task<ServiceResult<IReadOnlyList<ListingCardDto>>> GetListingCardsAsync(
    Guid? seasonId, Guid? studFarmId, string? type)
{
    ListingType? listingType = type switch
    {
        "Auction"    => ListingType.Auction,
        "FixedPrice" => ListingType.FixedPrice,
        _            => null
    };

    var listings = await _listingRepo.GetActiveAsync(seasonId, studFarmId, listingType);

    var auctionIds = listings.OfType<AuctionListing>().Select(l => l.Id).ToList();
    var bidAggregates = auctionIds.Count > 0
        ? await _listingRepo.GetBidAggregatesAsync(auctionIds)
        : new Dictionary<Guid, (int Count, decimal? Highest)>();

    var dtos = listings.Select(l => MapToCardDto(l, bidAggregates)).ToList();
    return ServiceResult<IReadOnlyList<ListingCardDto>>.Ok(dtos);
}

private static ListingCardDto MapToCardDto(
    Listing l,
    Dictionary<Guid, (int Count, decimal? Highest)> bidAggregates)
{
    var primaryImage = l.Stallion?.Images
        .OrderByDescending(i => i.IsPrimary)
        .ThenBy(i => i.DisplayOrder)
        .FirstOrDefault()?.BlobPath;

    if (l is AuctionListing al)
    {
        bidAggregates.TryGetValue(al.Id, out var bidData);
        return new ListingCardDto
        {
            Id             = al.Id,
            ListingType    = "Auction",
            StallionId     = al.StallionId,
            StallionName   = al.Stallion?.Name ?? string.Empty,
            PrimaryImagePath = primaryImage,
            StudFarmId     = al.StudFarmId,
            StudFarmName   = al.StudFarm?.Name ?? string.Empty,
            SeasonName     = al.Season?.Name,
            PriceIncGst    = al.StartingPrice,
            CurrentHighestBidIncGst = bidData.Highest,
            BidCount       = bidData.Count,
            AuctionClosesAt = al.EndDateTime,
            ReserveMet     = al.IsNoReserve
                ? null
                : al.ReservePrice.HasValue && bidData.Highest >= al.ReservePrice
        };
    }

    if (l is FixedPriceListing fpl)
    {
        return new ListingCardDto
        {
            Id             = fpl.Id,
            ListingType    = "FixedPrice",
            StallionId     = fpl.StallionId,
            StallionName   = fpl.Stallion?.Name ?? string.Empty,
            PrimaryImagePath = primaryImage,
            StudFarmId     = fpl.StudFarmId,
            StudFarmName   = fpl.StudFarm?.Name ?? string.Empty,
            SeasonName     = fpl.Season?.Name,
            PriceIncGst    = fpl.PriceIncGst,
            QuantityRemaining = fpl.QuantityRemaining
        };
    }

    throw new InvalidOperationException($"Unknown listing type: {l.GetType().Name}");
}
```

- [ ] **Step 10: Update `ListingsController.GetActive`**

Edit `src/Server/Controllers/ListingsController.cs` — change the `GetActive` action to call `GetListingCardsAsync` and add the new `studFarmId` query param:

```csharp
[HttpGet]
[AllowAnonymous]
public async Task<IActionResult> GetActive(
    [FromQuery] Guid? seasonId,
    [FromQuery] Guid? studFarmId,
    [FromQuery] string? type)
{
    var r = await _listings.GetListingCardsAsync(seasonId, studFarmId, type);
    return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
}
```

- [ ] **Step 11: Create `StudFarmsController`**

Create `src/Server/Controllers/StudFarmsController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.StudFarms;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/studfarms")]
[AllowAnonymous]
public class StudFarmsController : ControllerBase
{
    private readonly IStudFarmRepository _farms;
    public StudFarmsController(IStudFarmRepository farms) => _farms = farms;

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var farm = await _farms.GetByIdAsync(id);
        if (farm == null) return NotFound();
        return Ok(new StudFarmDto
        {
            Id           = farm.Id,
            Name         = farm.Name,
            Address      = farm.Address,
            ContactEmail = farm.ContactEmail,
            ContactPhone = farm.ContactPhone
        });
    }
}
```

- [ ] **Step 12: Run the tests — expect green**

```
dotnet test tests/Server.Tests --filter "ListingServiceCard" -v minimal
```

Expected: 2 tests PASS.

- [ ] **Step 13: Build server to confirm no regressions**

```
dotnet build src/Server -v minimal
```

Expected: 0 errors.

- [ ] **Step 14: Commit**

```
git add src/Shared/DTOs/Listings/ListingCardDto.cs \
        src/Shared/DTOs/Listings/ListingDto.cs \
        src/Shared/DTOs/StudFarms/ \
        src/Server/Data/Repositories/IListingRepository.cs \
        src/Server/Data/Repositories/ListingRepository.cs \
        src/Server/Services/IListingService.cs \
        src/Server/Services/ListingService.cs \
        src/Server/Controllers/ListingsController.cs \
        src/Server/Controllers/StudFarmsController.cs \
        tests/Server.Tests/Services/ListingServiceCardTests.cs
git commit -m "feat: add ListingCardDto, StudFarmDto, StudFarmsController; fix StudFarm+bid data on listing browse"
```

---

### Task 2: Client project setup

**Why:** The client needs MSAL for Entra ID auth. The test project needs bUnit so we can write component tests in Plans 3b and 3c. Both need to be in place before any client code is written.

**Files:**
- Modify: `src/Client/Stallions.Client.csproj` — add MSAL package
- Create: `tests/Client.Tests/Stallions.Client.Tests.csproj`
- Modify: `Stallions.sln` — add Client.Tests to solution

---

- [ ] **Step 1: Add MSAL to the client project**

Edit `src/Client/Stallions.Client.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="9.0.15" />
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.DevServer" Version="9.0.15" PrivateAssets="all" />
    <PackageReference Include="Microsoft.Authentication.WebAssembly.Msal" Version="9.0.15" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create `Client.Tests` project**

Create `tests/Client.Tests/Stallions.Client.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="bunit" Version="1.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" PrivateAssets="all" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="FluentAssertions" Version="7.*" />
    <PackageReference Include="Moq" Version="4.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Client\Stallions.Client.csproj" />
    <ProjectReference Include="..\..\src\Shared\Stallions.Shared.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Add `Client.Tests` to the solution**

```
dotnet sln add tests/Client.Tests/Stallions.Client.Tests.csproj
```

- [ ] **Step 4: Restore and build**

```
dotnet restore && dotnet build -v minimal
```

Expected: 0 errors across all projects.

- [ ] **Step 5: Commit**

```
git add src/Client/Stallions.Client.csproj \
        tests/Client.Tests/ \
        Stallions.sln
git commit -m "chore: add MSAL package to Client, create Client.Tests project with bUnit"
```

---

### Task 3: Client bootstrap

**Why:** The out-of-the-box `Program.cs` has a single anonymous `HttpClient`. `App.razor` has no auth awareness. `index.html` loads Bootstrap (not used — replaced with our custom CSS). All three need to be replaced before any page or component can be built.

**Files:**
- Rewrite: `src/Client/Program.cs`
- Modify: `src/Client/wwwroot/index.html`
- Modify: `src/Client/_Imports.razor`
- Rewrite: `src/Client/App.razor`
- Create: `src/Client/wwwroot/appsettings.json`
- Create: `src/Client/Pages/Authentication.razor`
- Create: `src/Client/Shared/RedirectToLogin.razor`
- Test: `tests/Client.Tests/AppRenderTests.cs`

---

- [ ] **Step 1: Write a smoke test for App.razor**

Create `tests/Client.Tests/AppRenderTests.cs`:

```csharp
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stallions.Client;

namespace Stallions.Client.Tests;

public class AppRenderTests : TestContext
{
    [Fact]
    public void App_RendersWithoutThrowing()
    {
        // Add fake auth so AuthorizeRouteView doesn't throw
        this.AddTestAuthorization();

        // Act — will throw if component graph is broken
        var act = () => RenderComponent<App>();

        act.Should().NotThrow();
    }
}
```

- [ ] **Step 2: Run test — expect failure**

```
dotnet test tests/Client.Tests --filter "AppRender" -v minimal
```

Expected: FAIL — `App` renders but `CascadingAuthenticationState` can't resolve its services without MSAL registered.

- [ ] **Step 3: Rewrite `Program.cs`**

```csharp
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Stallions.Client;
using Stallions.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Entra ID (MSAL) auth
builder.Services.AddMsalAuthentication(options =>
{
    builder.Configuration.Bind("AzureAd", options.ProviderOptions.Authentication);
    options.ProviderOptions.DefaultAccessTokenScopes.Add(builder.Configuration["ApiScope"]!);
});

// Typed API services — BaseAddressAuthorizationMessageHandler attaches the token when the
// user is signed in; unauthenticated requests to public endpoints pass through unchanged.
var apiBase = new Uri(builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress);

builder.Services.AddHttpClient<ListingApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
builder.Services.AddHttpClient<StallionApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
builder.Services.AddHttpClient<BidApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
builder.Services.AddHttpClient<CheckoutApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
builder.Services.AddHttpClient<EnquiryApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
builder.Services.AddHttpClient<UserApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();

await builder.Build().RunAsync();
```

- [ ] **Step 4: Create stub service files so Program.cs compiles**

Create these empty stubs in `src/Client/Services/` (full implementations come in Plan 3b Task 7):

`src/Client/Services/ApiException.cs`:
```csharp
namespace Stallions.Client.Services;

public class ApiException : Exception
{
    public int StatusCode { get; }
    public ApiException(int statusCode, string message) : base(message)
        => StatusCode = statusCode;
}
```

Create empty service classes (one file each — just the class declaration):

`src/Client/Services/ListingApiService.cs`:
```csharp
namespace Stallions.Client.Services;
public class ListingApiService { private readonly HttpClient _http; public ListingApiService(HttpClient http) => _http = http; }
```

Repeat this one-liner pattern for `StallionApiService`, `BidApiService`, `CheckoutApiService`, `EnquiryApiService`, `UserApiService`. Each file is just a class with an `HttpClient` constructor. The full implementations come in Plan 3b Task 7.

- [ ] **Step 5: Rewrite `App.razor`**

```razor
@using Microsoft.AspNetCore.Components.Authorization
@using Microsoft.AspNetCore.Components.WebAssembly.Authentication

<CascadingAuthenticationState>
    <Router AppAssembly="@typeof(App).Assembly">
        <Found Context="routeData">
            <AuthorizeRouteView RouteData="@routeData" DefaultLayout="@typeof(Layout.MainLayout)">
                <NotAuthorized>
                    @if (context.User.Identity?.IsAuthenticated != true)
                    {
                        <RedirectToLogin />
                    }
                    else
                    {
                        <p class="auth-error">You are not authorised to view this page.</p>
                    }
                </NotAuthorized>
            </AuthorizeRouteView>
            <FocusOnNavigate RouteData="@routeData" Selector="h1" />
        </Found>
        <NotFound>
            <PageTitle>Page not found — Stallions Nominations</PageTitle>
            <LayoutView Layout="@typeof(Layout.MainLayout)">
                <p role="alert">Sorry, there's nothing at this address. <a href="/">Browse listings</a></p>
            </LayoutView>
        </NotFound>
    </Router>
</CascadingAuthenticationState>
```

- [ ] **Step 6: Create `RedirectToLogin.razor`**

Create `src/Client/Shared/RedirectToLogin.razor`:

```razor
@inject NavigationManager Navigation
@using Microsoft.AspNetCore.Components.WebAssembly.Authentication

@code {
    protected override void OnInitialized()
    {
        Navigation.NavigateToLogin("authentication/login");
    }
}
```

- [ ] **Step 7: Create `Authentication.razor`**

Create `src/Client/Pages/Authentication.razor`:

```razor
@page "/authentication/{action}"
@using Microsoft.AspNetCore.Components.WebAssembly.Authentication

<RemoteAuthenticatorView Action="@Action" />

@code {
    [Parameter]
    public string? Action { get; set; }
}
```

- [ ] **Step 8: Update `_Imports.razor`**

```razor
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Authorization
@using Microsoft.AspNetCore.Components.Authorization
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.AspNetCore.Components.WebAssembly.Authentication
@using Microsoft.AspNetCore.Components.WebAssembly.Http
@using Microsoft.JSInterop
@using Stallions.Client
@using Stallions.Client.Layout
@using Stallions.Client.Services
@using Stallions.Shared.DTOs.Listings
@using Stallions.Shared.DTOs.StudFarms
@using Stallions.Shared.DTOs.Stallions
@using Stallions.Shared.DTOs.Bids
@using Stallions.Shared.DTOs.Checkout
@using Stallions.Shared.DTOs.Enquiries
@using Stallions.Shared.DTOs.Users
```

- [ ] **Step 9: Update `index.html`**

Remove the Bootstrap CSS link. Add MSAL auth script and link to our custom CSS. `Stallions.Client.styles.css` (Blazor's scoped CSS bundle) stays in place.

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Stallions Nominations Marketplace</title>
    <base href="/" />
    <link rel="stylesheet" href="css/app.css" />
    <link rel="stylesheet" href="css/utilities.css" />
    <link rel="icon" type="image/png" href="favicon.png" />
    <link href="Stallions.Client.styles.css" rel="stylesheet" />
</head>
<body>
    <div id="app">
        <div class="app-loading">
            <svg class="loading-progress">
                <circle r="40%" cx="50%" cy="50%" />
                <circle r="40%" cx="50%" cy="50%" />
            </svg>
        </div>
    </div>
    <div id="blazor-error-ui">
        An unhandled error has occurred.
        <a href="." class="reload">Reload</a>
        <span class="dismiss">🗙</span>
    </div>
    <script src="_content/Microsoft.Authentication.WebAssembly.Msal/AuthenticationService.js"></script>
    <script src="_framework/blazor.webassembly.js"></script>
</body>
</html>
```

- [ ] **Step 10: Create `appsettings.json`**

Create `src/Client/wwwroot/appsettings.json`:

```json
{
  "AzureAd": {
    "Authority": "https://login.microsoftonline.com/YOUR_TENANT_ID",
    "ClientId": "YOUR_CLIENT_APP_ID",
    "ValidateAuthority": true
  },
  "ApiScope": "api://YOUR_API_APP_ID/API.Access",
  "ApiBaseUrl": "https://localhost:7001"
}
```

Replace the `YOUR_*` placeholders with the values from the Entra ID app registrations documented in `docs/superpowers/specs/` (Plan 1 Azure infra spec).

- [ ] **Step 11: Run the smoke test — expect green**

```
dotnet test tests/Client.Tests --filter "AppRender" -v minimal
```

Expected: 1 test PASS.

- [ ] **Step 12: Build client**

```
dotnet build src/Client -v minimal
```

Expected: 0 errors.

- [ ] **Step 13: Commit**

```
git add src/Client/Program.cs \
        src/Client/App.razor \
        src/Client/_Imports.razor \
        src/Client/wwwroot/index.html \
        src/Client/wwwroot/appsettings.json \
        src/Client/Pages/Authentication.razor \
        src/Client/Shared/RedirectToLogin.razor \
        src/Client/Services/
git commit -m "feat: wire up MSAL auth, typed API service stubs, update App.razor and index.html"
```

---

### Task 4: CSS design token system

**Why:** Every component in Plans 3b and 3c uses these tokens. Getting them right now means all future styles are consistent from the first line.

**Files:**
- Create: `src/Client/wwwroot/css/app.css`
- Create: `src/Client/wwwroot/css/utilities.css`

No unit tests for CSS — visual correctness is verified by running the app in a browser (after pages are built in Plan 3c). Build must succeed.

---

- [ ] **Step 1: Create `app.css`**

Create `src/Client/wwwroot/css/app.css`:

```css
/* ============================================================
   DESIGN TOKENS
   ============================================================ */
:root {
  /* Brand */
  --navy:        #22456d;
  --navy-dark:   #1a344f;
  --navy-light:  #2d5a8e;
  --gold:        #c4993a;
  --gold-light:  #e2b84a;

  /* Surface */
  --cream:       #faf8f5;
  --warm-grey:   #f0ece6;
  --border:      #e8e1d8;
  --white:       #ffffff;

  /* Text */
  --text-primary:   #1c1c1e;
  --text-secondary: #555555;
  --text-muted:     #888888;

  /* Semantic */
  --danger:   #c0392b;
  --success:  #1a7a4a;
  --warning:  var(--gold);

  /* Spacing (4px base) */
  --space-1:  .25rem;
  --space-2:  .5rem;
  --space-3:  .75rem;
  --space-4:  1rem;
  --space-6:  1.5rem;
  --space-8:  2rem;
  --space-12: 3rem;
  --space-16: 4rem;

  /* Radii */
  --radius-sm:   6px;
  --radius-md:   10px;
  --radius-lg:   14px;
  --radius-pill: 99px;

  /* Shadows */
  --shadow-card:  0 1px 3px rgba(0,0,0,.08), 0 4px 16px rgba(34,69,109,.07);
  --shadow-hover: 0 8px 32px rgba(34,69,109,.14);
  --shadow-modal: 0 24px 64px rgba(0,0,0,.25);

  /* Typography */
  --font-sans: -apple-system, BlinkMacSystemFont, 'Segoe UI', system-ui, sans-serif;
  --font-size-xs:  .75rem;
  --font-size-sm:  .875rem;
  --font-size-md:  1rem;
  --font-size-lg:  1.125rem;
  --font-size-xl:  1.25rem;
  --font-size-2xl: 1.5rem;
  --font-size-3xl: 1.875rem;
  --font-size-4xl: 2.25rem;
  --font-weight-normal: 400;
  --font-weight-medium: 500;
  --font-weight-semibold: 600;
  --font-weight-bold: 700;

  /* Layout */
  --content-max: 1280px;
  --nav-height: 64px;
}

/* ============================================================
   RESET & BASE
   ============================================================ */
*, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }

html {
  font-size: 16px;
  scroll-behavior: smooth;
}

body {
  font-family: var(--font-sans);
  font-size: var(--font-size-md);
  color: var(--text-primary);
  background: var(--cream);
  line-height: 1.5;
  -webkit-font-smoothing: antialiased;
}

img { display: block; max-width: 100%; }
a   { color: var(--navy); }
a:hover { color: var(--navy-dark); }

/* ============================================================
   GLOBAL LAYOUT
   ============================================================ */
.page-wrapper {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
}

.content-area {
  flex: 1;
  padding-top: var(--nav-height);
}

.container {
  width: 100%;
  max-width: var(--content-max);
  margin-inline: auto;
  padding-inline: var(--space-4);
}

/* ============================================================
   BUTTONS
   ============================================================ */
.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--space-2);
  padding: var(--space-3) var(--space-6);
  border: none;
  border-radius: var(--radius-md);
  font-family: var(--font-sans);
  font-size: var(--font-size-sm);
  font-weight: var(--font-weight-semibold);
  cursor: pointer;
  text-decoration: none;
  transition: background .15s, transform .1s, box-shadow .15s;
  white-space: nowrap;
}
.btn:disabled { opacity: .55; cursor: not-allowed; }

.btn-primary {
  background: var(--navy);
  color: var(--white);
}
.btn-primary:hover:not(:disabled) {
  background: var(--navy-dark);
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(34,69,109,.25);
}

.btn-gold {
  background: var(--gold);
  color: var(--white);
}
.btn-gold:hover:not(:disabled) {
  background: var(--gold-light);
  transform: translateY(-1px);
}

.btn-outline {
  background: transparent;
  color: var(--navy);
  border: 1.5px solid var(--navy);
}
.btn-outline:hover:not(:disabled) {
  background: var(--navy);
  color: var(--white);
}

.btn-sm { padding: var(--space-2) var(--space-4); font-size: var(--font-size-xs); }
.btn-full { width: 100%; }

/* ============================================================
   FORMS
   ============================================================ */
.form-group  { display: flex; flex-direction: column; gap: var(--space-2); }
.form-label  { font-size: var(--font-size-sm); font-weight: var(--font-weight-medium); color: var(--text-secondary); }
.form-input,
.form-select,
.form-textarea {
  width: 100%;
  padding: var(--space-3) var(--space-4);
  border: 1.5px solid var(--border);
  border-radius: var(--radius-md);
  font-family: var(--font-sans);
  font-size: var(--font-size-md);
  background: var(--white);
  color: var(--text-primary);
  transition: border-color .15s, box-shadow .15s;
}
.form-input:focus, .form-select:focus, .form-textarea:focus {
  outline: none;
  border-color: var(--navy);
  box-shadow: 0 0 0 3px rgba(34,69,109,.12);
}
.form-textarea { resize: vertical; min-height: 100px; }

/* Blazor validation */
.valid.modified:not([type=checkbox]) { border-color: var(--success); }
.invalid { border-color: var(--danger); }
.validation-message { font-size: var(--font-size-xs); color: var(--danger); }

/* ============================================================
   BADGES / PILLS
   ============================================================ */
.badge {
  display: inline-flex;
  align-items: center;
  padding: var(--space-1) var(--space-3);
  border-radius: var(--radius-pill);
  font-size: var(--font-size-xs);
  font-weight: var(--font-weight-semibold);
  text-transform: uppercase;
  letter-spacing: .04em;
}
.badge-auction  { background: var(--gold);    color: var(--white); }
.badge-buynow   { background: var(--navy);    color: var(--white); }
.badge-ending   { background: var(--danger);  color: var(--white); }
.badge-leading  { background: var(--success); color: var(--white); }
.badge-outbid   { background: var(--gold);    color: var(--white); }
.badge-won      { background: var(--success); color: var(--white); }
.badge-lost     { background: var(--text-muted); color: var(--white); }

/* ============================================================
   PROGRESS BAR (3px — listing cards)
   ============================================================ */
.progress-bar-track {
  height: 3px;
  background: var(--warm-grey);
  border-radius: var(--radius-pill);
  overflow: hidden;
}
.progress-bar-fill {
  height: 100%;
  border-radius: var(--radius-pill);
  transition: width .3s;
}
.progress-bar-fill--gold  { background: var(--gold); }
.progress-bar-fill--navy  { background: var(--navy); }
.progress-bar-fill--urgent { background: var(--danger); }

/* ============================================================
   BLAZOR ERROR UI
   ============================================================ */
#blazor-error-ui {
  background: var(--danger);
  color: var(--white);
  padding: var(--space-3) var(--space-4);
  position: fixed;
  bottom: 0;
  left: 0;
  right: 0;
  display: none;
  z-index: 9999;
  font-size: var(--font-size-sm);
}
#blazor-error-ui.blazor-error-ui-show { display: flex; align-items: center; gap: var(--space-4); }
#blazor-error-ui .reload { color: var(--white); font-weight: var(--font-weight-semibold); }
#blazor-error-ui .dismiss { cursor: pointer; margin-left: auto; }

/* ============================================================
   RESPONSIVE BREAKPOINTS
   Note: component-level @media rules that don't work inside
   Blazor's ::deep go here rather than in .razor.css files.
   ============================================================ */

/* Base: mobile ≤ 639px — single column, full-width */
@media (min-width: 640px) {
  .container { padding-inline: var(--space-6); }
}

@media (min-width: 1024px) {
  .container { padding-inline: var(--space-8); }
}

@media (min-width: 1280px) {
  .container { padding-inline: var(--space-12); }
}
```

- [ ] **Step 2: Create `utilities.css`**

Create `src/Client/wwwroot/css/utilities.css`:

```css
/* Screen-reader only — visually hidden but available to assistive tech */
.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0,0,0,0);
  white-space: nowrap;
  border-width: 0;
}

/* Single-line truncation with ellipsis */
.truncate {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* Two-line clamp */
.clamp-2 {
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

/* Flex helpers */
.flex         { display: flex; }
.flex-col     { display: flex; flex-direction: column; }
.items-center { align-items: center; }
.justify-between { justify-content: space-between; }
.gap-2        { gap: var(--space-2); }
.gap-4        { gap: var(--space-4); }

/* Text colour shortcuts */
.text-muted     { color: var(--text-muted); }
.text-secondary { color: var(--text-secondary); }
.text-danger    { color: var(--danger); }
.text-success   { color: var(--success); }

/* Font size shortcuts */
.text-sm  { font-size: var(--font-size-sm); }
.text-xs  { font-size: var(--font-size-xs); }
.text-lg  { font-size: var(--font-size-lg); }

/* Font weight shortcuts */
.font-medium   { font-weight: var(--font-weight-medium); }
.font-semibold { font-weight: var(--font-weight-semibold); }
.font-bold     { font-weight: var(--font-weight-bold); }

/* Spacing helpers */
.mt-4  { margin-top: var(--space-4); }
.mt-8  { margin-top: var(--space-8); }
.mb-4  { margin-bottom: var(--space-4); }
.mb-8  { margin-bottom: var(--space-8); }
```

- [ ] **Step 3: Build client to confirm CSS files are picked up**

```
dotnet build src/Client -v minimal
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add src/Client/wwwroot/css/
git commit -m "feat: add CSS design token system (app.css + utilities.css)"
```
