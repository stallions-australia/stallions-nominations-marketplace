# Blazor WebAssembly Frontend — Plan 3c: Pages and Seed Data

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build all 9 pages of the public and buyer journey, plus the development seed data. At the end of this plan the application is functionally complete for Plan 3 scope: public browsing, listing detail, buyer checkout, and the authenticated buyer account pages.

**Prerequisite:** Plans 3a (foundation) and 3b (components) must be complete.

**Architecture:** Seven tasks in page-dependency order — public browse pages first, then authenticated buyer pages, then seed data last. Every page is mobile-first. Auth-required pages use `[Authorize]`. API calls go through the typed services from Plan 3b.

---

### Task 9: Home page

**Why:** The entry point. Combines the hero section, `FilterBar`, and `ListingGrid`. Tests verify that grid renders loaded listings and empty state renders when none are returned.

**Files:**
- Create: `src/Client/Pages/Home.razor` + `Home.razor.css`
- Test: `tests/Client.Tests/Pages/HomeTests.cs`

---

- [ ] **Step 1: Write failing tests**

Create `tests/Client.Tests/Pages/HomeTests.cs`:

```csharp
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Listings;

namespace Stallions.Client.Tests.Pages;

public class HomeTests : TestContext
{
    private Mock<ListingApiService> SetupService(List<ListingCardDto> cards)
    {
        var mock = new Mock<ListingApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        mock.Setup(s => s.GetListingsAsync(null, null, null)).ReturnsAsync(cards);
        Services.AddSingleton(mock.Object);
        return mock;
    }

    [Fact]
    public async Task Home_WhenListingsLoaded_RendersCards()
    {
        this.AddTestAuthorization();
        var cards = new List<ListingCardDto>
        {
            new() { Id = Guid.NewGuid(), StallionName = "Fastnet Rock", ListingType = "FixedPrice", PriceIncGst = 8000m }
        };
        SetupService(cards);

        var cut = RenderComponent<Home>();
        await Task.Delay(50); // allow async OnInitialized to complete
        cut.Render();

        cut.Markup.Should().Contain("Fastnet Rock");
    }

    [Fact]
    public async Task Home_WhenNoListings_RendersEmptyState()
    {
        this.AddTestAuthorization();
        SetupService(new List<ListingCardDto>());

        var cut = RenderComponent<Home>();
        await Task.Delay(50);
        cut.Render();

        cut.Find(".empty-state").Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test tests/Client.Tests --filter "HomeTests" -v minimal
```

Expected: Build error — `Home` page component does not exist.

- [ ] **Step 3: Create `Home.razor`**

Create `src/Client/Pages/Home.razor`:

```razor
@page "/"
@inject ListingApiService ListingApi

<PageTitle>Browse Nominations — Stallions Australia</PageTitle>

<!-- Hero -->
<section class="hero">
    <div class="container">
        <p class="hero-eyebrow">2025 Breeding Season</p>
        <h1 class="hero-headline">Find the Perfect Stallion Nomination</h1>
        <p class="hero-sub">Browse nominations from Australia's leading stud farms. Bid at auction or buy now.</p>

        <div class="hero-search">
            <input class="form-input hero-input" type="search"
                   placeholder="Search by stallion or stud farm…"
                   @bind="_search" @bind:event="oninput" @onkeyup="HandleSearchKey" />
            <button class="btn btn-gold" @onclick="Load">Search</button>
        </div>

        <div class="hero-stats">
            <div class="hero-stat">
                <span class="hero-stat-num">@_listings?.Count</span>
                <span class="hero-stat-label">active listings</span>
            </div>
            <div class="hero-stat">
                <span class="hero-stat-num">@_listings?.Count(l => l.ListingType == "Auction")</span>
                <span class="hero-stat-label">live auctions</span>
            </div>
        </div>
    </div>
</section>

<!-- Filter bar -->
<FilterBar ActiveFilter="@_typeFilter" OnFilterChanged="OnFilterChanged" />

<!-- Results -->
<div class="container results-section">
    @if (_error is not null)
    {
        <ErrorMessage Message="@_error" OnRetry="Load" />
    }
    else
    {
        <div class="results-header">
            @if (_listings is not null)
            {
                <span class="results-count">@_filteredListings.Count nomination@(_filteredListings.Count != 1 ? "s" : "")</span>
            }
        </div>
        <ListingGrid Listings="@(_listings is null ? null : _filteredListings)" />
    }
</div>

@code {
    private List<ListingCardDto>? _listings;
    private string? _error;
    private string? _typeFilter;
    private string? _search;

    private IReadOnlyList<ListingCardDto> _filteredListings =>
        _listings?.Where(l =>
            (string.IsNullOrEmpty(_search) ||
             l.StallionName.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
             l.StudFarmName.Contains(_search, StringComparison.OrdinalIgnoreCase))
        ).ToList() ?? new List<ListingCardDto>();

    protected override async Task OnInitializedAsync() => await Load();

    private async Task Load()
    {
        _error = null;
        _listings = null;
        try
        {
            _listings = await ListingApi.GetListingsAsync(type: _typeFilter);
        }
        catch (ApiException ex)
        {
            _error = ex.StatusCode == 503
                ? "We're having trouble connecting. Please try again."
                : "Failed to load listings. Please try again.";
        }
    }

    private async Task OnFilterChanged(string? filter)
    {
        _typeFilter = filter;
        await Load();
    }

    private async Task HandleSearchKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await Load();
    }
}
```

Create `src/Client/Pages/Home.razor.css`:

```css
/* Hero */
.hero {
    background: var(--navy-dark);
    color: var(--white);
    padding: var(--space-16) 0 var(--space-12);
}
.hero-eyebrow   { font-size: var(--font-size-sm); color: var(--gold); text-transform: uppercase; letter-spacing: .08em; margin-bottom: var(--space-4); }
.hero-headline  { font-size: var(--font-size-3xl); font-weight: var(--font-weight-bold); line-height: 1.15; margin-bottom: var(--space-4); }
.hero-sub       { font-size: var(--font-size-lg); color: rgba(255,255,255,.75); margin-bottom: var(--space-8); max-width: 560px; }
.hero-search    { display: flex; gap: var(--space-3); margin-bottom: var(--space-8); }
.hero-input     { flex: 1; }
.hero-stats     { display: flex; gap: var(--space-8); }
.hero-stat      { display: flex; flex-direction: column; }
.hero-stat-num  { font-size: var(--font-size-2xl); font-weight: var(--font-weight-bold); color: var(--gold); }
.hero-stat-label { font-size: var(--font-size-xs); color: rgba(255,255,255,.6); text-transform: uppercase; letter-spacing: .06em; }

/* Results */
.results-section { padding-block: var(--space-8); }
.results-header  { display: flex; align-items: center; margin-bottom: var(--space-6); }
.results-count   { font-size: var(--font-size-sm); color: var(--text-muted); }

@media (min-width: 640px) {
    .hero-headline { font-size: var(--font-size-4xl); }
}
```

- [ ] **Step 4: Run tests — expect green**

```
dotnet test tests/Client.Tests --filter "HomeTests" -v minimal
```

Expected: 2 tests PASS.

- [ ] **Step 5: Commit**

```
git add src/Client/Pages/Home.razor \
        src/Client/Pages/Home.razor.css \
        tests/Client.Tests/Pages/HomeTests.cs
git commit -m "feat: add Home page (hero, filter bar, listing grid)"
```

---

### Task 10: Listing detail page

**Why:** The most complex page — handles both auction and fixed-price listing types, bid placement, and unauthenticated state (sign-in prompts). Tests verify the type-specific CTA is shown and unauthenticated users see the sign-in prompt.

**Files:**
- Create: `src/Client/Pages/ListingDetail.razor` + `ListingDetail.razor.css`
- Test: `tests/Client.Tests/Pages/ListingDetailTests.cs`

---

- [ ] **Step 1: Write failing tests**

Create `tests/Client.Tests/Pages/ListingDetailTests.cs`:

```csharp
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Listings;

namespace Stallions.Client.Tests.Pages;

public class ListingDetailTests : TestContext
{
    private void RegisterServices(ListingDto listing, CurrentBidDto? currentBid = null)
    {
        var listingMock = new Mock<ListingApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        listingMock.Setup(s => s.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        Services.AddSingleton(listingMock.Object);

        var bidMock = new Mock<BidApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        bidMock.Setup(s => s.GetCurrentBidAsync(listing.Id)).ReturnsAsync(currentBid);
        Services.AddSingleton(bidMock.Object);
    }

    [Fact]
    public async Task ListingDetail_FixedPrice_ShowsPurchaseButton()
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com", "Buyer");
        var listing = new FixedPriceListingDto
        {
            Id = Guid.NewGuid(), StallionName = "Fastnet Rock",
            ListingType = "FixedPrice", Status = "Active",
            PriceIncGst = 8000m, QuantityRemaining = 3, Quantity = 10
        };
        RegisterServices(listing);

        var cut = RenderComponent<ListingDetail>(p =>
            p.Add(nameof(ListingDetail.Id), listing.Id));
        await Task.Delay(50); cut.Render();

        cut.Find("a[href*='checkout']").Should().NotBeNull();
    }

    [Fact]
    public async Task ListingDetail_Auction_Unauthenticated_ShowsSignInPrompt()
    {
        this.AddTestAuthorization(); // anonymous
        var listing = new AuctionListingDto
        {
            Id = Guid.NewGuid(), StallionName = "Snitzel",
            ListingType = "Auction", Status = "Active",
            StartingPrice = 5000m,
            EndDateTime = DateTime.UtcNow.AddDays(3)
        };
        RegisterServices(listing);

        var cut = RenderComponent<ListingDetail>(p =>
            p.Add(nameof(ListingDetail.Id), listing.Id));
        await Task.Delay(50); cut.Render();

        cut.Markup.Should().Contain("Sign in to bid");
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test tests/Client.Tests --filter "ListingDetail" -v minimal
```

