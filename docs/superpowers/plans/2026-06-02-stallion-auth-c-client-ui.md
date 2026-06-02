# Stallion Authorization — Plan C: Client UI

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build all Blazor client pieces: `DirectoryApiService` (Staff CRUD calls), updated `StaffApiService` and `AdminApiService` (new method wrappers), updated `client/Program.cs`, Staff directory CRUD pages, the new `StaffStudFarmDetail` page, updated Staff farm-list and new-farm pages, and the rewritten stud-admin My Stallions + stallion edit pages.

**Architecture:** `DirectoryApiService` is a new authenticated service registered alongside the existing `StaffApiService`. Staff pages follow the same pattern as existing Staff pages (inject `StaffApiService`/`DirectoryApiService`, `UserState`, `Nav`). Stud-admin pages use `AdminApiService`. No new CSS — use the existing `admin.css` tokens (`admin-page-header`, `admin-table`, `admin-form-card`, `btn-gold`, etc.).

**Tech Stack:** Blazor WASM, existing CSS design system

**Depends on:** Plans A and B must be complete and the server must be running locally for smoke testing.

---

### Task 1: `DirectoryApiService` + update client `Program.cs`

**Files:**
- Create: `src/Client/Services/DirectoryApiService.cs`
- Modify: `src/Client/Program.cs`

- [ ] **Step 1: Create `DirectoryApiService.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Directory;

namespace Stallions.Client.Services;

/// <summary>
/// Staff-only service for Stud Directory and Stallion Directory CRUD.
/// Registered with BaseAddressAuthorizationMessageHandler — always requires a Bearer token.
/// </summary>
public class DirectoryApiService
{
    private readonly HttpClient _http;
    public DirectoryApiService(HttpClient http) => _http = http;

    // ── Stud Directory ────────────────────────────────────────────────────

    public virtual async Task<List<StudDirectorySummaryDto>> GetStudDirectoriesAsync(bool includeInactive = false)
    {
        var r = await _http.GetAsync($"api/stud-directory?includeInactive={includeInactive}");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load stud directory.");
        return await r.Content.ReadFromJsonAsync<List<StudDirectorySummaryDto>>() ?? [];
    }

    public virtual async Task<StudDirectoryDto> GetStudDirectoryAsync(Guid id)
    {
        var r = await _http.GetAsync($"api/stud-directory/{id}");
        if (r.StatusCode == HttpStatusCode.NotFound)
            throw new ApiException(404, "Stud directory entry not found.");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load stud directory entry.");
        return await r.Content.ReadFromJsonAsync<StudDirectoryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<StudDirectoryDto> CreateStudDirectoryAsync(CreateStudDirectoryRequest request)
    {
        var r = await _http.PostAsJsonAsync("api/stud-directory", request);
        if (!r.IsSuccessStatusCode)
        {
            var msg = await r.Content.ReadAsStringAsync();
            throw new ApiException((int)r.StatusCode, string.IsNullOrWhiteSpace(msg)
                ? "Failed to create stud directory entry." : msg);
        }
        return await r.Content.ReadFromJsonAsync<StudDirectoryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<StudDirectoryDto> UpdateStudDirectoryAsync(Guid id, UpdateStudDirectoryRequest request)
    {
        var r = await _http.PutAsJsonAsync($"api/stud-directory/{id}", request);
        if (!r.IsSuccessStatusCode)
        {
            var msg = await r.Content.ReadAsStringAsync();
            throw new ApiException((int)r.StatusCode, string.IsNullOrWhiteSpace(msg)
                ? "Failed to update stud directory entry." : msg);
        }
        return await r.Content.ReadFromJsonAsync<StudDirectoryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    // ── Stallion Directory ────────────────────────────────────────────────

    public virtual async Task<List<StallionDirectorySummaryDto>> GetStallionDirectoriesAsync(
        bool includeInactive = false, Guid? studDirectoryId = null)
    {
        var url = $"api/stallion-directory?includeInactive={includeInactive}";
        if (studDirectoryId.HasValue) url += $"&studDirectoryId={studDirectoryId}";
        var r = await _http.GetAsync(url);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load stallion directory.");
        return await r.Content.ReadFromJsonAsync<List<StallionDirectorySummaryDto>>() ?? [];
    }

    public virtual async Task<StallionDirectoryDto> GetStallionDirectoryAsync(Guid id)
    {
        var r = await _http.GetAsync($"api/stallion-directory/{id}");
        if (r.StatusCode == HttpStatusCode.NotFound)
            throw new ApiException(404, "Stallion directory entry not found.");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load stallion directory entry.");
        return await r.Content.ReadFromJsonAsync<StallionDirectoryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<StallionDirectoryDto> CreateStallionDirectoryAsync(CreateStallionDirectoryRequest request)
    {
        var r = await _http.PostAsJsonAsync("api/stallion-directory", request);
        if (!r.IsSuccessStatusCode)
        {
            var msg = await r.Content.ReadAsStringAsync();
            throw new ApiException((int)r.StatusCode, string.IsNullOrWhiteSpace(msg)
                ? "Failed to create stallion directory entry." : msg);
        }
        return await r.Content.ReadFromJsonAsync<StallionDirectoryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<StallionDirectoryDto> UpdateStallionDirectoryAsync(Guid id, UpdateStallionDirectoryRequest request)
    {
        var r = await _http.PutAsJsonAsync($"api/stallion-directory/{id}", request);
        if (!r.IsSuccessStatusCode)
        {
            var msg = await r.Content.ReadAsStringAsync();
            throw new ApiException((int)r.StatusCode, string.IsNullOrWhiteSpace(msg)
                ? "Failed to update stallion directory entry." : msg);
        }
        return await r.Content.ReadFromJsonAsync<StallionDirectoryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }
}
```

- [ ] **Step 2: Register `DirectoryApiService` in `src/Client/Program.cs`**

Add after the `StaffApiService` registration:

```csharp
builder.Services.AddHttpClient<DirectoryApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
```

- [ ] **Step 3: Build client project**

```
dotnet build src/Client
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add src/Client/Services/DirectoryApiService.cs src/Client/Program.cs
git commit -m "feat: DirectoryApiService + register in client DI"
```

---

### Task 2: Update `StaffApiService` and `AdminApiService`

**Files:**
- Modify: `src/Client/Services/StaffApiService.cs`
- Modify: `src/Client/Services/AdminApiService.cs`

- [ ] **Step 1: Add `GetStudFarmsAsync` update + new methods to `StaffApiService.cs`**

Add these methods to the existing `StaffApiService` class (in the `// ── Stud Farms ──` section and a new `// ── Directory ──` section):

