# v2 Phase 4 — Make an Offer — Design

**Date:** 2026-10-11
**Status:** Approved (2026-10-11). Built on `feature/v2-phase4-make-an-offer` (2026-10-11); not merged. See the plan's "Changes made during the build" for refinements; `Offer.PurchaseId` was replaced by a lookup through `Purchase.OfferId` (unique).
**Branch:** `feature/v2-phase4-make-an-offer` (from master after Phase 3 merged as PR #5)
**Builds on:** `2026-10-09-business-model-v2-design.md` (D1, D4, D7) and
`2026-10-10-v2-phase3-auction-close-design.md` (charge pipeline, sale records, emails).

## Goal

A nomination that doesn't sell at auction can be offered as **Make an Offer**: buyers send
private offers, the stud accepts or declines, and on acceptance the buyer fee is charged
automatically — exactly as for an auction win.

1. The stud **converts** an Unsold auction lot into a Make an Offer listing.
2. Eligible buyers **make offers** (at least the buyer fee); only the stud sees them.
3. The stud **accepts** (buyer fee charged at once, sale record created) or **declines**.
4. Offers **expire** after the Staff-set period; the listing expires at the **end of the season**.
5. Emails keep buyer and stud informed at each step.

## Decisions (agreed 2026-10-11)

| # | Decision |
|---|---|
| M1 | **The stud converts** an Unsold lot ("Offer as Make an Offer"); it is never automatic. The no-sale email links to it. |
| M2 | **A new listing row per conversion** (`MakeAnOfferListing`, linked to the source auction), not a type change of the auction row. The auction keeps its bids, close reason and history. |
| M3 | **The buyer fee is charged during the stud's Accept action**, through the Phase 3 charge pipeline moved into a shared `BuyerFeeCharger`. The stud sees the outcome immediately. |
| M4 | **A buyer can withdraw** a pending offer and make a new one; still at most one pending offer per buyer per listing. No counter-offers. |
| M5 | **Unlimited supply:** accepting an offer does not close the listing or touch other pending offers; a stud may accept several. |

From CLAUDE.md / the business model (unchanged): offers are private to the stud; offers below
the buyer fee are rejected; a valid saved card is required to offer; the buyer sees the
mandatory disclosure before offering and in the acceptance email; no asking price is ever shown;
offers expire after `OfferExpiryDays` (Staff setting, currently 7); the listing expires at
`Season.EndDate`; fees are stored as three GST values.

## Data model

| Change | Detail |
|---|---|
| `ListingType` | Adds `MakeAnOffer`. |
| New `MakeAnOfferListing : Listing` | TPT table `MakeAnOfferListings`: `SourceAuctionListingId` (FK to the auction, Restrict), `ExpiresAt` (UTC instant of the end of `Season.EndDate`, Sydney time). Index on `SourceAuctionListingId`. |
| New `Offer` | `Id`, `ListingId` (FK → MakeAnOfferListing), `BuyerUserId`, `AmountIncGst` (12,2), `Status` (`Pending`, `Accepted`, `Declined`, `Withdrawn`, `Expired`, stored as string), `CreatedAt`, `ExpiresAt`, `RespondedAt?`, `PurchaseId?`, `ConcurrencyStamp`. Filtered unique index (`ListingId`, `BuyerUserId`) where `Status = 'Pending'`. Index (`Status`, `ExpiresAt`). |
| `Purchase` | Adds `OfferId?` (FK → Offer, NoAction) with a filtered unique index `[OfferId] IS NOT NULL AND [Status] <> 'Voided'` (mirrors `BidId`). A sale record comes from exactly one of `BidId` or `OfferId`. |
| `ListingStatus.Expired` | Now used: a Make an Offer listing past its season end. |

One migration: `V2Phase4MakeAnOffer`.

DTOs: `MakeAnOfferListingDto : ListingDto` (registered as JSON derived type `"MakeAnOffer"`,
`ExpiresAt`, no price fields); `ListingCardDto` gains nothing but carries
`ListingType = "MakeAnOffer"`; `OfferDto` (buyer view), `StudOfferDto` (stud view with buyer
name/email), `MakeOfferRequest { AmountIncGst }`, `AcceptOfferResultDto` (offer + sale-record
status + `ChargeDueBy`).

## Converting an Unsold lot

`POST api/listings/{id}/make-an-offer` (StudFarmAdminOnly) → 201 with the new listing.

Allowed only when:
- the caller's farm owns the auction listing;
- the auction is **Unsold** (any close reason);
- the season has not ended (`Season.EndDate` today or later, Sydney time);
- the stallion still has a **Paid or Waived** subscription for the season;
- no **Active** Make an Offer listing already exists for this auction.

The new listing copies stallion, season, farm, T&C and description; status **Active**,
`PublishedAt = now`; the buyer fee is snapshotted from current Platform Settings (the same rule
as publishing an auction); `ExpiresAt` from the season end. Audit `ConvertedToMakeAnOffer`
(source auction id, new listing id).

Studs can still close the Make an Offer listing (Cancelled) like any listing. Description stays
editable; T&C are locked (copied). Unpublish is not offered for Make an Offer listings.

## Making, withdrawing and seeing offers

**Make** — `POST api/listings/{id}/offers` (BuyerOnly), body `{ AmountIncGst }`:
- Shared buyer eligibility (moved out of `BidService` into one `BuyerEligibility` check used by
  both bids and offers): user Active (verified), current T&C accepted, valid saved card.
- Listing is an Active Make an Offer listing and `ExpiresAt` hasn't passed.
- `AmountIncGst >= listing.BuyerFeeIncGst` ("Offers must be at least $X, the buyer fee, which
  forms part of the price."); amounts to the cent, max 10,000,000.
- No Pending offer from this buyer on this listing (409).
- Creates the offer with `ExpiresAt = now + OfferExpiryDays` (setting read now), audits
  `OfferMade`, emails the stud ("Offer received"). Runs in a transaction with an in-transaction
  re-check of the listing status/expiry.

**Withdraw** — `POST api/offers/{id}/withdraw` (BuyerOnly; own Pending offer only) → Withdrawn,
audit `OfferWithdrawn`. No email.

**Buyer views** — `GET api/offers/mine` (BuyerOnly): own offers with listing/stallion/season,
status, expiry, sale-record id/status when accepted. `GET api/listings/{id}/my-offer`
(BuyerOnly): the buyer's latest offer on that listing (for the listing page).

**Stud views** — `GET api/listings/{id}/offers` (StudFarmOrStaff; stud must own the listing):
pending offers (buyer display name and email, amount, received, expires) and accepted offers with
their sale-record status.

**Privacy:** nothing public exposes offers — no counts, amounts or identities on the listing,
cards or browse. A buyer only ever sees their own offers.

## Accepting and declining

**Decline** — `POST api/offers/{id}/decline` (StudFarmAdminOnly; owning stud; Pending only) →
Declined, `RespondedAt`, audit `OfferDeclined`, email the buyer.

**Accept** — `POST api/offers/{id}/accept` (StudFarmAdminOnly; owning stud; Pending, not expired,
listing Active and not past `ExpiresAt`):
1. One transaction: offer → **Accepted** (`RespondedAt`); create a **Pending** `Purchase`
   (`OfferId`, price = offer amount, fee from the listing snapshot split with
   `GstBreakdown.FromIncGst`, balance = price − fee); link `Offer.PurchaseId`; audit
   `OfferAccepted`. A concurrency conflict (offer changed meanwhile) → 409.
2. Immediately run the first charge through `BuyerFeeCharger.ChargeAsync(purchaseId)`.
3. Return the result: paid; payment failed with the deadline; or "being processed" (provider
   unreachable — the background job repeats the attempt as in Phase 3).

Other pending offers and the listing are untouched. Accepting several offers is allowed.

## Shared charge pipeline — `BuyerFeeCharger`

The Phase 3 charge code (`StartAttemptAsync` / `ChargeCardAsync` / `RecordResultAsync`, charge
snapshot, idempotency key `buyer-fee-{purchaseId}-{attemptNo}`, interrupted repeats,
`ChargeAttemptLastSentAt`, stuck flag after 1 h, unmatched-success audit, grace period from
settings, retry on a newly saved card) moves unchanged from `AuctionCloser` into a scoped
`IBuyerFeeCharger`. `AuctionCloser` keeps closing auctions and calls the charger for due sale
records; the Accept action calls it once directly.

Only the effect on the listing differs, by the sale record's source:

| | Auction win (`BidId`) | Accepted offer (`OfferId`) |
|---|---|---|
| Charge succeeds | Listing **Sold**, `WinningBidId` set (as Phase 3) | Listing **unchanged** (stays Active) |
| Final failure (after grace) | Purchase Voided; listing **Unsold** (ChargeFailed); winning bid Lost (as Phase 3) | Purchase Voided; listing **unchanged** |
| Emails | as Phase 3 | same set, offer wording |

The Phase 3 auction behaviour and its tests stay exactly as they are; the extraction is a
refactor proven by the existing tests passing unchanged.

## Expiry (in the existing background job)

The periodic `AuctionCloseService` run gains a third step (`OfferExpirer`):
- Pending offers with `ExpiresAt <= now` → **Expired**; email the buyer.
- Active Make an Offer listings with `ExpiresAt <= now` → **Expired**; their Pending offers →
  Expired (buyer emailed).

When a stud closes a Make an Offer listing early (or Staff cancel it), its Pending offers →
Expired and the buyers are emailed ("the stud has closed this listing").

All steps are per-row transactions with concurrency stamps, safe on several instances, like the
closer.

## Emails

New (in the same email composer, made listing-type-neutral: methods take `Listing`):

| Email | To | When |
|---|---|---|
| Offer received | Stud | Offer made — buyer name, amount, offer expiry, link to the listing |
| Offer accepted and charged | Buyer | Charge succeeds on an accepted offer — price, buyer fee, balance payable directly to the stud, configured disclosure wording, stud contact, sale-record link |
| Offer declined | Buyer | Stud declines |
| Offer expired | Buyer | Offer expires, or the listing expires/closes with the offer pending |

Reused with offer wording where the text mentions the auction ("your accepted offer" instead of
"your winning bid"): payment failed, no sale (buyer), sale confirmation (stud), no sale (stud,
reason "the buyer's payment couldn't be completed"). The no-sale-to-stud email for an Unsold
auction gains a link: "Offer it as Make an Offer".

## Pages

- **Listing page (Make an Offer):** "Make an Offer" badge; buyer-fee notice; for an eligible
  buyer an offer form (amount ≥ fee) with a confirmation showing the mandatory disclosure
  (configured wording); the same card/verification gates as bidding. With a pending offer:
  "Your offer of $X is with the stud until {date}" + **Withdraw**. Otherwise the outcome of the
  buyer's last offer (declined / expired / accepted → sale-record link / payment failed → update
  card). Never shows a price, other offers or counts. Expired/closed listings show "No longer
  taking offers".
- **Cards / browse:** "Make an Offer" badge and button text; a "Make an Offer" filter pill.
- **Stud — My Listings / listing detail:** Unsold auctions get **Offer as Make an Offer**
  (confirmation explains it goes live immediately until the season ends). A Make an Offer listing
  shows an **Offers** panel: pending offers with **Accept** (confirm: "this charges the buyer the
  $X buyer fee") and **Decline**, and accepted offers with payment status. Close stays available.
- **Buyer — My Offers** (`/my-offers`, nav link): own offers with status and expiry, Withdraw for
  pending, sale-record link for accepted.
- **Staff — Listings:** the new type and Expired status display; no Staff offer management.

## Security

- Every endpoint authenticated and role-checked; ownership checked in services (stud owns the
  listing; buyer owns the offer).
- Offer amounts and identities are visible only to the owning stud (and the buyer for their own).
- Amounts come only from the buyer's offer and the listing's fee snapshot; fees never from the
  client.
- Accept is idempotent against double-clicks and two studs/tabs via the offer's concurrency
  stamp and the unique `OfferId` index; the charge keeps Phase 3's idempotency.
- Everything state-changing is audited.

## Errors

| Situation | Behaviour |
|---|---|
| Offer below the buyer fee | 400 with the fee amount |
| Second pending offer by the same buyer | 409 "You already have an offer with the stud — withdraw it to make a new one." |
| Offer on an expired/closed listing | 400 "This listing is no longer taking offers." |
| Accept an offer that just expired/was withdrawn | 409/400; nothing charged |
| Two accepts of the same offer at once | One succeeds, the other gets 409 |
| Charge fails on accept | Accepted; sale record Pending with grace deadline; buyer emailed; stud sees the deadline |
| Provider unreachable on accept | Accepted; "being processed"; the background job repeats the attempt |
| Convert when the season has ended or subscription lapsed | 400 with the reason |

## Testing

- **Unit (first):** conversion rules; shared buyer eligibility (bids unchanged); make / withdraw /
  re-offer / one-pending rule / ≥ fee / privacy of views; accept (transaction, sale-record values
  and GST split, immediate charge success / decline / provider error), accept several, decline,
  concurrency conflict; `BuyerFeeCharger` source-specific outcomes for offers, with all Phase 3
  auction tests passing unchanged; expiry of offers and listings and on stud close; email content
  and recipients.
- **bUnit:** listing page states (form, pending + withdraw, outcomes, closed), stud offers panel
  (accept confirm, results), My Offers, convert button.
- **Dev click-through (fake provider):** convert the below-reserve Dundeel; a buyer offers;
  the stud declines; the buyer offers again; the stud accepts on a good card (charged, emails);
  a second buyer offers on the declining card; accept → payment failed → save a good card →
  charged.

## Out of scope

- Counter-offers; Staff offer management; offer history beyond the buyer's and stud's own views.
- Relisting an Unsold lot as a new auction.
- GST tax invoices, Staff-editable email wording, NominationBinding removal — Phase 5.