Expected: Build error — `ListingDetail` does not exist.

- [ ] **Step 3: Create `ListingDetail.razor`**

Create `src/Client/Pages/ListingDetail.razor`:

```razor
@page "/listings/{Id:guid}"
@inject ListingApiService ListingApi
@inject BidApiService BidApi
@inject NavigationManager Nav

<PageTitle>@(_listing?.StallionName ?? "Listing") — Stallions Australia</PageTitle>

@if (_error is not null)
{
    <div class="container" style="padding-top: var(--space-12)">
        <ErrorMessage Message="@_error" OnRetry="Load" />
    </div>
}
else if (_listing is null)
{
    <LoadingSpinner Large="true" />
}
else
{
    <!-- Image header -->
    <div class="listing-hero">
        @if (!string.IsNullOrEmpty(_primaryImage))
        {
            <img src="@_primaryImage" alt="@_listing.StallionName" class="listing-hero-img" />
        }
        else
        {
            <div class="listing-hero-placeholder"></div>
        }
        <div class="listing-hero-overlay"></div>
        <div class="listing-hero-text container">
            <span class="badge @(_listing.ListingType == "Auction" ? "badge-auction" : "badge-buynow")">
                @_listing.ListingType
            </span>
            <h1 class="listing-hero-name">@_listing.StallionName</h1>
            <p class="listing-hero-farm">
                <a href="/studfarms/@_listing.StudFarmId">@_listing.StudFarmName</a>
            </p>
        </div>
    </div>

    <div class="container listing-body">
        <div class="listing-main">

            <!-- Price block -->
            <div class="listing-price-block">
                <PriceDisplay Amount="@GetDisplayPrice()" Large="true" />
                <span class="listing-gst-label">inc. GST</span>
                @if (_listing is AuctionListingDto al && al.IsNoReserve)
                {
                    <span class="badge badge-leading" style="margin-left: var(--space-3)">No reserve</span>
                }
            </div>

            <!-- Type-specific CTA -->
            @if (_listing is AuctionListingDto auction)
            {
                <div class="listing-auction-block">
                    <div class="auction-meta">
                        <span>@(_currentBid?.BidCount ?? 0) bid@((_currentBid?.BidCount ?? 0) != 1 ? "s" : "")</span>
                        <AuctionTimer ClosesAt="@auction.EndDateTime" />
                        @if (!auction.IsNoReserve)
                        {
                            <span class="@(ReserveMet ? "text-success" : "text-muted")">
                                @(ReserveMet ? "Reserve met" : "Reserve not met")
                            </span>
                        }
                    </div>

                    <AuthorizeView>
                        <Authorized>
                            <div class="bid-form">
                                <p class="bid-form-hint">
                                    Current: <strong>@FormatPrice(GetDisplayPrice())</strong>
                                    · Min next bid: <strong>@FormatPrice(GetDisplayPrice() + auction.MinimumBidIncrement)</strong>
                                </p>
                                <div class="bid-form-row">
                                    <input class="form-input" type="number" step="25" min="@(GetDisplayPrice() + auction.MinimumBidIncrement)"
                                           @bind="_bidAmount" placeholder="Your bid (inc. GST)" />
                                    <button class="btn btn-gold" @onclick="PlaceBid" disabled="@_bidBusy">
                                        @(_bidBusy ? "Placing…" : "Place bid")
                                    </button>
                                </div>
                                @if (_bidError is not null)
                                {
                                    <ErrorMessage Message="@_bidError" />
                                }
                                @if (_bidSuccess)
                                {
                                    <p class="text-success">✓ Bid placed successfully!</p>
                                }
                            </div>
                        </Authorized>
                        <NotAuthorized>
                            <a href="authentication/login" class="btn btn-gold btn-full">Sign in to bid</a>
                        </NotAuthorized>
                    </AuthorizeView>
                </div>
            }

            @if (_listing is FixedPriceListingDto fp)
            {
                <div class="listing-fp-block">
                    <p class="listing-qty-label">@fp.QuantityRemaining nominations remaining</p>
                    <AuthorizeView Roles="Buyer">
                        <Authorized>
                            <a href="/checkout/@_listing.Id" class="btn btn-gold btn-full">Purchase nomination</a>
                        </Authorized>
                        <NotAuthorized>
                            <a href="authentication/login" class="btn btn-gold btn-full">Sign in to purchase</a>
                        </NotAuthorized>
                    </AuthorizeView>
                </div>
            }

            <!-- Enquire CTA -->
            <div class="listing-enquire">
                <AuthorizeView>
                    <Authorized>
                        <button class="btn btn-outline btn-full" @onclick="OpenEnquiry">
                            Enquire about this listing
                        </button>
                    </Authorized>
                    <NotAuthorized>
                        <a href="authentication/login" class="btn btn-outline btn-full">Sign in to enquire</a>
                    </NotAuthorized>
                </AuthorizeView>
            </div>
        </div>

        <!-- Sidebar: stallion link -->
        <aside class="listing-sidebar">
            <h3>Stallion</h3>
            <a href="/stallions/@_listing.StallionId">View @_listing.StallionName profile →</a>
        </aside>
    </div>
}

@code {
    [Parameter] public Guid Id { get; set; }

    private ListingDto? _listing;
    private CurrentBidDto? _currentBid;
    private string? _error;
    private string? _primaryImage;

    // Bid form state
    private decimal _bidAmount;
    private bool _bidBusy;
    private string? _bidError;
    private bool _bidSuccess;

    private bool ReserveMet =>
        _listing is AuctionListingDto al &&
        _currentBid?.CurrentHighestBidIncGst >= (al.ReservePrice ?? decimal.MaxValue);

    protected override async Task OnInitializedAsync() => await Load();

    private async Task Load()
    {
        _error = null;
        _listing = null;
        try
        {
            _listing = await ListingApi.GetByIdAsync(Id);
            _primaryImage = null; // Primary image path not on ListingDto — load separately via stallion endpoint if needed
            if (_listing is AuctionListingDto)
                _currentBid = await BidApi.GetCurrentBidAsync(Id);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _error = "This listing could not be found. It may have been removed.";
        }
        catch (ApiException)
        {
            _error = "Failed to load listing. Please try again.";
        }
    }

    private decimal GetDisplayPrice() =>
        _currentBid?.CurrentHighestBidIncGst ??
        (_listing is AuctionListingDto al ? al.StartingPrice :
         _listing is FixedPriceListingDto fp ? fp.PriceIncGst : 0m);

    private static string FormatPrice(decimal amount) =>
        amount.ToString("C0", System.Globalization.CultureInfo.GetCultureInfo("en-AU"));

    private async Task PlaceBid()
    {
        _bidError = null;
        _bidSuccess = false;
        _bidBusy = true;
        try
        {
            await BidApi.PlaceBidAsync(Id, new Shared.DTOs.Bids.PlaceBidRequest { AmountIncGst = _bidAmount });
            _bidSuccess = true;
            _currentBid = await BidApi.GetCurrentBidAsync(Id);
        }
        catch (ApiException ex)
        {
            _bidError = ex.Message;
        }
        finally
        {
            _bidBusy = false;
        }
    }

    private void OpenEnquiry() => Nav.NavigateTo($"/listings/{Id}?enquire=1");
}
```

Create `src/Client/Pages/ListingDetail.razor.css`:

```css
.listing-hero {
    position: relative;
    height: 300px;
    overflow: hidden;
    background: var(--navy-dark);
}
.listing-hero-img { width: 100%; height: 100%; object-fit: cover; }
.listing-hero-placeholder { width: 100%; height: 100%; background: linear-gradient(135deg, var(--navy) 0%, var(--navy-dark) 100%); }
.listing-hero-overlay { position: absolute; inset: 0; background: linear-gradient(to top, rgba(0,0,0,.6), transparent 50%); }
.listing-hero-text { position: absolute; bottom: var(--space-6); left: 50%; transform: translateX(-50%); width: 100%; }
.listing-hero-name { font-size: var(--font-size-3xl); font-weight: var(--font-weight-bold); color: var(--white); margin-top: var(--space-2); }
.listing-hero-farm a { color: rgba(255,255,255,.8); font-size: var(--font-size-md); text-decoration: none; }

.listing-body { display: grid; gap: var(--space-8); padding-block: var(--space-8); }
.listing-price-block { margin-bottom: var(--space-4); display: flex; align-items: baseline; gap: var(--space-2); flex-wrap: wrap; }
.listing-gst-label { font-size: var(--font-size-sm); color: var(--text-muted); }

.listing-auction-block, .listing-fp-block { display: flex; flex-direction: column; gap: var(--space-4); }
.auction-meta { display: flex; gap: var(--space-6); align-items: center; flex-wrap: wrap; font-size: var(--font-size-sm); color: var(--text-secondary); }
.bid-form { background: var(--warm-grey); border-radius: var(--radius-md); padding: var(--space-6); display: flex; flex-direction: column; gap: var(--space-3); }
.bid-form-hint { font-size: var(--font-size-sm); color: var(--text-secondary); }
.bid-form-row { display: flex; gap: var(--space-3); }
.bid-form-row .form-input { flex: 1; }

.listing-qty-label { font-size: var(--font-size-sm); color: var(--text-secondary); }
.listing-enquire { margin-top: var(--space-4); }

@media (min-width: 768px) {
    .listing-hero { height: 500px; }
    .listing-body { grid-template-columns: 1fr 300px; }
}
```