```csharp
    // ── Stud Farms (additions) ─────────────────────────────────────────────

    public virtual async Task<StudFarmSummaryDto> GetStudFarmAsync(Guid id)
    {
        var r = await _http.GetAsync($"api/admin/studfarms/{id}");
        if (r.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new ApiException(404, "Stud farm not found.");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load stud farm.");
        return await r.Content.ReadFromJsonAsync<StudFarmSummaryDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task LinkStudFarmToDirectoryAsync(Guid farmId, Guid studDirectoryId)
    {
        var r = await _http.PutAsJsonAsync(
            $"api/admin/studfarms/{farmId}/link-directory",
            new { StudDirectoryId = studDirectoryId });
        if (!r.IsSuccessStatusCode)
        {
            var msg = await r.Content.ReadAsStringAsync();
            throw new ApiException((int)r.StatusCode, string.IsNullOrWhiteSpace(msg)
                ? "Failed to link stud farm to directory." : msg);
        }
    }
```

Also add the required using at the top of the file:

```csharp
using Stallions.Shared.DTOs.Admin;
```

(It likely already has this — check and add only if missing.)

- [ ] **Step 2: Add new methods to `AdminApiService.cs`**

Add these methods to the stud-farm-admin service (in the `// ── Stallions ──` section):

```csharp
    public virtual async Task<AuthorizedStallionsDto> GetAuthorizedStallionsAsync()
    {
        var r = await _http.GetAsync("api/stallions/authorized");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load authorized stallions.");
        return await r.Content.ReadFromJsonAsync<AuthorizedStallionsDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<StallionDto> AddFromDirectoryAsync(Guid directoryId)
    {
        var r = await _http.PostAsync($"api/stallions/add-from-directory/{directoryId}", null);
        if (!r.IsSuccessStatusCode)
        {
            var msg = await r.Content.ReadAsStringAsync();
            throw new ApiException((int)r.StatusCode, string.IsNullOrWhiteSpace(msg)
                ? "Failed to add stallion from directory." : msg);
        }
        return await r.Content.ReadFromJsonAsync<StallionDto>()
               ?? throw new ApiException(500, "Empty response.");
    }
```

Add the required using at the top of `AdminApiService.cs`:

```csharp
using Stallions.Shared.DTOs.Directory;
```

- [ ] **Step 3: Build**

```
dotnet build src/Client
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add src/Client/Services/StaffApiService.cs src/Client/Services/AdminApiService.cs
git commit -m "feat: StaffApiService + AdminApiService directory method additions"
```

---

### Task 3: Staff Stud Directory pages

**Files:**
- Create: `src/Client/Pages/Staff/StaffStudDirectory.razor`
- Create: `src/Client/Pages/Staff/StaffStudDirectoryDetail.razor`

- [ ] **Step 1: Create the list page `StaffStudDirectory.razor`**

```razor
@page "/staff/stud-directory"
@layout StaffLayout
@attribute [Authorize]
@inject DirectoryApiService DirectoryApi
@inject NavigationManager Nav
@inject UserStateService UserState

<PageTitle>Stud Directory — Staff Admin</PageTitle>

<div class="admin-page-header">
    <h1 class="admin-page-title">Stud Directory</h1>
    <a href="/staff/stud-directory/new" class="btn btn-gold btn-sm">+ Add Entry</a>
</div>

<div style="margin-bottom:var(--space-4);display:flex;gap:var(--space-3);align-items:center">
    <label class="form-check-label" style="font-size:var(--font-size-sm)">
        <input type="checkbox" @bind="_includeInactive" @bind:after="Load" />
        Show inactive
    </label>
</div>

@if (_loading)
{
    <p>Loading…</p>
}
else if (_error != null)
{
    <div class="alert alert-danger">@_error</div>
}
else if (!_entries.Any())
{
    <div class="admin-empty-state">
        <p>No stud directory entries found.</p>
        <a href="/staff/stud-directory/new" class="btn btn-gold">Add First Entry</a>
    </div>
}
else
{
    <table class="admin-table">
        <thead>
            <tr>
                <th>Name</th>
                <th>State</th>
                <th>Arion Study ID</th>
                <th>Stallions</th>
                <th>Active</th>
                <th></th>
            </tr>
        </thead>
        <tbody>
            @foreach (var e in _entries)
            {
                <tr>
                    <td><strong>@e.Name</strong></td>
                    <td>@(e.State ?? "—")</td>
                    <td>@(e.ArionStudId?.ToString() ?? "—")</td>
                    <td>@e.StallionCount</td>
                    <td>
                        <span class="badge @(e.IsActive ? "badge-status-active" : "badge-status-suspended")">
                            @(e.IsActive ? "Active" : "Inactive")
                        </span>
                    </td>
                    <td class="admin-table-actions">
                        <a href="/staff/stud-directory/@e.Id" class="btn btn-sm btn-outline">Edit</a>
                    </td>
                </tr>
            }
        </tbody>
    </table>
}

@code {
    private List<StudDirectorySummaryDto> _entries = [];
    private bool _loading = true;
    private bool _includeInactive;
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
        try { _entries = await DirectoryApi.GetStudDirectoriesAsync(_includeInactive); }
        catch (Exception ex) { _error = ex is ApiException ae ? ae.Message : "Failed to load stud directory."; }
        finally { _loading = false; }
    }
}
```

- [ ] **Step 2: Create the create/edit page `StaffStudDirectoryDetail.razor`**

