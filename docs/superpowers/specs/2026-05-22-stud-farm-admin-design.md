# Stud Farm Admin UI — Design Spec (Plan 4)

## Overview

The Stud Farm Admin UI gives authenticated stud farm operators a dedicated section of the Stallions Nominations Marketplace to manage their stallion roster, create and publish nomination listings, and respond to buyer enquiries. This is an authenticated, role-gated section of the existing Blazor WASM client — no new app, no new hosting.

**In scope for Plan 4:**
- My Stallions — add, edit, manage images
- My Listings — create, publish, edit, and manage listings
- Enquiries — read and reply to buyer enquiries

**Deferred to later plans:**
- Dashboard overview with stats
- Stud Profile editing (farm name, contact details, description)
- Sales history

---

## Architecture

### Layout

A new `AdminLayout.razor` Blazor layout component wraps all `/admin/*` routes. It renders a persistent left sidebar and a main content area. The existing `MainLayout.razor` (public site) is untouched.

The sidebar contains:
- **My Stallions** (link to `/admin/stallions`)
- **My Listings** (link to `/admin/listings`)
- **Enquiries** (link to `/admin/enquiries`, with unread count badge)

### Auth Guard

The entire `/admin` subtree is protected by `[Authorize(Roles = "StudFarmAdmin")]` applied via route-level configuration. Any unauthenticated or unauthorised user hitting a `/admin/*` URL is redirected to the Entra ID login page. The `StudFarmAdmin` role claim is set from the user's Entra ID group membership, consistent with how the rest of the app handles roles.

### Routing

All routes follow the dedicated page-per-action pattern (Option A). No slide-out panels, no component swapping.

```
/admin/stallions                   Stallion roster list
/admin/stallions/new               Add stallion form
/admin/stallions/{id}              Stallion detail + edit form

/admin/listings                    Listings grouped by stallion
/admin/listings/new                Create listing (stallion picker + form)
/admin/listings/new?stallionId=X   Create listing with stallion pre-selected
/admin/listings/{id}               Listing detail + edit form

/admin/enquiries                   Enquiries inbox
/admin/enquiries/{id}              Enquiry thread + reply form
```

### Client Service

A new `AdminApiService` is added to the Blazor client project. It is authenticated (uses `BaseAddressAuthorizationMessageHandler`) and covers all admin CRUD operations. It is kept separate from the public browse services (`StallionApiService`, `ListingApiService`, etc.) which do not require a Bearer token.

---

## My Stallions

### Roster List (`/admin/stallions`)

Displays a table of all stallions belonging to the logged-in stud farm. Columns: Name, Season, Status (Active / Inactive), Listing count, Edit action. An "Add Stallion" button sits top-right.

**Empty state:** If the stud farm has no stallions yet, the page shows a full-page empty state with a single "Add Your First Stallion" CTA instead of an empty table.

### Add / Edit Form (`/admin/stallions/new` and `/admin/stallions/{id}`)

A single-page form. On `/new`, all fields are blank. On `/{id}`, the form is pre-populated with the stallion's existing data.

| Field | Type | Required | Notes |
|---|---|---|---|
| Name | Text | Yes | |
| Season | Dropdown | Yes | Populated from `GET /api/seasons` |
| Colour | Text | No | e.g. "Bay", "Chestnut" |
| Height | Text | No | e.g. "16.1hh" |
| Sire | Text | No | |
| Dam | Text | No | |
| Description | Textarea | No | Shown on the public stallion profile page |
| Profile image | File upload | No | Single image; replaces existing primary image |
| Gallery images | Multi-file upload | No | Multiple images; individually deletable; reorderable |

**Image handling:** Uses existing API endpoints — `POST /api/stallions/{id}/images` and `DELETE /api/stallions/{id}/images/{imageId}`. The profile image is uploaded with `IsPrimary = true`. Images are stored in Azure Blob Storage; the API streams uploads to blob and returns the URL.

**Soft delete:** Stallions are never hard-deleted. The edit form includes an Active/Inactive toggle. Setting a stallion to Inactive hides it from public browse but preserves all listing and purchase history. An Inactive stallion's listings are also hidden from the marketplace.

---

## My Listings

### Listings View (`/admin/listings`)

Listings are grouped by stallion. Each stallion is a collapsible section header showing the stallion name and listing count. Within each group, listings are shown in a table with columns: Type (Fixed Price / Auction), Price, Status badge, Quantity remaining / Bid count, and Edit / View actions.

A "New Listing" button sits top-right.

**Status badges:** Draft (amber), Active (green), Closed (grey), Unsold (red).

**Empty state:** If the stud farm has no stallions, the page shows a prompt directing the admin to add a stallion first, with a link to `/admin/stallions/new`.