- [ ] **Step 4: Run tests — expect green**

```
dotnet test tests/Client.Tests --filter "ListingDetail" -v minimal
```

Expected: 2 tests PASS.

- [ ] **Step 5: Commit**

```
git add src/Client/Pages/ListingDetail.razor \
        src/Client/Pages/ListingDetail.razor.css \
        tests/Client.Tests/Pages/ListingDetailTests.cs
git commit -m "feat: add ListingDetail page (auction bid form, fixed-price CTA, auth-aware state)"
```

---

### Task 11: StallionDetail and StudFarmDetail pages

**Why:** Two simple public read-only pages. Both load an entity and show a listing grid below it. Tested together because they follow the same pattern.

**Files:**
- Create: `src/Client/Pages/StallionDetail.razor` + `StallionDetail.razor.css`
- Create: `src/Client/Pages/StudFarmDetail.razor` + `StudFarmDetail.razor.css`
- Test: `tests/Client.Tests/Pages/StallionDetailTests.cs`

---

- [ ] **Step 1: Write failing test**

Create `tests/Client.Tests/Pages/StallionDetailTests.cs`:

```csharp
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Stallions;

namespace Stallions.Client.Tests.Pages;

public class StallionDetailTests : TestContext
{
    [Fact]
    public async Task StallionDetail_ShowsStallionName()
    {
        this.AddTestAuthorization();
        var stallionId = Guid.NewGuid();
        var stallionMock = new Mock<StallionApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        stallionMock.Setup(s => s.GetByIdAsync(stallionId))
            .ReturnsAsync(new StallionDto { Id = stallionId, Name = "Fastnet Rock" });
        Services.AddSingleton(stallionMock.Object);

        var listingMock = new Mock<ListingApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        listingMock.Setup(s => s.GetListingsAsync(null, null, null))
            .ReturnsAsync(new List<Stallions.Shared.DTOs.Listings.ListingCardDto>());
        Services.AddSingleton(listingMock.Object);

        var cut = RenderComponent<StallionDetail>(p => p.Add(nameof(StallionDetail.Id), stallionId));
        await Task.Delay(50); cut.Render();

        cut.Markup.Should().Contain("Fastnet Rock");
    }
}
```

- [ ] **Step 2: Run test — expect compile failure**

```
dotnet test tests/Client.Tests --filter "StallionDetail" -v minimal
```

Expected: Build error.

- [ ] **Step 3: Create `StallionDetail.razor`**

Create `src/Client/Pages/StallionDetail.razor`:

```razor
@page "/stallions/{Id:guid}"
@inject StallionApiService StallionApi
@inject ListingApiService ListingApi

<PageTitle>@(_stallion?.Name ?? "Stallion") — Stallions Australia</PageTitle>

@if (_stallion is null && _error is null)
{
    <LoadingSpinner Large="true" />
}
else if (_error is not null)
{
    <div class="container" style="padding-top: var(--space-12)">
        <ErrorMessage Message="@_error" OnRetry="Load" />
    </div>
}
else if (_stallion is not null)
{
    <div class="profile-hero" style="background: var(--navy-dark)">
        @if (!string.IsNullOrEmpty(_stallion.PrimaryImagePath))
        {
            <img src="@_stallion.PrimaryImagePath" alt="@_stallion.Name" class="profile-hero-img" />
        }
        <div class="profile-hero-overlay"></div>
        <div class="container profile-hero-text">
            <h1>@_stallion.Name</h1>
            @if (!string.IsNullOrEmpty(_stallion.Breed))
            {
                <p>@_stallion.Breed</p>
            }
        </div>
    </div>

    <div class="container" style="padding-block: var(--space-12)">
        <h2 class="section-heading">Available Nominations</h2>
        <ListingGrid Listings="@_listings" />
    </div>
}

@code {
    [Parameter] public Guid Id { get; set; }
    private StallionDto? _stallion;
    private IReadOnlyList<Stallions.Shared.DTOs.Listings.ListingCardDto>? _listings;
    private string? _error;

    protected override async Task OnInitializedAsync() => await Load();

    private async Task Load()
    {
        _error = null;
        try
        {
            _stallion = await StallionApi.GetByIdAsync(Id);
            var all = await ListingApi.GetListingsAsync();
            _listings = all.Where(l => l.StallionId == Id).ToList();
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _error = "Stallion not found.";
        }
        catch (ApiException)
        {
            _error = "Failed to load stallion. Please try again.";
        }
    }
}
```

- [ ] **Step 4: Create `StudFarmDetail.razor`**

Create `src/Client/Pages/StudFarmDetail.razor`. First, add `StudFarmApiService` to the client (a simple one-method service not previously created — add it to `src/Client/Services/StudFarmApiService.cs` and register it in `Program.cs`):

`src/Client/Services/StudFarmApiService.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.StudFarms;

namespace Stallions.Client.Services;

public class StudFarmApiService
{
    private readonly HttpClient _http;
    public StudFarmApiService(HttpClient http) => _http = http;

    public async Task<StudFarmDto> GetByIdAsync(Guid id)
    {
        var response = await _http.GetAsync($"api/studfarms/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ApiException(404, "Stud farm not found.");
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load stud farm.");
        return await response.Content.ReadFromJsonAsync<StudFarmDto>()
               ?? throw new ApiException(500, "Empty response.");
    }
}
```

Register in `src/Client/Program.cs` (after the other `AddHttpClient` calls):

```csharp
builder.Services.AddHttpClient<StudFarmApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
```

Then create `src/Client/Pages/StudFarmDetail.razor`:

```razor
@page "/studfarms/{Id:guid}"
@inject StudFarmApiService StudFarmApi
@inject ListingApiService ListingApi

<PageTitle>@(_farm?.Name ?? "Stud Farm") — Stallions Australia</PageTitle>

@if (_farm is null && _error is null)
{
    <LoadingSpinner Large="true" />
}
else if (_error is not null)
{
    <div class="container" style="padding-top: var(--space-12)">
        <ErrorMessage Message="@_error" OnRetry="Load" />
    </div>
}
else if (_farm is not null)
{
    <div class="container" style="padding-block: var(--space-12)">
        <h1 class="page-title">@_farm.Name</h1>
        @if (!string.IsNullOrEmpty(_farm.Address))
        {
            <p class="text-secondary">📍 @_farm.Address</p>
        }
        @if (!string.IsNullOrEmpty(_farm.ContactEmail))
        {
            <p class="text-secondary" style="margin-top: var(--space-2)">
                <a href="mailto:@_farm.ContactEmail">@_farm.ContactEmail</a>
            </p>
        }

        <h2 class="section-heading" style="margin-top: var(--space-10)">Available Nominations</h2>
        <ListingGrid Listings="@_listings" />
    </div>
}

@code {
    [Parameter] public Guid Id { get; set; }
    private StudFarmDto? _farm;
    private IReadOnlyList<Stallions.Shared.DTOs.Listings.ListingCardDto>? _listings;
    private string? _error;

    protected override async Task OnInitializedAsync() => await Load();

    private async Task Load()
    {
        _error = null;
        try
        {
            _farm = await StudFarmApi.GetByIdAsync(Id);
            _listings = await ListingApi.GetListingsAsync(studFarmId: Id);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _error = "Stud farm not found.";
        }
        catch (ApiException)
        {
            _error = "Failed to load stud farm. Please try again.";
        }
    }
}
```

Add shared profile hero CSS to `app.css` (append):

```css
/* Shared profile hero (StallionDetail, StudFarmDetail) */
.profile-hero { position: relative; height: 240px; overflow: hidden; }
.profile-hero-img { width: 100%; height: 100%; object-fit: cover; }
.profile-hero-overlay { position: absolute; inset: 0; background: rgba(0,0,0,.45); }
.profile-hero-text { position: absolute; bottom: var(--space-6); left: 50%; transform: translateX(-50%); width: 100%; color: var(--white); }
.profile-hero-text h1 { font-size: var(--font-size-3xl); font-weight: var(--font-weight-bold); }
.section-heading { font-size: var(--font-size-xl); font-weight: var(--font-weight-semibold); margin-bottom: var(--space-6); }
.page-title { font-size: var(--font-size-3xl); font-weight: var(--font-weight-bold); margin-bottom: var(--space-4); }
```

- [ ] **Step 5: Run test — expect green**

```
dotnet test tests/Client.Tests --filter "StallionDetail" -v minimal
```

Expected: 1 test PASS.

- [ ] **Step 6: Build client**

```
dotnet build src/Client -v minimal
```

Expected: 0 errors.

- [ ] **Step 7: Commit**

```
git add src/Client/Pages/StallionDetail.razor \
        src/Client/Pages/StudFarmDetail.razor \
        src/Client/Services/StudFarmApiService.cs \
        src/Client/wwwroot/css/app.css \
        tests/Client.Tests/Pages/StallionDetailTests.cs
git commit -m "feat: add StallionDetail and StudFarmDetail public pages"
```