```razor
@page "/staff/stud-directory/new"
@page "/staff/stud-directory/{Id:guid}"
@layout StaffLayout
@attribute [Authorize]
@inject DirectoryApiService DirectoryApi
@inject NavigationManager Nav
@inject UserStateService UserState

<PageTitle>@(_isNew ? "New Stud Directory Entry" : "Edit Stud Directory Entry") — Staff Admin</PageTitle>

<div class="admin-page-header">
    <h1 class="admin-page-title">@(_isNew ? "New Stud Directory Entry" : "Edit: @_form.Name")</h1>
    <a href="/staff/stud-directory" class="btn btn-outline btn-sm">← Back</a>
</div>

@if (_loading)
{
    <p>Loading…</p>
}
else
{
    <div class="admin-form-card">
        <EditForm Model="_form" OnValidSubmit="SubmitAsync">
            <DataAnnotationsValidator />

            <div class="form-group">
                <label class="form-label">Name <span class="required">*</span></label>
                <InputText class="form-control" @bind-Value="_form.Name" />
                <ValidationMessage For="() => _form.Name" />
            </div>

            <div class="form-group">
                <label class="form-label">Arion Stud ID</label>
                <InputNumber class="form-control" @bind-Value="_form.ArionStudId" />
            </div>

            <div class="form-group">
                <label class="form-label">Website</label>
                <InputText class="form-control" @bind-Value="_form.Website" placeholder="https://" />
            </div>

            <div class="form-group">
                <label class="form-label">Address</label>
                <InputText class="form-control" @bind-Value="_form.Address" />
            </div>

            <div style="display:grid;grid-template-columns:1fr 1fr;gap:var(--space-4)">
                <div class="form-group">
                    <label class="form-label">Town</label>
                    <InputText class="form-control" @bind-Value="_form.Town" />
                </div>
                <div class="form-group">
                    <label class="form-label">State</label>
                    <InputText class="form-control" @bind-Value="_form.State" placeholder="e.g. NSW" />
                </div>
            </div>

            <div class="form-group">
                <label class="form-label">Country</label>
                <InputText class="form-control" @bind-Value="_form.Country" placeholder="Australia" />
            </div>

            <div style="display:grid;grid-template-columns:1fr 1fr;gap:var(--space-4)">
                <div class="form-group">
                    <label class="form-label">Phone</label>
                    <InputText class="form-control" @bind-Value="_form.Phone" />
                </div>
                <div class="form-group">
                    <label class="form-label">Email</label>
                    <InputText class="form-control" @bind-Value="_form.Email" />
                </div>
            </div>

            <div class="form-group">
                <label class="form-label">Logo URL</label>
                <InputText class="form-control" @bind-Value="_form.LogoUrl" placeholder="https://…" />
            </div>

            <div class="form-group">
                <label class="form-check-label">
                    <InputCheckbox @bind-Value="_form.IsActive" /> Active
                </label>
            </div>

            @if (_detail?.Stallions.Any() == true)
            {
                <hr style="margin:var(--space-6) 0" />
                <h3 style="font-size:var(--font-size-base);margin:0 0 var(--space-4)">
                    Stallions in this Stud Directory (@_detail.Stallions.Count)
                </h3>
                <table class="admin-table">
                    <thead>
                        <tr><th>Name</th><th>Year</th><th>Colour</th><th>Active</th><th></th></tr>
                    </thead>
                    <tbody>
                        @foreach (var s in _detail.Stallions)
                        {
                            <tr>
                                <td>@s.Name</td>
                                <td>@s.YearOfBirth</td>
                                <td>@(s.Colour ?? "—")</td>
                                <td>
                                    <span class="badge @(s.IsActive ? "badge-status-active" : "badge-status-suspended")">
                                        @(s.IsActive ? "Active" : "Inactive")
                                    </span>
                                </td>
                                <td><a href="/staff/stallion-directory/@s.Id" class="btn btn-sm btn-outline">Edit</a></td>
                            </tr>
                        }
                    </tbody>
                </table>
            }

            @if (_error != null)
            {
                <div class="alert alert-danger" style="margin-top:var(--space-4)">@_error</div>
            }

            <div style="margin-top:var(--space-6);display:flex;gap:var(--space-3)">
                <button type="submit" class="btn btn-gold" disabled="@_submitting">
                    @(_submitting ? "Saving…" : (_isNew ? "Create Entry" : "Save Changes"))
                </button>
                <a href="/staff/stud-directory" class="btn btn-outline">Cancel</a>
            </div>
        </EditForm>
    </div>
}

@code {
    [Parameter] public Guid? Id { get; set; }

    private bool _isNew => Id is null;
    private bool _loading = true;
    private bool _submitting;
    private string? _error;
    private StudDirectoryDto? _detail;

    // Form model — works for both create and update
    private UpdateStudDirectoryRequest _form = new() { Name = string.Empty, IsActive = true };

    protected override async Task OnInitializedAsync()
    {
        await UserState.LoadAsync();
        if (!UserState.IsStaff) { Nav.NavigateTo("/", replace: true); return; }

        if (!_isNew)
        {
            try
            {
                _detail = await DirectoryApi.GetStudDirectoryAsync(Id!.Value);
                _form = new UpdateStudDirectoryRequest
                {
                    Name = _detail.Name,
                    ArionStudId = _detail.ArionStudId,
                    Website = _detail.Website,
                    Address = _detail.Address,
                    Town = _detail.Town,
                    State = _detail.State,
                    Country = _detail.Country,
                    Phone = _detail.Phone,
                    Email = _detail.Email,
                    LogoUrl = _detail.LogoUrl,
                    IsActive = _detail.IsActive
                };
            }
            catch (Exception ex)
            {
                _error = ex is ApiException ae ? ae.Message : "Failed to load entry.";
            }
        }
        _loading = false;
    }

    private async Task SubmitAsync()
    {
        _submitting = true; _error = null;
        try
        {
            if (_isNew)
            {
                var create = new CreateStudDirectoryRequest
                {
                    Name = _form.Name,
                    ArionStudId = _form.ArionStudId,
                    Website = _form.Website,
                    Address = _form.Address,
                    Town = _form.Town,
                    State = _form.State,
                    Country = _form.Country,
                    Phone = _form.Phone,
                    Email = _form.Email,
                    LogoUrl = _form.LogoUrl,
                    IsActive = _form.IsActive
                };
                await DirectoryApi.CreateStudDirectoryAsync(create);
            }
            else
            {
                await DirectoryApi.UpdateStudDirectoryAsync(Id!.Value, _form);
            }
            Nav.NavigateTo("/staff/stud-directory");
        }
        catch (Exception ex)
        {
            _error = ex is ApiException ae ? ae.Message : "Failed to save entry.";
        }
        finally { _submitting = false; }
    }
}
```

- [ ] **Step 3: Build**

```
dotnet build src/Client
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add src/Client/Pages/Staff/StaffStudDirectory.razor src/Client/Pages/Staff/StaffStudDirectoryDetail.razor
git commit -m "feat: Staff Stud Directory list and edit pages"
```

---

### Task 4: Staff Stallion Directory pages

**Files:**
- Create: `src/Client/Pages/Staff/StaffStallionDirectory.razor`
- Create: `src/Client/Pages/Staff/StaffStallionDirectoryDetail.razor`

- [ ] **Step 1: Create `StaffStallionDirectory.razor`**

