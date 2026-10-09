# v2 Phase 1 — Domain Changes — Implementation Plan

**Date:** 2026-10-09
**Spec:** `docs/superpowers/specs/2026-10-09-business-model-v2-design.md`. Read it and
CLAUDE.md before starting.
**Branch:** create `feature/v2-phase1-domain` from `master`. Do not work on `master`
directly.

## Goal

Reshape the domain model to the v2 business model. This phase does not include payments,
the auction close job or Make an Offer (those are Phases 2–4). When Phase 1 is done:

- Fixed-price listings no longer exist anywhere: entity, DTOs, services, UI, tests and
  seed data.
- The per-listing fee % is gone. A flat **buyer fee** is read from Staff-managed
  **platform settings** and copied onto each listing when it is published.
- **Stallion season subscriptions** exist. A listing cannot be published without a paid
  or waived subscription for its stallion and season.
- Mare details are gone from purchases and the UI.
- The auction reserve is hidden from buyers, the starting price is removed, and bids
  below the buyer fee are rejected.
- Purchases record the price, the buyer fee (three GST values) and the balance payable to
  the stud.

## Ground rules

- **Test first.** For every service change, write or adjust the failing test first, then
  implement. Run `dotnet test` from the solution root after each task, and do not move on
  with red tests.
- **One commit per task**, with a conventional-commit message (`feat:` / `refactor:` /
  `chore:`) plus the project's usual co-author lines.
- **Line endings.** The repo stores LF and the working copy is CRLF via autocrlf. Never
  commit a mass line-ending change. Stage only the files you meant to change.
- **No hardcoded fee amounts.** Defaults live only in the settings seed (Task 2).
- **Database.** Run EF migrations against the **dev** database only. Before Task 9, check
  whether the **prod** database has any real data. If it does, stop and tell David. Do not
  deploy to prod in this phase.
- **Do not touch** the stud and stallion directory, sign-in, the T&C feature or enquiries,
  except where a compile error forces a one-line change.

## Tasks

### Task 1 — Remove fixed-price listings (server + shared)

Files:
- `Server/Data/Entities/FixedPriceListing.cs`: delete.
- `AppDbContext`: remove the TPT mapping.
- `Shared/Enums/ListingType.cs`: remove `FixedPrice`.
- `Shared/DTOs/Listings`: delete `FixedPriceListingDto.cs` and
  `CreateFixedPriceListingRequest.cs`, and remove the `[JsonDerivedType(...FixedPrice)]`
  line from `ListingDto`.
- `UpdateListingRequest`: remove `Price` and `Quantity`.
- `ListingService`: remove `CreateFixedPriceListingAsync`, plus the `FixedPriceListing`
  branches in `UpdateListingAsync`, `RelistAsync`, `GetListingCardsAsync` and the DTO
  mapping. `RelistAsync` now only relists auctions.
- `ListingsController`: remove the fixed-price create endpoint and the `type` filter value.
- `CheckoutService`: remove the fixed-price branch and the quantity decrement.
- `ListingRepository`: remove fixed-price queries and includes.

Tests: delete the fixed-price tests, and update `ListingDtoSerializationTests` to cover
only `Auction`.

### Task 2 — Platform settings

- New entity `PlatformSettings`, a single row with a fixed `Id`:
  - `BuyerFeeIncGst` (default 150)
  - `StandardListingFeeIncGst` (default 990)
  - `MinimumBidIncrement` (default 25)
  - `ChargeGracePeriodHours` (default 2)
  - `OfferExpiryDays` (default 7)
  - `UpdatedAt`, `UpdatedByUserId`
- Seed the single row through EF `HasData`. **These are the only place default amounts
  appear in the code.**
- `IPlatformSettingsRepository` / `PlatformSettingsRepository`: `GetAsync()` and
  `UpdateAsync()`.
- `IPlatformSettingsService` / `PlatformSettingsService`:
  - `GetAsync()` is available to any authenticated user, because buyers need to see the
    buyer fee.
  - `UpdateAsync(request)` is **StaffOnly**. Validate all values > 0, audit-log the old
    and new values, and set `UpdatedAt` / `UpdatedByUserId`.