---

### Task 12: Checkout flow

**Why:** The most legally critical flow. Two steps — mare details form (creates the purchase record), then the mandatory buyer disclosure (displays fee breakdown and refund policy). The "Confirm" button must be inactive until the user scrolls the disclosure into view (achieved via checkbox acknowledgment, no JS interop). Buyer role required; stud farm admins cannot access.

**Files:**
- Create: `src/Client/Components/Checkout/MareDetailsForm.razor` + `.razor.css`
- Create: `src/Client/Components/Checkout/BuyerDisclosure.razor` + `.razor.css`
- Create: `src/Client/Pages/Checkout.razor` + `.razor.css`
- Test: `tests/Client.Tests/Components/Checkout/BuyerDisclosureTests.cs`
- Test: `tests/Client.Tests/Pages/CheckoutTests.cs`

---

- [ ] **Step 1: Write failing tests**

Create `tests/Client.Tests/Components/Checkout/BuyerDisclosureTests.cs`:

```csharp
using Bunit;
using FluentAssertions;
using Stallions.Client.Components.Checkout;

namespace Stallions.Client.Tests.Components.Checkout;

public class BuyerDisclosureTests : TestContext
{
    [Fact]
    public void BuyerDisclosure_ConfirmButtonDisabled_UntilCheckboxTicked()
    {
        var confirmed = false;
        var cut = RenderComponent<BuyerDisclosure>(p => p
            .Add(c => c.TotalPriceIncGst, 10000m)
            .Add(c => c.PlatformFeeIncGst, 250m)
            .Add(c => c.OnConfirmed, EventCallback.Factory.Create(this, () => confirmed = true)));

        // Confirm button should be disabled initially
        var confirmBtn = cut.Find("button.btn-gold");
        confirmBtn.HasAttribute("disabled").Should().BeTrue();

        // Tick the acknowledgment checkbox
        cut.Find("input[type='checkbox']").Change(true);

        // Now confirm button should be enabled
        confirmBtn = cut.Find("button.btn-gold");
        confirmBtn.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void BuyerDisclosure_DisplaysCorrectFeeAndBalance()
    {
        var cut = RenderComponent<BuyerDisclosure>(p => p
            .Add(c => c.TotalPriceIncGst, 10000m)
            .Add(c => c.PlatformFeeIncGst, 250m)
            .Add(c => c.OnConfirmed, EventCallback.Empty));

        // $10,000 - $250 = $9,750 stud farm balance
        cut.Markup.Should().Contain("10,000");
        cut.Markup.Should().Contain("250");
        cut.Markup.Should().Contain("9,750");
    }
}
```

Create `tests/Client.Tests/Pages/CheckoutTests.cs`:

```csharp
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Checkout;
using Stallions.Shared.DTOs.Listings;

namespace Stallions.Client.Tests.Pages;

public class CheckoutTests : TestContext
{
    [Fact]
    public void Checkout_Step1_ShowsMareDetailsForm()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("buyer@example.com", "Buyer");

        var listingMock = new Mock<ListingApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        listingMock.Setup(s => s.GetByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new FixedPriceListingDto
            {
                Id = Guid.NewGuid(), StallionName = "Fastnet Rock",
                ListingType = "FixedPrice", Status = "Active",
                PriceIncGst = 10000m, QuantityRemaining = 3, Quantity = 10
            });
        Services.AddSingleton(listingMock.Object);
        Services.AddSingleton(new Mock<CheckoutApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") }).Object);

        var cut = RenderComponent<Checkout>(p =>
            p.Add(nameof(Checkout.ListingId), Guid.NewGuid()));

        cut.Find("input[id='mare-name']").Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test tests/Client.Tests --filter "BuyerDisclosure|CheckoutTests" -v minimal
```

Expected: Build error.

- [ ] **Step 3: Create `MareDetailsForm.razor`**

Create `src/Client/Components/Checkout/MareDetailsForm.razor`:

```razor
<EditForm Model="@Model" OnValidSubmit="OnSubmit">
    <DataAnnotationsValidator />
    <div class="form-group">
        <label class="form-label" for="mare-name">Mare name <span aria-hidden="true">*</span></label>
        <InputText id="mare-name" class="form-input" @bind-Value="Model.MareName"
                   placeholder="e.g. Brilliant Star" />
        <ValidationMessage For="() => Model.MareName" class="validation-message" />
    </div>
    <div class="form-group" style="margin-top: var(--space-4)">
        <label class="form-label" for="mare-notes">Additional notes (optional)</label>
        <InputTextArea id="mare-notes" class="form-textarea" @bind-Value="Model.Notes"
                       placeholder="Any additional information about the mare…" />
    </div>
    <button type="submit" class="btn btn-primary btn-full" style="margin-top: var(--space-6)"
            disabled="@string.IsNullOrWhiteSpace(Model.MareName)">
        Continue to review
    </button>
</EditForm>

@code {
    [Parameter, EditorRequired] public CheckoutRequest Model { get; set; } = null!;
    [Parameter, EditorRequired] public EventCallback OnSubmit { get; set; }
}
```

- [ ] **Step 4: Create `BuyerDisclosure.razor`**

Create `src/Client/Components/Checkout/BuyerDisclosure.razor`:

```razor
@* TODO: Refund policy text is hardcoded here per the design spec.
         A later plan will make this API-driven from a CMS/config endpoint. *@

<div class="disclosure-panel">
    <h3 class="disclosure-heading">Purchase Summary</h3>

    <dl class="disclosure-table">
        <div class="disclosure-row">
            <dt>Total listing price</dt>
            <dd>@TotalPriceIncGst.ToString("C0", AuCulture) <span class="text-muted text-sm">inc. GST</span></dd>
        </div>
        <div class="disclosure-row disclosure-row--fee">
            <dt>Platform fee (Stallions Australia)</dt>
            <dd>@PlatformFeeIncGst.ToString("C0", AuCulture) <span class="text-muted text-sm">inc. GST</span></dd>
        </div>
        <div class="disclosure-row disclosure-row--balance">
            <dt>Balance invoiced by stud farm</dt>
            <dd>@StudFarmBalance.ToString("C0", AuCulture)</dd>
        </div>
    </dl>

    <div class="disclosure-notice">
        <p><strong>Important — please read carefully:</strong></p>
        <ul>
            <li>The platform fee shown above is collected by Stallions Australia at the time of this purchase.</li>
            <li>The stud farm will contact you separately to arrange payment of the remaining balance under their own terms and conditions.</li>
            <li>The balance arrangement is entirely between you and the stud farm. Stallions Australia is not a party to that arrangement.</li>
            <li><strong>Refund policy:</strong> If the stud farm arrangement does not proceed, Stallions Australia will refund 90% of the platform fee and retain 10% as an administration fee.</li>
        </ul>
    </div>

    <div class="disclosure-ack">
        <label class="ack-label">
            <input type="checkbox" @onchange="OnAckChanged" />
            <span>I have read and understood the above disclosure.</span>
        </label>
    </div>

    <button class="btn btn-gold btn-full" @onclick="OnConfirmed.InvokeAsync"
            disabled="@(!_acknowledged)" style="margin-top: var(--space-4)">
        Confirm purchase
    </button>
</div>

@code {
    [Parameter, EditorRequired] public decimal TotalPriceIncGst { get; set; }
    [Parameter, EditorRequired] public decimal PlatformFeeIncGst { get; set; }
    [Parameter, EditorRequired] public EventCallback OnConfirmed { get; set; }

    private bool _acknowledged;
    private decimal StudFarmBalance => TotalPriceIncGst - PlatformFeeIncGst;
    private static readonly System.Globalization.CultureInfo AuCulture =
        System.Globalization.CultureInfo.GetCultureInfo("en-AU");

    private void OnAckChanged(ChangeEventArgs e) =>
        _acknowledged = e.Value is bool b && b;
}
```

Create `src/Client/Components/Checkout/BuyerDisclosure.razor.css`:

```css
.disclosure-panel { background: var(--warm-grey); border-radius: var(--radius-lg); padding: var(--space-6); }
.disclosure-heading { font-size: var(--font-size-xl); font-weight: var(--font-weight-semibold); margin-bottom: var(--space-6); }
.disclosure-table { display: flex; flex-direction: column; gap: var(--space-3); margin-bottom: var(--space-6); }
.disclosure-row { display: flex; justify-content: space-between; align-items: baseline; padding-bottom: var(--space-3); border-bottom: 1px solid var(--border); }
.disclosure-row--fee dd { color: var(--navy); font-weight: var(--font-weight-semibold); }
.disclosure-row--balance { border-bottom: none; font-weight: var(--font-weight-semibold); }
.disclosure-notice { background: var(--white); border-radius: var(--radius-md); padding: var(--space-4); font-size: var(--font-size-sm); line-height: 1.6; margin-bottom: var(--space-6); }
.disclosure-notice ul { margin-top: var(--space-2); padding-left: var(--space-6); display: flex; flex-direction: column; gap: var(--space-2); }
.disclosure-ack { margin-bottom: var(--space-2); }
.ack-label { display: flex; align-items: flex-start; gap: var(--space-3); cursor: pointer; font-size: var(--font-size-sm); }
.ack-label input { margin-top: 3px; flex-shrink: 0; }
```

