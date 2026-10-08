# Business Model v2 — Listing Fee + Buyer Fee — Design

**Date:** 2026-10-09
**Status:** All business decisions agreed with David (2026-10-09). One accounting question
(GST invoicing for the buyer fee) is with the accountant — see "Still open". Supersedes the
transaction model in the 2026-05-20 data model spec and the original CLAUDE.md.

## Why the model changed

The original model took a percentage of each sale, collected partly at checkout and partly
months later from the stud. That model depends on the platform controlling the sale, and
it doesn't:

- A nomination has no real supply limit. A stud can list 10 on the platform and sell 10
  more privately the same week.
- A buyer who sees a stallion on the platform can always ring the stud directly. Any fee
  charged at the point of sale can be avoided, and the platform can't police it.
- The deferred "top-up to 2.5%" owed by the stud months later is messy to track and hard
  to collect.

v2 charges for what the platform does control, which is exposure to buyers. The stud pays
up front to list. After that, a private deal is the stud's business, not a loss to us.

## The model in one paragraph

A stud pays **one listing fee per stallion per season**, which lets them list as many
nominations for that stallion as they like that season. Nominations are sold by
**auction**, with open bidding and no asking price shown. A nomination that doesn't sell at
auction drops to **Make an Offer** until the end of the season. When a buyer wins an
auction or the stud accepts their offer, a flat **buyer fee ($150 inc. GST)** is paid to
Stallions Australia. The fee is charged **automatically at that moment** to a card the
buyer saved before bidding. The fee is **part of the nomination price, not on top of it**.
The buyer pays the stud the balance (price minus the buyer fee) directly, under the stud's
own terms, so the stud bears the cost. There are no percentage fees and nothing is
collected later.

## Agreed decisions

### D1 — Listing types: Auction, then Make an Offer. Fixed price is removed.

- Every nomination **must** go to auction first. A stud cannot list straight to Make an
  Offer.
- If an auction ends without a sale (no bids, reserve not met, or the winner's charge
  failed), the lot drops to **Make an Offer**.
- A Make an Offer listing **expires automatically at the end of the season**
  (`Season.EndDate`). The stud can also close it earlier.
- Fixed-price listings are removed completely: Buy Now, quantity, and decrementing stock.

### D2 — Auction format: open bidding, no asking price shown

- Bids are visible, showing the current high bid and the bid history. Bidder identities
  are anonymised to other buyers.
- No starting price or asking price is displayed.
- The stud may set a **hidden reserve**. The reserve is never shown, only whether it has
  been met ("Reserve met" / "Reserve not met").
- No-reserve auctions are allowed and are labelled as such.
- The end time is fixed, with no extensions. Highest bid at close wins if the reserve is
  met.
- Minimum bid increment is staff-configurable (current default $25).
- Why: open bidding gives the buyer price discovery, which is the one thing they can't get
  by ringing the stud. Hiding the asking price stops the listing acting as a free price
  sheet for private deals.

### D3 — Stud listing fee: per stallion, per season, unlimited nominations

- One fee covers one stallion for one season. Within that, the stud can run any number of
  auction lots and Make an Offer listings for that stallion.
- The standard fee amount is **set by Staff** and can be changed. A change applies to
  subscriptions taken out after it. Existing ones keep the amount they were charged.
- **Introductory offers** are configurable by Staff. Examples: waive the fee for a
  founding stud's first season, or a discounted amount until a date. Each subscription
  records which offer was applied and why.
- The stud must have an active, paid (or waived) subscription for a stallion and season
  **before any nomination for that stallion can be listed**.
- The stud pays by card through the payment provider when they activate the stallion for
  the season. Staff can also mark a subscription as paid by invoice or bank transfer, or
  as waived.
- Working figure: about $990 inc. GST per stallion per season. This is a setting, not a
  code value.
- The listing-fee terms sit in the configurable T&C content, never in code.
- The fee is stored as `FeeIncGst`, `FeeExGst` and `GstAmount` for BAS.

