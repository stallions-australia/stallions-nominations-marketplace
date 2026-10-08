# Stallions Nominations Marketplace

## Project Overview

A web-based marketplace for the Australian thoroughbred horse racing industry, enabling stud farms to sell stallion nominations by auction, with unsold nominations able to stay listed as "Make an Offer". Registered buyers can browse, bid and make offers. Stallions Australia earns revenue from two flat fees: a **listing fee** paid by the stud (one per stallion per season) and a **buyer fee** charged to the buyer when a sale happens. The nomination price itself is paid by the buyer directly to the stud. Built and maintained by Stallions Australia.

The business model was redesigned in October 2026 — see `docs/superpowers/specs/2026-10-09-business-model-v2-design.md`. Older specs and plans (May–June 2026) describe the previous percentage-fee / fixed-price model; where they conflict with this file, this file wins.

## Tech Stack

- **Frontend:** Blazor WebAssembly (.NET 9)
- **Backend:** ASP.NET Core Web API
- **Database:** Azure SQL Database
- **Storage:** Azure Blob Storage (stallion images, documents)
- **Auth:** Microsoft Entra External ID (customer sign-in: email/password + Google); roles stored in the app database
- **Hosting:** Azure App Service
- **Serverless:** Azure Functions (background tasks, notifications)
- **Payments:** Stripe (v1) — behind a provider interface
- **Version Control:** GitHub — https://github.com/stallions-australia/stallions-nominations-marketplace

## Architecture Notes

- Blazor WASM client communicates with ASP.NET Core API backend
- Entra External ID handles authentication for all user roles; authorisation is database-backed (`StaffOnly`, `StudFarmAdminOnly`, `StudFarmOrStaff` policies)
- Blob Storage used for stallion profile images and nomination documents
- Azure Functions handle async tasks (auction closing, automatic buyer-fee charging, email notifications)
- The Stud / Stallion Directory is seeded from ArionWeb data via one-off SQL scripts — there is no live integration with ArionWeb

## User Roles & Authentication

| Role | Access |
|---|---|
| **General Public** | Browse listings, view stallion profiles (no login required) |
| **Registered Buyer** | Save a card, bid on auctions, make offers, pay the buyer fee, view their bids/offers/purchases |
| **Stud Farm Admin** | Pay listing fees, create and manage their stud's auction and Make an Offer listings, accept/decline offers (cannot set any fee) |
| **Stallions Australia Staff** | Full admin — manage all listings, users, fee settings, intro offers, subscriptions, invoices, and platform content |

All authenticated roles sign in through Entra External ID. The user's role comes from the `Users` table, not from token claims.

- Fee amounts and fee settings are only writeable by the **Stallions Australia Staff** role — enforce this at the API level, not just the UI

## Transaction Model

This is critical — get this right throughout the entire codebase.

### How sales work

- Nominations are sold by **Auction** first. A nomination that doesn't sell can stay listed as **Make an Offer** for as long as the stud wants
- There are **no fixed-price listings** — do not build Buy Now, quantity or stock tracking
- A nomination has effectively unlimited supply — a stud may sell the same stallion's nominations privately as well; the platform does not try to prevent this
- All amounts are displayed **inclusive of GST**
- Transaction volume is low but values are high (e.g. $20,000+ per nomination)

### Stud listing fee (per stallion, per season)

- A stud pays **one listing fee per stallion per season** — this allows unlimited auction lots and Make an Offer listings for that stallion in that season
- The standard fee amount is **set by Staff and changeable** — never hardcoded. A change applies only to subscriptions taken out afterwards
- Staff can configure **introductory offers** (waivers or discounts, scoped to a stud / all studs / a season, with an end date). Each subscription records the offer applied
- A listing cannot be published unless the stallion has a **paid or waived** subscription for that season
- Staff can mark a subscription paid by invoice / bank transfer, or waived

### Buyer fee (flat, charged instantly)