- [ ] **Step 5: Create `Checkout.razor`**

Create `src/Client/Pages/Checkout.razor`:

```razor
@page "/checkout/{ListingId:guid}"
@attribute [Authorize(Roles = "Buyer")]
@inject ListingApiService ListingApi
@inject CheckoutApiService CheckoutApi
@inject NavigationManager Nav

<PageTitle>Checkout — Stallions Australia</PageTitle>

<div class="container checkout-container">
    @if (_error is not null)
    {
        <ErrorMessage Message="@_error" />
    }
    else if (_listing is null)
    {
        <LoadingSpinner Large="true" />
    }
    else if (_step == CheckoutStep.Success)
    {
        <div class="checkout-success">
            <div class="success-icon">✓</div>
            <h2>Nomination secured!</h2>
            <p>Your nomination for <strong>@_listing.StallionName</strong> has been confirmed.</p>
            <p>The stud farm will be in touch to arrange the balance payment.</p>
            <a href="/my-purchases" class="btn btn-primary" style="margin-top: var(--space-6)">View my purchases</a>
        </div>
    }
    else
    {
        <div class="checkout-header">
            <h1>Purchase nomination</h1>
            <p class="text-secondary">@_listing.StallionName — @(_listing.StudFarmName)</p>
            <div class="checkout-steps">
                <span class="step @(_step == CheckoutStep.MareDetails ? "step--active" : "")">1. Mare details</span>
                <span class="step-sep">›</span>
                <span class="step @(_step == CheckoutStep.Disclosure ? "step--active" : "")">2. Review & confirm</span>
            </div>
        </div>

        @if (_step == CheckoutStep.MareDetails)
        {
            <MareDetailsForm Model="@_request" OnSubmit="AdvanceToDisclosure" />
        }
        else if (_step == CheckoutStep.Disclosure && _disclosure is not null)
        {
            <BuyerDisclosure TotalPriceIncGst="@_disclosure.TotalPriceIncGst"
                             PlatformFeeIncGst="@_disclosure.PlatformFeeIncGst"
                             OnConfirmed="Confirm" />
            @if (_confirmError is not null)
            {
                <ErrorMessage Message="@_confirmError" style="margin-top: var(--space-4)" />
            }
        }
    }
</div>

@code {
    [Parameter] public Guid ListingId { get; set; }

    private enum CheckoutStep { MareDetails, Disclosure, Success }
    private CheckoutStep _step = CheckoutStep.MareDetails;

    private ListingDto? _listing;
    private CheckoutDisclosureDto? _disclosure;
    private Shared.DTOs.Checkout.CheckoutRequest _request = new();
    private string? _error;
    private string? _confirmError;
    private bool _confirmBusy;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _listing = await ListingApi.GetByIdAsync(ListingId);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _error = "This listing could not be found.";
        }
        catch (ApiException)
        {
            _error = "Failed to load listing. Please try again.";
        }
    }

    private async Task AdvanceToDisclosure()
    {
        _error = null;
        try
        {
            // POST checkout: server creates the purchase and returns disclosure amounts
            var response = await CheckoutApi.InitiateAsync(ListingId, _request);
            _disclosure = response.Disclosure;
            _step = CheckoutStep.Disclosure;
        }
        catch (ApiException ex)
        {
            _error = ex.Message;
        }
    }

    private async Task Confirm()
    {
        if (_confirmBusy) return;
        _confirmBusy = true;
        _confirmError = null;
        try
        {
            // Purchase was already created by InitiateAsync — this confirms (no second API call needed)
            _step = CheckoutStep.Success;
        }
        catch (ApiException ex)
        {
            _confirmError = ex.Message;
        }
        finally
        {
            _confirmBusy = false;
        }
    }
}
```

Create `src/Client/Pages/Checkout.razor.css`:

```css
.checkout-container { max-width: 600px; padding-block: var(--space-12); }
.checkout-header { margin-bottom: var(--space-8); }
.checkout-header h1 { font-size: var(--font-size-2xl); font-weight: var(--font-weight-bold); margin-bottom: var(--space-2); }
.checkout-steps { display: flex; align-items: center; gap: var(--space-3); margin-top: var(--space-4); font-size: var(--font-size-sm); color: var(--text-muted); }
.step--active { color: var(--navy); font-weight: var(--font-weight-semibold); }
.step-sep { color: var(--border); }
.checkout-success { text-align: center; padding-block: var(--space-16); }
.success-icon { font-size: 3rem; color: var(--success); background: #d1fae5; width: 72px; height: 72px; border-radius: 50%; display: flex; align-items: center; justify-content: center; margin: 0 auto var(--space-6); }
.checkout-success h2 { font-size: var(--font-size-2xl); margin-bottom: var(--space-4); }
```

- [ ] **Step 6: Run tests — expect green**

```
dotnet test tests/Client.Tests --filter "BuyerDisclosure|CheckoutTests" -v minimal
```

Expected: 3 tests PASS.

- [ ] **Step 7: Build client**

```
dotnet build src/Client -v minimal
```

Expected: 0 errors.

- [ ] **Step 8: Commit**

```
git add src/Client/Components/Checkout/ \
        src/Client/Pages/Checkout.razor \
        src/Client/Pages/Checkout.razor.css \
        tests/Client.Tests/Components/Checkout/ \
        tests/Client.Tests/Pages/CheckoutTests.cs
git commit -m "feat: add checkout flow (MareDetailsForm, BuyerDisclosure, Checkout page)"
```

---

### Task 13: MyPurchases and MyBids pages

**Why:** Two authenticated buyer account pages showing purchase history and bid history. Both are simple list renders with status badge logic.

**Files:**
- Create: `src/Client/Pages/MyPurchases.razor` + `.razor.css`
- Create: `src/Client/Pages/MyBids.razor` + `.razor.css`
- Test: `tests/Client.Tests/Pages/MyPurchasesTests.cs`

---

- [ ] **Step 1: Write failing test**

Create `tests/Client.Tests/Pages/MyPurchasesTests.cs`:

```csharp
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Checkout;

namespace Stallions.Client.Tests.Pages;

public class MyPurchasesTests : TestContext
{
    [Fact]
    public async Task MyPurchases_WithNoPurchases_ShowsEmptyState()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("buyer@test.com", "Buyer");

        var mock = new Mock<CheckoutApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        mock.Setup(s => s.GetMyPurchasesAsync()).ReturnsAsync(new List<PurchaseDto>());
        Services.AddSingleton(mock.Object);

        var cut = RenderComponent<MyPurchases>();
        await Task.Delay(50); cut.Render();

        cut.Find(".empty-state").Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run test — expect compile failure**

```
dotnet test tests/Client.Tests --filter "MyPurchases" -v minimal
```

Expected: Build error.

- [ ] **Step 3: Create `MyPurchases.razor`**

Create `src/Client/Pages/MyPurchases.razor`:

```razor
@page "/my-purchases"
@attribute [Authorize]
@inject CheckoutApiService CheckoutApi

<PageTitle>My Purchases — Stallions Australia</PageTitle>

<div class="container" style="padding-block: var(--space-10)">
    <h1 class="page-title">My Purchases</h1>

    @if (_error is not null)
    {
        <ErrorMessage Message="@_error" OnRetry="Load" />
    }
    else if (_purchases is null)
    {
        <LoadingSpinner />
    }
    else if (!_purchases.Any())
    {
        <EmptyState Message="No purchases yet" Icon="🐎">
            <a href="/" class="btn btn-primary">Browse nominations</a>
        </EmptyState>
    }
    else
    {
        <div class="purchases-list">
            @foreach (var p in _purchases)
            {
                <div class="purchase-row">
                    <div class="purchase-info">
                        <div class="purchase-stallion">@p.StallionName</div>
                        <div class="purchase-farm text-secondary text-sm">@p.StudFarmName</div>
                        <div class="purchase-mare text-muted text-sm">Mare: @p.MareName</div>
                    </div>
                    <div class="purchase-amounts">
                        <div class="purchase-price">@p.ListingPriceIncGst.ToString("C0", AuCulture)</div>
                        <div class="purchase-fee text-muted text-sm">Fee: @p.PlatformFeeIncGst.ToString("C0", AuCulture)</div>
                    </div>
                    <div class="purchase-status">
                        <span class="badge @GetStatusBadge(p.Status)">@p.Status</span>
                        <div class="purchase-date text-muted text-xs">@p.PurchasedAt.ToString("d MMM yyyy")</div>
                    </div>
                </div>
            }
        </div>
    }
</div>

@code {
    private List<Stallions.Shared.DTOs.Checkout.PurchaseDto>? _purchases;
    private string? _error;
    private static readonly System.Globalization.CultureInfo AuCulture =
        System.Globalization.CultureInfo.GetCultureInfo("en-AU");

    protected override async Task OnInitializedAsync() => await Load();

    private async Task Load()
    {
        _error = null;
        try { _purchases = await CheckoutApi.GetMyPurchasesAsync(); }
        catch (ApiException) { _error = "Failed to load purchases. Please try again."; }
    }

    private static string GetStatusBadge(string status) => status switch
    {
        "Complete"           => "badge-leading",
        "AwaitingSignatures" => "badge-auction",
        "Disputed"           => "badge-ending",
        _                    => ""
    };
}
```

Create `src/Client/Pages/MyPurchases.razor.css`:

```css
.purchases-list { display: flex; flex-direction: column; gap: var(--space-4); }
.purchase-row {
    background: var(--white);
    border: 1px solid var(--border);
    border-radius: var(--radius-md);
    padding: var(--space-4) var(--space-6);
    display: grid;
    grid-template-columns: 1fr auto auto;
    gap: var(--space-4);
    align-items: center;
}
.purchase-stallion { font-weight: var(--font-weight-semibold); }
.purchase-price    { font-weight: var(--font-weight-semibold); font-size: var(--font-size-lg); }
.purchase-status   { display: flex; flex-direction: column; align-items: flex-end; gap: var(--space-2); }
```

- [ ] **Step 4: Create `MyBids.razor`**

Create `src/Client/Pages/MyBids.razor`:

```razor
@page "/my-bids"
@attribute [Authorize]
@inject BidApiService BidApi

