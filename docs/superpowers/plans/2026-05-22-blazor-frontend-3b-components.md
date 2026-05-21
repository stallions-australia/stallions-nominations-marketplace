# Blazor WebAssembly Frontend — Plan 3b: Components

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the full reusable component library — layout shell, shared UI components, typed API services, and all listing-specific components. At the end of this plan every building block needed for the pages in Plan 3c exists and is unit tested. No pages yet.

**Prerequisite:** Plan 3a must be complete (API extensions, MSAL bootstrap, CSS system).

**Architecture:** Four tasks in dependency order — layout shell first (pages need a layout before they can render), then shared components (used everywhere), then API services (pages call these), then listing-specific components (biggest, most tested chunk).

**Tech Stack:** Blazor WebAssembly .NET 9, bUnit 1.x + xunit for component tests, `System.Threading.Timer` for the countdown (no JS interop), custom scoped `.razor.css` per component.

---

### Task 5: Layout shell (MainLayout, NavBar, Footer)

**Why:** Every page in Plan 3c uses `MainLayout` as its `DefaultLayout`. The auth-aware `NavBar` renders inside it. Both must exist before a single page can be tested. CSS here uses scoped `.razor.css`; media-query overrides (breakpoints that don't work inside Blazor `::deep`) live in `app.css`.

**Files:**
- Modify: `src/Client/Layout/MainLayout.razor` + `MainLayout.razor.css`
- Create: `src/Client/Layout/NavBar.razor` + `NavBar.razor.css`
- Create: `src/Client/Layout/Footer.razor` + `Footer.razor.css`
- Test: `tests/Client.Tests/Layout/NavBarTests.cs`

---

- [ ] **Step 1: Write NavBar tests**

Create `tests/Client.Tests/Layout/NavBarTests.cs`:

```csharp
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Stallions.Client.Layout;

namespace Stallions.Client.Tests.Layout;

public class NavBarTests : TestContext
{
    [Fact]
    public void NavBar_Unauthenticated_ShowsSignInLink()
    {
        this.AddTestAuthorization();  // anonymous by default

        var cut = RenderComponent<NavBar>();

        cut.Find("a[href='authentication/login']").Should().NotBeNull();
    }

    [Fact]
    public void NavBar_Authenticated_ShowsMyBidsLink()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("test@example.com");

        var cut = RenderComponent<NavBar>();

        cut.Find("a[href='/my-bids']").Should().NotBeNull();
    }

    [Fact]
    public void NavBar_HamburgerClick_OpensDrawer()
    {
        this.AddTestAuthorization();
        var cut = RenderComponent<NavBar>();

        cut.Find("button.navbar-hamburger").Click();

        cut.Find(".navbar-drawer").Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test tests/Client.Tests --filter "NavBar" -v minimal
```

Expected: Build error — `NavBar` component does not exist yet.

- [ ] **Step 3: Create `NavBar.razor`**

Create `src/Client/Layout/NavBar.razor`:

```razor
@using Microsoft.AspNetCore.Components.Authorization
@using Microsoft.AspNetCore.Components.WebAssembly.Authentication
@inject NavigationManager Nav

<nav class="navbar">
    <div class="container navbar-inner">
        <a href="/" class="navbar-brand">
            <img src="images/logo.png" alt="Stallions Nominations Marketplace" class="navbar-logo" />
        </a>

        <div class="navbar-links desktop-only">
            <NavLink href="/" Match="NavLinkMatch.All" class="nav-link">Browse</NavLink>
        </div>

        <div class="navbar-auth desktop-only">
            <AuthorizeView>
                <Authorized>
                    <NavLink href="/my-bids"      class="nav-link">My Bids</NavLink>
                    <NavLink href="/my-purchases" class="nav-link">My Purchases</NavLink>
                    <NavLink href="/enquiries"    class="nav-link">Messages</NavLink>
                    <button class="btn btn-outline btn-sm" @onclick="SignOut">Sign out</button>
                </Authorized>
                <NotAuthorized>
                    <a href="authentication/login"    class="btn btn-outline btn-sm">Sign in</a>
                    <a href="authentication/register" class="btn btn-primary btn-sm">Register</a>
                </NotAuthorized>
            </AuthorizeView>
        </div>

        <button class="navbar-hamburger mobile-only" @onclick="ToggleDrawer" aria-label="Menu">
            <span></span>
            <span></span>
            <span></span>
        </button>
    </div>

    @if (_drawerOpen)
    {
        <div class="navbar-backdrop" @onclick="CloseDrawer"></div>
        <div class="navbar-drawer">
            <NavLink href="/" Match="NavLinkMatch.All" class="drawer-link" @onclick="CloseDrawer">Browse</NavLink>
            <AuthorizeView>
                <Authorized>
                    <NavLink href="/my-bids"      class="drawer-link" @onclick="CloseDrawer">My Bids</NavLink>
                    <NavLink href="/my-purchases" class="drawer-link" @onclick="CloseDrawer">My Purchases</NavLink>
                    <NavLink href="/enquiries"    class="drawer-link" @onclick="CloseDrawer">Messages</NavLink>
                    <button class="btn btn-outline btn-full" @onclick="SignOut">Sign out</button>
                </Authorized>
                <NotAuthorized>
                    <a href="authentication/login"    class="btn btn-outline btn-full" @onclick="CloseDrawer">Sign in</a>
                    <a href="authentication/register" class="btn btn-primary btn-full"  @onclick="CloseDrawer">Register</a>
                </NotAuthorized>
            </AuthorizeView>
        </div>
    }
</nav>

@code {
    private bool _drawerOpen;
    private void ToggleDrawer() => _drawerOpen = !_drawerOpen;
    private void CloseDrawer()  => _drawerOpen = false;
    private void SignOut()      => Nav.NavigateToLogout("authentication/logout");
}
```

- [ ] **Step 4: Create `NavBar.razor.css`**

Create `src/Client/Layout/NavBar.razor.css`:

```css
.navbar {
    position: fixed;
    top: 0;
    left: 0;
    right: 0;
    height: var(--nav-height);
    background: var(--navy-dark);
    z-index: 100;
    box-shadow: 0 1px 0 rgba(255,255,255,.08);
}

.navbar-inner {
    height: 100%;
    display: flex;
    align-items: center;
    gap: var(--space-4);
}

.navbar-logo    { height: 36px; width: auto; }
.navbar-brand   { flex-shrink: 0; }
.navbar-links   { display: flex; align-items: center; gap: var(--space-6); margin-left: auto; }
.navbar-auth    { display: flex; align-items: center; gap: var(--space-3); }

.nav-link {
    color: rgba(255,255,255,.8);
    text-decoration: none;
    font-size: var(--font-size-sm);
    font-weight: var(--font-weight-medium);
    transition: color .15s;
}
.nav-link:hover, .nav-link.active { color: var(--white); }

/* Hamburger */
.navbar-hamburger {
    display: flex;
    flex-direction: column;
    gap: 5px;
    background: none;
    border: none;
    cursor: pointer;
    padding: var(--space-2);
    margin-left: auto;
}
.navbar-hamburger span {
    display: block;
    width: 22px;
    height: 2px;
    background: var(--white);
    border-radius: 2px;
}

/* Mobile drawer */
.navbar-backdrop {
    position: fixed;
    inset: 0;
    background: rgba(0,0,0,.4);
    z-index: 101;
}
.navbar-drawer {
    position: fixed;
    top: 0;
    right: 0;
    bottom: 0;
    width: min(280px, 85vw);
    background: var(--white);
    z-index: 102;
    padding: var(--space-8) var(--space-6);
    display: flex;
    flex-direction: column;
    gap: var(--space-4);
}
.drawer-link {
    color: var(--text-primary);
    text-decoration: none;
    font-size: var(--font-size-lg);
    font-weight: var(--font-weight-medium);
    padding-block: var(--space-2);
    border-bottom: 1px solid var(--border);
}
.drawer-link:hover { color: var(--navy); }

/* Visibility helpers — media queries here because ::deep doesn't work in @media */
.desktop-only { display: none; }
.mobile-only  { display: flex; }
```

Add to `src/Client/wwwroot/css/app.css` (at the bottom, under the breakpoints section):

```css
/* NavBar responsive — cannot use ::deep inside @media so these live in app.css */
@media (min-width: 768px) {
    .desktop-only { display: flex; }
    .mobile-only  { display: none !important; }
}
```

- [ ] **Step 5: Update `MainLayout.razor`**

```razor
@inherits LayoutComponentBase

<div class="page-wrapper">
    <NavBar />
    <main class="content-area">
        <ErrorBoundary>
            <ChildContent>
                @Body
            </ChildContent>
            <ErrorContent Context="ex">
                <div class="container" style="padding-top: var(--space-12)">
                    <div class="error-boundary">
                        <h2>Something went wrong</h2>
                        <p>An unexpected error occurred. Please <a href="/">return to listings</a> or try again.</p>
                    </div>
                </div>
            </ErrorContent>
        </ErrorBoundary>
    </main>
    <Footer />
</div>
```

Create `src/Client/Layout/MainLayout.razor.css`:

```css
.error-boundary {
    background: var(--white);
    border: 1px solid var(--border);
    border-radius: var(--radius-lg);
    padding: var(--space-12);
    text-align: center;
    max-width: 480px;
    margin-inline: auto;
}
.error-boundary h2 { margin-bottom: var(--space-4); color: var(--text-primary); }
```

- [ ] **Step 6: Create `Footer.razor`**

Create `src/Client/Layout/Footer.razor`:

```razor
<footer class="footer">
    <div class="container footer-inner">
        <span class="footer-copy">&copy; @DateTime.Now.Year Stallions Australia. All rights reserved.</span>
        <div class="footer-links">
            <a href="/terms">Terms & Conditions</a>
            <a href="/privacy">Privacy Policy</a>
        </div>
    </div>
</footer>
```

Create `src/Client/Layout/Footer.razor.css`:

```css
.footer {
    background: var(--navy-dark);
    color: rgba(255,255,255,.7);
    padding-block: var(--space-8);
    margin-top: var(--space-16);
}
.footer-inner {
    display: flex;
    flex-direction: column;
    gap: var(--space-4);
    align-items: center;
    text-align: center;
}
.footer-copy  { font-size: var(--font-size-sm); }
.footer-links { display: flex; gap: var(--space-6); }
.footer-links a { color: rgba(255,255,255,.7); font-size: var(--font-size-sm); text-decoration: none; }
.footer-links a:hover { color: var(--white); }

@media (min-width: 640px) {
    .footer-inner { flex-direction: row; justify-content: space-between; text-align: left; }
}
```

- [ ] **Step 7: Run tests — expect green**

```
dotnet test tests/Client.Tests --filter "NavBar" -v minimal
```

Expected: 3 tests PASS.

- [ ] **Step 8: Build client**

```
dotnet build src/Client -v minimal
```

Expected: 0 errors.

- [ ] **Step 9: Commit**

```
git add src/Client/Layout/
git commit -m "feat: add NavBar, MainLayout, Footer layout components"
```

---

### Task 6: Shared UI components

**Why:** `LoadingSpinner`, `EmptyState`, `ErrorMessage`, and `Modal` are used by multiple pages and listing components. Building and testing them now keeps the page tasks in Plan 3c clean.

**Files:**
- Create: `src/Client/Components/Shared/LoadingSpinner.razor` + `.razor.css`
- Create: `src/Client/Components/Shared/EmptyState.razor` + `.razor.css`
- Create: `src/Client/Components/Shared/ErrorMessage.razor`
- Create: `src/Client/Components/Shared/Modal.razor` + `.razor.css`
- Test: `tests/Client.Tests/Components/Shared/EmptyStateTests.cs`
- Test: `tests/Client.Tests/Components/Shared/ModalTests.cs`

---

- [ ] **Step 1: Write tests**

Create `tests/Client.Tests/Components/Shared/EmptyStateTests.cs`:

```csharp
using Bunit;
using FluentAssertions;
using Stallions.Client.Components.Shared;

namespace Stallions.Client.Tests.Components.Shared;

public class EmptyStateTests : TestContext
{
    [Fact]
    public void EmptyState_RendersMessageAndIcon()
    {
        var cut = RenderComponent<EmptyState>(p => p
            .Add(c => c.Message, "No listings found")
            .Add(c => c.Icon, "🔍"));

        cut.Find(".empty-state-message").TextContent.Should().Be("No listings found");
        cut.Find(".empty-state-icon").TextContent.Trim().Should().Be("🔍");
    }
}
```

Create `tests/Client.Tests/Components/Shared/ModalTests.cs`:

```csharp
using Bunit;
using FluentAssertions;
using Stallions.Client.Components.Shared;

namespace Stallions.Client.Tests.Components.Shared;

public class ModalTests : TestContext
{
    [Fact]
    public void Modal_WhenIsOpen_RendersChildContent()
    {
        var cut = RenderComponent<Modal>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Title, "Test Modal")
            .Add(c => c.ChildContent, "<p class='modal-test-content'>Hello</p>"));

        cut.Find(".modal-test-content").Should().NotBeNull();
        cut.Find(".modal-title").TextContent.Should().Be("Test Modal");
    }

    [Fact]
    public void Modal_WhenClosed_DoesNotRenderContent()
    {
        var cut = RenderComponent<Modal>(p => p
            .Add(c => c.IsOpen, false)
            .Add(c => c.Title, "Test")
            .Add(c => c.ChildContent, "<p class='modal-test-content'>Hidden</p>"));

        cut.FindAll(".modal-test-content").Should().BeEmpty();
    }

    [Fact]
    public void Modal_CloseButton_InvokesCallback()
    {
        var closed = false;
        var cut = RenderComponent<Modal>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Title, "Test")
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closed = true))
            .Add(c => c.ChildContent, "<p>content</p>"));

        cut.Find("button.modal-close").Click();

        closed.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test tests/Client.Tests --filter "EmptyState|Modal" -v minimal
```

Expected: Build error — components don't exist yet.

- [ ] **Step 3: Create `LoadingSpinner.razor`**

Create `src/Client/Components/Shared/LoadingSpinner.razor`:

```razor
<div class="spinner @(Large ? "spinner--large" : "")">
    <div class="spinner-ring"></div>
</div>

@code {
    [Parameter] public bool Large { get; set; }
}
```

Create `src/Client/Components/Shared/LoadingSpinner.razor.css`:

```css
.spinner { display: flex; justify-content: center; padding: var(--space-8); }
.spinner-ring {
    width: 32px; height: 32px;
    border: 3px solid var(--warm-grey);
    border-top-color: var(--navy);
    border-radius: 50%;
    animation: spin .8s linear infinite;
}
.spinner--large .spinner-ring { width: 48px; height: 48px; }
@keyframes spin { to { transform: rotate(360deg); } }
```

- [ ] **Step 4: Create `EmptyState.razor`**

Create `src/Client/Components/Shared/EmptyState.razor`:

```razor
<div class="empty-state">
    @if (!string.IsNullOrEmpty(Icon))
    {
        <div class="empty-state-icon">@Icon</div>
    }
    <p class="empty-state-message">@Message</p>
    @if (ChildContent is not null)
    {
        <div class="empty-state-action">@ChildContent</div>
    }
</div>

@code {
    [Parameter, EditorRequired] public string Message { get; set; } = string.Empty;
    [Parameter] public string? Icon { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
```

Create `src/Client/Components/Shared/EmptyState.razor.css`:

```css
.empty-state {
    text-align: center;
    padding: var(--space-16) var(--space-8);
    color: var(--text-muted);
}
.empty-state-icon    { font-size: 3rem; margin-bottom: var(--space-4); }
.empty-state-message { font-size: var(--font-size-lg); margin-bottom: var(--space-6); }
.empty-state-action  { margin-top: var(--space-4); }
```

- [ ] **Step 5: Create `ErrorMessage.razor`**

Create `src/Client/Components/Shared/ErrorMessage.razor`:

```razor
@if (!string.IsNullOrEmpty(Message))
{
    <div class="error-message" role="alert">
        <span class="error-icon">⚠</span>
        <span>@Message</span>
        @if (OnRetry.HasDelegate)
        {
            <button class="btn btn-sm btn-outline" @onclick="OnRetry">Try again</button>
        }
    </div>
}

<style>
    .error-message {
        display: flex;
        align-items: center;
        gap: var(--space-3);
        background: #fef2f2;
        border: 1px solid #fecaca;
        border-radius: var(--radius-md);
        padding: var(--space-4);
        font-size: var(--font-size-sm);
        color: var(--danger);
    }
    .error-icon { font-size: 1.1em; }
</style>

@code {
    [Parameter] public string? Message { get; set; }
    [Parameter] public EventCallback OnRetry { get; set; }
}
```

- [ ] **Step 6: Create `Modal.razor`**

Create `src/Client/Components/Shared/Modal.razor`:

```razor
@if (IsOpen)
{
    <div class="modal-backdrop" @onclick="HandleBackdropClick"></div>
    <div class="modal-shell" role="dialog" aria-modal="true" aria-labelledby="modal-title">
        <div class="modal-header">
            <h2 class="modal-title" id="modal-title">@Title</h2>
            <button class="modal-close" @onclick="OnClose" aria-label="Close">✕</button>
        </div>
        <div class="modal-body">
            @ChildContent
        </div>
    </div>
}

@code {
    [Parameter] public bool IsOpen { get; set; }
    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public bool CloseOnBackdrop { get; set; } = true;

    private void HandleBackdropClick()
    {
        if (CloseOnBackdrop) OnClose.InvokeAsync();
    }
}
```

Create `src/Client/Components/Shared/Modal.razor.css`:

```css
.modal-backdrop {
    position: fixed; inset: 0;
    background: rgba(0,0,0,.45);
    z-index: 200;
}
.modal-shell {
    position: fixed;
    top: 50%; left: 50%;
    transform: translate(-50%, -50%);
    z-index: 201;
    background: var(--white);
    border-radius: var(--radius-lg);
    box-shadow: var(--shadow-modal);
    width: min(520px, 92vw);
    max-height: 90vh;
    overflow-y: auto;
}
.modal-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: var(--space-6);
    border-bottom: 1px solid var(--border);
}
.modal-title  { font-size: var(--font-size-xl); font-weight: var(--font-weight-semibold); }
.modal-close  { background: none; border: none; cursor: pointer; font-size: 1.2rem; color: var(--text-muted); padding: var(--space-1); }
.modal-close:hover { color: var(--text-primary); }
.modal-body   { padding: var(--space-6); }
```

- [ ] **Step 7: Add `@using` for Components.Shared to `_Imports.razor`**

Edit `src/Client/_Imports.razor` — append:

```razor
@using Stallions.Client.Components.Shared
@using Stallions.Client.Components.Listings
@using Stallions.Client.Components.Checkout
@using Stallions.Client.Components.Enquiries
```

- [ ] **Step 8: Run tests — expect green**

```
dotnet test tests/Client.Tests --filter "EmptyState|Modal" -v minimal
```

Expected: 4 tests PASS.

- [ ] **Step 9: Commit**

```
git add src/Client/Components/Shared/ \
        src/Client/_Imports.razor \
        tests/Client.Tests/Components/Shared/
git commit -m "feat: add LoadingSpinner, EmptyState, ErrorMessage, Modal shared components"
```

---

### Task 7: API services (full implementations)

**Why:** Plan 3a Task 3 created one-liner stubs so `Program.cs` would compile. This task replaces them with full implementations that the pages in Plan 3c will call.

**Files:**
- Modify: `src/Client/Services/ListingApiService.cs`
- Modify: `src/Client/Services/StallionApiService.cs`
- Modify: `src/Client/Services/BidApiService.cs`
- Modify: `src/Client/Services/CheckoutApiService.cs`
- Modify: `src/Client/Services/EnquiryApiService.cs`
- Modify: `src/Client/Services/UserApiService.cs`
- Test: `tests/Client.Tests/Services/ListingApiServiceTests.cs`

---

- [ ] **Step 1: Write `ListingApiService` tests**

Create `tests/Client.Tests/Services/ListingApiServiceTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Bunit;
using FluentAssertions;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Listings;

namespace Stallions.Client.Tests.Services;

public class ListingApiServiceTests
{
    private static HttpClient FakeClient(HttpStatusCode status, object body)
    {
        var handler = new FakeHttpMessageHandler(status, body);
        return new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
    }

    [Fact]
    public async Task GetListingsAsync_ReturnsCards_OnSuccess()
    {
        var cards = new List<ListingCardDto>
        {
            new() { Id = Guid.NewGuid(), StallionName = "Fastnet Rock", ListingType = "FixedPrice" }
        };
        var sut = new ListingApiService(FakeClient(HttpStatusCode.OK, cards));

        var result = await sut.GetListingsAsync();

        result.Should().HaveCount(1);
        result[0].StallionName.Should().Be("Fastnet Rock");
    }

    [Fact]
    public async Task GetListingsAsync_Throws_On500()
    {
        var sut = new ListingApiService(FakeClient(HttpStatusCode.InternalServerError, "error"));

        await Assert.ThrowsAsync<ApiException>(() => sut.GetListingsAsync());
    }
}

// Minimal fake handler — reuse across service tests
public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly object _body;
    public FakeHttpMessageHandler(HttpStatusCode status, object body) { _status = status; _body = body; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = new HttpResponseMessage(_status)
        {
            Content = JsonContent.Create(_body)
        };
        return Task.FromResult(response);
    }
}
```

- [ ] **Step 2: Run test — expect compile failure**

```
dotnet test tests/Client.Tests --filter "ListingApiService" -v minimal
```

Expected: Build error — `GetListingsAsync` method not on the stub class.

- [ ] **Step 3: Implement `ListingApiService`**

Replace `src/Client/Services/ListingApiService.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Listings;

namespace Stallions.Client.Services;

public class ListingApiService
{
    private readonly HttpClient _http;
    public ListingApiService(HttpClient http) => _http = http;

    public async Task<List<ListingCardDto>> GetListingsAsync(
        Guid? seasonId = null, Guid? studFarmId = null, string? type = null)
    {
        var qs = new List<string>();
        if (seasonId.HasValue)           qs.Add($"seasonId={seasonId}");
        if (studFarmId.HasValue)         qs.Add($"studFarmId={studFarmId}");
        if (!string.IsNullOrEmpty(type)) qs.Add($"type={type}");
        var url = "api/listings" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");

        var response = await _http.GetAsync(url);
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load listings.");
        return await response.Content.ReadFromJsonAsync<List<ListingCardDto>>()
               ?? new List<ListingCardDto>();
    }

    public async Task<ListingDto> GetByIdAsync(Guid id)
    {
        var response = await _http.GetAsync($"api/listings/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ApiException(404, "Listing not found.");
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load listing.");
        return await response.Content.ReadFromJsonAsync<ListingDto>()
               ?? throw new ApiException(500, "Empty response from server.");
    }
}
```

- [ ] **Step 4: Implement `StallionApiService`**

Replace `src/Client/Services/StallionApiService.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Stallions;

namespace Stallions.Client.Services;

public class StallionApiService
{
    private readonly HttpClient _http;
    public StallionApiService(HttpClient http) => _http = http;

    public async Task<StallionDto> GetByIdAsync(Guid id)
    {
        var response = await _http.GetAsync($"api/stallions/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ApiException(404, "Stallion not found.");
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load stallion.");
        return await response.Content.ReadFromJsonAsync<StallionDto>()
               ?? throw new ApiException(500, "Empty response from server.");
    }
}
```

- [ ] **Step 5: Implement `BidApiService`**

Replace `src/Client/Services/BidApiService.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Bids;

namespace Stallions.Client.Services;

public class BidApiService
{
    private readonly HttpClient _http;
    public BidApiService(HttpClient http) => _http = http;

    public async Task<CurrentBidDto?> GetCurrentBidAsync(Guid listingId)
    {
        var response = await _http.GetAsync($"api/listings/{listingId}/bids/current");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load current bid.");
        return await response.Content.ReadFromJsonAsync<CurrentBidDto>();
    }

    public async Task PlaceBidAsync(Guid listingId, PlaceBidRequest request)
    {
        var response = await _http.PostAsJsonAsync($"api/listings/{listingId}/bids", request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new ApiException((int)response.StatusCode, error.Trim('"'));
        }
    }

    public async Task<List<BidDto>> GetMyBidsAsync()
    {
        var response = await _http.GetAsync("api/bids/mine");
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load bids.");
        return await response.Content.ReadFromJsonAsync<List<BidDto>>()
               ?? new List<BidDto>();
    }
}
```

- [ ] **Step 6: Implement `CheckoutApiService`**

Replace `src/Client/Services/CheckoutApiService.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Checkout;

namespace Stallions.Client.Services;

public class CheckoutApiService
{
    private readonly HttpClient _http;
    public CheckoutApiService(HttpClient http) => _http = http;

    /// <summary>
    /// Posts mare details and initiates checkout. The server creates the purchase record
    /// and returns a disclosure summary. The buyer reviews it, then calls ConfirmCheckoutAsync.
    /// </summary>
    public async Task<CheckoutResponse> InitiateAsync(Guid listingId, CheckoutRequest request)
    {
        var response = await _http.PostAsJsonAsync($"api/listings/{listingId}/checkout", request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new ApiException((int)response.StatusCode, error.Trim('"'));
        }
        return await response.Content.ReadFromJsonAsync<CheckoutResponse>()
               ?? throw new ApiException(500, "Empty response from server.");
    }

    public async Task<List<PurchaseDto>> GetMyPurchasesAsync()
    {
        var response = await _http.GetAsync("api/purchases");
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load purchases.");
        return await response.Content.ReadFromJsonAsync<List<PurchaseDto>>()
               ?? new List<PurchaseDto>();
    }
}
```

- [ ] **Step 7: Implement `EnquiryApiService`**

Replace `src/Client/Services/EnquiryApiService.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Enquiries;

namespace Stallions.Client.Services;

public class EnquiryApiService
{
    private readonly HttpClient _http;
    public EnquiryApiService(HttpClient http) => _http = http;

    public async Task<List<EnquirySummaryDto>> GetAllAsync()
    {
        var response = await _http.GetAsync("api/enquiries");
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load enquiries.");
        return await response.Content.ReadFromJsonAsync<List<EnquirySummaryDto>>()
               ?? new List<EnquirySummaryDto>();
    }

    public async Task<EnquiryDto> GetByIdAsync(Guid id)
    {
        var response = await _http.GetAsync($"api/enquiries/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ApiException(404, "Enquiry not found.");
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load enquiry.");
        return await response.Content.ReadFromJsonAsync<EnquiryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public async Task PostMessageAsync(Guid enquiryId, SendMessageRequest request)
    {
        var response = await _http.PostAsJsonAsync($"api/enquiries/{enquiryId}/messages", request);
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to send message.");
    }

    public async Task<EnquiryDto> CreateAsync(Guid listingId, OpenEnquiryRequest request)
    {
        var response = await _http.PostAsJsonAsync($"api/listings/{listingId}/enquiries", request);
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to open enquiry.");
        return await response.Content.ReadFromJsonAsync<EnquiryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }
}
```

- [ ] **Step 8: Implement `UserApiService`**

Replace `src/Client/Services/UserApiService.cs`:

```csharp
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Services;

public class UserApiService
{
    private readonly HttpClient _http;
    public UserApiService(HttpClient http) => _http = http;

    public async Task<UserDto?> GetMeAsync()
    {
        var response = await _http.GetAsync("api/users/me");
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<UserDto>();
    }
}
```

- [ ] **Step 9: Run all service tests — expect green**

```
dotnet test tests/Client.Tests --filter "ApiService" -v minimal
```

Expected: 3 tests PASS (the 2 `ListingApiService` tests + any others written above).

- [ ] **Step 10: Build client**

```
dotnet build src/Client -v minimal
```

Expected: 0 errors.

- [ ] **Step 11: Commit**

```
git add src/Client/Services/ \
        tests/Client.Tests/Services/
git commit -m "feat: implement all typed API services (ListingApiService, BidApiService, etc.)"
```

---

### Task 8: Listing components (ListingCard, ListingGrid, AuctionTimer, PriceDisplay, FilterBar)

**Why:** These are the most-used components — they appear on the Home page, ListingDetail, StallionDetail, and StudFarmDetail. Building and thoroughly testing them here means the page tasks in Plan 3c stay lightweight.

**Note:** This task also adds `TotalQuantity` to `ListingCardDto` (and `MapToCardDto` in `ListingService`) so the fixed-price progress bar can show a meaningful fill percentage.

**Files:**
- Modify: `src/Shared/DTOs/Listings/ListingCardDto.cs` — add `TotalQuantity`
- Modify: `src/Server/Services/ListingService.cs` — populate `TotalQuantity` in `MapToCardDto`
- Create: `src/Client/Components/Listings/PriceDisplay.razor`
- Create: `src/Client/Components/Listings/AuctionTimer.razor` + `.razor.css`
- Create: `src/Client/Components/Listings/FilterBar.razor` + `.razor.css`
- Create: `src/Client/Components/Listings/ListingCard.razor` + `.razor.css`
- Create: `src/Client/Components/Listings/ListingGrid.razor` + `.razor.css`
- Test: `tests/Client.Tests/Components/Listings/ListingCardTests.cs`
- Test: `tests/Client.Tests/Components/Listings/AuctionTimerTests.cs`

---

- [ ] **Step 1: Add `TotalQuantity` to `ListingCardDto`**

Edit `src/Shared/DTOs/Listings/ListingCardDto.cs` — add the property (after `QuantityRemaining`):

```csharp
/// <summary>Original quantity for fixed-price listings (used for progress bar fill).</summary>
public int? TotalQuantity { get; set; }
```

Edit `src/Server/Services/ListingService.cs` — in the `MapToCardDto` FixedPriceListing branch, add:

```csharp
TotalQuantity = fpl.Quantity
```

- [ ] **Step 2: Write component tests**

Create `tests/Client.Tests/Components/Listings/ListingCardTests.cs`:

```csharp
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Stallions.Client.Components.Listings;
using Stallions.Shared.DTOs.Listings;

namespace Stallions.Client.Tests.Components.Listings;

public class ListingCardTests : TestContext
{
    private static ListingCardDto FixedPriceCard() => new()
    {
        Id = Guid.NewGuid(),
        ListingType = "FixedPrice",
        StallionName = "Fastnet Rock",
        StudFarmName = "Coolmore Australia",
        PriceIncGst = 8000m,
        QuantityRemaining = 4,
        TotalQuantity = 10
    };

    private static ListingCardDto AuctionCard() => new()
    {
        Id = Guid.NewGuid(),
        ListingType = "Auction",
        StallionName = "Snitzel",
        StudFarmName = "Arrowfield Stud",
        PriceIncGst = 5000m,
        CurrentHighestBidIncGst = 6500m,
        BidCount = 3,
        AuctionClosesAt = DateTime.UtcNow.AddDays(2)
    };

    [Fact]
    public void ListingCard_FixedPrice_ShowsBuyNowBadge()
    {
        this.AddTestAuthorization();
        Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(
            new FakeNavigationManager());

        var cut = RenderComponent<ListingCard>(p => p.Add(c => c.Listing, FixedPriceCard()));

        cut.Find(".badge-buynow").Should().NotBeNull();
        cut.Find(".card-stallion-name").TextContent.Should().Be("Fastnet Rock");
    }

    [Fact]
    public void ListingCard_Auction_ShowsBidCount()
    {
        this.AddTestAuthorization();
        Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(
            new FakeNavigationManager());

        var cut = RenderComponent<ListingCard>(p => p.Add(c => c.Listing, AuctionCard()));

        cut.Find(".badge-auction").Should().NotBeNull();
        cut.Markup.Should().Contain("3 bids");
    }

    [Fact]
    public void ListingCard_Auction_ShowsHighestBidAsPrice()
    {
        this.AddTestAuthorization();
        Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(
            new FakeNavigationManager());

        var cut = RenderComponent<ListingCard>(p => p.Add(c => c.Listing, AuctionCard()));

        // $6,500 is the current highest bid — should be the displayed price
        cut.Markup.Should().Contain("6,500");
    }
}
```

Create `tests/Client.Tests/Components/Listings/AuctionTimerTests.cs`:

```csharp
using Bunit;
using FluentAssertions;
using Stallions.Client.Components.Listings;

namespace Stallions.Client.Tests.Components.Listings;

public class AuctionTimerTests : TestContext
{
    [Fact]
    public void AuctionTimer_ShowsHoursAndMinutes()
    {
        var closesAt = DateTime.UtcNow.AddHours(5).AddMinutes(30);
        var cut = RenderComponent<AuctionTimer>(p => p.Add(c => c.ClosesAt, closesAt));

        cut.Markup.Should().Contain("5");   // hours
        cut.Markup.Should().Contain("30");  // minutes
    }

    [Fact]
    public void AuctionTimer_LessThan6Hours_AppliesUrgentClass()
    {
        var closesAt = DateTime.UtcNow.AddHours(3);
        var cut = RenderComponent<AuctionTimer>(p => p.Add(c => c.ClosesAt, closesAt));

        cut.Find(".auction-timer--urgent").Should().NotBeNull();
    }
}
```

- [ ] **Step 3: Run tests — expect compile failure**

```
dotnet test tests/Client.Tests --filter "ListingCard|AuctionTimer" -v minimal
```

Expected: Build error — components don't exist yet.

- [ ] **Step 4: Create `PriceDisplay.razor`**

Create `src/Client/Components/Listings/PriceDisplay.razor`:

```razor
<span class="price-display">@Amount.ToString("C0", AustralianCulture)</span>

@code {
    [Parameter, EditorRequired] public decimal Amount { get; set; }
    [Parameter] public bool Large { get; set; }

    private static readonly System.Globalization.CultureInfo AustralianCulture =
        System.Globalization.CultureInfo.GetCultureInfo("en-AU");
}
```

- [ ] **Step 5: Create `AuctionTimer.razor`**

Create `src/Client/Components/Listings/AuctionTimer.razor`:

```razor
@implements IDisposable

<div class="auction-timer @(IsUrgent ? "auction-timer--urgent" : "")">
    @if (Days > 0)
    {
        <span class="timer-segment">@Days<small>d</small></span>
        <span class="timer-sep">·</span>
    }
    <span class="timer-segment">@Hours<small>h</small></span>
    <span class="timer-sep">·</span>
    <span class="timer-segment">@Minutes<small>m</small></span>
</div>

@code {
    [Parameter, EditorRequired] public DateTime ClosesAt { get; set; }

    private int Days;
    private int Hours;
    private int Minutes;
    private bool IsUrgent;
    private System.Threading.Timer? _timer;

    protected override void OnInitialized()
    {
        UpdateCountdown();
        _timer = new System.Threading.Timer(
            _ => InvokeAsync(() => { UpdateCountdown(); StateHasChanged(); }),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));
    }

    private void UpdateCountdown()
    {
        var remaining = ClosesAt - DateTime.UtcNow;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        IsUrgent = remaining.TotalHours < 6;
        Days    = (int)remaining.TotalDays;
        Hours   = remaining.Hours;
        Minutes = remaining.Minutes;
    }

    public void Dispose() => _timer?.Dispose();
}
```

Create `src/Client/Components/Listings/AuctionTimer.razor.css`:

```css
.auction-timer { display: flex; align-items: center; gap: var(--space-2); font-size: var(--font-size-sm); color: var(--text-secondary); }
.timer-segment { font-weight: var(--font-weight-semibold); }
.timer-segment small { font-weight: var(--font-weight-normal); margin-left: 1px; }
.timer-sep { color: var(--border); }
.auction-timer--urgent { color: var(--danger); }
```

- [ ] **Step 6: Create `FilterBar.razor`**

Create `src/Client/Components/Listings/FilterBar.razor`:

```razor
<div class="filter-bar">
    <div class="filter-pills">
        <button class="filter-pill @(ActiveFilter == null ? "active" : "")"
                @onclick="() => SetFilter(null)">All</button>
        <button class="filter-pill @(ActiveFilter == "Auction" ? "active" : "")"
                @onclick="() => SetFilter("Auction")">Auctions</button>
        <button class="filter-pill @(ActiveFilter == "FixedPrice" ? "active" : "")"
                @onclick="() => SetFilter("FixedPrice")">Buy Now</button>
    </div>
</div>

@code {
    [Parameter] public string? ActiveFilter { get; set; }
    [Parameter] public EventCallback<string?> OnFilterChanged { get; set; }

    private Task SetFilter(string? filter) => OnFilterChanged.InvokeAsync(filter);
}
```

Create `src/Client/Components/Listings/FilterBar.razor.css`:

```css
.filter-bar {
    background: var(--white);
    border-bottom: 1px solid var(--border);
    padding: var(--space-3) 0;
    position: sticky;
    top: var(--nav-height);
    z-index: 50;
}
.filter-pills {
    display: flex;
    gap: var(--space-2);
    overflow-x: auto;
    scrollbar-width: none;
    padding-inline: var(--space-4);
}
.filter-pills::-webkit-scrollbar { display: none; }

.filter-pill {
    flex-shrink: 0;
    padding: var(--space-2) var(--space-4);
    border-radius: var(--radius-pill);
    border: 1.5px solid var(--border);
    background: transparent;
    font-size: var(--font-size-sm);
    font-weight: var(--font-weight-medium);
    color: var(--text-secondary);
    cursor: pointer;
    white-space: nowrap;
    transition: background .15s, color .15s, border-color .15s;
}
.filter-pill:hover { border-color: var(--navy); color: var(--navy); }
.filter-pill.active { background: var(--navy); color: var(--white); border-color: var(--navy); }
```

- [ ] **Step 7: Create `ListingCard.razor`**

Create `src/Client/Components/Listings/ListingCard.razor`:

```razor
@inject NavigationManager Nav

<article class="listing-card" @onclick="NavigateToListing" tabindex="0" role="button"
         aria-label="@Listing.StallionName nomination — @(Listing.ListingType == "Auction" ? "Auction" : "Buy Now")">

    <div class="card-image">
        @if (!string.IsNullOrEmpty(Listing.PrimaryImagePath))
        {
            <img src="@Listing.PrimaryImagePath" alt="@Listing.StallionName" loading="lazy" />
        }
        else
        {
            <div class="card-image-placeholder"></div>
        }
        <div class="card-image-overlay"></div>

        <span class="badge @GetBadgeClass()">@GetBadgeText()</span>

        <div class="card-title-overlay">
            <div class="card-stallion-name">@Listing.StallionName</div>
            <div class="card-farm-name">@Listing.StudFarmName</div>
        </div>
    </div>

    <div class="card-body">
        <div class="card-price-row">
            <div>
                <PriceDisplay Amount="@GetDisplayPrice()" />
                <span class="card-gst-label">inc. GST</span>
            </div>
            @if (Listing.ListingType == "Auction" && Listing.BidCount.HasValue)
            {
                <span class="card-meta">@Listing.BidCount bid@(Listing.BidCount != 1 ? "s" : "")</span>
            }
            @if (Listing.ListingType == "FixedPrice" && Listing.QuantityRemaining.HasValue)
            {
                <span class="card-meta @(Listing.QuantityRemaining <= 3 ? "card-meta--urgent" : "")">
                    @Listing.QuantityRemaining remaining
                </span>
            }
        </div>

        <div class="progress-bar-track" style="margin-block: var(--space-2)">
            <div class="progress-bar-fill @GetProgressClass()" style="width: @GetProgressWidthPct()%"></div>
        </div>

        @if (Listing.ListingType == "Auction" && Listing.AuctionClosesAt.HasValue)
        {
            <AuctionTimer ClosesAt="@Listing.AuctionClosesAt.Value" />
        }

        <div class="card-actions">
            <button class="btn btn-primary btn-sm"
                    @onclick:stopPropagation="true"
                    @onclick="NavigateToListing">
                @(Listing.ListingType == "Auction" ? "Place a bid" : "Purchase nomination")
            </button>
            <button class="btn btn-outline btn-sm"
                    @onclick:stopPropagation="true"
                    @onclick="HandleEnquire">
                Enquire
            </button>
        </div>
    </div>
</article>

@code {
    [Parameter, EditorRequired] public ListingCardDto Listing { get; set; } = null!;

    private void NavigateToListing() => Nav.NavigateTo($"/listings/{Listing.Id}");
    private void HandleEnquire()     => Nav.NavigateTo($"/listings/{Listing.Id}?enquire=1");

    private bool IsEndingSoon() =>
        Listing.AuctionClosesAt.HasValue &&
        (Listing.AuctionClosesAt.Value - DateTime.UtcNow).TotalHours < 6;

    private string GetBadgeClass() => Listing.ListingType == "Auction"
        ? (IsEndingSoon() ? "badge-ending" : "badge-auction")
        : "badge-buynow";

    private string GetBadgeText() => Listing.ListingType == "Auction"
        ? (IsEndingSoon() ? "Ending soon" : "Auction")
        : "Buy Now";

    private decimal GetDisplayPrice() =>
        Listing.ListingType == "Auction" && Listing.CurrentHighestBidIncGst.HasValue
            ? Listing.CurrentHighestBidIncGst.Value
            : Listing.PriceIncGst;

    private string GetProgressClass()
    {
        if (Listing.ListingType == "Auction")
            return IsEndingSoon() ? "progress-bar-fill--urgent" : "progress-bar-fill--gold";
        return Listing.QuantityRemaining <= 3 ? "progress-bar-fill--urgent" : "progress-bar-fill--navy";
    }

    private double GetProgressWidthPct()
    {
        if (Listing.ListingType == "Auction" && Listing.AuctionClosesAt.HasValue)
        {
            var remaining = Listing.AuctionClosesAt.Value - DateTime.UtcNow;
            if (remaining.TotalSeconds <= 0) return 100;
            // Show time elapsed as % of a 7-day window (visual approximation)
            var elapsed = TimeSpan.FromDays(7) - remaining;
            return Math.Clamp(elapsed.TotalSeconds / TimeSpan.FromDays(7).TotalSeconds * 100, 5, 99);
        }
        if (Listing.TotalQuantity is > 0 && Listing.QuantityRemaining.HasValue)
        {
            var sold = Listing.TotalQuantity.Value - Listing.QuantityRemaining.Value;
            return Math.Clamp((double)sold / Listing.TotalQuantity.Value * 100, 0, 100);
        }
        return 50;
    }
}
```

Create `src/Client/Components/Listings/ListingCard.razor.css`:

```css
.listing-card {
    background: var(--white);
    border-radius: var(--radius-lg);
    box-shadow: var(--shadow-card);
    overflow: hidden;
    cursor: pointer;
    display: flex;
    flex-direction: column;
    transition: transform .2s, box-shadow .2s;
}
.listing-card:hover {
    transform: translateY(-3px);
    box-shadow: var(--shadow-hover);
}
.listing-card:hover .card-image img { transform: scale(1.05); }

/* Image area */
.card-image {
    position: relative;
    height: 210px;
    overflow: hidden;
    background: var(--warm-grey);
}
.card-image img {
    width: 100%; height: 100%;
    object-fit: cover;
    transition: transform .4s;
}
.card-image-placeholder {
    width: 100%; height: 100%;
    background: linear-gradient(135deg, var(--warm-grey) 0%, var(--border) 100%);
}
.card-image-overlay {
    position: absolute;
    inset: 0;
    background: linear-gradient(to top, rgba(0,0,0,.55) 0%, transparent 55%);
}

/* Badge top-left */
.listing-card .badge {
    position: absolute;
    top: var(--space-3);
    left: var(--space-3);
}

/* Title overlay on image */
.card-title-overlay {
    position: absolute;
    bottom: var(--space-3);
    left: var(--space-3);
    right: var(--space-3);
}
.card-stallion-name {
    font-size: var(--font-size-lg);
    font-weight: var(--font-weight-bold);
    color: var(--white);
    line-height: 1.2;
}
.card-farm-name {
    font-size: var(--font-size-xs);
    color: rgba(255,255,255,.8);
    margin-top: 2px;
}

/* Card body */
.card-body { padding: var(--space-4); display: flex; flex-direction: column; gap: var(--space-3); flex: 1; }

.card-price-row {
    display: flex;
    justify-content: space-between;
    align-items: flex-end;
}
.card-gst-label {
    display: block;
    font-size: var(--font-size-xs);
    color: var(--text-muted);
    margin-top: 2px;
}
.card-meta         { font-size: var(--font-size-xs); color: var(--text-muted); }
.card-meta--urgent { color: var(--danger); font-weight: var(--font-weight-semibold); }

.card-actions { display: flex; gap: var(--space-2); margin-top: auto; }
.card-actions .btn { flex: 1; }
```

- [ ] **Step 8: Create `ListingGrid.razor`**

Create `src/Client/Components/Listings/ListingGrid.razor`:

```razor
@if (Listings is null)
{
    <LoadingSpinner Large="true" />
}
else if (!Listings.Any())
{
    <EmptyState Message="No nominations found" Icon="🐎">
        <a href="/" class="btn btn-outline">Clear filters</a>
    </EmptyState>
}
else
{
    <div class="listing-grid">
        @foreach (var listing in Listings)
        {
            <ListingCard Listing="@listing" />
        }
    </div>
}

@code {
    [Parameter] public IReadOnlyList<ListingCardDto>? Listings { get; set; }
}
```

Create `src/Client/Components/Listings/ListingGrid.razor.css`:

```css
.listing-grid {
    display: grid;
    grid-template-columns: 1fr;
    gap: var(--space-6);
}
```

Add these breakpoint overrides to `src/Client/wwwroot/css/app.css` (append under existing responsive section):

```css
/* ListingGrid breakpoints — ::deep doesn't work inside @media so these live here */
@media (min-width: 640px) {
    .listing-grid { grid-template-columns: repeat(2, 1fr); }
}
@media (min-width: 1024px) {
    .listing-grid { grid-template-columns: repeat(3, 1fr); }
}
```

- [ ] **Step 9: Run all listing component tests — expect green**

```
dotnet test tests/Client.Tests --filter "ListingCard|AuctionTimer" -v minimal
```

Expected: 5 tests PASS.

- [ ] **Step 10: Build server (to verify `TotalQuantity` addition didn't break anything)**

```
dotnet build src/Server -v minimal && dotnet test tests/Server.Tests -v minimal
```

Expected: 0 build errors, all server tests PASS.

- [ ] **Step 11: Commit**

```
git add src/Shared/DTOs/Listings/ListingCardDto.cs \
        src/Server/Services/ListingService.cs \
        src/Client/Components/Listings/ \
        src/Client/wwwroot/css/app.css \
        tests/Client.Tests/Components/Listings/
git commit -m "feat: add listing components (ListingCard, ListingGrid, AuctionTimer, PriceDisplay, FilterBar)"
```
