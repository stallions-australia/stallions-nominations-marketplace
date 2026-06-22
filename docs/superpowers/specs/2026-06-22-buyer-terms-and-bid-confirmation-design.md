# Buyer T&C Onboarding + First-Bid Confirmation — Design

**Date:** 2026-06-22
**Status:** Approved (pending implementation plan)

## Goal

Require verified buyers to read and agree to the platform Terms & Conditions before
they can bid, and surface a "legally binding" confirmation on each bid until the buyer
opts out. The T&C text is stored as configurable, versioned content editable by staff —
never hardcoded (per project policy).

## Background / Current State

- New buyers log in via Azure AD B2C and are provisioned `Status = PendingVerification`.
  There is **no email verification link** — a staff member flips them to `Active` via the
  `/staff/users` Verify button. "First portal entry" and "verified" are therefore separate
  moments.
- `BidService.PlaceBidAsync` currently has a single guard: `caller.Status != Active`.
- T&C today exists only per-listing (`Listing.TermsAndConditions`). There is **no**
  platform-level T&C store. The User entity has no acceptance fields.
- A reusable `Modal.razor` exists (params: `IsOpen`, `Title`, `ChildContent`, `OnClose`,
  `CloseOnBackdrop` defaulting to `false`, with JS focus-trapping).

## Key Decisions

1. **Trigger:** The welcome + T&C agreement is shown on the buyer's **first login after
   they are verified (`Active`)**, not while pending.
2. **Content store:** A versioned DB content record with a **staff editor** in the portal.
3. **Bid confirmation recurrence:** Shows on **every bid by default**; a "Don't show this
   again" checkbox sets a permanent suppression flag on the user.
4. **Enforcement architecture (Approach A):** A global gate in `MainLayout` shows a blocking
   modal when a verified buyer's accepted T&C version is behind the current published
   version. `BidService` independently enforces acceptance server-side (defense in depth).
5. **Content format:** Markdown, rendered client-side (Markdig in Blazor WASM).
6. **Consent evidence:** The "I agree" button is enabled only after the buyer scrolls to the
   bottom of the T&C body.

## Architecture

### Data Model

**New entity `TermsDocument`** — one row per published version, history retained for legal
evidence:

| Field             | Type           | Notes                                            |
|-------------------|----------------|--------------------------------------------------|
| `Id`              | Guid (PK)      |                                                  |
| `Version`         | int, unique    | Monotonic; current T&C = row with max Version    |
| `Body`            | nvarchar(max)  | Markdown source                                  |
| `CreatedAt`       | DateTime       | UTC                                              |
| `CreatedByUserId` | Guid (FK User) | Staff member who published this version          |

The **current** T&C is the row with the highest `Version`. Publishing creates a new row
with `Version = max + 1`; prior versions are never edited or deleted.

**`User` entity — three new fields:**

| Field                     | Type      | Default | Notes                                   |
|---------------------------|-----------|---------|-----------------------------------------|
| `AcceptedTermsVersion`    | int?      | null    | null = never accepted                   |
| `AcceptedTermsAt`         | DateTime? | null    | UTC timestamp of acceptance             |
| `SuppressBidConfirmation` | bool      | false   | true once the buyer opts out of the modal|

Acceptance is also written to the existing `AuditLog` via `LogAsync("User", userId,
"TermsAccepted", null, "{\"Version\":N}")`.

### Server

**`ITermsService` / `TermsService`:**
- `GetCurrentAsync()` → current `TermsDocument` (or null if none published).
- `PublishAsync(string body)` → Staff only; creates `Version = max + 1`.
- `GetHistoryAsync()` → Staff only; all versions descending.

**`TermsController` (`api/terms`):**
- `GET api/terms/current` — `[Authorize]` (any authenticated user). Returns current doc or 404.
- `POST api/terms` — `[Authorize(Policy = "StaffOnly")]`; body `{ Body }`; publishes new version.
- `GET api/terms/history` — `[Authorize(Policy = "StaffOnly")]`.

**User self-service endpoints (`UsersController` / `UserService`):**
- `POST api/users/me/accept-terms` — body `{ Version }`. Sets `AcceptedTermsVersion`/
  `AcceptedTermsAt`, writes audit log. **Rejects** (`BadRequest`) if `Version` ≠ current
  published version, so a buyer cannot accept a stale document.
- `POST api/users/me/suppress-bid-confirmation` — sets `SuppressBidConfirmation = true`.