<PageTitle>My Bids — Stallions Australia</PageTitle>

<div class="container" style="padding-block: var(--space-10)">
    <h1 class="page-title">My Bids</h1>

    @if (_error is not null)
    {
        <ErrorMessage Message="@_error" OnRetry="Load" />
    }
    else if (_bids is null)
    {
        <LoadingSpinner />
    }
    else if (!_bids.Any())
    {
        <EmptyState Message="No bids yet" Icon="🔨">
            <a href="/" class="btn btn-primary">Browse auctions</a>
        </EmptyState>
    }
    else
    {
        <div class="bids-list">
            @foreach (var bid in _bids)
            {
                <div class="bid-row">
                    <div class="bid-info">
                        <div class="bid-stallion">@bid.StallionName</div>
                        <div class="bid-date text-muted text-sm">@bid.PlacedAt.ToString("d MMM yyyy, h:mm tt")</div>
                    </div>
                    <div class="bid-amount">@bid.AmountIncGst.ToString("C0", AuCulture)</div>
                    <div class="bid-status">
                        <span class="badge @GetStatusBadge(bid.Status)">@GetStatusLabel(bid.Status)</span>
                        @if (bid.Status == "Outbid")
                        {
                            <a href="/listings/@bid.ListingId" class="btn btn-sm btn-outline" style="margin-top: var(--space-2)">Bid again</a>
                        }
                    </div>
                </div>
            }
        </div>
    }
</div>

@code {
    private List<Stallions.Shared.DTOs.Bids.BidDto>? _bids;
    private string? _error;
    private static readonly System.Globalization.CultureInfo AuCulture =
        System.Globalization.CultureInfo.GetCultureInfo("en-AU");

    protected override async Task OnInitializedAsync() => await Load();

    private async Task Load()
    {
        _error = null;
        try { _bids = await BidApi.GetMyBidsAsync(); }
        catch (ApiException) { _error = "Failed to load bids. Please try again."; }
    }

    private static string GetStatusBadge(string status) => status switch
    {
        "Leading" => "badge-leading",
        "Outbid"  => "badge-outbid",
        "Won"     => "badge-leading",
        _         => ""
    };

    private static string GetStatusLabel(string status) => status switch
    {
        "AuctionClosed" => "Auction closed",
        _               => status
    };
}
```

Create `src/Client/Pages/MyBids.razor.css`:

```css
.bids-list { display: flex; flex-direction: column; gap: var(--space-4); }
.bid-row {
    background: var(--white);
    border: 1px solid var(--border);
    border-radius: var(--radius-md);
    padding: var(--space-4) var(--space-6);
    display: grid;
    grid-template-columns: 1fr auto auto;
    gap: var(--space-4);
    align-items: center;
}
.bid-stallion  { font-weight: var(--font-weight-semibold); }
.bid-amount    { font-weight: var(--font-weight-semibold); font-size: var(--font-size-lg); }
.bid-status    { display: flex; flex-direction: column; align-items: flex-end; gap: var(--space-2); }
```

- [ ] **Step 5: Run test — expect green**

```
dotnet test tests/Client.Tests --filter "MyPurchases" -v minimal
```

Expected: 1 test PASS.

- [ ] **Step 6: Build client**

```
dotnet build src/Client -v minimal
```

Expected: 0 errors.

- [ ] **Step 7: Commit**

```
git add src/Client/Pages/MyPurchases.razor \
        src/Client/Pages/MyPurchases.razor.css \
        src/Client/Pages/MyBids.razor \
        src/Client/Pages/MyBids.razor.css \
        tests/Client.Tests/Pages/MyPurchasesTests.cs
git commit -m "feat: add MyPurchases and MyBids buyer account pages"
```

---

### Task 14: Enquiries pages and components

**Why:** Buyers can view and reply to enquiry threads. Two pages (list + detail) and two components (chat thread + message composer). Tests verify message sending invokes the service.

**Files:**
- Create: `src/Client/Components/Enquiries/MessageThread.razor` + `.razor.css`
- Create: `src/Client/Components/Enquiries/MessageComposer.razor` + `.razor.css`
- Create: `src/Client/Pages/Enquiries.razor` + `.razor.css`
- Create: `src/Client/Pages/EnquiryDetail.razor` + `.razor.css`
- Test: `tests/Client.Tests/Components/Enquiries/MessageComposerTests.cs`

---

- [ ] **Step 1: Write failing test**

Create `tests/Client.Tests/Components/Enquiries/MessageComposerTests.cs`:

```csharp
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Components.Enquiries;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Enquiries;

namespace Stallions.Client.Tests.Components.Enquiries;

public class MessageComposerTests : TestContext
{
    [Fact]
    public async Task MessageComposer_Send_InvokesCallback()
    {
        string? sentText = null;
        var cut = RenderComponent<MessageComposer>(p => p
            .Add(c => c.OnSend, EventCallback.Factory.Create<string>(this, text => sentText = text)));

        cut.Find("textarea").Change("Hello from buyer!");
        cut.Find("button[type='submit']").Click();

        await Task.Delay(10);
        sentText.Should().Be("Hello from buyer!");
    }

