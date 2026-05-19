# Stallions Nominations Marketplace

## Project Overview

A web-based marketplace for the Australian thoroughbred horse racing industry, enabling stud farms to list stallion nominations for sale via fixed-price listings or timed auctions. Registered buyers can browse, bid, and purchase nominations. The platform collects a percentage-based fee (inc. GST) on each successful transaction on behalf of Stallions Australia, then remits the remainder to the stud farm. Built and maintained by Stallions Australia.

## Tech Stack

- **Frontend:** Blazor WebAssembly (.NET 9)
- **Backend:** ASP.NET Core Web API
- **Database:** Azure SQL Database
- **Storage:** Azure Blob Storage (stallion images, documents)
- **Auth:** Azure AD / Microsoft Entra ID
- **Hosting:** Azure App Service
- **Serverless:** Azure Functions (background tasks, notifications)
- **Version Control:** GitHub — https://github.com/stallions-australia/stallions-nominations-marketplace

## Architecture Notes

- Blazor WASM client communicates with ASP.NET Core API backend
- Azure AD / Entra ID handles authentication for all user roles
- Blob Storage used for stallion profile images and nomination documents
- Azure Functions handle async tasks (email notifications, data processing)
- This project is currently standalone — future integration with ArionWeb data is possible but not in scope yet

## User Roles & Authentication

| Role | Access |
|---|---|
| **General Public** | Browse listings, view stallion profiles (no login required) |
| **Registered Buyer** | Bid on auctions, purchase fixed-price listings, pay platform fee, manage purchases |
| **Stud Farm Admin** | Create and manage their stud's nomination listings (cannot set fee %) |
| **Stallions Australia Staff** | Full admin — manage all listings, users, fee percentages, invoices, and platform content |

All authenticated roles use Azure AD / Entra ID. Role claims are used to control access throughout the app.

- Fee percentage fields are only writeable by the **Stallions Australia Staff** role — enforce this at the API level, not just the UI

## Transaction Model

This is critical — get this right throughout the entire codebase.

### How sales work

- Nominations are sold in two ways: **Fixed Price** or **Timed Auction**
- All listing prices are displayed **inclusive of GST**
- Transaction volume is low (expected fewer than 12/week) but values are high (e.g. $50,000 per nomination)

### Platform fee

- Stallions Australia charges a **percentage-based platform fee** on each successful transaction
- The fee percentage is set **per nomination by admin only** — it is never calculated automatically or assumed
- Fee percentages will vary per listing (e.g. 1% for one nomination, 3.5% for another)
- All fees are displayed and charged **inclusive of GST**
- The platform fee is **deducted from the listing price** — the stud farm receives the remainder
- Example: $10,000 listing with a 2% fee → Stallions Australia retains $200 (inc. GST), stud receives $9,800
- The stud farm then invoices the buyer separately for the balance at their own discretion and timing

### Buyer transparency (mandatory)

- At the point of purchase (fixed price or winning auction), the buyer must be shown clearly:
  - The total listing price (inc. GST)
  - The platform fee amount being paid to Stallions Australia
  - That the stud farm will contact them separately for the remaining balance
  - That the remaining balance arrangement is entirely between buyer and stud farm
- This disclosure must appear on the checkout/confirmation screen and in confirmation emails

### Fixed price listings

- A stud farm may offer **multiple nominations of the same stallion** via quantity on a single listing
- A stud farm may also create **multiple listings for the same stallion at different price points** (e.g. first 30 at $8,000, next 30 at $10,000) — each is a separate listing with its own quantity and price
- When a buyer purchases, the listing quantity decrements by one; when it reaches zero the listing closes automatically

### Auction rules

- Auctions end at a **fixed date/time** set by the listing admin at creation
- No rolling/last-bid extensions
- Highest bid at close time wins
- Minimum bid increment: **$25** (reviewable — nominations are expected to trade in the thousands)
- **Reserve price**: optional, defaults to enabled when creating a listing. If reserve is not met, the listing goes unsold
- **No-reserve auctions**: explicitly selectable by the listing admin
- If an auction ends with **zero bids**: the stud farm is notified and offered the choice to re-list (at a different starting price or as a fixed-price listing)
- If an auction ends with **bids below reserve**: same — stud farm is notified and offered the same re-list options
- Losing bidders are notified when the auction closes