```razor
@page "/staff/stallion-directory"
@layout StaffLayout
@attribute [Authorize]
@inject DirectoryApiService DirectoryApi
@inject NavigationManager Nav
@inject UserStateService UserState

<PageTitle>Stallion Directory — Staff Admin</PageTitle>

<div class="admin-page-header">
    <h1 class="admin-page-title">Stallion Directory</h1>
    <a href="/staff/stallion-directory/new" class="btn btn-gold btn-sm">+ Add Entry</a>
</div>

<div style="margin-bottom:var(--space-4);display:flex;gap:var(--space-4);align-items:center;flex-wrap:wrap">
    <div>
        <label class="form-label" style="margin:0;font-size:var(--font-size-sm)">Filter by stud</label>
        <select class="form-select" style="width:auto" @bind="_studFilter" @bind:after="Load">
            <option value="">— All studs —</option>
            @foreach (var s in _studs)
            {
                <option value="@s.Id">@s.Name</option>
            }
        </select>
    </div>
    <label class="form-check-label" style="font-size:var(--font-size-sm);margin-top:var(--space-5)">
        <input type="checkbox" @bind="_includeInactive" @bind:after="Load" />
        Show inactive
    </label>
</div>

@if (_loading)
{
    <p>Loading…</p>
}
else if (_error != null)
{
    <div class="alert alert-danger">@_error</div>
}
else if (!_entries.Any())
{
    <div class="admin-empty-state">
        <p>No stallion directory entries found.</p>
        <a href="/staff/stallion-directory/new" class="btn btn-gold">Add First Entry</a>
    </div>
}
else
{
    <table class="admin-table">
        <thead>
            <tr>
                <th>Name</th>
                <th>Stud</th>
                <th>Year</th>
                <th>Colour</th>
                <th>Stallion ID</th>
                <th>Active</th>
                <th></th>
            </tr>
        </thead>
        <tbody>
            @foreach (var e in _entries)
            {
                <tr>
                    <td><strong>@e.Name</strong></td>
                    <td>@e.StudName</td>
                    <td>@e.YearOfBirth</td>
                    <td>@(e.Colour ?? "—")</td>
                    <td style="font-size:var(--font-size-sm);color:var(--text-muted)">@e.StallionId</td>
                    <td>
                        <span class="badge @(e.IsActive ? "badge-status-active" : "badge-status-suspended")">
                            @(e.IsActive ? "Active" : "Inactive")
                        </span>
                    </td>
                    <td class="admin-table-actions">
                        <a href="/staff/stallion-directory/@e.Id" class="btn btn-sm btn-outline">Edit</a>
                    </td>
                </tr>
            }
        </tbody>
    </table>
}

@code {
    private List<StallionDirectorySummaryDto> _entries = [];
    private List<StudDirectorySummaryDto> _studs = [];
    private bool _loading = true;
    private bool _includeInactive;
    private string _studFilter = string.Empty;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        await UserState.LoadAsync();
        if (!UserState.IsStaff) { Nav.NavigateTo("/", replace: true); return; }
        _studs = await DirectoryApi.GetStudDirectoriesAsync(includeInactive: false);
        await Load();
    }

    private async Task Load()
    {
        _loading = true; _error = null;
        try
        {
            Guid? studId = Guid.TryParse(_studFilter, out var g) ? g : null;
            _entries = await DirectoryApi.GetStallionDirectoriesAsync(_includeInactive, studId);
        }
        catch (Exception ex) { _error = ex is ApiException ae ? ae.Message : "Failed to load stallion directory."; }
        finally { _loading = false; }
    }
}
```

- [ ] **Step 2: Create `StaffStallionDirectoryDetail.razor`**

```razor
@page "/staff/stallion-directory/new"
@page "/staff/stallion-directory/{Id:guid}"
@layout StaffLayout
@attribute [Authorize]
@inject DirectoryApiService DirectoryApi
@inject NavigationManager Nav
@inject UserStateService UserState

<PageTitle>@(_isNew ? "New Stallion Directory Entry" : "Edit Stallion Directory Entry") — Staff Admin</PageTitle>

<div class="admin-page-header">
    <h1 class="admin-page-title">@(_isNew ? "New Stallion Directory Entry" : "Edit: @_form.Name")</h1>
    <a href="/staff/stallion-directory" class="btn btn-outline btn-sm">← Back</a>
</div>

@if (_loading)
{
    <p>Loading…</p>
}
else
{
    <div class="admin-form-card">
        <EditForm Model="_form" OnValidSubmit="SubmitAsync">
            <DataAnnotationsValidator />

            <div class="form-group">
                <label class="form-label">Stud <span class="required">*</span></label>
                <select class="form-select" @bind="_form.StudDirectoryId">
                    <option value="@Guid.Empty">— Select stud —</option>
                    @foreach (var s in _studs)
                    {
                        <option value="@s.Id">@s.Name</option>
                    }
                </select>
            </div>

            <div class="form-group">
                <label class="form-label">Name <span class="required">*</span></label>
                <InputText class="form-control" @bind-Value="_form.Name" />
                <ValidationMessage For="() => _form.Name" />
            </div>

            <div style="display:grid;grid-template-columns:1fr 1fr 1fr;gap:var(--space-4)">
                <div class="form-group">
                    <label class="form-label">Year of Birth</label>
                    <InputNumber class="form-control" @bind-Value="_form.YearOfBirth" />
                </div>
                <div class="form-group">
                    <label class="form-label">Stallion ID (original)</label>
                    <InputNumber class="form-control" @bind-Value="_form.StallionId" />
                </div>
                <div class="form-group">
                    <label class="form-label">Arion ID (0 if n/a)</label>
                    <InputNumber class="form-control" @bind-Value="_form.ArionId" />
                </div>
            </div>

            <div style="display:grid;grid-template-columns:1fr 1fr;gap:var(--space-4)">
                <div class="form-group">
                    <label class="form-label">Colour</label>
                    <InputText class="form-control" @bind-Value="_form.Colour" />
                </div>
                <div class="form-group">
                    <label class="form-label">Height</label>
                    <InputText class="form-control" @bind-Value="_form.Height" placeholder="e.g. 16.2hh" />
                </div>
            </div>

            <div style="display:grid;grid-template-columns:1fr 1fr;gap:var(--space-4)">
                <div class="form-group">
                    <label class="form-label">Sire</label>
                    <InputText class="form-control" @bind-Value="_form.SireName" />
                </div>
                <div class="form-group">
                    <label class="form-label">Dam</label>
                    <InputText class="form-control" @bind-Value="_form.DamName" />
                </div>
            </div>

            <div class="form-group">
                <label class="form-check-label">
                    <InputCheckbox @bind-Value="_form.IsActive" /> Active
                </label>
            </div>

            @if (_error != null)
            {
                <div class="alert alert-danger">@_error</div>
            }

            <div style="margin-top:var(--space-6);display:flex;gap:var(--space-3)">
                <button type="submit" class="btn btn-gold" disabled="@_submitting">
                    @(_submitting ? "Saving…" : (_isNew ? "Create Entry" : "Save Changes"))
                </button>
                <a href="/staff/stallion-directory" class="btn btn-outline">Cancel</a>
            </div>
        </EditForm>
    </div>
}

@code {
    [Parameter] public Guid? Id { get; set; }

    private bool _isNew => Id is null;
    private bool _loading = true;
    private bool _submitting;
    private string? _error;
    private List<StudDirectorySummaryDto> _studs = [];
    private UpdateStallionDirectoryRequest _form = new() { Name = string.Empty, IsActive = true };

    protected override async Task OnInitializedAsync()
    {
        await UserState.LoadAsync();
        if (!UserState.IsStaff) { Nav.NavigateTo("/", replace: true); return; }

        _studs = await DirectoryApi.GetStudDirectoriesAsync();

        if (!_isNew)
        {
            try
            {
                var entry = await DirectoryApi.GetStallionDirectoryAsync(Id!.Value);
                _form = new UpdateStallionDirectoryRequest
                {
                    StudDirectoryId = entry.StudDirectoryId,
                    StallionId = entry.StallionId,
                    ArionId = entry.ArionId,
                    Name = entry.Name,
                    YearOfBirth = entry.YearOfBirth,
                    Colour = entry.Colour,
                    Height = entry.Height,
                    SireName = entry.SireName,
                    DamName = entry.DamName,
                    IsActive = entry.IsActive
                };
            }
            catch (Exception ex)
            {
                _error = ex is ApiException ae ? ae.Message : "Failed to load entry.";
            }
        }
        _loading = false;
    }

    private async Task SubmitAsync()
    {
        if (_form.StudDirectoryId == Guid.Empty) { _error = "Please select a stud."; return; }
        _submitting = true; _error = null;
        try
        {
            if (_isNew)
            {
                var create = new CreateStallionDirectoryRequest
                {
                    StudDirectoryId = _form.StudDirectoryId,
                    StallionId = _form.StallionId,
                    ArionId = _form.ArionId,
                    Name = _form.Name,
                    YearOfBirth = _form.YearOfBirth,
                    Colour = _form.Colour,
                    Height = _form.Height,
                    SireName = _form.SireName,
                    DamName = _form.DamName,
                    IsActive = _form.IsActive
                };
                await DirectoryApi.CreateStallionDirectoryAsync(create);
            }
            else
            {
                await DirectoryApi.UpdateStallionDirectoryAsync(Id!.Value, _form);
            }
            Nav.NavigateTo("/staff/stallion-directory");
        }
        catch (Exception ex)
        {
            _error = ex is ApiException ae ? ae.Message : "Failed to save entry.";
        }
        finally { _submitting = false; }
    }
}
```