    [Fact]
    public void MessageComposer_SendButton_DisabledWhenEmpty()
    {
        var cut = RenderComponent<MessageComposer>(p => p
            .Add(c => c.OnSend, EventCallback.Factory.Create<string>(this, _ => { })));

        cut.Find("button[type='submit']").HasAttribute("disabled").Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run test — expect compile failure**

```
dotnet test tests/Client.Tests --filter "MessageComposer" -v minimal
```

Expected: Build error.

- [ ] **Step 3: Create `MessageThread.razor`**

Create `src/Client/Components/Enquiries/MessageThread.razor`:

```razor
@if (Messages is null || !Messages.Any())
{
    <EmptyState Message="No messages yet" Icon="💬" />
}
else
{
    <div class="message-thread">
        @foreach (var msg in Messages)
        {
            <div class="message @(msg.IsFromBuyer ? "message--buyer" : "message--farm")">
                <div class="message-sender">@(msg.IsFromBuyer ? "You" : msg.SenderName)</div>
                <div class="message-bubble">@msg.Body</div>
                <div class="message-time text-muted text-xs">@msg.SentAt.ToString("d MMM, h:mm tt")</div>
            </div>
        }
    </div>
}

@code {
    [Parameter] public IReadOnlyList<Stallions.Shared.DTOs.Enquiries.EnquiryMessageDto>? Messages { get; set; }
}
```

Create `src/Client/Components/Enquiries/MessageThread.razor.css`:

```css
.message-thread { display: flex; flex-direction: column; gap: var(--space-4); }
.message { display: flex; flex-direction: column; max-width: 80%; }
.message--buyer { align-self: flex-end; align-items: flex-end; }
.message--farm  { align-self: flex-start; }
.message-sender { font-size: var(--font-size-xs); color: var(--text-muted); margin-bottom: var(--space-1); }
.message-bubble { padding: var(--space-3) var(--space-4); border-radius: var(--radius-lg); font-size: var(--font-size-sm); line-height: 1.5; }
.message--buyer .message-bubble { background: var(--navy); color: var(--white); border-bottom-right-radius: var(--radius-sm); }
.message--farm  .message-bubble { background: var(--white); border: 1px solid var(--border); border-bottom-left-radius: var(--radius-sm); }
.message-time { margin-top: var(--space-1); }
```

- [ ] **Step 4: Create `MessageComposer.razor`**

Create `src/Client/Components/Enquiries/MessageComposer.razor`:

```razor
<form class="composer" @onsubmit="HandleSend" @onsubmit:preventDefault="true">
    <textarea class="form-textarea composer-input"
              placeholder="Type your message…"
              @bind="_text" @bind:event="oninput"
              rows="3"></textarea>
    <button type="submit" class="btn btn-primary composer-send"
            disabled="@string.IsNullOrWhiteSpace(_text)">
        Send
    </button>
</form>

@code {
    [Parameter, EditorRequired] public EventCallback<string> OnSend { get; set; }
    private string _text = string.Empty;

    private async Task HandleSend()
    {
        if (string.IsNullOrWhiteSpace(_text)) return;
        await OnSend.InvokeAsync(_text.Trim());
        _text = string.Empty;
    }
}
```

Create `src/Client/Components/Enquiries/MessageComposer.razor.css`:

```css
.composer { display: flex; flex-direction: column; gap: var(--space-3); }
.composer-input { resize: none; }
.composer-send { align-self: flex-end; }
```

- [ ] **Step 5: Create `Enquiries.razor`**

Create `src/Client/Pages/Enquiries.razor`:

```razor
@page "/enquiries"
@attribute [Authorize]
@inject EnquiryApiService EnquiryApi
@inject NavigationManager Nav

<PageTitle>Messages — Stallions Australia</PageTitle>

<div class="container" style="padding-block: var(--space-10)">
    <h1 class="page-title">Messages</h1>

    @if (_error is not null)   { <ErrorMessage Message="@_error" OnRetry="Load" /> }
    else if (_enquiries is null) { <LoadingSpinner /> }
    else if (!_enquiries.Any())
    {
        <EmptyState Message="No enquiries yet" Icon="💬">
            <a href="/" class="btn btn-primary">Browse listings</a>
        </EmptyState>
    }
    else
    {
        <div class="enquiry-list">
            @foreach (var e in _enquiries)
            {
                <div class="enquiry-row" @onclick="() => Nav.NavigateTo($"/enquiries/{e.Id}")">
                    <div class="enquiry-info">
                        <div class="enquiry-subject">@e.Subject</div>
                        <div class="enquiry-meta text-muted text-sm">@e.MessageCount message@(e.MessageCount != 1 ? "s" : "") · @e.LastMessageAt.ToString("d MMM")</div>
                    </div>
                    <span class="badge @(e.Status == "Open" ? "badge-leading" : "")">@e.Status</span>
                </div>
            }
        </div>
    }
</div>

@code {
    private List<Stallions.Shared.DTOs.Enquiries.EnquirySummaryDto>? _enquiries;
    private string? _error;

    protected override async Task OnInitializedAsync() => await Load();
    private async Task Load()
    {
        _error = null;
        try { _enquiries = await EnquiryApi.GetAllAsync(); }
        catch (ApiException) { _error = "Failed to load enquiries. Please try again."; }
    }
}
```

Create `src/Client/Pages/Enquiries.razor.css`:

```css
.enquiry-list { display: flex; flex-direction: column; gap: var(--space-2); }
.enquiry-row {
    background: var(--white);
    border: 1px solid var(--border);
    border-radius: var(--radius-md);
    padding: var(--space-4) var(--space-6);
    display: flex;
    justify-content: space-between;
    align-items: center;
    cursor: pointer;
    transition: border-color .15s;
}
.enquiry-row:hover { border-color: var(--navy); }
.enquiry-subject { font-weight: var(--font-weight-medium); }
```

- [ ] **Step 6: Create `EnquiryDetail.razor`**

Create `src/Client/Pages/EnquiryDetail.razor`:

```razor
@page "/enquiries/{Id:guid}"
@attribute [Authorize]
@inject EnquiryApiService EnquiryApi

<PageTitle>@(_enquiry?.Subject ?? "Enquiry") — Stallions Australia</PageTitle>

<div class="container enquiry-detail-container">
    @if (_error is not null)   { <ErrorMessage Message="@_error" OnRetry="Load" /> }
    else if (_enquiry is null)  { <LoadingSpinner /> }
    else
    {
        <div class="enquiry-detail-header">
            <h1 class="page-title">@_enquiry.Subject</h1>
            <a href="/enquiries" class="text-muted text-sm">← All messages</a>
        </div>

        <div class="enquiry-thread">
            <MessageThread Messages="@_enquiry.Messages" />
        </div>

        @if (_sendError is not null)
        {
            <ErrorMessage Message="@_sendError" />
        }

        <div class="enquiry-composer">
            <MessageComposer OnSend="HandleSend" />
        </div>
    }
</div>

@code {
    [Parameter] public Guid Id { get; set; }
    private Stallions.Shared.DTOs.Enquiries.EnquiryDto? _enquiry;
    private string? _error;
    private string? _sendError;

    protected override async Task OnInitializedAsync() => await Load();

    private async Task Load()
    {
        _error = null;
        try { _enquiry = await EnquiryApi.GetByIdAsync(Id); }
        catch (ApiException ex) when (ex.StatusCode == 404) { _error = "Enquiry not found."; }
        catch (ApiException) { _error = "Failed to load enquiry. Please try again."; }
    }

    private async Task HandleSend(string text)
    {
        _sendError = null;
        try
        {
            await EnquiryApi.PostMessageAsync(Id, new Stallions.Shared.DTOs.Enquiries.SendMessageRequest { Body = text });
            await Load(); // Refresh thread
        }
        catch (ApiException)
        {
            _sendError = "Failed to send message. Please try again.";
        }
    }
}
```

Create `src/Client/Pages/EnquiryDetail.razor.css`:

```css
.enquiry-detail-container { max-width: 760px; padding-block: var(--space-10); }
.enquiry-detail-header { margin-bottom: var(--space-8); }
.enquiry-thread { background: var(--warm-grey); border-radius: var(--radius-lg); padding: var(--space-6); min-height: 200px; margin-bottom: var(--space-6); }
.enquiry-composer { background: var(--white); border: 1px solid var(--border); border-radius: var(--radius-lg); padding: var(--space-4); }
```

- [ ] **Step 7: Run tests — expect green**

```
dotnet test tests/Client.Tests --filter "MessageComposer" -v minimal
```

Expected: 2 tests PASS.

- [ ] **Step 8: Run full test suite**

```
dotnet test -v minimal
```

Expected: All tests in Server.Tests and Client.Tests PASS.

- [ ] **Step 9: Commit**

```
git add src/Client/Components/Enquiries/ \
        src/Client/Pages/Enquiries.razor \
        src/Client/Pages/Enquiries.razor.css \
        src/Client/Pages/EnquiryDetail.razor \
        src/Client/Pages/EnquiryDetail.razor.css \
        tests/Client.Tests/Components/Enquiries/
git commit -m "feat: add Enquiries list and detail pages with chat thread and composer"
```

---

### Task 15: Development seed data

**Why:** Developers need real-looking data to exercise the UI. The seed script creates two stud farms, four stallions with placeholder images, three fixed-price listings, and two auction listings (one ending soon, one with days remaining), plus two test user accounts.

**Files:**
- Create: `scripts/seed-dev.sql`

No unit test — run the script against the local dev database and verify the app renders correctly in the browser.

---

- [ ] **Step 1: Create the seed script**

Create `scripts/seed-dev.sql`:

```sql
-- ============================================================
-- Development seed data for Stallions Nominations Marketplace
-- Run against the local dev database (stallions_dev)
-- Safe to re-run: uses IF NOT EXISTS / MERGE patterns
-- ============================================================

BEGIN TRANSACTION;

-- ── Seasons ──────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM Seasons WHERE Name = '2025 Season')
BEGIN
    INSERT INTO Seasons (Id, Name, Year, StartDate, EndDate, IsOpen, CreatedAt)
    VALUES (
        '11111111-0000-0000-0000-000000000001',
        '2025 Season', 2025,
        '2025-08-01', '2026-01-31',
        1, GETUTCDATE()
    );
END

-- ── Stud Farms ───────────────────────────────────────────────
-- Note: UserId references must match actual Entra ID user objects.
-- For local dev, insert placeholder GUIDs and update them once
-- you have the test user IDs from Entra ID.

IF NOT EXISTS (SELECT 1 FROM StudFarms WHERE Name = 'Coolmore Australia (Dev)')
BEGIN
    INSERT INTO StudFarms (Id, UserId, Name, ABN, ContactPhone, ContactEmail, Address, IsActive, CreatedAt)
    VALUES (
        '22222222-0000-0000-0000-000000000001',
        '00000000-0000-0000-0000-000000000001',  -- Replace with stud farm admin user ID
        'Coolmore Australia (Dev)',
        '12 345 678 901',
        '02 4998 6700',
        'info@coolmoreaustralia.com.au',
        'Jerrys Plains NSW 2330',
        1, GETUTCDATE()
    );
END

IF NOT EXISTS (SELECT 1 FROM StudFarms WHERE Name = 'Arrowfield Stud (Dev)')
BEGIN
    INSERT INTO StudFarms (Id, UserId, Name, ABN, ContactPhone, ContactEmail, Address, IsActive, CreatedAt)
    VALUES (
        '22222222-0000-0000-0000-000000000002',
        '00000000-0000-0000-0000-000000000002',  -- Replace with second stud farm admin user ID
        'Arrowfield Stud (Dev)',
        '98 765 432 109',
        '02 6545 3000',
        'info@arrowfield.com.au',
        'Scone NSW 2337',
        1, GETUTCDATE()
    );
END

-- ── Stallions ────────────────────────────────────────────────
DECLARE @CoolmoreId UNIQUEIDENTIFIER = '22222222-0000-0000-0000-000000000001';
DECLARE @ArrowfieldId UNIQUEIDENTIFIER = '22222222-0000-0000-0000-000000000002';
DECLARE @SeasonId UNIQUEIDENTIFIER = '11111111-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM Stallions WHERE Name = 'Fastnet Rock (Dev)')
BEGIN
    DECLARE @FastnetId UNIQUEIDENTIFIER = '33333333-0000-0000-0000-000000000001';
    INSERT INTO Stallions (Id, StudFarmId, Name, Breed, YearOfBirth, IsActive, CreatedAt)
    VALUES (@FastnetId, @CoolmoreId, 'Fastnet Rock (Dev)', 'Thoroughbred', 2001, 1, GETUTCDATE());

    INSERT INTO StallionImages (Id, StallionId, BlobPath, IsPrimary, DisplayOrder, UploadedAt)
    VALUES (NEWID(), @FastnetId,
        'https://images.unsplash.com/photo-1553284966-19b8815c7817?w=800&q=80',
        1, 1, GETUTCDATE());
END

IF NOT EXISTS (SELECT 1 FROM Stallions WHERE Name = 'Snitzel (Dev)')
BEGIN
    DECLARE @SnitzelId UNIQUEIDENTIFIER = '33333333-0000-0000-0000-000000000002';
    INSERT INTO Stallions (Id, StudFarmId, Name, Breed, YearOfBirth, IsActive, CreatedAt)
    VALUES (@SnitzelId, @ArrowfieldId, 'Snitzel (Dev)', 'Thoroughbred', 2002, 1, GETUTCDATE());

    INSERT INTO StallionImages (Id, StallionId, BlobPath, IsPrimary, DisplayOrder, UploadedAt)
    VALUES (NEWID(), @SnitzelId,
        'https://images.unsplash.com/photo-1598974357801-cbca100e65d3?w=800&q=80',
        1, 1, GETUTCDATE());
END

IF NOT EXISTS (SELECT 1 FROM Stallions WHERE Name = 'So You Think (Dev)')
BEGIN
    DECLARE @SYTId UNIQUEIDENTIFIER = '33333333-0000-0000-0000-000000000003';
    INSERT INTO Stallions (Id, StudFarmId, Name, Breed, YearOfBirth, IsActive, CreatedAt)
    VALUES (@SYTId, @CoolmoreId, 'So You Think (Dev)', 'Thoroughbred', 2007, 1, GETUTCDATE());

    INSERT INTO StallionImages (Id, StallionId, BlobPath, IsPrimary, DisplayOrder, UploadedAt)
    VALUES (NEWID(), @SYTId,
        'https://images.unsplash.com/photo-1534113534176-3b5d5a2c4b9c?w=800&q=80',
        1, 1, GETUTCDATE());
END

IF NOT EXISTS (SELECT 1 FROM Stallions WHERE Name = 'Deep Impact (Dev)')
BEGIN
    DECLARE @DeepImpactId UNIQUEIDENTIFIER = '33333333-0000-0000-0000-000000000004';
    INSERT INTO Stallions (Id, StudFarmId, Name, Breed, YearOfBirth, IsActive, CreatedAt)
    VALUES (@DeepImpactId, @ArrowfieldId, 'Deep Impact (Dev)', 'Thoroughbred', 2002, 1, GETUTCDATE());

    INSERT INTO StallionImages (Id, StallionId, BlobPath, IsPrimary, DisplayOrder, UploadedAt)
    VALUES (NEWID(), @DeepImpactId,
        'https://images.unsplash.com/photo-1489391722045-1e6f39ca7c49?w=800&q=80',
        1, 1, GETUTCDATE());
END

-- ── Fixed Price Listings (Active, fee set by staff) ──────────
-- Fastnet Rock: first 20 at $8,000
IF NOT EXISTS (SELECT 1 FROM Listings WHERE Id = '44444444-0000-0000-0000-000000000001')
BEGIN
    INSERT INTO Listings (Id, StallionId, SeasonId, StudFarmId, ListingType, Status,
        PlatformFeePercent, PublishedAt, CreatedAt)
    VALUES ('44444444-0000-0000-0000-000000000001',
        '33333333-0000-0000-0000-000000000001', @SeasonId, @CoolmoreId,
        0 /* FixedPrice */, 2 /* Active */, 2.5, GETUTCDATE(), GETUTCDATE());

    INSERT INTO FixedPriceListings (Id, PriceIncGst, Quantity, QuantityRemaining)
    VALUES ('44444444-0000-0000-0000-000000000001', 8000.00, 20, 17);
END

-- Fastnet Rock: next 10 at $10,000
IF NOT EXISTS (SELECT 1 FROM Listings WHERE Id = '44444444-0000-0000-0000-000000000002')
BEGIN
    INSERT INTO Listings (Id, StallionId, SeasonId, StudFarmId, ListingType, Status,
        PlatformFeePercent, PublishedAt, CreatedAt)
    VALUES ('44444444-0000-0000-0000-000000000002',
        '33333333-0000-0000-0000-000000000001', @SeasonId, @CoolmoreId,
        0, 2, 2.5, GETUTCDATE(), GETUTCDATE());

    INSERT INTO FixedPriceListings (Id, PriceIncGst, Quantity, QuantityRemaining)
    VALUES ('44444444-0000-0000-0000-000000000002', 10000.00, 10, 10);
END

-- Snitzel: limited quantity, nearly sold out
IF NOT EXISTS (SELECT 1 FROM Listings WHERE Id = '44444444-0000-0000-0000-000000000003')
BEGIN
    INSERT INTO Listings (Id, StallionId, SeasonId, StudFarmId, ListingType, Status,
        PlatformFeePercent, PublishedAt, CreatedAt)
    VALUES ('44444444-0000-0000-0000-000000000003',
        '33333333-0000-0000-0000-000000000002', @SeasonId, @ArrowfieldId,
        0, 2, 3.0, GETUTCDATE(), GETUTCDATE());

    INSERT INTO FixedPriceListings (Id, PriceIncGst, Quantity, QuantityRemaining)
    VALUES ('44444444-0000-0000-0000-000000000003', 15000.00, 5, 2);
END

-- ── Auction Listings ─────────────────────────────────────────
-- So You Think: ending in ~4 hours (tests "ending soon" styling)
IF NOT EXISTS (SELECT 1 FROM Listings WHERE Id = '44444444-0000-0000-0000-000000000004')
BEGIN
    INSERT INTO Listings (Id, StallionId, SeasonId, StudFarmId, ListingType, Status,
        PlatformFeePercent, PublishedAt, CreatedAt)
    VALUES ('44444444-0000-0000-0000-000000000004',
        '33333333-0000-0000-0000-000000000003', @SeasonId, @CoolmoreId,
        1 /* Auction */, 2, 2.0, GETUTCDATE(), GETUTCDATE());

    INSERT INTO AuctionListings (Id, StartingPrice, ReservePrice, IsNoReserve,
        MinimumBidIncrement, EndDateTime)
    VALUES ('44444444-0000-0000-0000-000000000004',
        5000.00, 12000.00, 0, 25.00,
        DATEADD(hour, 4, GETUTCDATE()));   -- Ends in 4 hours
END

-- Deep Impact: 5 days remaining, no reserve
IF NOT EXISTS (SELECT 1 FROM Listings WHERE Id = '44444444-0000-0000-0000-000000000005')
BEGIN
    INSERT INTO Listings (Id, StallionId, SeasonId, StudFarmId, ListingType, Status,
        PlatformFeePercent, PublishedAt, CreatedAt)
    VALUES ('44444444-0000-0000-0000-000000000005',
        '33333333-0000-0000-0000-000000000004', @SeasonId, @ArrowfieldId,
        1, 2, 1.5, GETUTCDATE(), GETUTCDATE());

    INSERT INTO AuctionListings (Id, StartingPrice, ReservePrice, IsNoReserve,
        MinimumBidIncrement, EndDateTime)
    VALUES ('44444444-0000-0000-0000-000000000005',
        8000.00, NULL, 1, 25.00,
        DATEADD(day, 5, GETUTCDATE()));    -- Ends in 5 days, no reserve
END

COMMIT;

-- ── Reminder ─────────────────────────────────────────────────
PRINT 'Seed data inserted.';
PRINT 'ACTION REQUIRED: Update the placeholder UserId values in StudFarms to match';
PRINT 'the actual test user GUIDs from your Entra ID tenant.';
PRINT 'Test user role assignments must be done in Entra ID app roles, not the database.';
```

- [ ] **Step 2: Run the seed script against local dev database**

```
sqlcmd -S localhost -d stallions_dev -E -i scripts/seed-dev.sql
```

Expected: `Seed data inserted.` with no errors.

- [ ] **Step 3: Verify the app renders the seed data**

Start both server and client:

```
# Terminal 1
dotnet run --project src/Server

# Terminal 2
dotnet run --project src/Client
```

Open `https://localhost:7000` (or the configured client port). Verify:
- Home page shows 5 listing cards (3 fixed price, 2 auction)
- One auction card shows "Ending soon" badge
- One auction card shows "No reserve" info
- Fixed-price cards show quantity remaining
- Browse filtering (All / Auction / Buy Now) works
- Stallion name and stud farm name are visible on each card

- [ ] **Step 4: Run final full test suite**

```
dotnet test -v minimal
```

Expected: All tests PASS.

- [ ] **Step 5: Commit**

```
git add scripts/seed-dev.sql
git commit -m "feat: add development seed data (2 farms, 4 stallions, 5 listings)"
```

---

## Plan 3c Complete

At this point the full Plan 3 scope is implemented:

| Area | Done |
|---|---|
| Public browse (Home, filter, search) | ✅ |
| Listing detail (auction bid form, FP purchase, auth-aware CTAs) | ✅ |
| Stallion profile page | ✅ |
| Stud farm profile page | ✅ |
| Buyer checkout (mare details + mandatory disclosure) | ✅ |
| My Purchases (history + status badges) | ✅ |
| My Bids (active/past + bid again CTA) | ✅ |
| Enquiries list | ✅ |
| Enquiry detail (chat thread + message composer) | ✅ |
| Development seed data | ✅ |

**Next step:** Plan 4 — Stud Farm Admin panel and Staff admin panel.