**`BidService.PlaceBidAsync` — new guard** (after the existing `Status != Active` check):
```
var current = await _terms.GetCurrentAsync();
if (current != null && caller.AcceptedTermsVersion != current.Version)
    return ServiceResult<BidDto>.BadRequest(
        "You must accept the current Terms & Conditions before placing a bid.");
```
If **no** T&C has ever been published (`current == null`), the gate is skipped — the platform
cannot enforce acceptance of a document that does not exist. (This is a launch-prep safety
valve; in production a T&C will always be published.)

**DTO changes:**
- `UserDto` gains `AcceptedTermsVersion` (int?) and `SuppressBidConfirmation` (bool), so the
  client knows acceptance state from the existing `GetMeAsync` call.
- New `TermsDocumentDto { Id, Version, Body, CreatedAt }`.
- New `PublishTermsRequest { Body }` and `AcceptTermsRequest { Version }`.

### Client

**State (`UserStateService`):** expose `AcceptedTermsVersion` and `SuppressBidConfirmation`
from `CurrentUser`. Add a method to mark terms accepted / confirmation suppressed locally
after the corresponding API calls succeed (so the UI updates without a full reload).

**Welcome / T&C gate (`MainLayout`):** after `UserState.LoadAsync()`, if the user
`IsBuyer && IsVerified`, fetch `GET api/terms/current`. If a current doc exists and
`AcceptedTermsVersion < current.Version` (or is null), show a **blocking**
`TermsAgreementModal` over the current page (`Modal` with `CloseOnBackdrop = false`, no
close button path that dismisses without agreeing). The buyer cannot interact with the app
behind it until they agree.

**`TermsAgreementModal.razor`:** welcome message + the T&C `Body` rendered from Markdown in a
scrollable container. The "I have read and agree" button is **disabled until the buyer
scrolls to the bottom** of the body (scroll-position detection on the scroll container). On
agree → `POST accept-terms { Version }` → update `UserState` → close.

**First-bid confirmation (`ListingDetail.razor` bid form):** on Place Bid click:
1. If `SuppressBidConfirmation` is true → place the bid directly.
2. Else show `BidConfirmationModal`: "This is a legally binding bid…" text + a "Don't show
   this again" checkbox + Confirm / Cancel. On Confirm → if the checkbox is ticked, call
   `POST suppress-bid-confirmation` and update state; then place the bid.
3. **Fallback:** if the server rejects a bid with the T&C message (e.g. staff published a new
   version mid-session), the bid form catches it and triggers the `TermsAgreementModal`.

**`BidConfirmationModal.razor`:** wraps `Modal.razor`; exposes a confirm callback that
reports whether "don't show again" was ticked.

**Staff editor (`StaffTerms.razor` at `/staff/terms`):** shows the current version + body in a
Markdown textarea with a live preview, a "Publish new version" action (creates `Version + 1`),
and a collapsible version history. Linked from the Staff navigation.

**API services:** new `TermsApiService` (current/publish/history) and additions to the user
API service for `accept-terms` / `suppress-bid-confirmation`. Error bodies surfaced to the UI
(consistent with the existing `ReadError`/`ExtractErrorMessageAsync` pattern).

### Content Rendering

T&C `Body` is Markdown. The client renders it to HTML with Markdig (compatible with Blazor
WASM) inside the agreement modal and the staff preview. Storing Markdown (not raw HTML)
avoids a stored-XSS surface from staff-authored content and keeps the source human-readable.

## Error Handling

- Accepting a stale version → `BadRequest` with a clear message; client refetches current
  terms and re-renders the modal.
- Bidding without current acceptance → `BadRequest`; client opens the T&C modal.
- No T&C published → gate skipped, bidding allowed (documented launch-prep behavior).
- Publish by non-staff → blocked by `StaffOnly` policy (403).

## Testing

**Server unit tests:**
- `PlaceBidAsync` rejects when `AcceptedTermsVersion` is null or behind current.
- `PlaceBidAsync` allows when accepted version == current, and when no T&C exists.
- `accept-terms` sets fields + audit; rejects a stale version.
- `PublishAsync` increments version; `GetCurrentAsync` returns the max version.
- `suppress-bid-confirmation` sets the flag.

**Client:** lighter coverage — the gate shows for an out-of-date verified buyer; the agree
button enables only after scroll-to-bottom; the bid confirmation respects the suppression
flag.

## Scope / Out of Scope

- **In scope:** buyers only. Staff and StudFarmAdmin are never gated.
- **Re-acceptance:** a buyer mid-session when staff publish a new version is re-prompted on
  next page load (no live push).
- **Out of scope:** email notifications on T&C changes; rich T&C diffing between versions;
  applying the same gate to fixed-price checkout (checkout already has its own buyer
  disclosure — revisit separately if a binding T&C gate is wanted there too).
