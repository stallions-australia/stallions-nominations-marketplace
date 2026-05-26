# Staff Admin Panel — Design Spec (Plan 5)

## Overview

The Staff Admin Panel gives Stallions Australia staff a dedicated, role-gated section of the platform to manage the full operation: onboarding stud farms, verifying buyers, overseeing all listings, viewing transactions, and generating per-farm remittance invoices.

**In scope for Plan 5:**
- Dashboard — platform stats overview
- Users — verify buyers, suspend accounts
- Stud Farms — onboard new farms, view all registered farms
- Listings — view all listings, set platform fee %, force-override status
- Transactions — read-only financial record
- Invoices — per-farm remittance summaries

**Out of scope:**
- Email notifications (Azure Functions — later plan)
- Payment processing / refund management (later plan)
- Audit log viewer UI (data is logged server-side; UI deferred)

---

## Architecture

### Layout

A new `StaffLayout.razor` Blazor layout component wraps all `/staff/*` routes. It renders a persistent left sidebar and a main content area. It follows the identical structural pattern as `AdminLayout.razor` (Plan 4) but is completely separate — the two layouts never share state or routing.

The sidebar contains:
- **Dashboard** (link to `/staff/dashboard`)
- **Users** (link to `/staff/users`)
- **Stud Farms** (link to `/staff/studfarms`)
- **Listings** (link to `/staff/listings`)
- **Transactions** (link to `/staff/transactions`)
- **Invoices** (link to `/staff/invoices`)

A gold "Staff" badge appears in the sidebar header to distinguish it from the stud farm admin panel.

### Auth Guard

All `/staff/*` routes carry `[Authorize(Roles = "Staff")]`. Unauthenticated users or users without the `Staff` role are redirected to the Entra ID login page. The `StaffLayout` sidebar never renders for non-staff users. Staff who do not also hold `StudFarmAdmin` will not see the stud farm admin sidebar — the layouts are fully independent.

### Client Service

A new `StaffApiService` is added to the Blazor client. It is authenticated (uses `BaseAddressAuthorizationMessageHandler`) and covers all staff-facing API calls. It is kept separate from `AdminApiService` (stud farm admin) and the public browse services.

### Routing

```
/staff/dashboard          Stats overview
/staff/users              All users — filterable by role and status
/staff/users/{id}         User detail — verify, suspend
/staff/studfarms          All stud farms list
/staff/studfarms/new      Onboard new stud farm
/staff/listings           All listings — fee management, status override
/staff/transactions       All completed sales — read only
/staff/invoices           Per-farm remittance summaries — read only
```

---

## Server Changes

Most server logic already exists. Three additions are needed:

### New endpoints in `AdminController`

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/admin/studfarms` | List all stud farms |
| `POST` | `/api/admin/studfarms` | Create a new stud farm |
| `GET` | `/api/admin/listings` | All listings across all farms (staff view) |
| `POST` | `/api/admin/listings/{id}/force-status` | Override listing status |

### New service methods in `AdminService`

- `GetAllStudFarmsAsync()` — returns all `StudFarm` records
- `CreateStudFarmAsync(CreateStudFarmRequest)` — creates a `StudFarm` linked to an existing user; validates the user exists and holds `StudFarmAdmin` role and does not already have a farm
- `ForceListingStatusAsync(Guid listingId, ForceListingStatusRequest)` — sets listing status to any value; audit logged with optional reason

### New DTOs

- `StudFarmSummaryDto` — Id, Name, ABN, ContactEmail, LinkedUserDisplayName, LinkedUserEmail, IsActive, CreatedAt
- `CreateStudFarmRequest` — UserId, Name, ABN, ContactPhone, ContactEmail, Address
- `ForceListingStatusRequest` — Status (string), Reason (string, optional)

### Audit logging

All write actions pass through `AuditLogRepository.LogAsync`:
- Verify user — action `"VerifyUser"`
- Suspend user — action `"SuspendUser"` (already implemented)
- Create stud farm — action `"CreateStudFarm"`
- Set listing fee — action `"SetListingFee"` (already implemented)
- Force listing status — action `"ForceListingStatus"` with reason in details

---

## Pages

### Dashboard (`/staff/dashboard`)

Four stat cards fetched from `GET /api/admin/dashboard`:
- **Active Listings** — total count
- **Auctions / Fixed Price** — split of the active count
- **Fee Revenue (30 days)** — `RecentFeeRevenueIncGst` formatted as AUD
- **Pending Verifications** — count with a direct link to `/staff/users?status=PendingVerification`

The pending verifications card is highlighted in amber when count > 0 to draw staff attention.

### Users (`/staff/users` and `/staff/users/{id}`)

**List page:** Table of all users from `GET /api/users`. Filterable by Role (All / Buyer / StudFarmAdmin / Staff) and Status (All / PendingVerification / Active / Suspended) via dropdowns. Columns: Display Name, Email, Role badge, Status badge, Joined date, Actions.

Actions per row:
- `PendingVerification` → green **Verify** button
- `Active` → **Suspend** button
- `Suspended` → no actions

**Detail page (`/staff/users/{id}`):** Full user record. Same Verify / Suspend actions. Shows: Display Name, Email, Role, Status, Joined, Verified At (if applicable). No edit capability — display name and profile edits are the user's own responsibility.

### Stud Farms (`/staff/studfarms` and `/staff/studfarms/new`)

**List page:** Table from `GET /api/admin/studfarms`. Columns: Farm Name, ABN, Contact Email, Linked User (display name + email), Active status badge, Created date. An "Onboard New Farm" button sits top-right.

**Onboard form (`/staff/studfarms/new`):**

| Field | Type | Required | Notes |
|---|---|---|---|
| User | Dropdown | Yes | Lists users with `StudFarmAdmin` role who have no existing farm |
| Farm Name | Text | Yes | |
| ABN | Text | Yes | Stored as string; no format validation enforced |
| Contact Phone | Text | No | |
| Contact Email | Text | No | |
| Address | Textarea | No | Free-form |

On submit: `POST /api/admin/studfarms`. On success: redirect to `/staff/studfarms`. The linked user can immediately access their farm dashboard after this record is created.

### Listings (`/staff/listings`)

Table of all listings from `GET /api/admin/listings` (new staff-scoped endpoint added to `AdminController`). Columns: Stallion, Farm, Type badge, Status badge, Price (inc. GST), Fee %, Publish date, Actions.

**Set fee:** Inline edit — click the Fee % cell to open a small inline input; save calls `PUT /api/admin/listings/{id}/fee`. Fee % is displayed as "—" if not yet set.

**Force status:** A "Override Status" button opens a modal with:
- Status dropdown: Draft / Active / Closed / Unsold
- Optional reason text field
- Confirm button — calls `POST /api/admin/listings/{id}/force-status`

**Empty state:** If no listings exist, a message directs staff to onboard a stud farm first.

### Transactions (`/staff/transactions`)

Read-only table from `GET /api/admin/transactions`. Columns: Date, Stallion, Farm, Buyer, Sale Price (inc. GST), Platform Fee (inc. GST), Fee ex GST, GST Amount, Status badge. Sorted newest first. No actions.

### Invoices (`/staff/invoices`)

Read-only view from `GET /api/admin/invoices`. Grouped by stud farm — each farm is a collapsible section. Section header shows: Farm Name, Total Sales (inc. GST), Total Fees Retained, Total Remittance Amount. Expanded section shows a table of individual sale lines: Stallion, Sale Price, Platform Fee, Remittance Amount, Sale Date.

---

## Error Handling

- `ErrorBoundary` wraps the `@Body` in `StaffLayout`
- Page-level load errors show an inline error message; they do not crash the layout
- Form submission errors display inline below the submit button
- All write operations are wrapped in try/catch; unexpected failures show a generic "Something went wrong" message with a retry option

---

## What Does Not Change

- Public-facing `MainLayout` and all public pages — untouched
- Stud farm admin at `/admin/*` and `AdminLayout.razor` — untouched
- Platform fee enforcement at the API level — already locked to `Staff` role
- `AdminController`, `AdminService`, `UserService` server logic — additions only, no modifications to existing methods
- All pricing displayed inclusive of GST, consistent with the rest of the platform