- [ ] **Step 3: Build**

```
dotnet build src/Client
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add src/Client/Pages/Staff/StaffStallionDirectory.razor src/Client/Pages/Staff/StaffStallionDirectoryDetail.razor
git commit -m "feat: Staff Stallion Directory list and edit pages"
```

---

### Task 5: Staff farm pages — nav, `StaffStudFarmNew`, `StaffStudFarms`, `StaffStudFarmDetail`

**Files:**
- Modify: `src/Client/Layout/StaffLayout.razor`
- Modify: `src/Client/Pages/Staff/StaffStudFarmNew.razor`
- Modify: `src/Client/Pages/Staff/StaffStudFarms.razor`
- Create: `src/Client/Pages/Staff/StaffStudFarmDetail.razor`

- [ ] **Step 1: Update `StaffLayout.razor` — add directory nav links**

Add two `<NavLink>` items after the existing Stud Farms link:

```razor
            <NavLink href="/staff/stud-directory" class="admin-nav-link" Match="NavLinkMatch.Prefix">
                <span class="admin-nav-icon">📗</span> Stud Directory
            </NavLink>
            <NavLink href="/staff/stallion-directory" class="admin-nav-link" Match="NavLinkMatch.Prefix">
                <span class="admin-nav-icon">📘</span> Stallion Directory
            </NavLink>
```

The updated nav block in full:

```razor
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
            <NavLink href="/staff/stud-directory" class="admin-nav-link" Match="NavLinkMatch.Prefix">
                <span class="admin-nav-icon">📗</span> Stud Directory
            </NavLink>
            <NavLink href="/staff/stallion-directory" class="admin-nav-link" Match="NavLinkMatch.Prefix">
                <span class="admin-nav-icon">📘</span> Stallion Directory
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
```

- [ ] **Step 2: Update `StaffStudFarmNew.razor` — add directory dropdown**

In `@code`, add:

```csharp
    private List<StudDirectorySummaryDto> _studDirectories = [];
    private bool _loadingDirectories = true;
```

In `OnInitializedAsync`, after `_studFarmAdmins` is loaded, add:

```csharp
        try { _studDirectories = await DirectoryApi.GetStudDirectoriesAsync(); }
        catch { /* non-fatal — dropdown will be empty */ }
        finally { _loadingDirectories = false; }
```

Add `@inject DirectoryApiService DirectoryApi` at the top (after existing injects).

In the form markup, add a new form group for the directory dropdown after the Address field:

```razor
        <div class="form-group">
            <label class="form-label">Link to Stud Directory</label>
            @if (_loadingDirectories)
            {
                <p style="font-size:var(--font-size-sm);color:var(--text-muted)">Loading directory…</p>
            }
            else
            {
                <select class="form-select" @bind="_request.StudDirectoryId">
                    <option value="@((Guid?)null)">— None (link later) —</option>
                    @foreach (var d in _studDirectories)
                    {
                        <option value="@d.Id">@d.Name@(d.State != null ? $" ({d.State})" : "")</option>
                    }
                </select>
                <p style="font-size:var(--font-size-sm);color:var(--text-muted);margin-top:var(--space-1)">
                    Linking the farm to its directory entry enables the stud admin's authorized stallion list.
                    Can be set later from the farm's detail page.
                </p>
            }
        </div>
```

**Note:** `_request.StudDirectoryId` is `Guid?` — the `select` binds `null` for the "None" option. Make sure the `CreateStudFarmRequest` DTO already has `public Guid? StudDirectoryId { get; set; }` (added in Plan A Task 4).

- [ ] **Step 3: Update `StaffStudFarms.razor` — add directory column + clickable rows**

Replace the list page with this updated version:

```razor
@page "/staff/studfarms"
@layout StaffLayout
@attribute [Authorize]
@inject StaffApiService StaffApi
@inject NavigationManager Nav
@inject UserStateService UserState

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
                <th>Directory</th>
                <th>Active</th>
                <th>Created</th>
                <th></th>
            </tr>
        </thead>
        <tbody>
            @foreach (var f in _farms)
            {
                <tr>
                    <td><strong>@f.Name</strong></td>
                    <td>@(f.ABN ?? "—")</td>
                    <td>@(f.ContactEmail ?? "—")</td>
                    <td>
                        <div>@f.LinkedUserDisplayName</div>
                        <div style="font-size:var(--font-size-sm);color:var(--text-muted)">@f.LinkedUserEmail</div>
                    </td>
                    <td>
                        @if (f.StudDirectoryId.HasValue)
                        {
                            <span class="badge badge-status-active">@(f.StudDirectoryName ?? "Linked")</span>
                        }
                        else
                        {
                            <span class="badge badge-status-suspended">Not linked</span>
                        }
                    </td>
                    <td>
                        <span class="badge @(f.IsActive ? "badge-status-active" : "badge-status-suspended")">
                            @(f.IsActive ? "Active" : "Inactive")
                        </span>
                    </td>
                    <td>@f.CreatedAt.ToString("d MMM yyyy")</td>
                    <td class="admin-table-actions">
                        <a href="/staff/studfarms/@f.Id" class="btn btn-sm btn-outline">Detail</a>
                    </td>
                </tr>
            }
        </tbody>
    </table>
}

@code {
    private List<StudFarmSummaryDto> _farms = [];
    private bool _loading = true;
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
        try { _farms = await StaffApi.GetStudFarmsAsync(); }
        catch (Exception ex) { _error = ex is ApiException ae ? ae.Message : "Failed to load stud farms."; }
        finally { _loading = false; }
    }
}
```

- [ ] **Step 4: Create `StaffStudFarmDetail.razor` — new detail/link page**

```razor
@page "/staff/studfarms/{Id:guid}"
@layout StaffLayout
@attribute [Authorize]
@inject StaffApiService StaffApi
@inject DirectoryApiService DirectoryApi
@inject NavigationManager Nav
@inject UserStateService UserState

<PageTitle>Stud Farm Detail — Staff Admin</PageTitle>

@if (_loading)
{
    <p>Loading…</p>
}
else if (_error != null)
{
    <div class="alert alert-danger">@_error</div>
}
else if (_farm != null)
{
    <div class="admin-page-header">
        <div>
            <h1 class="admin-page-title">@_farm.Name</h1>
            <p style="color:var(--text-muted);margin:0">@_farm.LinkedUserDisplayName · @_farm.LinkedUserEmail</p>
        </div>
        <a href="/staff/studfarms" class="btn btn-outline btn-sm">← Back to Stud Farms</a>
    </div>

    <div class="admin-detail-card">
        <dl class="admin-dl">
            <dt>ABN</dt>
            <dd>@(_farm.ABN ?? "—")</dd>
            <dt>Contact Email</dt>
            <dd>@(_farm.ContactEmail ?? "—")</dd>
            <dt>Status</dt>
            <dd>
                <span class="badge @(_farm.IsActive ? "badge-status-active" : "badge-status-suspended")">
                    @(_farm.IsActive ? "Active" : "Inactive")
                </span>
            </dd>
            <dt>Created</dt>
            <dd>@_farm.CreatedAt.ToString("d MMMM yyyy")</dd>
        </dl>
    </div>

    <div class="admin-detail-card" style="margin-top:var(--space-5)">
        <h2 style="font-size:var(--font-size-lg);margin:0 0 var(--space-4)">Stallion Directory Link</h2>

        @if (_farm.StudDirectoryId.HasValue)
        {
            <p style="color:var(--text-muted);font-size:var(--font-size-sm);margin:0 0 var(--space-4)">
                This farm is currently linked to <strong>@(_farm.StudDirectoryName ?? _farm.StudDirectoryId.ToString())</strong>.
                You can reassign it below.
            </p>
        }
        else
        {
            <p style="color:var(--text-muted);font-size:var(--font-size-sm);margin:0 0 var(--space-4)">
                This farm is not yet linked to a stud directory entry. Linking it will enable the stud admin's
                authorized stallion list and hide the free-form add button.
            </p>
        }

        <div style="display:flex;gap:var(--space-3);align-items:center;flex-wrap:wrap">
            <select class="form-select" style="width:auto" @bind="_selectedDirectoryId">
                <option value="@Guid.Empty">— Select stud directory entry —</option>
                @foreach (var d in _directories)
                {
                    <option value="@d.Id">@d.Name@(d.State != null ? $" ({d.State})" : "")</option>
                }
            </select>
            <button class="btn btn-gold btn-sm" @onclick="LinkAsync"
                    disabled="@(_linking || _selectedDirectoryId == Guid.Empty)">
                @(_linking ? "Saving…" : "Link to Directory")
            </button>
        </div>

        @if (_linkSuccess)
        {
            <div class="alert alert-success" style="margin-top:var(--space-4)">Directory linked successfully.</div>
        }
        @if (_linkError != null)
        {
            <div class="alert alert-danger" style="margin-top:var(--space-4)">@_linkError</div>
        }
    </div>
}

@code {
    [Parameter] public Guid Id { get; set; }

    private StudFarmSummaryDto? _farm;
    private List<StudDirectorySummaryDto> _directories = [];
    private Guid _selectedDirectoryId = Guid.Empty;
    private bool _loading = true;
    private bool _linking;
    private bool _linkSuccess;
    private string? _error;
    private string? _linkError;

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
            _farm = await StaffApi.GetStudFarmAsync(Id);
            _directories = await DirectoryApi.GetStudDirectoriesAsync();
            // Pre-select the current link if already set
            if (_farm.StudDirectoryId.HasValue)
                _selectedDirectoryId = _farm.StudDirectoryId.Value;
        }
        catch (Exception ex) { _error = ex is ApiException ae ? ae.Message : "Failed to load farm."; }
        finally { _loading = false; }
    }

    private async Task LinkAsync()
    {
        _linking = true; _linkError = null; _linkSuccess = false;
        try
        {
            await StaffApi.LinkStudFarmToDirectoryAsync(Id, _selectedDirectoryId);
            _linkSuccess = true;
            await Load(); // refresh to show updated link name
        }
        catch (Exception ex)
        {
            _linkError = ex is ApiException ae ? ae.Message : "Failed to link directory.";
        }
        finally { _linking = false; }
    }
}
```

- [ ] **Step 5: Build**

```
dotnet build src/Client
```

Expected: 0 errors.

- [ ] **Step 6: Commit**

```
git add src/Client/Layout/StaffLayout.razor src/Client/Pages/Staff/StaffStudFarmNew.razor src/Client/Pages/Staff/StaffStudFarms.razor src/Client/Pages/Staff/StaffStudFarmDetail.razor
git commit -m "feat: Staff farm pages — directory nav, farm detail/link page, directory column"
```

---

### Task 6: Rewrite `AdminStallions.razor` — authorized stable + available-to-add

**Files:**
- Modify: `src/Client/Pages/Admin/AdminStallions.razor`