- Shared: `PlatformSettingsDto` and `UpdatePlatformSettingsRequest`.
- `PlatformSettingsController` (`api/settings`):
  - `GET` with `[Authorize]`
  - `PUT` with `[Authorize(Policy = "StaffOnly")]`
- Add a GST helper in `Shared` (or Server): `GstBreakdown.FromIncGst(decimal incGst)`,
  which returns `(IncGst, ExGst, Gst)` with Gst = round(inc / 11, 2) and
  ExGst = inc − Gst.
  - Unit-test it with $150 → 13.64 / 136.36 and $990 → 90.00 / 900.00.
  - Use it everywhere fees are split from now on.

Tests: the service rejects non-Staff updates, rejects zero or negative values, writes an
audit log entry, and `GetAsync` returns the seeded defaults.

### Task 3 — Replace fee % with a snapshotted buyer fee

- `Listing`: remove `PlatformFeePercent` and add `BuyerFeeIncGst` (decimal?, null until
  published).
- `ListingService.PublishListingAsync`:
  - Remove the "fee % must be set" guard.
  - On publish, set `BuyerFeeIncGst` from the platform settings **only if it is still
    null**. That way the first publish locks the fee in, and unpublishing then
    republishing does not change it, following the existing `PublishedAt` pattern.
- `RelistAsync`: the new listing starts with `BuyerFeeIncGst = null`, so it picks up the
  current setting when published.
- Remove:
  - `AdminService.SetListingFeeAsync` and its endpoint
  - `SetListingFeeRequest` and `ListingFeeDto`
  - the fee column and fee editor on `StaffListings.razor` and the matching
    `StaffApiService` method
  - `PlatformFeePercent` from `ListingDto` and `ListingStaffSummaryDto`
- `ListingDto`: add `BuyerFeeIncGst`, visible to everyone because buyers must see it.

Tests: publishing snapshots the fee, republishing keeps the original, and a settings
change does not alter an already-published listing.

### Task 4 — Stallion season subscriptions

- New enum `SubscriptionStatus { Pending, Paid, Waived }`.
- New enum `SubscriptionPaymentMethod { Card, Invoice, BankTransfer, Waived }`.
- New entity `StallionSeasonSubscription`:
  - `Id`, `StallionId` (FK), `SeasonId` (FK), `StudFarmId` (FK)
  - `FeeIncGst`, `FeeExGst`, `GstAmount`
  - `Status`, `PaymentMethod?`, `PaymentReference?`, `PaidAt?`
  - `WaiverReason?`, `Notes?`
  - `CreatedAt`, `CreatedByUserId`
- Add a **unique index on (StallionId, SeasonId)**.
- Repository plus `SubscriptionService`:
  - `CreateAsync(stallionId, seasonId, feeIncGstOverride?)` is **StaffOnly**. The fee
    defaults to `StandardListingFeeIncGst` from settings, and the override is how intro
    offers are applied manually until Phase 5. Store the three GST values. Status starts
    as Pending.
  - `MarkPaidAsync(id, method, reference)` is **StaffOnly**.
  - `WaiveAsync(id, reason)` is **StaffOnly**. It sets the fee values to 0, the status to
    Waived and the method to Waived.
  - `GetForStudFarmAsync()` is for a **StudFarmAdmin** and returns their own farm's
    subscriptions only.
  - `GetAllAsync(seasonId?, status?)` is **StaffOnly**.
  - `HasActiveSubscriptionAsync(stallionId, seasonId)` returns true when the status is
    Paid or Waived.
  - Audit-log every create, paid and waive action.
- Controller `api/subscriptions` with the matching endpoints and policies.
- **Publish guard:** `ListingService.PublishListingAsync` returns `BadRequest` with
  "This stallion has no paid listing fee for {Season}. Contact Stallions Australia." when
  `HasActiveSubscriptionAsync` is false. **Staff-created listings are not exempt.**

