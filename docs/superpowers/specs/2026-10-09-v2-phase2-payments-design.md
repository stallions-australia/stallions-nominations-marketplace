# v2 Phase 2 — Payments Foundation (Stripe) — Design

**Date:** 2026-10-09
**Status:** Design agreed with David (2026-10-09); awaiting written-spec review
**Branch:** `feature/v2-phase2-payments` (from master after Phase 1 merged as PR #1)
**Builds on:** `2026-10-09-business-model-v2-design.md` (D3, D4, D8) and Phase 1.

## Goal

Give the platform a real payment foundation, without yet charging any buyer fee:

1. **Buyers save a card** with the payment provider. A valid saved card becomes required to bid.
2. **Studs pay the listing fee by card** by activating a stallion for the open season.
3. **A signed webhook** from the provider is the single source of truth for both.

Charging the saved card automatically (auction win, accepted offer) is **Phase 3**. The
interim checkout flow stays as it is until then.

## Decisions (agreed 2026-10-09)

| # | Decision |
|---|---|
| P1 | **Stripe-hosted Checkout** for both card capture (*setup* mode) and the listing-fee payment (*payment* mode). Card data never touches the app; Stripe handles 3-D Secure, wallets and receipts. |
| P2 | **Self-serve stud activation.** A stud activates a stallion for the open season and pays by card. Staff can still mark paid by invoice or bank transfer, or waive. |
| P3 | No Stripe account yet. A **fake provider** (`Payments:Provider = Fake`) lets dev be click-tested now; switching dev to Stripe later is a configuration change. The fake provider is allowed only in the Development and Staging environments (dev's App Service runs as Staging); anywhere else the server refuses to start. |
| P4 | **One saved card per buyer.** Replacing it overwrites the previous card. |
| P5 | Proper GST **tax invoices** for the listing fee are Phase 5. Phase 2 relies on Stripe's card receipt email (a Stripe dashboard setting). |

## Architecture

### Payment provider interface — `Server/Payments/IPaymentProvider`

Only what Phase 2 needs. The off-session charge is added in Phase 3.

| Member | Purpose |
|---|---|
| `EnsureCustomerAsync(User)` | Create or reuse the buyer's provider customer; returns its id. |
| `CreateCardSetupSessionAsync(customerId, successUrl, cancelUrl)` | Hosted page in setup mode; returns the redirect URL. |
| `CreateListingFeeSessionAsync(subscription, amountIncGst, payerEmail, successUrl, cancelUrl)` | Hosted page in payment mode, AUD. Session metadata carries the subscription id. Returns the redirect URL. |
| `ParseWebhookAsync(rawBody, signatureHeader)` | Verifies the signature and returns a provider-neutral `PaymentEvent`. Throws on an invalid signature. |
| `DetachCardAsync(paymentMethodId)` | Removes a replaced card at the provider. |

Provider-neutral `PaymentEvent` kinds handled in Phase 2:

- **CardSaved** — user, customer id, payment-method id, brand, last 4, expiry month/year.
- **ListingFeePaid** — subscription id, amount paid (cents), currency, provider payment id.

Other event types are acknowledged and ignored.

### Implementations

- **`StripePaymentProvider`** — official Stripe.net SDK. Webhook verification uses
  `EventUtility.ConstructEvent` with the signing secret. It handles `checkout.session.completed`:
  setup mode becomes CardSaved (it reads the payment method for brand, last 4 and expiry), and
  payment mode with `payment_status = paid` becomes ListingFeePaid.
- **`FakePaymentProvider`** — sessions are kept in memory, which suits single-instance dev. Its
  redirect URL is a server page, `/payments/fake/{sessionId}`, offering **Approve** and
  **Decline**. Approve builds the same `PaymentEvent` the Stripe webhook would and hands it to the
  processor, then returns to the success URL only if the event was processed (otherwise the
  cancel URL). Decline goes back to the cancel URL. The fake controller returns 404 unless the
  fake provider is active.

### `PaymentEventProcessor`

The only code that changes data because of a payment. It is provider-neutral.

- **Idempotent:** the provider event id is recorded in `ProcessedPaymentEvents`; a duplicate is
  ignored and still acknowledged.
- **CardSaved:** upserts the buyer's `SavedCard`. A previously saved, different payment method is
  detached at the provider. Audit: `SaveCard` or `ReplaceCard`.
- **ListingFeePaid:** loads the subscription.
  - If it is already Paid or Waived, nothing happens (logged).
  - If the amount or currency doesn't match the subscription's `FeeIncGst` in AUD, it is **not**
    marked paid; the event is logged as an error for Staff to investigate.
  - Otherwise it sets Status `Paid`, PaymentMethod `Card`, PaymentReference = provider payment id,
    and PaidAt. Audit: `ListingFeePaidByCard`.

### Configuration and secrets

- `Payments:Provider` — `Fake` or `Stripe`. Dev starts on `Fake`.
- `Payments:Stripe:SecretKey` and `Payments:Stripe:WebhookSigningSecret` — Key Vault secrets
  surfaced as App Service Key Vault references (the same pattern as the SQL connection string).
  They are never in a committed file. David loads the values; Claude never handles them.
- No publishable key is needed (hosted pages).
- **Infra:** `infra/modules/appservice.bicep` gains three app settings — `Payments__Provider`
  (a parameter: `Fake` for dev until Stripe is ready) and the two Key Vault references
  `Payments__Stripe__SecretKey` / `Payments__Stripe__WebhookSigningSecret`. This needs an
  `azd provision --environment dev` (dev only, with David's go-ahead). While the provider is
  `Fake`, the unresolved Key Vault references are never read.
- Startup fails if `Payments:Provider = Fake` outside Development/Staging, or if `Stripe` is chosen
  without both secrets.

## Data model

| Change | Detail |
|---|---|
| New `SavedCards` | `Id`, `UserId` (unique — one per buyer), `Provider`, `ProviderCustomerId`, `ProviderPaymentMethodId`, `Brand`, `Last4`, `ExpMonth`, `ExpYear`, `CreatedAt`, `UpdatedAt`. **Valid** = today is on or before the last day of the expiry month. |
| `Users.PaymentCustomerId` | Nullable; the provider customer id, created on first card setup. |
| New `ProcessedPaymentEvents` | `EventId` (key), `Provider`, `Type`, `ProcessedAt`. |
| `StallionSeasonSubscription` | No schema change. Card payments use `PaymentMethod = Card`, `PaymentReference` = provider payment id, `PaidAt`. |

One migration: `V2Phase2Payments`.

## Flows

### Buyer saves a card — `/account/card`

- Linked from the buyer nav and from the listing page when a card is needed.
- Shows the saved card ("Visa •••• 4242, expires 08/28", flagged if expired) or "No card saved".
- Shows the **mandatory disclosure** in configured wording (never hardcoded): the card is
  charged the buyer fee automatically on an auction win or accepted offer, the fee forms part
  of the price, and the balance is paid directly to the stud under the stud's terms.
- **Add card / Replace card** → `POST api/payments/card/setup-session` (BuyerOnly) → redirect
  to the hosted page → return to `/account/card?result=success|cancelled`.
- On success the page shows "Confirming…" and polls `GET api/payments/card` every 2 seconds for
  up to 60 seconds until the card appears (the webhook is the source of truth). Then it shows
  "Your card is still being confirmed — check back shortly."

### Bid gate

- `BidService.PlaceBidAsync` rejects a bid unless the buyer has a **valid** saved card:
  "Save a card before bidding."
- On the listing page, a verified buyer without a valid card sees **Save a card to bid**
  (linking to `/account/card`) instead of the bid form.

### Stud activates a stallion — My Stallions

- For the open season, each active stallion without a Paid or Waived subscription shows
  **Activate for {season} — {fee}**. The fee is the Pending subscription's amount if Staff
  created one, otherwise the standard listing fee.
- `POST api/subscriptions/activate { stallionId }` (StudFarmAdminOnly):
  - The stallion must belong to the caller's farm and be active; a season must be open; the
    stallion must not already be Paid or Waived for it.
  - Reuses an existing Pending subscription (keeping any Staff discount and its Staff notes),
    otherwise creates one at the standard fee with the three GST values. A repeat click never
    creates a duplicate (the unique index guarantees it).
  - Creates a payment session for that subscription's `FeeIncGst` and returns the redirect URL.
- The stud returns to `/admin/stallions?payment=success|cancelled`. On success the page polls
  until the stallion shows **Paid** (same timings as the card page).
- Staff's mark-paid (invoice or bank transfer) and waive actions are unchanged.

### Webhook — `POST api/payments/webhook/stripe`

- Anonymous, reads the raw body, verifies the signature before anything else.
- Invalid signature → 400, nothing processed. Duplicate or unhandled event → 200.
- Processing errors → 500 so Stripe retries (up to 3 days); safe because processing is
  idempotent.

## Security

- No card data is stored or seen by the app — only provider ids, brand, last 4 and expiry.
- Every amount is computed on the server from the subscription or settings, never taken from
  the browser.
- Webhook signature verified on the raw body; events processed once; every state change audited.
- Saving a card is BuyerOnly; activation is StudFarmAdminOnly and limited to the caller's own
  stallions in the open season. Staff payment actions stay StaffOnly.
- Secrets live only in Key Vault. The fake provider can only run in Development or Staging.

## Errors

| Situation | Behaviour |
|---|---|
| Card setup cancelled or failed | Nothing changes; "Card not saved — please try again." |
| Listing-fee payment declined or abandoned | Subscription stays Pending; the stud can try again. |
| Webhook slow to arrive | "Confirming…" for up to 60 s, then "still being confirmed — check back shortly." |
| Paid amount ≠ subscription fee | Not marked paid; error logged with both amounts for Staff. |
| Event already processed | Ignored, acknowledged with 200. |

## Testing

- **Unit tests (written first):**
  - Processor: card saved, card replaced (old card detached), fee paid, amount mismatch, wrong
    currency, already paid, duplicate event.
  - Bid gate: no card, expired card, card valid until the end of its expiry month.
  - Activation: own stallion only, open season required, already active rejected, Pending
    subscription reused with its discount, new subscription at the standard fee.
  - Startup guard: fake provider refused outside Development/Staging; Stripe refused without secrets.
- **Stripe adapter:** webhook verification using Stripe.net signing with a test secret —
  valid signature, tampered body, stale timestamp; mapping of setup and payment sessions to
  `PaymentEvent`s.
- **bUnit:** card page states (none / valid / expired / confirming); "Save a card to bid" on the
  listing page; the Activate button and its states on My Stallions.
- **Dev:** full click-through with the fake provider. After David creates the Stripe account and
  loads test keys, the same click-through with Stripe test cards (4242 4242 4242 4242, and
  4000 0025 0000 3155 for 3-D Secure).

## Out of scope (later phases)

- Charging the buyer fee automatically, the grace period and failure handling — Phase 3.
- Removing the interim checkout page and its shared-secret "complete" endpoint — Phase 3.
- Make an Offer — Phase 4.
- Intro-offer records, GST tax invoices and receipts in-app — Phase 5.
- Additional providers (PayPal etc.) — future; the interface allows it.

## Still open (not blocking Phase 2)

- **GST invoicing for the buyer fee** — with the accountant; needed before Phase 3.
- **Stripe account** — David to create it, enable card receipt emails, and add the dev webhook
  endpoint (`https://app-stallions-noms-dev.azurewebsites.net/api/payments/webhook/stripe`,
  event `checkout.session.completed`) when ready to switch dev from Fake to Stripe.
