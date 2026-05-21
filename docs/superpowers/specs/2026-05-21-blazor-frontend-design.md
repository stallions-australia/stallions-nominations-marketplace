# Blazor WebAssembly Frontend Design — Plan 3 (Public + Buyer)

**Date:** 2026-05-21  
**Status:** Approved  
**Scope:** Public browsing and authenticated buyer flows. Stud Farm Admin and Staff admin panel are deferred to Plan 4.

---

## Goal

Build the Blazor WebAssembly frontend for the Stallions Nominations Marketplace — public listing browse, stallion/stud farm profiles, and the full buyer journey (bid, purchase, checkout, enquiries). The UI connects to the ASP.NET Core API from Plan 2.

---

## Visual Design Direction

- **Primary colour:** `#22456d` (navy)
- **Accent:** `#c4993a` (gold) — used for CTAs in the hero, badges, progress bars
- **Background:** `#faf8f5` (warm cream) — not pure white, adds premium warmth
- **Logo:** White PNG on transparent, sits on the dark navy nav
- **Aesthetic:** Photo-first listing cards (carsales/Airbnb card structure), restrained use of colour, generous whitespace, premium but not stuffy
- **Typography:** System font stack — `-apple-system, BlinkMacSystemFont, 'Segoe UI', system-ui, sans-serif` — no web font download
- **No component library** — hand-crafted CSS with custom properties throughout

---

## Architecture

### Tech Stack

| Concern | Choice |
|---|---|
| Framework | Blazor WebAssembly (.NET 9) |
| Auth | `Microsoft.Authentication.WebAssembly.Msal` (Entra ID) |
| API client | Typed `HttpClient` services, one per domain |
| CSS | Custom properties + scoped `.razor.css` per component |
| Tests | bUnit for component unit tests |

### Project Structure

```
src/Client/
  Layout/
    MainLayout.razor          # Nav + footer shell for all pages
    MainLayout.razor.css
    NavBar.razor              # Responsive nav with auth state awareness
    NavBar.razor.css
    Footer.razor

  Pages/
    Home.razor                # Listings browse — search hero + filter bar + card grid
    ListingDetail.razor       # Single listing detail — bid/buy CTA
    StallionDetail.razor      # Stallion profile (public, read-only)
    StudFarmDetail.razor      # Stud farm page (public, read-only)
    Checkout.razor            # Buyer checkout flow (auth required)
    MyPurchases.razor         # Buyer purchase history
    MyBids.razor              # Buyer active/past bids
    Enquiries.razor           # Enquiry thread list
    EnquiryDetail.razor       # Single enquiry thread

  Components/
    Listings/
      ListingCard.razor       # Photo-first card: image, badge, name, price, CTA
      ListingCard.razor.css
      ListingGrid.razor       # Responsive 3→2→1 col grid wrapper
      AuctionTimer.razor      # Live countdown with gold progress bar
      PriceDisplay.razor      # Consistent price + "inc. GST" formatting
      FilterBar.razor         # Horizontal scrollable pill filters
    Checkout/
      MareDetailsForm.razor   # Required mare name/details before purchase
      BuyerDisclosure.razor   # Mandatory fee/balance disclosure panel
    Enquiries/
      MessageThread.razor     # Chat-style message list
      MessageComposer.razor   # Reply input + send button
    Shared/
      LoadingSpinner.razor
      EmptyState.razor        # Zero results state with illustration/message
      ErrorMessage.razor      # Friendly API/network error display
      Modal.razor             # Generic modal shell

  Services/
    ListingApiService.cs
    StallionApiService.cs
    BidApiService.cs
    CheckoutApiService.cs
    EnquiryApiService.cs
    UserApiService.cs

  wwwroot/
    css/
      app.css                 # Design tokens, resets, global layout, breakpoints
      utilities.css           # .sr-only, .truncate, .visually-hidden
    images/
      logo.png
```

---

## CSS System

All styles use CSS custom properties defined in `app.css`. Scoped `.razor.css` files handle component-specific styles. Global media-query overrides (where Blazor's `::deep` doesn't work) go in `app.css`.

### Design Tokens

```css
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

  /* Radii */
  --radius-sm:   6px;
  --radius-md:   10px;
  --radius-lg:   14px;
  --radius-pill: 99px;

  /* Shadows */
  --shadow-card:  0 1px 3px rgba(0,0,0,.08), 0 4px 16px rgba(34,69,109,.07);
  --shadow-hover: 0 8px 32px rgba(34,69,109,.14);
  --shadow-modal: 0 24px 64px rgba(0,0,0,.25);
}
```

### Responsive Breakpoints (mobile-first)

```css
/* Base: mobile ≤ 639px — single column, full-width elements */
@media (min-width: 640px)  { /* tablet  — 2-col grid */ }
@media (min-width: 1024px) { /* desktop — 3-col grid */ }
@media (min-width: 1280px) { /* wide    — max-width content container */ }
```