Tests:
- Create uses the settings default.
- An override is respected.
- The unique constraint blocks a duplicate.
- Mark paid and waive set the right fields.
- Publish is blocked for Pending and allowed for Paid and Waived.
- A stud admin cannot see another farm's subscriptions.

### Task 5 — Remove mare details; reshape the purchase fee fields

- `Purchase`:
  - Remove `MareName`, `MareRegistration` and `MareBreed`.
  - Rename `PlatformFeeIncGst` / `PlatformFeeExGst` / `PlatformFeeGst` to
    `BuyerFeeIncGst` / `BuyerFeeExGst` / `BuyerFeeGst`.
  - Add `BalancePayableToStudIncGst` (= `TotalPriceIncGst − BuyerFeeIncGst`).
- `CheckoutRequest`: remove the mare fields. `CheckoutService.InitiateCheckoutAsync`:
  - Remove the mare-name guard and the fee % guard.
  - Take the fee from `listing.BuyerFeeIncGst` through the GST helper.
  - Set `BalancePayableToStudIncGst`.
  - Keep the auction-winner path. It gets replaced by the automatic charge in Phase 3.
- `CheckoutOptions`: remove `RefundPolicy`, and remove it from appsettings.
  `CheckoutDisclosureDto`:
  - Remove `RefundPolicy` and rename `PlatformFeeIncGst` to `BuyerFeeIncGst`.
  - Add `BalancePayableToStudIncGst`.
- Update `PurchaseDto`, `TransactionDto`, `InvoiceLineDto` and `AdminService` (dashboard
  revenue, transactions, invoices) for the renamed fields.
  - Invoice "remittance" becomes "balance payable to the stud".
  - Leave the overall invoices page structure alone, because it gets reworked in Phase 5.
- `NominationBinding`: no field changes in this phase (its future is decided in Phase 3).
  Just make sure it compiles.
- `AdminService.RefundAsync` / `CheckoutService.RefundAsync`: remove any 90%
  calculation. A refund is a full buyer-fee refund that Staff action manually.

Tests: update the checkout and admin tests for the renamed fields. Add tests that the
balance is computed correctly and that checkout no longer requires a mare.

### Task 6 — Auction rules: hidden reserve, no starting price, fee as the bid floor

- `AuctionListing`:
  - Remove `StartingPrice`.
  - `MinimumBidIncrement` is set from settings when the listing is created. The stud
    cannot edit it.
- `CreateAuctionListingRequest` / `UpdateListingRequest`: remove `StartingPrice` and
  `MinimumBidIncrement`. Keep `ReservePrice` and `IsNoReserve`.
- `BidService.PlaceBidAsync`:
  - The minimum first bid becomes `listing.BuyerFeeIncGst`.
  - Later bids must be at least the highest bid plus `MinimumBidIncrement`.
  - Reject any bid below `BuyerFeeIncGst`, so a sale can never be worth less than the
    fee.
- `AuctionListingDto`:
  - Remove `StartingPrice`.
  - `ReservePrice` is populated **only for Staff and the owning StudFarmAdmin**. Check
    the current mapping only hides it from non-Staff, and extend that to the owning stud.
  - Add `ReserveMet` (bool?): null when there are no bids or no reserve, otherwise
    whether the highest bid is at or above the reserve.
  - Keep `IsNoReserve`.
- `ListingCardDto`: remove any starting or asking price, and show the current high bid
  only.

Tests:
- A first bid below the fee is rejected.
- The increment is enforced.
- A buyer and another farm's admin get a null `ReservePrice`.
- The owning stud and Staff see the reserve.
- `ReserveMet` is right for each case.

### Task 7 — Client updates

- **Remove:**
  - `MareDetailsForm.razor` (and its css)
  - the fixed-price option and fields in `AdminListingForm.razor`
  - the fixed-price display in `ListingCard`, `ListingDetail`, `PriceDisplay`,
    `FilterBar`, `MyPurchases` and the Staff listing and transaction pages
  - Buy Now
  - starting price and increment inputs
  - the Staff fee-% editor