### Buyer commitment (mare requirement)

- At the point of purchase (fixed price or winning auction), the buyer **must provide the mare details** they intend to use with the nomination before the sale is confirmed
- This creates a legal record tying the buyer, the nomination, and the mare — preventing on-selling of nominations outside the platform
- After purchase, the nomination is formally **bound through Stallions Australia** — the platform issues the binding record to the stud farm
- This requirement must be enforced at checkout — no mare details, no completed purchase

### Payment window

- After clicking Buy Now or winning an auction, the buyer must complete payment **within the same checkout session**
- If a fixed-price buyer abandons checkout or payment fails, the sale is voided and the listing quantity reverts
- If a winning auction bidder's payment fails: the **second-highest bidder** (if one exists) is offered the nomination — one offer, no further cascade to third or fourth bidders
- If the second bidder also fails to pay, or there was no second bidder, the stud farm receives the re-list notification (same flow as no bids / reserve not met)

### Cancellation and refunds

- The platform collects only the platform fee at time of purchase (e.g. $250 on a $10,000 nomination)
- The stud farm invoices the buyer separately for the remainder under its own payment terms (e.g. live foal guarantee conditions — potentially 6+ months after purchase)
- If the stud-side arrangement falls through entirely: Stallions Australia refunds **90% of the platform fee** and retains 10%
- This policy must be clearly stated in Terms & Conditions and on the checkout/confirmation screens
- The refund policy and exact T&C language are to be finalised closer to production — do not hardcode policy text in the application, store it as configurable content
- The goal of mare requirement + platform binding is to legally discourage buyers purchasing nominations and then cutting the platform out by dealing directly with the stud

### GST and financial reporting

- All amounts displayed inclusive of GST
- The platform fee must be stored with **three values**: `FeeIncGst`, `FeeExGst`, `GstAmount` — required for BAS/tax reporting
- Example: $10,000 listing, 2.5% fee → `FeeIncGst` = $250, `GstAmount` = $250 / 11 = $22.73, `FeeExGst` = $227.27

### Payment providers

The platform collects only the **platform fee** (not the full nomination price) via:

- **Stripe** (preferred for card payments)
- **PayPal**
- **POLi** (Australian direct bank transfer)
- **BPAY**
- Payment provider selection is TBD — design the payment layer to be provider-agnostic where possible

## Security Requirements

This platform handles high-value financial transactions — security is a top priority throughout.

- All API endpoints must be authenticated and authorised by role — no exceptions
- Platform fee percentage fields are **admin-only** — never editable by stud farms or buyers
- All payment-related data handled via payment provider SDKs — never store raw card data
- Implement HTTPS everywhere; no HTTP fallback
- Use Azure AD / Entra ID for all authentication — no custom username/password auth
- Apply rate limiting on all public-facing API endpoints
- Audit log all financial transactions, fee changes, and admin actions
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
- **Listing** — a nomination that has been published to the marketplace (fixed price or auction)
- **Fixed Price Listing** — a nomination available to buy immediately at a set price
- **Auction Listing** — a nomination sold to the highest bidder by a fixed end date/time
- **Bid** — a buyer's offer on an auction listing
- **Platform Fee** — the percentage-based fee charged by Stallions Australia on a successful transaction (inc. GST)
- **Enquiry** — a buyer's question or expression of interest prior to purchase
- **Season** — the breeding year (e.g. 2025 Season)
- **Invoice** — the post-transaction document sent to the stud farm summarising the sale and fee deduction

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
- Do not write SQL directly in controllers or Blazor components
- Do not assume CSS component library is in use until confirmed
- Do not integrate with ArionWeb databases unless explicitly instructed
- Do not skip the brainstorming/planning phase for new features — always spec first
- Do not allow fee percentage to be set or edited by any role other than Stallions Australia Staff
- Do not store raw payment card data under any circumstances
- Do not build checkout flows without the mandatory buyer disclosure about stud farm balance arrangement
- Do not assume a payment provider — keep the payment layer provider-agnostic until confirmed

## Future Integrations (out of scope for now)

- ArionWeb stallion and stud data
- Racing Australia / studbook data feeds
- Automated stud farm payouts / reconciliation
