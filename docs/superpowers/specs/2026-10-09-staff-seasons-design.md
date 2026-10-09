# Staff Seasons Screen — Design + Plan

**Date:** 2026-10-09
**Status:** Draft for David's approval
**Branch:** `feature/v2-phase1-domain` (small add-on to v2 Phase 1)

## Why

Staff need to manage breeding seasons from the app. The API already exists
(`api/seasons`: list, current, create, update, open, close — writes are `StaffOnly`) but there
is no Staff page, so dev is stuck on a stale "2025 Season" (Aug 2025 – Jan 2026). Seasons
matter more in v2: a stud's listing fee is per stallion **per season**, stud admins can only
create listings in an **open** season, and their My Stallions page shows listing-fee status for
the open season.

## What a season does today (unchanged)

- A season has a name, a start date and an end date.
- **Only one season can be open at a time.** Opening a second one is refused until the first
  is closed.
- Studs can only **create** listings in the open season. Closing a season does not touch
  existing listings, subscriptions or live auctions.
- Opening and closing are **manual** Staff actions. Nothing happens automatically on the start
  or end date.

## Scope

### Staff page `/staff/seasons` ("Seasons" in the Staff nav)

- Table of all seasons, newest first: name, start date, end date, status (**Open** / Closed),
  and when it was opened.
- **New season** (modal): name, start date, end date. End must be after start.
- **Edit** (modal): name and dates, for any season.
- **Open** / **Close** buttons, each behind a confirmation that says what it means:
  - Open: "Studs will be able to create listings in {name}." If another season is open, the
    page says so and asks Staff to close that one first (the server enforces it too).
  - Close: "Studs won't be able to create new listings in {name}. Existing listings, auctions
    and listing fees are not affected."

### Server hardening (existing endpoints)

- **Audit log** every create, update, open and close (`EntityType = "Season"`), with old and
  new values for updates. CLAUDE.md requires this for admin actions; it is missing today.
- **Re-check Staff in the service** for create/update/open/close (defence in depth, same as
  platform settings and subscriptions). The controller policies stay as they are.
- Trim and require a name on create/update (already done), and keep the end-after-start check.

### Out of scope

- Automatic open/close on the start/end dates (a timer job could do it in Phase 3 alongside
  the auction close job).
- Make an Offer expiry at season end (Phase 4).
- Deleting seasons.

## Plan

### Task A — Server: audit + Staff re-check (TDD)

1. `SeasonService` takes `IAuditLogRepository`. Tests first:
   - create/update/open/close each write one audit entry with the caller's id;
   - update's details contain the old and new name/dates;
   - a non-Staff caller gets 403 from create/update/open/close and nothing is saved.
2. Implement; update `SeasonServiceTests.CreateSut`.

### Task B — Client: Staff Seasons page

1. `StaffApiService`: `CreateSeasonAsync`, `UpdateSeasonAsync`, `OpenSeasonAsync`,
   `CloseSeasonAsync` (`GetSeasonsAsync` exists).
2. `/staff/seasons` page as above; "Seasons" link in `StaffLayout`.
3. bUnit tests: open/close ask for confirmation before calling the API; the open button tells
   Staff to close the currently open season first; date validation blocks end ≤ start.

### Task C — Verify and ship

1. Full build (0 warnings) and `dotnet test` green.
2. One commit per task, push the branch, redeploy dev (with David's go-ahead).
3. On dev, David creates "2026 Season", closes "2025 Season" and opens 2026.