- **`ListingDetail.razor`:**
  - Show the current high bid and the bid history.
  - Show "No reserve", or "Reserve met / Reserve not met". Never show the reserve amount
    to buyers.
  - Show the buyer fee amount and a short line saying it forms part of the price, with
    the balance paid to the stud directly.
- **`BuyerDisclosure.razor`:** rewrite it for v2 to show the price, the buyer fee paid to
  Stallions Australia, the balance payable to the stud and the stud's own terms. Wording
  comes from the disclosure DTO or config, not from the component.
- **`Checkout.razor`:** remove the mare step.
- **New Staff page `/staff/settings`:**
  - The form edits all the platform settings, with validation.
  - Saving asks for confirmation.
  - Add it to the Staff nav.
- **New Staff page `/staff/subscriptions`:**
  - A list filterable by season and status.
  - **New subscription:** pick the stud farm, stallion and season. The fee is pre-filled
    from settings and can be overridden for intro offers.
  - Actions: **Mark paid** (method plus reference) and **Waive** (reason).
  - Add it to the Staff nav.
- **Stud admin `AdminStallions.razor`:**
  - Per stallion, show the subscription status for the open season: Paid / Waived /
    Pending / None.
  - When it isn't active, show "Listing fee required. Contact Stallions Australia to
    activate" (card payment arrives in Phase 2).
  - `AdminListingForm` shows the same notice, and the publish button is disabled when
    there's no active subscription. The server enforces this regardless.
- New client services: `PlatformSettingsApiService` and `SubscriptionApiService`
  (follow the existing `ServiceHelpers` / `ExtractErrorMessageAsync` pattern).
- **Client tests:** update `CheckoutTests` and `ListingDetailTests`, and add tests that
  the reserve amount never renders for a buyer and that publish is disabled without a
  subscription.

### Task 8 — Seed data and docs

- `scripts/seed-dev.sql`:
  - Remove the fixed-price inserts.
  - Remove `StartingPrice`, `PlatformFeePercent` and the mare columns.
  - Add Paid subscriptions for the seeded stallions in the open season, so dev listings
    can be published.
- Add a short note at the top of the older specs that the v2 spec supersedes their
  transaction model:
  - `2026-05-20-data-model-and-api-design.md`
  - `2026-05-21-blazor-frontend-design.md`
  - `2026-05-22-stud-farm-admin-design.md`

  Don't rewrite them.

### Task 9 — Migration and full verification

1. Check the prod database for real data, read-only (row counts in Listings, Purchases
   and Users). Report what you find. **If there is real data, stop and ask David before
   continuing.**
2. Create **one** EF migration, `V2Phase1Domain`, covering every model change above. Read
   the generated migration before applying it:
   - Existing fixed-price listings in **dev** and their dependent rows (bids none,
     purchases, enquiries, bindings) must be deleted in the migration's `Up` with
     explicit SQL **before** the table is dropped, so the foreign keys don't fail.
   - The renamed fee columns must be `RenameColumn`, not drop-and-add.
3. Apply it to the dev database and run `seed-dev.sql` against dev.
4. Run `dotnet build` and `dotnet test`. Everything must pass with no warnings newly
   introduced by this phase.
5. Run the app locally and click through with Playwright:
   - **As Staff:** edit the settings, create a subscription and mark it paid.
   - **As a stud admin:** a listing can't be published without a subscription and can be
     published with one. The reserve is visible to them.
   - **As a buyer:** the reserve is not visible, a bid below the fee is rejected, and the
     buyer fee is shown on the listing.
6. Report the results with screenshots of the three checks above.

### Task 10 — Finish

- Make sure `git status` is clean apart from `.claude/settings.local.json`.
- Push the `feature/v2-phase1-domain` branch to GitHub. **Do not merge to `master`.**
  David reviews first.
- Write a short summary covering:
  - what changed
  - anything that deviated from this plan and why
  - anything left for Phase 2