---

## Pages

### Home (`/`)

- **Hero section:** Dark navy background, eyebrow text ("2025 Breeding Season"), headline, subheading, and a search bar with stallion/type/state inputs and a gold Search button. Stat row beneath (active listings, live auctions, stud farms).
- **Filter bar:** Sticky below hero on scroll. Horizontal scrollable pills: All / Auction / Buy Now / Ending today / [location filters] / [price range filters]. Active pill fills navy.
- **Listing grid:** `ListingGrid` wraps `ListingCard` components. 3 cols desktop → 2 tablet → 1 mobile. Results count + sort dropdown in header row.
- **Data:** `GET /api/listings` on page load with filter params. Client-side filter state drives query string; filter changes trigger a new fetch. Pagination at 24 results.

### Listing Detail (`/listings/{id}`)

- **Image header:** Full-width stallion photo (600px tall desktop, 300px mobile). Stallion name and stud farm overlaid on gradient. Listing type badge top-left.
- **Content below image:** Price (large, prominent), GST label, then context-specific content:
  - **Auction:** Current bid, bid count, reserve status (met / not met / no reserve), live countdown timer with progress bar, bid history (anonymised), "Place a bid" form with current bid + min increment shown, minimum bid enforced client-side.
  - **Fixed price:** Quantity remaining bar, "Purchase nomination" button.
- **Secondary CTA:** "Enquire about this listing" below the primary action.
- **Unauthenticated state:** Primary CTAs show "Sign in to bid" / "Sign in to purchase" — clicking redirects to Entra ID login, then back to this page.

### Stallion Detail (`/stallions/{id}`)

Public read-only page. Stallion photo, name, breeding season, any descriptive info returned by the API. Below: grid of that stallion's active listings as `ListingCard` components.

### Stud Farm Detail (`/studfarms/{id}`)

Public read-only page. Farm name, state/location, description. Below: all active listings from this farm as a `ListingGrid`.

### Checkout (`/checkout/{listingId}`) — `[Authorize]`

Two-step flow, no payment gateway in Plan 3:

**Step 1 — Mare details**
- Form: mare name (required), additional notes (optional)
- Cannot proceed without mare name

**Step 2 — Confirmation & disclosure**
- Display: listing name, stallion, stud farm
- Display: total listing price (inc. GST)
- Display: platform fee amount in dollars (inc. GST) — calculated client-side as `price × (feePercent / 100)` for display only; the actual fee recording always happens server-side in `CheckoutService`
- Disclosure text: that the stud farm will invoice separately for the balance; that the balance arrangement is between buyer and stud farm; refund policy summary (90% / 10% split — stored as a constant string in `BuyerDisclosure.razor`, with a TODO comment to make it API-driven in a later plan)
- "Confirm purchase" button only active once the disclosure panel has been scrolled to / is fully visible
- On confirm: `POST /api/checkout/initiate` → success screen: "Nomination secured. The stud farm will be in touch."

### My Purchases (`/my-purchases`) — `[Authorize]`

Table/list of purchases: stallion name, listing price, platform fee paid, purchase date, binding status (shown inline as a pill — Pending / Awaiting Signatures / Complete / Disputed). No separate binding detail page in Plan 3. Empty state if no purchases.

### My Bids (`/my-bids`) — `[Authorize]`

Active and past bids. Status labels: **Leading** (green), **Outbid** (amber + "Bid again" CTA), **Won**, **Lost**, **Auction closed**. Sorted by auction end date.

### Enquiries (`/enquiries`) — `[Authorize]`

List of enquiry threads: stallion name, listing title, last message preview, unread badge (dot), timestamp. Sorted by most recent activity.

### Enquiry Detail (`/enquiries/{id}`) — `[Authorize]`

Chat-style thread. Messages grouped by sender (buyer vs stud farm). Timestamps. `MessageComposer` at bottom — textarea + Send button. Calls `POST /api/enquiries/{id}/messages`. Read status is managed server-side; no explicit mark-as-read call from the client.

---

## Components

### `ListingCard`

Parameters: `ListingSummaryDto Listing`

- **Image area (210px):** Stallion photo with bottom gradient overlay. Badge top-left (Auction / Buy Now / Ending today / N remaining). Save/favourite button top-right (heart icon, state stored client-side for now — persistence deferred to Plan 4). Stallion name + stud farm overlaid on gradient bottom.
- **Card body:** Price (large, bold), GST label. Right side: bid count or quantity remaining.
- **Progress bar (3px):** Gold for auction time remaining, navy for quantity remaining, red when urgent (< 6h or < 3 remaining).
- **Action row:** Primary button (Place a bid / Purchase nomination) + secondary (Enquire).
- **Hover:** `translateY(-3px)` lift + stronger box shadow. Image scales 1.05.