### D4 — Buyer fee: flat amount, charged instantly

- A flat **buyer fee**, currently **$150 inc. GST** and staff-configurable, is charged on:
  - winning an auction (reserve met), and
  - a stud accepting a Make an Offer offer.
- The fee is **charged automatically the moment the sale happens**. This closes the
  "win the auction, then go direct to the stud and never pay us" leak.
- To make an automatic charge possible, a buyer must have a **valid saved card on file**
  before they can bid or make an offer. The card is saved through the payment provider;
  we never store card details.
- The fee is **part of the nomination price, not on top of it**. Example: winning bid
  $10,000 → $150 is charged now, and the stud invoices the buyer for $9,850. The buyer's
  total outlay equals their bid, and the stud carries the cost.
- The sale record and both confirmations must show this split: price, buyer fee paid to
  Stallions Australia, and balance payable to the stud.
- Bids and offers below the buyer fee are rejected, so a sale can never be worth less
  than the fee.
- If the charge fails, the winner has a **2-hour grace period** (staff-configurable) to
  update their card. If it still fails, there's no sale, the winner loses it, and the lot
  drops to Make an Offer. It is not offered to the second-highest bidder.
- The fee amount is **copied onto each listing when it is published**, so a later change
  by Staff never changes the price of a live auction.
- The fee is stored as `FeeIncGst`, `FeeExGst` and `GstAmount`.

### D5 — No percentage fees and no deferred collection

- The following are removed: the per-listing fee %, the "fee deducted from the listing
  price", the stud top-up to 2.5%, and the 90% refund-on-fallthrough policy.
- Stallions Australia collects only the stud listing fee and the buyer fee.

### D6 — Mare details dropped

- Buyers are no longer asked for mare details at purchase.
- The platform still creates a **sale record** linking buyer, stud, stallion, season and
  amount. Both parties get a confirmation, so the deal is on record on both sides.
- The mare and the remaining terms are settled between buyer and stud.

### D7 — Make an Offer happens on the platform

- A buyer submits an offer amount through the platform. Only the stud sees it.
- When the stud accepts, the buyer fee is charged instantly (as D4) and the sale record
  and confirmations are created.
- The stud can accept or decline. Counter-offers are left for a later version.
- An offer expires after 7 days (staff-configurable).
- A buyer can hold one live offer per listing.
- Supply is effectively unlimited, so an accepted offer does not close the listing. It
  stays open until the stud closes it or the season ends.

### D8 — Auction lots and payment provider

- One auction lot is one nomination. A stud auctions several by running several lots,
  which they can do freely within the season fee.
- **Stripe** is the only provider for v1. Charging a saved card without the buyer present
  needs a card provider; BPAY can't do it, and POLi is believed to have closed in 2023.
  The payment layer stays behind an interface so PayPal or others can be added later.

## Still open

**GST invoicing for the buyer fee: confirm with the accountant before Phase 2.**
The fee comes out of the nomination price, so there are two ways to set it up:

- **(a) Stallions Australia's own fee to the buyer.** Stallions Australia issues the tax
  invoice to the buyer. The stud agrees, under the listing-fee terms, to reduce its price
  by the same amount.
- **(b) A deposit collected for the stud.** Stallions Australia collects the $150 as a
  part-payment on the stud's behalf and keeps it as its fee, charged to the stud. The tax
  invoice for the fee then goes to the stud, and the stud's own invoice to the buyer shows
  the full price less the deposit received.

The build is the same either way, apart from who receives the tax invoice and what the
documents say. Both store `FeeIncGst`, `FeeExGst` and `GstAmount`.

**Pricing note, not a build blocker.** Flat fees fall harder on cheaper stallions.

| Stallion | 3 nominations sold | Listing fee + buyer fees | Share of sales |
|---|---|---|---|
| $3,300 | $9,900 | $990 + $450 = $1,440 | about 15% |
| $33,000 | $99,000 | $990 + $450 = $1,440 | about 1.5% |