Replace the entire file:

- [ ] **Step 1: Rewrite `AdminStallions.razor`**

```razor
@page "/admin/stallions"
@layout AdminLayout
@attribute [Authorize]
@inject AdminApiService AdminApi
@inject NavigationManager Nav
@inject UserStateService UserState

<PageTitle>My Stallions — Stallions Australia</PageTitle>

<div class="admin-page-header">
    <h1>My Stallions</h1>
    @* Free-form add is hidden when the farm is directory-managed *@
    @if (_authorized != null && !_authorized.IsLinked)
    {
        <a href="/admin/stallions/new" class="btn btn-primary">+ Add Stallion</a>
    }
</div>

@if (_loading)
{
    <LoadingSpinner />
}
else if (_error is not null)
{
    <ErrorMessage Message="@_error" OnRetry="Load" />
}
else if (_authorized != null)
{
    @* ── Farm not linked ──────────────────────────────────────────────────── *@
    @if (!_authorized.IsLinked)
    {
        <div class="alert alert-warning">
            <strong>Your stud farm hasn't been linked to the stallion directory yet.</strong>
            Please contact Stallions Australia to have your farm linked.
            Once linked, your authorized stallions will appear here.
        </div>
    }

    @* ── Your stable ─────────────────────────────────────────────────────── *@
    <h2 style="font-size:var(--font-size-lg);margin:var(--space-6) 0 var(--space-4)">Your Stable</h2>

    @if (_stallions.Count == 0)
    {
        <EmptyState Message="You haven't added any stallions yet." Icon="🐴" />
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
                    <th>Total Listings</th>
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
                        <td>@s.TotalListingCount</td>
                        <td class="admin-table-actions">
                            <a href="/admin/stallions/@s.Id" class="btn btn-sm btn-secondary">Edit</a>
                            <a href="/admin/listings/new?stallionId=@s.Id" class="btn btn-sm btn-outline">+ Listing</a>
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    }

    @* ── Available to add (directory-linked farms only) ───────────────────── *@
    @if (_authorized.IsLinked)
    {
        <h2 style="font-size:var(--font-size-lg);margin:var(--space-8) 0 var(--space-4)">
            Available to Add
        </h2>

        @if (!_authorized.Available.Any())
        {
            <p style="color:var(--text-muted)">
                All stallions in your stud directory have been added to your stable.
            </p>
        }
        else
        {
            <table class="admin-table">
                <thead>
                    <tr>
                        <th>Name</th>
                        <th>Year</th>
                        <th>Colour</th>
                        <th>Sire</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var d in _authorized.Available)
                    {
                        <tr>
                            <td><strong>@d.Name</strong></td>
                            <td>@d.YearOfBirth</td>
                            <td>@(d.Colour ?? "—")</td>
                            <td>@("—")</td>
                            <td class="admin-table-actions">
                                <button class="btn btn-sm btn-primary"
                                        disabled="@_adding.Contains(d.Id)"
                                        @onclick="() => AddAsync(d.Id)">
                                    @(_adding.Contains(d.Id) ? "Adding…" : "Add to Stable")
                                </button>
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
        }

        @if (_addError != null)
        {
            <div class="alert alert-error" style="margin-top:var(--space-4)">@_addError</div>
        }

        @* ── Missing a stallion? mailto ────────────────────────────────── *@
        <div style="margin-top:var(--space-6)">
            <a href="@_missingMailto" class="btn btn-outline btn-sm">
                Missing a stallion?
            </a>
            <p style="font-size:var(--font-size-sm);color:var(--text-muted);margin-top:var(--space-2)">
                If a stallion is missing from the list above, contact Stallions Australia
                and we'll add it to the directory.
            </p>
        </div>
    }
}

@code {
    private List<StallionSummaryDto> _stallions = [];
    private AuthorizedStallionsDto? _authorized;
    private readonly HashSet<Guid> _adding = [];
    private bool _loading = true;
    private string? _error;
    private string? _addError;
    private string _missingMailto = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        await UserState.LoadAsync();
        if (!UserState.IsStudFarmAdmin) { Nav.NavigateTo("/", replace: true); return; }
        await Load();
    }

    private async Task Load()
    {
        _loading = true; _error = null;
        try
        {
            var tasks = Task.WhenAll(
                AdminApi.GetMyStallionsAsync(),
                AdminApi.GetAuthorizedStallionsAsync());
            await tasks;
            _stallions = tasks.Result[0] as List<StallionSummaryDto> ?? [];

            // GetAuthorizedStallionsAsync returns AuthorizedStallionsDto, not StallionSummaryDto
            // Load them separately for type safety:
            _stallions = await AdminApi.GetMyStallionsAsync();
            _authorized = await AdminApi.GetAuthorizedStallionsAsync();

            if (_authorized != null)
            {
                var farmName = Uri.EscapeDataString(_authorized.FarmName);
                _missingMailto = $"mailto:nominations@stallionsaustralia.com.au" +
                    $"?subject={Uri.EscapeDataString($"Missing Stallion – {_authorized.FarmName}")}" +
                    $"&body={Uri.EscapeDataString("Hi,\n\nI'm missing the following stallion from my Available to Add list:\n\nStallion name: \nYear of birth: \n\nPlease add it to the directory.\n\nThanks")}";
            }
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

    private async Task AddAsync(Guid directoryId)
    {
        _adding.Add(directoryId);
        _addError = null;
        try
        {
            await AdminApi.AddFromDirectoryAsync(directoryId);
            await Load(); // refresh both sections
        }
        catch (ApiException ex)
        {
            _addError = ex.Message;
        }
        finally
        {
            _adding.Remove(directoryId);
        }
    }
}
```

**Note:** The `Task.WhenAll` block in `Load()` has dead code — simplify it by removing the `tasks` variable and keeping only the two separate awaited calls:

```csharp
    private async Task Load()
    {
        _loading = true; _error = null;
        try
        {
            _stallions = await AdminApi.GetMyStallionsAsync();
            _authorized = await AdminApi.GetAuthorizedStallionsAsync();

            if (_authorized != null)
            {
                _missingMailto = $"mailto:nominations@stallionsaustralia.com.au" +
                    $"?subject={Uri.EscapeDataString($"Missing Stallion – {_authorized.FarmName}")}" +
                    $"&body={Uri.EscapeDataString("Hi,\n\nI'm missing the following stallion from my Available to Add list:\n\nStallion name: \nYear of birth: \n\nPlease add it to the directory.\n\nThanks")}";
            }
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
```

Use the clean version above — not the one with `Task.WhenAll`.

- [ ] **Step 2: Build**

