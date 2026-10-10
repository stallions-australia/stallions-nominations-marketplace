# v2 Phase 3 — Auction Close and Automatic Buyer Fee — Design

**Date:** 2026-10-10
**Status:** Design agreed with David (2026-10-10); awaiting written-spec review
**Branch:** `feature/v2-phase3-auction-close` (from master after Phase 2 merged as PR #2)
**Builds on:** `2026-10-09-business-model-v2-design.md` (D2, D4, D6) and
`2026-10-09-v2-phase2-payments-design.md` (saved cards, payment provider, transaction runner).

## Goal

Close ended auctions automatically and charge the winner's saved card the buyer fee at that
moment, so a win can no longer be taken off-platform without paying the fee.

1. **A timed job** closes ended auctions: hidden reserve check, highest bid wins.
2. **The winner's saved card is charged** the listing's buyer fee automatically.
3. **A failed charge** gives the winner the grace period from Platform Settings to update their
   card. If it still fails, there is no sale.
4. **A sale record** is created with the price, the buyer fee (three GST values) and the balance
   payable to the stud.
5. **Emails** go to buyers and the stud at each step.
6. **The interim checkout** page and its shared-secret complete endpoint are removed.

## Decisions (agreed 2026-10-10)

| # | Decision |
|---|---|
| A1 | **The close job runs inside the API** as a hosted background service, not an Azure Function. It reuses the API's services and payment code. Dev's App Service gets Always On so the job runs when the site is idle. CLAUDE.md is updated to match. |
| A2 | **Email via Azure Communication Services**, sent with the API's managed identity (no keys). Dev uses an Azure-managed sender domain. Emails go through an **outbox table** written in the same transaction as the change that triggers them. |
| A3 | **Grace-period retries:** retry as soon as the winner saves a new card, plus one final attempt on the card on file when the grace period ends. Then no sale. |
| A4 | **`Purchase` is the sale record.** Created Pending at close, Completed when charged, Voided on no sale. Shown to users as "Sale record". Close no longer creates nomination bindings; the binding code stays, unused, until the Phase 5 cleanup. |

## Architecture

### Close job

- **`AuctionCloseService`** (`BackgroundService`) wakes every `AuctionClose:IntervalSeconds`
  (default 60), creates a DI scope and calls **`IAuctionCloser.RunAsync()`**. All logic lives in
  `AuctionCloser`, which is unit-tested without the hosted service.
- One run, in order:
  1. **Close** auctions with `Status = Active` and `EndDateTime <= now − 30 s`, oldest first, up to
     `AuctionClose:BatchSize` (default 50).
  2. **Retry** Pending purchases flagged `RetryRequested` (the winner saved a new card).
  3. **Final attempt** for Pending purchases whose `ChargeDueBy <= now`.
- Each listing and purchase is processed independently. One failure is logged and the run moves
  on; the next run picks it up again.
- `AuctionClose:Enabled` (default true) can switch the job off, e.g. in tests or during
  maintenance. These three values are operational config, not business settings.

### Concurrency

- **Multiple instances:** `Listing` gains a `RowVersion` concurrency token. A second instance
  closing the same auction gets a concurrency conflict and skips it. Purchase charge steps are
  guarded the same way (`Purchase` gains a `RowVersion`).
- **Bids at the deadline:** `BidService.PlaceBidAsync` re-checks `Status == Active` and
  `EndDateTime > now` inside its transaction (it currently checks only before). The job's 30 s
  buffer means a bid that passed the check is committed before the job reads the bids.
- **Never charge inside a database transaction.** Each step is: transaction (record intent) →
  provider call → transaction (record result).

### Closing one auction (one transaction)

| Situation | Result |
|---|---|
| No bids | Listing **Unsold**, reason `NoBids`, `ClosedAt` set. Stud gets "No sale". |
| Highest bid below the hidden reserve | Listing **Unsold**, reason `ReserveNotMet`, `ClosedAt` set. All bidders get "Auction ended without a sale". Stud gets "No sale". |
| Reserve met, or no reserve | Highest bid **Won**; every other Active/Outbid bid **Lost**. Listing **AwaitingPayment**, `ClosedAt` set. A **Pending Purchase** is created. Losing bidders get "Auction ended — you were outbid". |

- The winner is the highest bid (`GetHighestBidAsync`, read before any bid status changes).
- The Pending Purchase holds: `TotalPriceIncGst` = winning bid; the buyer fee from the listing's
  `BuyerFeeIncGst` snapshot split with `GstBreakdown.FromIncGst` into `BuyerFeeIncGst`,
  `BuyerFeeExGst`, `BuyerFeeGst`; `BalancePayableToStudIncGst` = price − fee; `BidId`.
- A listing without a buyer-fee snapshot cannot be charged: it is closed **Unsold** with reason
  `ChargeFailed`, an error is logged and audited for Staff. (Phase 1 snapshots the fee at
  publish, so this should not happen.)
- Audit: `AuctionClosed` with the outcome.

### Charging

- The charge runs immediately after the closing transaction commits, in the same run.
- **Attempt:** load the winner's saved card. Call `IPaymentProvider.ChargeSavedCardAsync` with:
  customer id, payment-method id, `BuyerFeeIncGst`, a description ("Buyer fee — {stallion},
  {season}"), metadata (purchase id, listing id) and the idempotency key
  `buyer-fee-{purchaseId}-{attemptNo}`.
  - `ChargeAttempts` is incremented and `ChargeAttemptStartedAt` set in a transaction **before**
    the call. If the app stops before the result is recorded, the next run repeats the same
    attempt number with the same key, so the provider returns the original result rather than
    charging again. A pending attempt older than 2 minutes counts as interrupted.
  - A winner with no saved card, or one that is no longer valid, counts as a failed attempt
    without calling the provider (reason "No valid card on file").
- **Success** (one transaction): Purchase **Completed**, `PaymentProvider`, `PaymentReference`,
  `PaidAt`; listing **Sold**, `WinningBidId`. Winner gets "Won and charged"; stud gets "Sale
  confirmation". Audit `BuyerFeeCharged`.
- **First failure:** Purchase stays Pending; `ChargeDueBy = now + ChargeGracePeriodHours`
  (Platform Settings, read at that moment), `LastChargeFailure` = the provider's message. Winner
  gets "Payment failed — update your card by {time}". Audit `BuyerFeeChargeFailed`.
- **Retry on a new card:** when `PaymentEventProcessor` saves a card (`CardSavedEvent`) for a
  user with a Pending purchase past its first failure, it sets `RetryRequested = true` in the same
  transaction. The next run retries. Further failures only update `LastChargeFailure` (no repeat
  email); the deadline doesn't move.
- **Final attempt** at `ChargeDueBy`: success as above; failure → Purchase **Voided**, listing
  **Unsold** with reason `ChargeFailed`. Winner gets "No sale — payment not completed"; stud gets
  "No sale". Audit `BuyerFeeChargeAbandoned`. The lot is not offered to the next bidder.

### Payment provider additions

- **`IPaymentProvider.ChargeSavedCardAsync(ChargeRequest)`** → **`ChargeResult`**
  (`Succeeded`, `PaymentReference`, `FailureCode`, `FailureMessage`). Expected declines are
  results, not exceptions. Network or provider errors throw; the attempt stays interrupted and is
  repeated with the same key.
- **Stripe:** PaymentIntent create, AUD, `amount` via `PaymentAmounts.ToCents`, `customer`,
  `payment_method`, `off_session = true`, `confirm = true`, metadata, idempotency key. Status
  `succeeded` → success. A `card_error` (including `authentication_required`, when the bank asks
  the cardholder to approve) → failure with Stripe's decline message. Added to `IStripeApi` as
  `CreatePaymentIntentAsync`.
- **Fake provider:** the fake card page gains **Approve with a declining card**, which saves a card
  ending 0002 (like Stripe's decline test card). Charges to a 0002 card fail with "Your card was
  declined."; all others succeed with a fake reference. Development/Staging only, as before.

## Emails

### Outbox and sending

- **`OutboundEmails`** table: `Id`, `ToAddress`, `Subject`, `HtmlBody`, `TextBody`, `Template`,
  `RelatedEntityType`, `RelatedEntityId`, `CreatedAt`, `SentAt?`, `Attempts`, `NextAttemptAt?`,
  `LastError?`, `FailedAt?`.
- **`IEmailOutbox.Enqueue(...)`** adds a row through the current `DbContext`, so the email is
  saved in the same transaction as the change that caused it.
- **`EmailDispatchService`** (`BackgroundService`, every 30 s) sends due rows through
  **`IEmailSender`**. On error it retries with backoff (1, 5, 15, 60 min); after 5 attempts it
  sets `FailedAt` and logs an error.
- **`AcsEmailSender`** uses `Azure.Communication.Email` with `DefaultAzureCredential` (the App
  Service's managed identity) and the configured sender address.
- **`LogEmailSender`** logs the email instead of sending it, for local runs and tests.
- Config: `Email:Provider` (`Acs` or `Log`), `Email:AcsEndpoint`, `Email:SenderAddress`,
  `Email:PublicBaseUrl` (for links in emails). Startup fails if `Acs` is chosen without the
  endpoint and sender. Dev uses `Acs`; local `launchSettings.json` uses `Log`.

### Templates

- One small class per email in `Server/Email/Templates`, building subject, HTML and text from a
  model. Wording is plain and factual. Amounts inc. GST.
- Buyer emails about a win include the configured `BuyerFeeExplanation` and `StudFarmBalanceArrangement`
  text (the same wording as the disclosure). Policy wording is never hardcoded.
- Stud recipient: the farm's `ContactEmail`, falling back to the email of the farm's owner user.
- Bidders are never shown each other's identities. The stud's sale confirmation names the buyer
  (display name and email) so the stud can invoice the balance.

| Email | To | Trigger | Content |
|---|---|---|---|
| Outbid | Previous high bidder | A higher bid is placed (BidService) | Stallion, new high bid, end time, link to bid again |
| Won and charged | Winner | Charge succeeds | Price, buyer fee charged to Stallions Australia, balance payable to the stud directly under the stud's terms, stud contact, link to sale record |
| Payment failed | Winner | First failed charge | Reason, deadline, link to update card |
| No sale — payment not completed | Winner | Final failure | The lot is no longer theirs |
| Auction ended — you were outbid | Other bidders | Close with a winner | Stallion, that the auction has closed |
| Auction ended without a sale | All bidders | Close below reserve | Stallion, that it ended without a sale |
| Sale confirmation | Stud | Charge succeeds | Buyer, price, buyer fee, balance the buyer will pay the stud directly |
| No sale | Stud | No bids, below reserve, or final charge failure | Reason; that the lot can be offered as Make an Offer (Phase 4) |

### Infra

- New `infra/modules/communication.bicep`: Communication Services, an Email Communication
  Service with an Azure-managed domain, the domain linked to Communication Services. Outputs the
  endpoint and the managed-domain sender address (`DoNotReply@…azurecomm.net`).
- The App Service's managed identity gets an RBAC role on the Communication Services resource
  that allows sending email (exact role confirmed in the plan).
- `appservice.bicep`: `alwaysOn: true` for all environments; new settings `Email__Provider`
  (`Acs`), `Email__AcsEndpoint`, `Email__SenderAddress`, `Email__PublicBaseUrl`.
- Needs `azd provision --environment dev` (dev only, with David's go-ahead).
- A custom domain (e.g. stallions.com.au) can replace the managed domain later; that needs DNS
  records and is not part of this phase.

## Data model

| Change | Detail |
|---|---|
| `ListingStatus` | Adds `AwaitingPayment` and `Unsold`. |
| `Listing.CloseReason` | New, nullable enum `ListingCloseReason`: `NoBids`, `ReserveNotMet`, `ChargeFailed`. Set when Unsold. |
| `Listing.RowVersion` | New concurrency token. |
| `BidStatus` | Adds `Lost`. |
| `Purchase` | New: `ChargeAttempts` (int), `ChargeAttemptStartedAt?`, `ChargeDueBy?`, `LastChargeFailure?` (max 500), `RetryRequested` (bool), `RowVersion`. Unique index on `BidId` (filtered, non-null) so one winning bid can't produce two sale records. |
| `OutboundEmails` | New table (above). Index on (`SentAt`, `FailedAt`, `NextAttemptAt`). |

One migration: `V2Phase3AuctionClose`. Dev data only.

`GetHighestBidAsync` filters on `Status == Active`. The closer reads the highest bid before
changing any bid status, and bid status changes happen only at close, so this stays correct.

## Removing the interim checkout

Removed:
- Client: `Pages/Checkout.razor` and `.razor.css`, `CheckoutApiService.InitiateAsync`, and their tests.
- Server: `POST api/listings/{id}/checkout`, `POST api/purchases/{id}/complete` (the shared-secret
  endpoint), `InitiateCheckoutAsync`, `CompleteCheckoutAsync`, `CheckoutOptions.WebhookSecret`
  (and its `appsettings.Development.json` entry), `CheckoutRequest`, `CheckoutDisclosureDto`,
  and their tests.

Renamed:
- `CheckoutService`/`ICheckoutService` → `PurchaseService`/`IPurchaseService`, keeping
  `GetPurchasesAsync`, `GetPurchaseByIdAsync` and the Staff `RefundAsync`.
- `CheckoutOptions` → `DisclosureOptions`, keeping `StudFarmBalanceArrangement`,
  `BuyerFeeExplanation` and `SavedCardExplanation`. Still bound from the `Checkout` config section
  so no deployed setting changes.
- `CheckoutApiService` → `PurchaseApiService` (client), keeping `GetMyPurchasesAsync`.

## Pages

- **Listing page:** the bid form is hidden when the auction is not Active or its end time has
  passed; it shows "Auction closed". The winner sees "You won this auction" with the sale-record
  link, or "Payment failed — update your card by {time}" with the card link. Other users see
  "Auction closed". The reserve is still never shown.
- **My Bids:** shows Won, Lost and Outbid (and Leading for the current high bid). The Won row
  links to the sale record. "Bid again" only while the auction is open.
- **My Purchases** → titled **My sale records**. Pending shows "Charging buyer fee" or "Payment
  failed — update your card by {time}"; Voided shows "No sale — payment not completed".
- **Stud My Listings / listing detail and Staff Listings:** new **Awaiting payment** and
  **Unsold** badges, with the reason. Close is hidden for AwaitingPayment, Sold and Unsold. Staff
  force-status cannot set AwaitingPayment or Unsold (they are set only by the closer).
- **Card page:** when the buyer has a payment-failed purchase, it shows "Saving a new card will
  retry the payment for {stallion}".

## Security

- No new public endpoints. The job runs in-process; the shared-secret complete endpoint is
  removed, so nothing anonymous can mark a sale paid.
- Amounts come only from the listing's fee snapshot and the winning bid on the server.
- Charges use provider references only; no card data is stored or seen.
- Each charge has an idempotency key; each state change is audited; each email is queued in the
  same transaction as its change.
- Email sending uses managed identity; no keys in config.
- Buyers see only their own sale records; studs see only their own listings (unchanged).

## Errors

| Situation | Behaviour |
|---|---|
| Two instances close the same auction | Concurrency conflict; one wins, the other skips. |
| App stops after a charge is sent | Next run repeats the attempt with the same idempotency key; no double charge. |
| Provider unreachable | Attempt stays interrupted; repeated next run with the same key. |
| Charge declined | Grace period, email, retry on new card, final attempt, then no sale. |
| Listing has no fee snapshot | Unsold (`ChargeFailed`), error logged and audited. |
| Email send fails | Retried with backoff; after 5 attempts marked failed and logged. The sale is unaffected. |
| Stud has no contact email | Falls back to the owner's email; if none, the email is skipped and a warning logged. |

## Testing

- **Unit tests (written first):**
  - Closer: no bids; below reserve; reserve met; no-reserve; bid statuses Won/Lost; Pending
    purchase values and GST split; missing fee snapshot; concurrency conflict skipped; 30 s buffer.
  - Charging: success; first failure sets the deadline from settings; no valid card; retry on new
    card; repeat failures don't re-email; final attempt success and failure; interrupted attempt
    reuses the same idempotency key.
  - `PaymentEventProcessor`: a saved card flags the pending purchase for retry.
  - BidService: end-time re-check inside the transaction; outbid email queued.
  - Emails: each template's content, the configured disclosure wording, stud recipient fallback.
  - Outbox dispatcher: sends, backoff, gives up after 5.
  - Stripe adapter: PaymentIntent options, success and decline mapping. Fake: 0002 declines.
  - Startup guard for `Email:Provider`.
- **bUnit:** listing page closed/won/payment-failed states; My Bids statuses; sale-record
  statuses; Awaiting payment and Unsold badges; card-page retry notice.
- **Dev click-through (fake provider):** a short auction ending in a sale; a declining card then
  a new card recovering the sale; a declining card left to expire into no sale; an auction ending
  below reserve; emails received for each.

## Out of scope

- Make an Offer and converting Unsold lots to it — Phase 4.
- GST tax invoices, removing the binding code, moving email wording into Staff-editable content
  — Phase 5.
- Staff alerts for emails that fail to send — later.
- A custom sender domain — when DNS is ready.