### Create Listing Form (`/admin/listings/new`)

**Step 1 — Stallion selection:** A dropdown of the stud farm's active stallions. Pre-selected if `?stallionId=X` is present in the URL (allowing deep-linking from a stallion's page). If no stallions exist, the dropdown is empty with a prompt to add one first.

**Step 2 — Listing type:** Fixed Price or Auction. Selecting a type reveals the relevant fields below.

#### Fixed Price Fields

| Field | Type | Required | Locked after publish |
|---|---|---|---|
| Season | Dropdown | Yes | Yes |
| Price (inc. GST) | Currency input | Yes | Yes |
| Quantity | Integer | Yes | No (safe edit) |
| Terms & Conditions | Textarea | Yes | Yes |
| Description | Textarea | No | No (safe edit) |

#### Auction Fields

| Field | Type | Required | Locked after publish |
|---|---|---|---|
| Season | Dropdown | Yes | Yes |
| Starting price | Currency input | Yes | Yes |
| Reserve price | Currency input | No | Yes |
| No reserve | Checkbox | No | Yes |
| End date/time | Date + time picker | Yes | Yes |
| Terms & Conditions | Textarea | Yes | Yes |
| Description | Textarea | No | No (safe edit) |

**Reserve price:** Reserve is on by default. Checking "No reserve" clears the reserve price field and flags the listing explicitly as no-reserve.

### Listing Lifecycle

1. **Draft** — Created but not visible on the marketplace. All fields editable.
2. **Active** — Published and visible on the marketplace. Locked fields cannot be edited. Safe edits (description, quantity for fixed price) are allowed.
3. **Closed** — Manually closed by the admin, or fixed-price listing reached zero quantity, or auction reached its end date/time. Not visible on the marketplace.
4. **Unsold** — Auction ended with no bids, or reserve not met. Admin is notified and offered the option to re-list (Azure Functions, later plan).

**Publish:** A "Publish" button appears on the listing detail/edit page for Draft listings. Clicking it immediately makes the listing live — no approval queue. (An approval queue can be added in a later iteration if needed.)

**Unpublish:** A published listing can be pulled back to Draft via an "Unpublish" button. This removes it from the marketplace immediately. Safe fields (description, quantity for fixed price) remain editable in the Draft state. Locked fields (price, T&C, type, end date, reserve) remain locked — they cannot be changed once the listing has been published at any point. Re-publishing makes it live again.

**Terms & Conditions locked permanently after first publish:** T&C behaves identically to price — once the listing has been published, T&C cannot be edited regardless of whether the listing is later unpublished. If T&C needs changing, the admin must close the listing and create a new one.

**Safe edits on published listings:**
- Fixed Price: description and quantity can be updated at any time.
- Auction: description only (quantity is N/A; end date is locked).

### Listing Detail / Edit (`/admin/listings/{id}`)

Shows all listing fields. For Draft listings, all fields are editable. For Active listings, locked fields are read-only and clearly marked; safe fields have edit controls. For Closed/Unsold listings, all fields are read-only.

The listing detail page also shows: sale progress (nominations sold / remaining), current high bid (auctions), and a link to view the listing on the public marketplace.

---

## Enquiries

### Inbox (`/admin/enquiries`)

A list of all enquiries across all of the stud farm's stallions. Each row shows: buyer name, stallion name, listing title (linked), message preview, date received, and a status indicator (Unread / Replied). Sorted newest first. Unread rows are visually highlighted (bold or coloured indicator).

A badge on the "Enquiries" sidebar nav item shows the current unread count.

**Scope:** Stud farm admins see only enquiries for their own stallions and listings. This is enforced at the API level (already implemented in Plan 2).

### Enquiry Thread (`/admin/enquiries/{id}`)

Displays the full conversation: the buyer's original message, followed by any previous replies in chronological order. Each message shows sender name and timestamp.

Above the thread, a context block shows: stallion name, listing title, listing type and price.

Below the thread, a reply textarea with a "Send Reply" button. Submitting calls `POST /api/enquiries/{id}/reply`. Opening a thread marks the enquiry as read.

**Email notifications:** Buyer email notifications on reply are out of scope for Plan 4 — that requires Azure Functions (later plan). The reply is stored and visible to the buyer in their My Enquiries page (built in Plan 3).

---

## What Does Not Change

- The public-facing `MainLayout.razor` and all public pages are untouched.
- The `[AllowAnonymous]` public browse API endpoints (`GET /api/listings`, `GET /api/stallions`, etc.) are unchanged.
- Platform fee percentage fields remain admin-only (Stallions Australia Staff role) — the stud farm admin UI never exposes fee fields.
- All pricing is displayed and entered inclusive of GST, consistent with the rest of the platform.