```
dotnet build src/Client
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```
git add src/Client/Pages/Admin/AdminStallions.razor
git commit -m "feat: AdminStallions — authorized stable + available-to-add + mailto button"
```

---

### Task 7: Update `AdminStallionForm.razor` — read-only core fields for directory-managed stallions

**Files:**
- Modify: `src/Client/Pages/Admin/AdminStallionForm.razor`

The edit form needs to detect whether the stallion is directory-managed (`StallionDirectoryId` is set on the loaded `StallionDto`). When it is, Name/YearOfBirth/Colour/Sire/Dam are shown as read-only text, not editable inputs. Description, RegistrationNumber, images, and IsActive remain editable.

- [ ] **Step 1: Add directory-managed detection in the `@code` block**

Replace `OnInitializedAsync` to capture the flag:

```csharp
    private bool _isDirectoryManaged;

    protected override async Task OnInitializedAsync()
    {
        await UserState.LoadAsync();
        if (!UserState.IsStudFarmAdmin) { Nav.NavigateTo("/", replace: true); return; }
        if (!_isNew)
        {
            try
            {
                _stallion = await AdminApi.GetStallionAsync(Id!.Value);
                _isDirectoryManaged = _stallion.IsDirectoryManaged;
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
```

- [ ] **Step 2: Update the Basic Details form section to show read-only fields when directory-managed**

Replace the "Basic Details" `<div class="form-section">` block:

```razor
            <div class="form-section">
                <div class="form-section-title">
                    Basic Details
                    @if (_isDirectoryManaged)
                    {
                        <span style="font-size:var(--font-size-sm);font-weight:normal;color:var(--text-muted);margin-left:var(--space-2)">
                            (sourced from Stallions Australia directory — read-only)
                        </span>
                    }
                </div>

                <div class="form-group">
                    <label class="form-label">Name</label>
                    @if (_isDirectoryManaged)
                    {
                        <p class="form-control" style="background:var(--bg-subtle)">@_model.Name</p>
                    }
                    else
                    {
                        <InputText class="form-input" @bind-Value="_model.Name" />
                        <ValidationMessage For="() => _model.Name" />
                    }
                </div>

                <div class="form-row">
                    <div class="form-group">
                        <label class="form-label">Year of Birth</label>
                        @if (_isDirectoryManaged)
                        {
                            <p class="form-control" style="background:var(--bg-subtle)">@(_model.YearOfBirth?.ToString() ?? "—")</p>
                        }
                        else
                        {
                            <InputNumber class="form-input" @bind-Value="_model.YearOfBirth" />
                        }
                    </div>
                    <div class="form-group">
                        <label class="form-label">Colour</label>
                        @if (_isDirectoryManaged)
                        {
                            <p class="form-control" style="background:var(--bg-subtle)">@(_model.Colour ?? "—")</p>
                        }
                        else
                        {
                            <InputText class="form-input" @bind-Value="_model.Colour" placeholder="e.g. Bay, Chestnut" />
                        }
                    </div>
                </div>

                <div class="form-row">
                    <div class="form-group">
                        <label class="form-label">Sire</label>
                        @if (_isDirectoryManaged)
                        {
                            <p class="form-control" style="background:var(--bg-subtle)">@(_model.Sire ?? "—")</p>
                        }
                        else
                        {
                            <InputText class="form-input" @bind-Value="_model.Sire" />
                        }
                    </div>
                    <div class="form-group">
                        <label class="form-label">Dam</label>
                        @if (_isDirectoryManaged)
                        {
                            <p class="form-control" style="background:var(--bg-subtle)">@(_model.Dam ?? "—")</p>
                        }
                        else
                        {
                            <InputText class="form-input" @bind-Value="_model.Dam" />
                        }
                    </div>
                </div>

                <div class="form-group">
                    <label class="form-label">Registration Number</label>
                    <InputText class="form-input" @bind-Value="_model.RegistrationNumber" />
                </div>
            </div>
```

- [ ] **Step 3: Build**

```
dotnet build src/Client
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add src/Client/Pages/Admin/AdminStallionForm.razor
git commit -m "feat: AdminStallionForm — read-only core fields for directory-managed stallions"
```

---

### Task 8: Smoke test end-to-end

- [ ] **Step 1: Start server**

```
dotnet run --project src/Server
```

Wait for "Application started" and for the EF migration to apply automatically (watch logs for "Applying migration 'StallionAuthorization'").

- [ ] **Step 2: Staff smoke test — directory CRUD**

Log in as a Staff user, then verify in order:

1. `/staff/stud-directory` — loads (empty state or populated from seed)
2. `/staff/stud-directory/new` — create an entry, submit → redirects to list, entry appears
3. Click **Edit** on the new entry — `/staff/stud-directory/{id}` loads with prefilled form
4. `/staff/stallion-directory/new` — create an entry linked to the stud above, submit → redirects to list, entry appears
5. `/staff/studfarms` — Directory column shows "Not linked" for unlinked farms
6. Click **Detail** on a farm → `/staff/studfarms/{id}` loads; select the new stud directory entry and click **Link to Directory** → success banner appears, dropdown updates

- [ ] **Step 3: Stud admin smoke test — My Stallions**

Log in as a stud admin whose farm is **not** linked:

1. `/admin/stallions` — "not linked" warning banner shows; no Available to Add section; **+ Add Stallion** button visible

Log in as a stud admin whose farm **is** linked (use the farm just linked in Step 2):

1. `/admin/stallions` — Your Stable section shows; Available to Add section shows stallions from the directory
2. Click **Add to Stable** on a directory stallion → button changes to "Adding…", then page reloads; that stallion now appears in Your Stable and disappears from Available to Add
3. Click **Edit** on the newly added stallion → `/admin/stallions/{id}` loads; Name/Year/Colour/Sire/Dam displayed read-only with "sourced from directory" label; Description is editable

- [ ] **Step 4: Verify free-form add is blocked for linked farms**

With the linked stud admin session:

1. Confirm the **+ Add Stallion** button is absent from `/admin/stallions`
2. Attempt direct navigation to `/admin/stallions/new` — the form loads (still accessible for edge cases), but submitting it should return 403 from the API

- [ ] **Step 5: Commit smoke test notes (optional)**

```
git commit --allow-empty -m "chore: smoke test complete — Plan C done"
```

---

## ✅ Plan C Complete

All three plans together deliver the full stallion authorization feature:
- Staff maintain `StudDirectory` and `StallionDirectory` via CRUD pages
- Stud farms are linked to their directory entry at creation or via the farm detail page
- Stud admins see only their authorized stallions and add them with one click
- Core fields are read-only for directory-managed stallions
- Free-form add is locked out for directory-managed farms at both UI and API level