### `AuctionTimer`

Parameters: `DateTime ClosesAt`

Renders a countdown (days / hours / minutes) updated every 30 seconds via `System.Threading.Timer`. Colour shifts to red when < 6 hours remain. Disposes timer on component disposal.

### `BuyerDisclosure`

Parameters: `decimal ListingPriceIncGst`, `decimal PlatformFeeIncGst`, `EventCallback OnConfirmed`

Renders the mandatory disclosure panel. Uses `IntersectionObserver` (JS interop) to detect when the panel bottom is visible — enables the Confirm button only then. Displays calculated stud farm amount (`ListingPriceIncGst - PlatformFeeIncGst`) for transparency.

### `NavBar`

- Desktop: logo left, nav links centre, Sign In + Register right.
- Mobile: logo left, hamburger right. Hamburger toggles a drawer (Blazor boolean state, no JS). Drawer overlays page with nav links + auth buttons.
- Auth-aware: `AuthorizeView` shows user's display name and "My Account" dropdown when signed in; shows Sign In / Register when not.

### `Modal`

Generic shell with backdrop click-to-close, focus trap via JS interop, and `ChildContent` render fragment. Used for bid confirmation, error dialogs.

---

## API Services

Each service is a typed `HttpClient` wrapper:

```csharp
// Registration in Program.cs
builder.Services.AddHttpClient<ListingApiService>(c =>
    c.BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"]!))
    .AddHttpMessageHandler<AuthorizationMessageHandler>();
```

`AuthorizationMessageHandler` (from `Microsoft.AspNetCore.Components.WebAssembly.Authentication`) attaches the Entra ID access token to every request automatically.

**Services:**

| Service | Key methods |
|---|---|
| `ListingApiService` | `GetListingsAsync(filter)`, `GetByIdAsync(id)` |
| `StallionApiService` | `GetByIdAsync(id)` |
| `BidApiService` | `PlaceBidAsync(listingId, request)`, `GetMyBidsAsync()` |
| `CheckoutApiService` | `InitiateAsync(listingId, request)`, `GetMyPurchasesAsync()` |
| `EnquiryApiService` | `GetAllAsync()`, `GetByIdAsync(id)`, `PostMessageAsync(id, request)`, `CreateAsync(listingId, request)` |
| `UserApiService` | `GetMeAsync()` |

All services return `null` or throw a typed `ApiException` on failure. Components handle `ApiException` and render `<ErrorMessage>`.

---

## Auth

`Microsoft.Authentication.WebAssembly.Msal` configured in `Program.cs` with the Entra ID client ID and authority from `appsettings.json`.

- Public pages load without auth — API calls for public data (listings, stallions, stud farms) go unauthenticated.
- `[Authorize]` pages redirect to Entra ID login automatically. After login, the user is redirected back to the original page.
- Role claims (`Buyer`, `StudFarmAdmin`, `Staff`) come from the Entra ID token and are used in `AuthorizeView` to show/hide UI elements.
- The `BuyerDisclosure` component and checkout page require `Roles="Buyer"` — stud farm admins cannot purchase.

---

## Error Handling

| Layer | Approach |
|---|---|
| API 4xx/5xx | `ApiException` thrown by services, caught by components, rendered via `<ErrorMessage>` |
| Network failure | Caught at service level, friendly "check your connection" message |
| Unhandled exception | `ErrorBoundary` in `MainLayout` shows recovery screen |
| Form validation | DataAnnotations on request DTOs, Blazor `EditForm` with `ValidationSummary` |
| 404 route | `NotFound.razor` with a friendly message and link back to listings |

---

## Seed Data

A SQL seed script (`/scripts/seed-dev.sql`) creates:
- 2 stud farms (Coolmore Australia, Arrowfield Stud)
- 4 stallions with placeholder photos
- 3 fixed-price listings (varying quantities and prices)
- 2 auction listings (one ending soon, one with several days remaining)
- 1 test buyer user and 1 test stud farm admin user

Placeholder stallion images sourced from Unsplash horse photos (development only — replaced with real images in production via Azure Blob Storage).

---

## Testing

- **bUnit** for component tests: `ListingCard` renders correct price/badge for each listing type, `AuctionTimer` displays correct countdown, `BuyerDisclosure` disables confirm button until scrolled
- **Service tests:** Mock `HttpClient` responses, verify correct endpoints called with correct parameters
- **No Playwright in Plan 3** — end-to-end browser tests deferred to after Plan 4 when the full app is assembled

---

## Out of Scope (Plan 4)

- Stud Farm Admin pages (create/manage listings, view enquiries)
- Staff admin panel (fee management, user management, invoices)
- Save/favourite persistence (server-side)
- Payment gateway integration
- Real-time bid updates (SignalR)
- Playwright end-to-end tests
