# Stallion Authorization Design

## Goal

Control which stallions a stud farm is permitted to list on the marketplace. An authoritative directory of studs and stallions is maintained by Staff and used to gate what each stud farm admin can add to their stable.

## Architecture

Two new reference tables (`StudDirectory`, `StallionDirectory`) are added to the existing app DB and maintained exclusively by Staff. A foreign key on `StudFarm` links each registered farm to its directory entry. When a stud farm admin adds a stallion, the system creates a marketplace `Stallion` record as a snapshot copy of the directory data — the directory and marketplace record are decoupled after that point.

## Tech Stack

- ASP.NET Core API (server-side entities, repositories, service layer, controllers)
- Blazor WASM (Staff CRUD pages, updated stud admin My Stallions page)
- EF Core (migrations for new tables and FK columns)
- Azure SQL Database
- SSMS (one-time CSV-to-INSERT seeding, not application code)

---

## Data Model

### New entity: `StudDirectory`

Authoritative list of stud farms. Maintained by Staff only.

| Column | Type | Notes |
|---|---|---|
| `Id` | Guid | PK |
| `ArionStudId` | int? | Original `stud_key_Id` from external DB, for seeding traceability |
| `Name` | string (required) | |
| `Website` | string? | |
| `Address` | string? | Street address |
| `Town` | string? | |
| `State` | string? | |
| `Country` | string? | |
| `Phone` | string? | |
| `Email` | string? | |
| `LogoUrl` | string? | |
| `IsActive` | bool | Soft-delete; defaults true |
| `CreatedAt` | DateTime | UTC |
| `UpdatedAt` | DateTime | UTC, updated on every save |

### New entity: `StallionDirectory`

Authoritative list of stallions, each assigned to a stud. Maintained by Staff only.

| Column | Type | Notes |
|---|---|---|
| `Id` | Guid | PK |
| `StallionId` | int | Original `stallionID` from external DB (not null) |
| `ArionId` | int | Original `arionID`; 0 when not applicable, never null |
| `StudDirectoryId` | Guid (FK) | → StudDirectory |
| `Name` | string (required) | |
| `YearOfBirth` | int | |
| `Colour` | string? | |
| `Height` | string? | |
| `SireName` | string? | |
| `DamName` | string? | |
| `IsActive` | bool | Soft-delete; defaults true |
| `CreatedAt` | DateTime | UTC |
| `UpdatedAt` | DateTime | UTC, updated on every save |

### Changes to existing entities

**`StudFarm`** — add one nullable column:
- `StudDirectoryId` (Guid?) — FK → `StudDirectory`. Null until Staff assigns it. Once set, unlocks the stallion list for that farm's admin.

**`Stallion`** — add one nullable column:
- `StallionDirectoryId` (Guid?) — FK → `StallionDirectory`. Set when created from the directory; null for legacy records. When set, core fields (Name, YearOfBirth, Colour, SireName, DamName) are read-only in the UI.

---

## Authorization Flow

### Onboarding a new stud farm (Staff)

The `StaffStudFarmNew.razor` form adds a required **"Link to Stud Directory"** dropdown populated from active `StudDirectory` entries. Staff selects the matching stud when creating the farm record. This sets `StudFarm.StudDirectoryId` in the same POST that creates the farm.

### Linking existing farms (Staff)

For stud farms onboarded before this feature ships, the `StaffStudFarms` detail page gets a **"Link to Directory"** action — a dropdown to pick the `StudDirectory` entry and save. One-time catch-up; no automated migration.

### Stud admin My Stallions page

When a stud admin loads their My Stallions page:

1. The server looks up `StudFarm.StudDirectoryId` for the caller's farm.
2. If `StudDirectoryId` is null, the page shows a notice: *"Your stud farm hasn't been linked to the stallion directory yet. Please contact Stallions Australia."* No available-to-add list is shown.
3. If `StudDirectoryId` is set, the server fetches all active `StallionDirectory` entries for that stud, and all existing `Stallion` records for the farm (by `StudFarmId`).
4. The page renders two sections:
   - **Your stable** — existing `Stallion` records (current behaviour, unchanged)
   - **Available to add** — `StallionDirectory` entries that do not yet have a corresponding `Stallion` record (matched on `StallionDirectoryId`)

### Adding an authorized stallion (one-click)

When the stud admin clicks **Add** on an available directory stallion:

- The API creates a new `Stallion` row under the caller's `StudFarmId`
- Copies `Name`, `YearOfBirth`, `Colour`, `SireName`, `DamName` from the `StallionDirectory` row
- Sets `Stallion.StallionDirectoryId` to the directory entry's `Id`
- Returns the new stallion — no form, no confirmation step