- The buyer pays a **flat buyer fee** (currently $250 inc. GST, Staff-configurable — never hardcoded) when:
  - they win an auction (reserve met), or
  - a stud accepts their Make an Offer offer
- The fee is **charged automatically at that moment** to the buyer's saved card — there is no later payment window. This closes the "win then deal direct with the stud" leak
- A buyer **must have a valid saved card** (via the payment provider) before they can bid or make an offer
- The buyer fee amount is **snapshotted onto the listing at publish** — a settings change never alters a live listing
- The nomination price is paid by the buyer **directly to the stud**, under the stud's own terms. The platform does not collect, hold or deduct from it
- There are **no percentage fees**, no deferred stud top-ups, and no 90% refund policy

### Auction rules

- Open bidding: current high bid and bid history are visible; bidder identities are anonymised to other buyers
- **No starting or asking price is displayed**
- The stud may set a **hidden reserve** — never shown, only "Reserve met / not met". No-reserve auctions are allowed and labelled
- Auctions end at a **fixed date/time**; no rolling/last-bid extensions; highest bid at close wins if reserve is met
- Minimum bid increment is Staff-configurable (default $25)
- One auction lot = one nomination
- At close: winner's card is charged the buyer fee automatically. If the charge fails, the winner is notified and gets a Staff-configurable grace period to update their card; if it still fails, no sale
- If an auction ends with no bids, below reserve, or with a failed charge: the stud is notified and can convert the lot to Make an Offer. No cascade to the second-highest bidder
- Losing bidders are notified when the auction closes

### Make an Offer

- Offers are made **on the platform**; only the stud sees them
- The stud accepts or declines; offers expire after a Staff-configurable period
- On acceptance the buyer fee is charged instantly (same as an auction win)
- An accepted offer does **not** close the listing — it stays open until the stud closes it

### Sale record

- Every completed sale (auction win or accepted offer) creates a sale record linking buyer, stud, stallion, season and price, and sends a confirmation to **both** buyer and stud
- Mare details are **not** collected — that is settled between buyer and stud

### Buyer transparency (mandatory)

- Before bidding / making an offer, and in the win/acceptance confirmation and emails, the buyer must be shown clearly:
  - The buyer fee amount charged by Stallions Australia
  - That the nomination price is paid directly to the stud, under the stud's terms
  - That the price arrangement is entirely between buyer and stud
- All policy and T&C wording is **configurable content** (versioned T&C store) — never hardcode policy text

### GST and financial reporting

- All amounts displayed inclusive of GST
- Every fee collected (listing fee and buyer fee) must be stored with **three values**: `FeeIncGst`, `FeeExGst`, `GstAmount` — required for BAS/tax reporting
- Example: $250 buyer fee → `FeeIncGst` = $250.00, `GstAmount` = $250 / 11 = $22.73, `FeeExGst` = $227.27

### Payment provider

- **Stripe** for v1 — needed for saved cards and off-session (automatic) charges
- Keep the payment layer behind an interface so another provider can be added later
- Never store raw card data; use the provider's customer / payment method references only

## Security Requirements

This platform handles high-value financial transactions — security is a top priority throughout.

- All API endpoints must be authenticated and authorised by role — no exceptions
- Fee amounts and fee settings are **Staff-only** — never editable by stud farms or buyers
- All payment-related data handled via payment provider SDKs — never store raw card data
- Payment webhooks must validate the provider's signature before processing
- Implement HTTPS everywhere; no HTTP fallback
- Use Entra External ID for all authentication — no custom username/password auth
- Apply rate limiting on all public-facing API endpoints
- Audit log all financial transactions, fee and settings changes, and admin actions
- Input validation on all forms, both client-side and server-side
- Use parameterised queries only — no string-concatenated SQL
- Secrets (connection strings, API keys, payment credentials) via Azure Key Vault only
- OWASP Top 10 should be considered for every feature built
- When in doubt, default to the more restrictive permission

## Domain Language

Use this terminology consistently throughout the codebase:

- **Nomination** — a single breeding right being offered for sale by a stud farm
- **Stallion** — the sire whose nomination is being listed
- **Stud / Stud Farm** — the farm offering nominations
- **Season** — the breeding year (e.g. 2025 Season)
- **Season Subscription** — a stud's paid (or waived) right to list a stallion for a season
- **Listing Fee** — the fee a stud pays for a Season Subscription (inc. GST)
- **Intro Offer** — a Staff-configured waiver or discount on the Listing Fee
- **Listing** — a nomination published to the marketplace (Auction or Make an Offer)
- **Auction Listing** — a nomination sold to the highest bidder by a fixed end date/time, with an optional hidden reserve
- **Make an Offer Listing** — a nomination open to private offers that the stud accepts or declines
- **Bid** — a buyer's offer on an auction listing
- **Offer** — a buyer's private offer on a Make an Offer listing
- **Buyer Fee** — the flat fee charged to the buyer on an auction win or accepted offer (inc. GST)
- **Sale Record** — the record of a completed sale, confirmed to both buyer and stud
- **Enquiry** — a buyer's question to a stud prior to bidding or offering
- **Invoice** — a receipt / tax invoice for a Listing Fee or Buyer Fee

## Project Structure (target)

```
/src
  /Client          # Blazor WASM frontend
    /Pages
    /Components
    /Services
    /wwwroot
      /css
      /images
  /Server          # ASP.NET Core Web API
    /Controllers
    /Services
    /Data
      /Entities
      /Repositories
  /Shared          # Shared models, DTOs, constants
/functions         # Azure Functions
/tests
  /Client.Tests
  /Server.Tests
```

## CSS & Design

- CSS approach is TBD — do not assume a component library until confirmed
- Use CSS custom properties (variables) for all colours, spacing, and typography
- Design should feel premium and industry-specific — avoid generic AI aesthetics
- Mobile-first, responsive across all breakpoints
- When the Frontend Design skill is active, commit to a specific aesthetic direction before writing any CSS — do not default to generic patterns

## Coding Conventions

- Follow standard .NET / C# conventions (PascalCase for types, camelCase for locals)
- Blazor components use `.razor` files; scoped CSS in `.razor.css`
- **Important:** Blazor scoped CSS `::deep` does not work inside `@media` blocks — use global CSS for media-query overrides instead
- API endpoints follow RESTful conventions
- All database access via repository pattern — no raw SQL in controllers
- Use `async/await` throughout; no blocking calls
- Structured logging via `ILogger<T>` in all services

## Azure Conventions

- Use `azd` (Azure Developer CLI) for all deployments — not `az` CLI
- Managed Identity preferred over connection strings where possible
- All secrets via Azure Key Vault — never hardcoded
- Environment-specific config via Azure App Configuration or `appsettings.{env}.json`

## Testing

- Write failing tests before implementation (TDD — red/green/refactor)
- Unit tests for all service layer logic
- Integration tests for API endpoints
- Playwright for end-to-end browser testing

## What NOT to Do

- Do not hardcode connection strings, API keys, or payment credentials anywhere
- Do not hardcode fee amounts, increments, grace periods or offer expiry — read them from Staff-managed settings
- Do not write SQL directly in controllers or Blazor components
- Do not assume CSS component library is in use until confirmed
- Do not integrate with ArionWeb databases unless explicitly instructed
- Do not skip the brainstorming/planning phase for new features — always spec first
- Do not allow any fee or fee setting to be set or edited by any role other than Stallions Australia Staff
- Do not store raw payment card data under any circumstances
- Do not build fixed-price / Buy Now listings, percentage fees, deferred stud fee collection, or mare-detail capture — these were removed in the v2 model
- Do not display an auction's reserve or an asking price
- Do not let a buyer bid or make an offer without a valid saved card
- Do not build bid, offer or win flows without the mandatory buyer disclosure about the buyer fee and the direct buyer–stud price arrangement

## Future Integrations (out of scope for now)

- Live ArionWeb stallion and stud data sync
- Racing Australia / studbook data feeds
- Additional payment providers (PayPal etc.)