Every amount is a setting, and intro offers can be scoped per stud, so this can be tuned
after launch without code changes. Tiered listing fees by stud-fee band would be a later
feature.

## Leaks: what's closed and what isn't

| Leak | Status |
|---|---|
| Winner goes direct to the stud and never pays the buyer fee | **Closed:** the fee is charged automatically at close |
| Stud sells more nominations privately than through the platform | **Not our loss:** the stud paid the season fee regardless |
| Buyer rings the stud before the auction closes and deals privately | **Not our loss:** the season fee is already paid. Hiding the asking price reduces the incentive |
| Stud collects the price and never pays the deferred top-up | **Gone:** there is no deferred top-up |

## Impact on the existing build

### Unaffected
- Sign-in (Entra External ID) and database-backed roles
- Buyer verification
- The stud and stallion directory, and the stallion authorisation rules
- Enquiries
- Staff user management
- Stallion and stud profile pages
- Browse and search (the content changes, the structure doesn't)
- The T&C versioning, the agreement gate and the bid confirmation (only the wording
  changes, and that is editable content)

### Removed or reworked
| Area | Change |
|---|---|
| `FixedPriceListing` (~47 files) | Removed. Its data model, Buy Now checkout, quantity tracking, stud listing form option, listing cards and staff screens all go |
| Fee % on listings (~23 files) | Replaced by a flat buyer fee, read from settings and copied onto the listing at publish |
| `AuctionListing.StartingPrice` | Removed from display, and possibly removed entirely. The reserve becomes hidden |
| `Purchase` mare fields (~18 files) | Removed (`MareName`, `MareRegistration`, `MareBreed`) |
| `NominationBinding` (~21 files) | Simplified into the sale record with confirmations to both parties, with no mare details |
| Checkout page + `CheckoutService` | Replaced by "charge saved card at close or on offer acceptance". There is no interactive checkout page for buyers |
| `BuyerDisclosure` component | Rewritten for v2: the buyer fee is part of the price, the balance is paid to the stud directly under the stud's own terms |
| Refund flow (`RefundAsync`) | The 90% policy is removed. Staff keep a manual refund action for genuine errors |
| Staff Invoices | Changes from "sale summary to the stud" to listing-fee invoices and receipts for studs, plus buyer fee receipts |

### New
- **Platform settings (Staff):** buyer fee, standard listing fee, bid increment, grace
  period and offer expiry.
- **Stallion season subscription:** stallion and season, the fee charged (three GST
  values), the intro offer applied, status (pending / paid / waived) and paid date.
- **Intro offers (Staff):** a waiver or discount, scoped to a stud, all studs or a season,
  with an end date.
- **Buyer saved card:** the payment-provider customer and saved card, required before
  bidding or making an offer.
- **Auction close job:** a timer that runs every few minutes. It closes ended auctions,
  checks the reserve, charges the winner, handles the grace period, and sends
  notifications. This was designed in May but never built.
- **Make an Offer:** offers, stud accept and decline, expiry, and the charge on
  acceptance. It can reuse parts of the enquiry thread UI.
- **Emails:** outbid, won and charged, charge failed, offer received, offer
  accepted/declined, listing-fee receipt, and the sale confirmation to both parties.

## Suggested build order

0. **Housekeeping:** merge `feature/stallion-authorization` into `master`, push to GitHub,
   and clean up the old Claude worktrees.
1. **Domain changes:** remove fixed price and fee %, add settings and the season
   subscription, drop the mare fields, hide the reserve. Dev data only, so the migrations
   can be destructive.
2. **Payments foundation (Stripe):** buyer saved cards, the stud listing-fee charge, and
   the webhook.
3. **Auction close job:** automatic charge, grace period and notifications.
4. **Make an Offer.**
5. **Staff screens:** settings, intro offers, subscriptions, reworked invoices, and the
   updated T&C and disclosure copy.

Each phase gets its own implementation plan before any code is written.