Once added, the stallion appears in the **Your stable** section. The stud admin can add a `Description` and upload images, but Name, YearOfBirth, Colour, SireName, and DamName are displayed as read-only when `StallionDirectoryId` is set.

### Free-form add

The existing "Add Stallion" free-form button is hidden for stud admins whose farm has a `StudDirectoryId` assigned. It remains available to Staff if a manual override is ever needed.

### Missing stallion

A **"Missing a stallion?"** button appears at the bottom of the Available to Add section. It opens a `mailto:` link pre-addressed to the Stallions Australia contact email, with a pre-filled subject line: `"Missing Stallion – [Farm Name]"` and a body prompt asking the admin to describe the missing horse. No DB tracking required.

---

## Staff CRUD: Stud Directory

**Routes:** `/staff/stud-directory` (list) · `/staff/stud-directory/new` (create) · `/staff/stud-directory/{id}` (edit)

**List page:** Searchable by name. Defaults to active only, with toggle to include inactive. Each row links to the edit page. Shows stallion count per stud.

**Create/Edit form fields:** Name (required), ArionStudId, Website, Address, Town, State, Country, Phone, Email, LogoUrl, IsActive toggle.

**Edit page** also shows a read-only list of `StallionDirectory` entries assigned to that stud, with links to each.

**Delete:** Soft-delete only (set `IsActive = false`). No hard delete — foreign key references from `StudFarm` must remain intact.

---

## Staff CRUD: Stallion Directory

**Routes:** `/staff/stallion-directory` (list) · `/staff/stallion-directory/new` (create) · `/staff/stallion-directory/{id}` (edit)

**List page:** Searchable by name. Filterable by stud (dropdown). Defaults to active only, with toggle to include inactive.

**Create/Edit form fields:** Stud (required, dropdown from active StudDirectory), StallionId (int), ArionId (int, defaults 0), Name (required), YearOfBirth, Colour, Height, SireName, DamName, IsActive toggle.

**Delete:** Soft-delete only. If a `Stallion` record has already been created from this directory entry (`StallionDirectoryId` is set), the marketplace record is not affected — the snapshot stands.

---

## API Endpoints

All new endpoints under `[Authorize(Policy = "StaffOnly")]` except the stud admin add-from-directory endpoint.

| Method | Route | Auth | Purpose |
|---|---|---|---|
| GET | `/api/stud-directory` | Staff | List all (filterable) |
| POST | `/api/stud-directory` | Staff | Create entry |
| GET | `/api/stud-directory/{id}` | Staff | Get single |
| PUT | `/api/stud-directory/{id}` | Staff | Update entry |
| GET | `/api/stallion-directory` | Staff | List all (filterable by stud) |
| POST | `/api/stallion-directory` | Staff | Create entry |
| GET | `/api/stallion-directory/{id}` | Staff | Get single |
| PUT | `/api/stallion-directory/{id}` | Staff | Update entry |
| GET | `/api/stallions/authorized` | StudFarmAdmin | Returns authorized StallionDirectory entries not yet added to stable |
| POST | `/api/stallions/add-from-directory/{directoryId}` | StudFarmAdmin | One-click add from directory |
| PUT | `/api/admin/studfarms/{id}/link-directory` | Staff | Link existing StudFarm to StudDirectory entry |

The existing `POST /api/stallions` (free-form create) is retained but `StallionService.CreateAsync` is updated to return `Forbidden` if the caller's farm has a `StudDirectoryId` set (i.e., directory-managed farms cannot bypass via direct API call).

---

## Seeding

Initial data is loaded via a SQL INSERT script generated from a CSV export of the external ArionWeb DB. The script is run once in SSMS against the dev (and later prod) Azure SQL DB. No application code is involved in seeding.

Mapping:
- `studDetails.stud_key_Id` → `StudDirectory.ArionStudId`
- `stallionDetails.stallionID` → `StallionDirectory.StallionId`
- `stallionDetails.arionID` → `StallionDirectory.ArionId` (0 if source is NULL)
- `stallionDetails.studID` → used to resolve `StallionDirectory.StudDirectoryId` during seeding (join on `StudDirectory.ArionStudId`)

---

## What Is Not In Scope

- Automatic sync between the external ArionWeb DB and the app DB — all updates go through Staff CRUD
- Hard deletes on either directory table
- Stallion authorization for Staff-created listings (Staff bypass the directory gate entirely)
- Any change to the buyer-facing browse experience — stallion profiles look identical to buyers regardless of how they were created
