# v2 Phase 5a — Intro Offers, Tax Invoices and Refunds — Design

**Date:** 2026-10-11
**Status:** Approved (2026-10-11). Built on `feature/v2-phase5a-intro-offers-tax-invoices` (2026-10-12); not merged. See the plan's "Plan decisions" and "Changes made during the build" for refinements (business details on Staff-only endpoints; a reversed unpaid invoice is shown as reversed, not owed).
**Branch:** `feature/v2-phase5a-intro-offers-tax-invoices` (from master after Phase 4 merged as PR #6)
**Builds on:** `2026-10-09-business-model-v2-design.md` (D2 listing fee and intro offers, "Still open" GST
question, "Staff Invoices" rework), `2026-10-09-v2-phase2-payments-design.md` (subscriptions, card
activation, webhook), `2026-10-10-v2-phase3-auction-close-design.md` and
`2026-10-11-v2-phase4-make-an-offer-design.md` (buyer fee charge pipeline, sale records).

## Goal

Phase 5 of the v2 build order ("Staff screens") is split in two. **5a** is the money side:

1. Staff configure **intro offers** (waiver or dollars off the listing fee), applied automatically.
2. Every fee collected gets a proper Australian **tax invoice**: listing fee → the stud, buyer fee →
   the buyer. Studs paying by invoice / bank transfer get an unpaid tax invoice they can pay against.
3. A Staff **refund** of a buyer fee issues a GST **adjustment note** to the buyer.
4. **Staff → Invoices** becomes the register of every tax document, with BAS totals and CSV export.
5. Leftover v1 code is removed: `NominationBinding` and the "Sales by Stud Farm" invoices page.

**5b (later, separate spec):** Staff-editable disclosure and email wording (database-backed content),
settings history and T&C history views.

## Decisions (agreed 2026-10-11)

| # | Decision |
|---|---|
| 1 | **GST issuer — option (a):** Stallions Australia issues the buyer fee tax invoice **to the buyer**. (Resolves the business-model spec's "Still open" question.) The listing fee tax invoice goes **to the stud**. |
| 2 | **Format:** each tax document is a stored record with a sequential number, emailed as HTML and viewable / printable in the app (browser "Save as PDF"). No PDF library. |
| 3 | **Intro offers:** two types — **Waiver** (fee $0) or **AmountOff** (fixed dollars off the standard listing fee, inc. GST). Scoped to one stud or all studs, optionally to one season, with an end date. Applied automatically; the best (lowest fee) matching offer wins. |
| 4 | **Refunds:** Staff refund a buyer fee in Stripe, then record it in the app; the buyer is emailed a numbered **adjustment note** referencing the original tax invoice. |
| 5 | **Listing fee by invoice / bank transfer:** Staff **issue an unpaid tax invoice** on a Pending subscription (stud emailed amount due, due date, bank details). Marking the subscription paid marks that invoice Paid and sends a receipt. Card payments get a Paid tax invoice immediately. |
| 6 | Stallions Australia's **business details** (legal name, ABN, address, bank details, payment terms) are Staff-managed settings — never hardcoded. |
| 7 | A tax document is **never edited or deleted** after issue. Corrections are made with an adjustment note. |
| 8 | **No backfill:** existing dev subscriptions and sale records don't get documents retrospectively. |

## Data model

### `IntroOffer` (new)

| Column | Notes |
|---|---|
| `Id` | Guid |
| `Name` | required, ≤ 100 — shown on the tax invoice line ("Intro offer: Launch 2026") |
| `Type` | `Waiver` / `AmountOff` (string) |
| `AmountOffIncGst` | decimal(12,2), required and > 0 for `AmountOff`; null for `Waiver` |
| `StudFarmId?` | null = all studs |
| `SeasonId?` | null = any season |
| `EndsAt` | UTC; the offer applies to subscriptions **created** before this instant. Entered in Sydney time in the UI (end of the chosen day) via `SydneyTime` |
| `IsRetired` | Staff can retire an offer early; retired offers never apply |
| `CreatedAt`, `CreatedByUserId`, `UpdatedAt?`, `UpdatedByUserId?` | |

Index (`IsRetired`, `EndsAt`).

### `StallionSeasonSubscription` (changed)

- `IntroOfferId?` (FK → IntroOffer, restrict delete) — the offer applied, if any.
- `StandardFeeIncGst` decimal(12,2) — the standard listing fee at the time the subscription was
  created (so the invoice can show standard fee, offer, and fee payable). Existing rows: set equal to
  `FeeIncGst` in the migration (or 0 for Waived rows).

### `PlatformSettings` (changed) — business details

`BusinessLegalName`, `BusinessAbn` (11 digits, stored without spaces, displayed `12 345 678 901`),
`BusinessAddress` (multi-line), `BusinessEmail`, `BankAccountName`, `BankBsb` (6 digits),
`BankAccountNumber`, `InvoicePaymentTermsDays` (int, default 14). All nullable in the database except
the terms; the migration seeds empty values. **Issuing any tax document requires legal name, ABN and
address to be set**; issuing an *unpaid* invoice also requires the bank fields. If missing, the issue
step fails with a clear Staff-facing message (and for automatic issue paths, see Errors).

### `TaxDocument` (new)

| Column | Notes |
|---|---|
| `Id` | Guid |
| `Number` | string, unique: `SA-` + 6-digit zero-padded value from SQL sequence `TaxDocumentNumbers` (start 1). Sequences are safe across instances; gaps (rolled-back transactions) are acceptable |
| `Kind` | `ListingFeeInvoice` / `BuyerFeeInvoice` / `AdjustmentNote` (string) |
| `IssuedAt` | UTC |
| `Status` | `Unpaid` / `Paid` (adjustment notes are always `Paid` = settled) |
| `PaidAt?`, `DueAt?` | `DueAt` only for unpaid listing fee invoices (`IssuedAt` + terms days) |
| `Description` | e.g. "Listing fee — Dundeel, 2026 Season" / "Buyer fee — nomination to Dundeel, 2026 Season (auction)" |
| `StandardAmountIncGst?`, `IntroOfferName?`, `DiscountIncGst?` | only for listing fee invoices with an offer applied |
| `AmountIncGst`, `AmountExGst`, `GstAmount` | the three values (`GstBreakdown.FromIncGst`). For an adjustment note these are the **positive** amounts being reversed; the register subtracts them |
| `Supplier*` snapshot | `SupplierName`, `SupplierAbn`, `SupplierAddress`, `SupplierEmail` |
| `Recipient*` snapshot | `RecipientName`, `RecipientEmail`, `RecipientAbn?`, `RecipientAddress?` (stud: farm name, ABN, address, contact email; buyer: full name + email) |
| `Bank*` snapshot | `BankAccountName?`, `BankBsb?`, `BankAccountNumber?` — only on unpaid invoices |
| `SubscriptionId?` | FK, listing fee invoices and their adjustment notes |
| `PurchaseId?` | FK, buyer fee invoices and their adjustment notes |
| `OriginalDocumentId?` | FK → TaxDocument, adjustment notes only |
| `Reason?` | adjustment notes: "Buyer fee refunded" / "Listing fee waived" plus Staff's note |
| `StudFarmId?`, `BuyerUserId?` | recipient owner, for access checks and lists |
| `ConcurrencyStamp` | `IHasConcurrencyStamp` (status change Unpaid → Paid) |

Unique filtered indexes so a document can't be issued twice:
- `SubscriptionId` where `Kind = 'ListingFeeInvoice'`
- `PurchaseId` where `Kind = 'BuyerFeeInvoice'`
- `OriginalDocumentId` where `Kind = 'AdjustmentNote'` (one note reverses one invoice in full)

Indexes on (`Kind`, `IssuedAt`), `StudFarmId`, `BuyerUserId`.

### Removed

`NominationBinding` entity, table (migration drops it), `BindingsController`, `NominationBindingService`
/ `INominationBindingService`, its repository, `NominationBindingDto`, and any `Purchase` navigation to
it. The v1 `InvoiceDto` / `InvoiceLineDto`, `AdminService.GetInvoicesAsync` and its `AdminController`
endpoint.

## Intro offers

### Matching — `IntroOfferResolver` (pure, unit-tested)

Input: stud farm id, season id, standard fee, now, candidate offers. An offer matches when
`!IsRetired && now < EndsAt && (StudFarmId == null || == farm) && (SeasonId == null || == season)`.
Fee under an offer: Waiver → 0; AmountOff → `max(0, standard − AmountOffIncGst)`. Pick the offer
giving the **lowest fee**; tie → the offer that ends soonest, then the earliest created (deterministic).
No match → standard fee, no offer.

### Where it applies

- **Staff create subscription** (`SubscriptionService.CreateAsync`): if Staff leave the fee override
  blank, the resolver runs. If Staff enter an override, it is used as-is and no offer is recorded
  (existing behaviour; Notes explain). The create form shows a preview: "Intro offer *Launch 2026*
  applies: $990 → $495" (new endpoint `GET api/intro-offers/preview?studFarmId&seasonId`, Staff).
- **Stud self-serve activation** (`ActivateAsync`, new subscription only): the resolver runs. A
  Staff-created Pending subscription is reused unchanged (existing behaviour).
- Resulting fee **0** (Waiver, or AmountOff ≥ standard): the subscription is created **Waived**
  immediately, `PaymentMethod = Waived`, `WaiverReason = "Intro offer: {Name}"`, `PaidAt = now`. For
  activation the stud gets a success result without a payment page (`PaymentRedirectDto` gains
  `ActivatedWithoutPayment = true`; the client shows "{Stallion} is active for {Season} — intro offer
  applied"). No tax document is issued for $0.
- Otherwise: `FeeIncGst/ExGst/Gst` from the resolved fee, `IntroOfferId` and `StandardFeeIncGst` set.
- Audit: `CreateSubscription` details include `IntroOfferId`.
- An offer created later does not change existing Pending subscriptions.

### Staff CRUD — `IntroOffersController` (`api/intro-offers`, StaffOnly)

`GET` (list, incl. retired, with "times applied" count), `POST` (create), `PUT {id}` (edit),
`POST {id}/retire`. Validation: name required; AmountOff needs amount > 0; `EndsAt` in the future on
create; stud/season must exist. **Once an offer has been applied** to any subscription, only `Name`,
`EndsAt` and retire may change (type, amount and scope are locked so recorded history stays true).
No delete. Every create / edit / retire is audited (`IntroOffer` entity, old/new JSON).

## Tax documents — `TaxDocumentService`

One service owns numbering, snapshots and emails. Internal methods are called **inside the caller's
existing transaction** so the document, its email and the money state commit together.

| Method | Called from | Result |
|---|---|---|
| `IssueListingFeeInvoiceAsync(subscription, paid: bool)` | webhook (card paid) → `paid: true`; Staff "Issue tax invoice" → `paid: false`; Staff mark paid with no invoice yet → `paid: true` | `ListingFeeInvoice`, `Paid` or `Unpaid` (with `DueAt` and bank snapshot) |
| `MarkListingFeeInvoicePaidAsync(subscription, paidAt)` | Staff mark paid; webhook card payment for a subscription that already has an Unpaid invoice | invoice → `Paid`, `PaidAt`; receipt email |
| `IssueBuyerFeeInvoiceAsync(purchase)` | `BuyerFeeCharger` at the moment the purchase becomes `Completed` (auction win or accepted offer) | `BuyerFeeInvoice`, `Paid` |
| `IssueAdjustmentNoteAsync(original, reason)` | Staff refund (buyer fee); Staff waive of a subscription that has an invoice | `AdjustmentNote` for the full original amount |

Each is idempotent: if the document already exists (unique indexes above), it returns the existing one
and sends nothing new.

### Flows in detail

- **Card payment of the listing fee** (`PaymentEventProcessor.MarkListingFeePaidAsync`): after the
  subscription is marked Paid, in the same transaction — if an Unpaid invoice exists, mark it Paid
  (receipt email); else issue a Paid invoice (tax invoice email).
- **Staff → Subscriptions → Issue tax invoice** (new `POST api/subscriptions/{id}/issue-invoice`,
  StaffOnly): only for `Pending` subscriptions with fee > 0 and no invoice yet. Email: "Tax invoice
  SA-000123 — amount due $990.00 by 26 Oct 2026", with bank details and a link to the document.
- **Staff mark paid** (existing `MarkPaidAsync`, method Invoice / BankTransfer / Card): marks the
  existing invoice Paid (receipt email) or issues a Paid one.
- **Staff waive** (existing `WaiveAsync`, which only accepts `Pending` subscriptions — unchanged): if
  an Unpaid invoice exists, issue an adjustment note reversing it (reason "Listing fee waived —
  {WaiverReason}"), email to the stud. A Paid subscription can't be waived, so a paid listing fee is
  never reversed in 5a.
- **Buyer fee charged** (`BuyerFeeCharger.RecordResultAsync`, success branch): issue the buyer fee
  invoice in the same transaction as `Status = Completed`. The existing win / offer-accepted email
  gains a line "Your tax invoice SA-000124 is available here: {link}"; a separate tax-invoice email
  is also sent so the buyer has the document on its own.
- **Refund** (existing `PurchaseService.RefundAsync`, now with a request body `{ note }`): in one
  transaction — purchase → Refunded (as today), adjustment note against the buyer fee invoice
  (reason "Buyer fee refunded — {note}"), email to the buyer. If the purchase has no invoice (pre-5a
  dev data), refund is recorded without a note and Staff are told so.

### Emails (new, via the existing outbox; wording in code until 5b)

| Type key | To | When |
|---|---|---|
| `ListingFeeTaxInvoice` | stud contact email | paid invoice issued (card / mark paid) |
| `ListingFeeInvoiceDue` | stud | unpaid invoice issued |
| `ListingFeeReceipt` | stud | unpaid invoice marked paid |
| `BuyerFeeTaxInvoice` | buyer | buyer fee invoice issued |
| `AdjustmentNote` | recipient of the original | adjustment note issued |

Each email carries the document's key details (number, date, description, amounts inc/ex/GST,
supplier ABN) and a link to `/tax-documents/{id}`. The stud's email goes to the stud farm's contact
email, falling back to the stud admin user's email.

### Tax document content (page and email)

Heading **"Tax invoice"** (or **"Adjustment note"**), number, issue date, supplier legal name, ABN,
address, email; recipient name (and ABN / address for studs); description; for listing fee invoices
with an offer: standard fee, "Intro offer: {name} −$x", fee payable; "Amount (excl. GST)", "GST",
"Total (incl. GST)"; status Paid / Unpaid. Unpaid: due date, bank details and "Please use SA-000123 as
your payment reference". Adjustment note: "Adjusts tax invoice SA-000123 dated …", reason.
Buyer fee invoice: a fixed line "This fee forms part of the nomination price; the balance is payable
directly to the stud." taken from the existing configured disclosure text (`BuyerFeeExplanation`).

## Pages

| Page | Route | Who | Content |
|---|---|---|---|
| Tax document | `/tax-documents/{id}` | the buyer it was issued to; stud admins of the recipient farm; Staff | the document as above; print-friendly CSS (`@media print` hides nav); "Print / Save as PDF" button |
| My tax invoices (buyer) | `/account/tax-invoices`, linked from the account area beside My sale records | Registered buyer | number, date, description, total, kind, link |
| Stud tax invoices | section on the stud's subscriptions page | Stud Farm Admin | same list for the farm, incl. unpaid with due date |
| Staff → Invoices | `/staff/invoices` (replaces "Sales by Stud Farm") | Staff | filters: kind, status, from/to date (Sydney); totals block: invoices total inc / ex / GST, adjustment notes total, **net**, and unpaid total shown separately; table with recipient and links; **Export CSV** (filtered rows, one line per document, amounts signed) |
| Staff → Intro offers | `/staff/intro-offers` (new nav link) | Staff | list (active / ended / retired), create / edit modal, retire with confirm, times applied |
| Staff → Subscriptions | existing | Staff | create form shows the offer preview; new **Issue tax invoice** action on Pending; invoice number / status column with link |
| Staff → Transactions | existing | Staff | new **Refund buyer fee** action on Completed purchases: confirm modal "Refund $150.00 in Stripe first. This records the refund and emails the buyer an adjustment note." + note field; invoice number column |
| Staff → Settings | existing | Staff | new **Business details** section (fields above, client and server validation: ABN 11 digits, BSB 6 digits) |

API for documents (`TaxDocumentsController`): `GET api/tax-documents/{id}` (authorised as above,
others get **404**), `GET api/tax-documents/mine` (buyer), `GET api/tax-documents/stud` (stud admin),
`GET api/tax-documents` (Staff, filters + totals), `GET api/tax-documents/export.csv` (Staff).

## Security

- Every new endpoint authenticated; intro offers, issue invoice, refund, register and export are
  **StaffOnly** (and re-checked in the service, as `PlatformSettingsService` does).
- Business details and intro offers are fee settings → Staff-only writes, audited (old/new JSON).
- Document access: buyer only their own (`BuyerUserId`), stud admins only their farm's
  (`StudFarmId`), Staff all; anything else **404** (no existence leak). Ids are Guids.
- CSV export: values that start with `=`, `+`, `-`, `@` are prefixed with `'` (formula injection);
  amounts are written as numbers.
- Every document issue, invoice paid, adjustment note and refund is audited.
- No card data anywhere; documents hold names, emails, ABNs and addresses only.

## Errors

- **Business details missing** when a document must be issued automatically (webhook, buyer fee
  charge): the money path must not fail. The charge / payment still commits; the document is **not**
  issued; an error is logged and an audit row `TaxDocumentPending` is written. Staff → Invoices shows a
  banner "N fees have no tax invoice because business details are incomplete — complete them in
  Settings, then click **Issue missing tax invoices**" (`POST api/tax-documents/issue-missing`,
  StaffOnly: issues the document for every subscription / purchase that has a `TaxDocumentPending`
  audit row and still no document; the banner count is the same set). For Staff-initiated issue (issue invoice, mark paid, refund, waive) the action is
  rejected with "Complete the business details in Settings first."
- Numbering conflict is impossible (sequence + unique index); a duplicate-issue race resolves via the
  unique filtered indexes (catch the unique violation, return the existing document).
- Concurrent mark-paid vs webhook on the same invoice: `ConcurrencyStamp`; the loser re-reads and sees
  Paid (idempotent).

## Testing (test-first)

- `IntroOfferResolver`: scope (stud / all, season / any), end date boundary, retired, lowest fee wins,
  tie-breaks, AmountOff ≥ standard → 0.
- `SubscriptionService`: create and activate apply offers; $0 → Waived without payment page; override
  skips offers; issue invoice; mark paid with / without invoice; waive with invoice → adjustment note;
  missing business details rejected.
- `TaxDocumentService`: numbering format, GST three values, snapshots, idempotency (second issue
  returns existing, no second email), adjustment note amounts and link.
- `PaymentEventProcessor`: card paid → Paid invoice; with existing Unpaid → marked Paid; missing
  business details → payment still recorded, `TaxDocumentPending` audited.
- `BuyerFeeCharger`: success issues the buyer fee invoice in the same transaction; failure / void
  issues none.
- `PurchaseService.RefundAsync`: adjustment note + email; no invoice case.
- Access rules on `TaxDocumentsController` (buyer / other buyer / stud / other stud / Staff).
- Register totals (adjustments subtracted, unpaid separate) and CSV escaping.
- Intro offer CRUD: validation, lock after applied, audit.
- bUnit: tax document page (paid, unpaid, adjustment), Staff Invoices (filters, totals), Staff Intro
  offers, Business details form, refund modal, issue-invoice action.
- Migration: adds sequence, `IntroOffers`, `TaxDocuments`, subscription columns, settings columns;
  drops `NominationBindings`. Dev database only.

## Out of scope (5b or later)

- Staff-editable email and disclosure wording; settings and T&C history views.
- Partial refunds / partial adjustment notes; refunding listing fees through the app.
- PDF generation and attachments.
- Backfilling documents for pre-5a dev data ("Issue missing tax invoices" only covers fees recorded
  with a `TaxDocumentPending` audit row).
- Counter-offers, tiered listing fees.
