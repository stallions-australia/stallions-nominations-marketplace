# v2 Phase 5a — Intro Offers, Tax Invoices and Refunds — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Staff-configured intro offers apply automatically to listing fees (a $0 result waives the fee at once); every fee collected gets a numbered Australian tax invoice (listing fee → stud, buyer fee → buyer), studs paying by invoice get an unpaid invoice to pay against, a Staff refund of a buyer fee issues an adjustment note, and Staff → Invoices becomes the register of every tax document with BAS totals and CSV export. Leftover v1 code (`NominationBinding`, "Sales by Stud Farm") is removed.

**Architecture:** Two new tables — `IntroOffers` and `TaxDocuments` (numbered from a SQL sequence, with filtered unique indexes so a fee can only ever get one invoice and one adjustment note) — plus offer/standard-fee columns on subscriptions and business-detail columns on `PlatformSettings`. A pure `IntroOfferResolver` picks the best offer; `SubscriptionService` applies it on Staff create and stud self-serve activation. One `TaxDocumentService` owns numbering, snapshots and emails and is always called **inside the caller's existing transaction** (webhook, Staff subscription actions, `BuyerFeeCharger`, refund), so the document, its email and the money state commit together. Automatic paths never fail because business details are missing — they audit `TaxDocumentPending` instead, and Staff issue those later. A read-side `TaxDocumentQueryService` serves the document page (404 for anyone but the recipient and Staff), the buyer and stud lists, the Staff register with totals, CSV export and "Issue missing tax invoices".

**Tech Stack:** .NET 9, ASP.NET Core, EF Core (SQL Server; in-memory provider in tests), Blazor WASM, xUnit + Moq + FluentAssertions, bUnit.

**Spec:** `docs/superpowers/specs/2026-10-11-v2-phase5a-intro-offers-tax-invoices-design.md`. Read it and CLAUDE.md first.
**Branch:** `feature/v2-phase5a-intro-offers-tax-invoices` (already created; the spec is committed).

> **Plan decisions that refine the spec (2026-10-11):**
> - **Numbering behind an interface.** The in-memory EF provider used by every server test can't run SQL
>   sequences, so `TaxDocumentService` takes an `ITaxDocumentNumberSource`: `SqlTaxDocumentNumberSource`
>   runs `SELECT NEXT VALUE FOR dbo.TaxDocumentNumbers` (inside the caller's transaction); tests use
>   `FakeTaxDocumentNumberSource` (a counter). The SQL implementation is exercised in the local and dev
>   click-throughs (Task 12).
> - **Business details have their own Staff-only endpoints** (`GET`/`PUT api/settings/business-details`).
>   `GET api/settings` is readable by any signed-in user (buyers need the buyer fee) and must not expose
>   bank details. Fee settings and business details are saved separately on the Settings page.
> - **ABN / BSB input** is normalised by removing spaces and hyphens, then must be exactly 11 / 6 digits
>   (no ABN checksum — the spec asks for 11 digits). Stored digits-only; shown as `12 345 678 901` and
>   `062-000` via the shared `BusinessNumbers` helper (server emails and client pages use the same code).
> - **Intro offer end date** travels as a Sydney calendar date (`EndsOn`, `DateOnly`); the server stores
>   `EndsAt = SydneyTime.EndOfDayUtc(EndsOn)`.
> - **Extra `TaxDocumentService` members** beyond the spec's four: `CanIssueAsync`, `SettleListingFeeAsync`
>   (mark the Unpaid invoice Paid, else issue a Paid one — used by the webhook and Staff mark paid) and
>   `RecordPendingAsync`. Issue methods take the acting user id for the audit row.
> - **Staff-initiated paths write the document first.** Issue invoice, mark paid, waive and refund call
>   `TaxDocumentService` before changing the subscription / sale record, inside one transaction, so a
>   "Complete the business details in Settings first." rejection has written nothing.
> - **An Unpaid invoice reversed by an adjustment note keeps `Status = Unpaid`** (documents are never
>   edited); it shows as "Reversed by SA-…" and is excluded from the register's unpaid total.
> - **Who sees which document:** listing fee invoices (and their notes) carry `StudFarmId` → the stud's
>   admins; buyer fee invoices (and their notes) carry `BuyerUserId` → that buyer only. A stud never sees
>   the buyer fee invoice for a sale of its nomination. Staff see everything.
> - **Win / offer-accepted email link:** `AuctionEmails` looks up the buyer fee invoice itself
>   (`ITaxDocumentRepository`), so `IAuctionEmails` signatures — and every Phase 3/4 Moq verification —
>   are unchanged. The charger issues the invoice before queuing those emails.
> - **Buyer fee invoice line** "This fee forms part of the nomination price…" is the configured
>   `DisclosureOptions.BuyerFeeExplanation`, read when the document is shown or emailed (not stored).
> - **Refund** takes an optional note (≤ 500 characters) and returns `RefundResultDto` with the adjustment
>   note number, or a message saying no note was issued (pre-5a sale with no invoice).
> - **CSV export** also prefixes values starting with a tab or carriage return (OWASP), quotes values
>   containing `,` `"` or line breaks, writes a UTF-8 BOM (Excel), and signs adjustment-note amounts negative.
> - **Document dates** (`IssuedOn`, `DueOn`, `PaidOn`) are Sydney calendar dates computed on the server, so
>   the page and email show the same date whatever the viewer's time zone. Register date filters are Sydney dates.
> - **The v1 `InvoiceDto` / `GetInvoicesAsync` removal happens in Task 11**, together with the new register
>   page, so the client compiles after every task. `NominationBinding` goes in Task 1.
> - **The buyer's name on a tax invoice is `User.DisplayName`** (there is no separate full-name field).
>
> **Changes made during the build (after reviews, 2026-10-12):**
> - Callers of `ITaxDocumentService` must have no unsaved tracked changes when they call it (its insert saves the
>   whole context); documented on the interface and respected at every call site.
> - The tax document page shows an Unpaid invoice that an adjustment note reversed as "Reversed by SA-…" with no
>   due date, bank details or payment reference; adjustment notes show an "Issued" badge.
> - `PlatformSettings.InvoicePaymentTermsDays` has no code default — only the `HasData` seed. The CSV BOM is written
>   as `String.fromCharCode(0xFEFF)`.
> - Final review: if a Staff "Issue tax invoice" beats the card webhook to the insert, the webhook now marks that
>   invoice Paid (receipt sent); Staff mark paid returns 409 if the fee was just paid by card, and waive returns 409
>   if the invoice is already Paid; "Issue missing tax invoices" carries on past an item that fails (logged, left
>   pending); a refund that races another refund returns 409; Staff Intro offers / Invoices keep their tables visible
>   when an action or lookup fails.
> - Task 12's local smoke was a direct localdb check of the sequence and indexes (local sign-in needs Entra); the
>   numbering runs end to end in the dev click-through.

## Global Constraints

- All amounts inc. GST; every document stores `AmountIncGst` / `AmountExGst` / `GstAmount` from
  `GstBreakdown.FromIncGst`. Adjustment notes store **positive** amounts; the register and CSV subtract them.
- Fee amounts, intro offers, business details and payment terms come from Staff-managed data — never hardcoded.
  `PlatformSettings` `HasData` stays the only place a default appears (`InvoicePaymentTermsDays = 14`).
- A tax document is never edited or deleted after issue; the only change is `Unpaid → Paid` (concurrency stamp).
- The money path never fails because business details are missing: webhook and buyer-fee charge commit and
  audit `TaxDocumentPending`. Staff-initiated paths reject with "Complete the business details in Settings first."
- Every endpoint authenticated and role-checked; intro offers, issue invoice, refund, register, export and
  issue-missing are `StaffOnly` **and** re-checked in the service. Document access failures are **404**.
- Audit: intro offer create / edit / retire (old/new JSON), business details (old/new JSON), every document
  issued, every invoice paid, every refund, every `TaxDocumentPending`.
- Phase 2–4 behaviour and its tests stay unchanged; test **fixtures** (constructors, `CreateSut`, setup
  helpers) may change, and existing test bodies change only where a signature in the spec changes
  (`RefundAsync`, `ActivateAsync` client return type) — each such edit is listed in its task.

## Review Focus

- **Business details missing on an automatic path must not fail the payment** → Task 6
  (`ListingFeePaid_WithoutBusinessDetails_StillRecordsThePayment_AndAuditsPending`) and Task 7
  (`Paid_WithoutBusinessDetails_StillCompletesTheSale_AndRecordsPending`).
- **Duplicate issue race** (two instances, double-click, webhook retry): one document, one email → Task 5
  (`Issue_LosingTheInsertRace_ReturnsTheWinnersDocument_AndSendsNoEmail`,
  `MarkPaid_WhenAnotherInstancePaidItFirst_IsIdempotent`) and the repository's
  `TryAdd_OnAUniqueViolation_ReturnsFalse_AndStopsTracking`.
- **Another buyer / another stud opening a document id** must get 404, and a stud must not see the buyer fee
  invoice for its own sale → Task 8 (`Get_AnotherBuyersDocument_IsNotFound`, `Get_AnotherStudsDocument_IsNotFound`,
  `Get_AStudCannotSeeTheBuyerFeeInvoiceForItsSale`).
- **Register totals:** adjustment notes subtracted (inc / ex / GST), unpaid shown separately, an Unpaid invoice
  reversed by a note not counted as owed → Task 8 (`Register_SubtractsAdjustmentNotes_AndShowsUnpaidSeparately`).
- **ABN / BSB typed with spaces, hyphens or the wrong length** → Task 4
  (`UpdateBusinessDetails_AcceptsSpacesAndHyphens_AndStoresDigitsOnly`, `UpdateBusinessDetails_RejectsABadAbn`,
  `UpdateBusinessDetails_RejectsABadBsb`) and the shared `BusinessNumbersTests`.

---

## Ground rules

- **Test first**: write the failing test, run it red, implement, run it green.
- After each task: `dotnet build stallions-nominations-marketplace.slnx` (0 warnings) and
  `dotnet test stallions-nominations-marketplace.slnx` green. `tests/Server.Tests` builds the client
  too, so keep the client compiling at every task.
- **One commit per task**, conventional message, ending with a blank line then
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Line endings:** repo LF, working copy CRLF. Stage only the files you changed.
- **Database:** the one migration goes to the local dev DB only (`(localdb)\mssqllocaldb`, `StallionsNomsDev`);
  Azure dev migrates itself on deploy (`db.Database.Migrate()` at startup). No prod. `azd deploy` only by David.
- In test code `Options.Create` clashes with the `Stallions.Server.Options` namespace — use
  `Microsoft.Extensions.Options.Options.Create`.
- Existing patterns: `ServiceResult`/`ServiceResult<T>` (`Ok`, `Created`, `BadRequest`, `Forbidden`,
  `NotFound`, `Conflict`); repositories save on each call; `ITransactionRunner.RunAsync` clears the
  change tracker per attempt (re-read inside it); `IAuditLogRepository.LogAsync(type, id, action, userId, details)`;
  `TimeProvider` for new code (tests: `TestClock`); `InlineTransactionRunner`; in-memory DB via
  `DbContextFactory.Create(name)` (+ `Database.EnsureCreated()` to get the seeded `PlatformSettings` row);
  `SqlExceptionFactory.WithNumber(2601)` for unique violations; `IHasConcurrencyStamp` renews the stamp on
  every update. Client: services have `virtual` methods for Moq; pages check `UserState.IsStaff` etc.
- The first line of each code block (`// src/...`, `@* src/... *@`) only names the file — don't copy it.
- `file:line` references are to the code as it is at the start of this branch. When an earlier task has already
  edited that file (e.g. `SubscriptionService` in Tasks 3 and 6), find the method or block by the name given.

## File map

| File | Responsibility |
|---|---|
| `src/Shared/Enums/IntroOfferType.cs`, `TaxDocumentKind.cs`, `TaxDocumentStatus.cs` | New enums (stored as strings) |
| `src/Shared/BusinessNumbers.cs` | ABN / BSB normalise, validate, format |
| `src/Server/Data/Entities/IntroOffer.cs`, `TaxDocument.cs` | New entities |
| `src/Server/Data/Entities/StallionSeasonSubscription.cs`, `PlatformSettings.cs` | Offer + standard fee; business details |
| `src/Server/Data/DbErrors.cs` | `IsUniqueViolation` (SQL 2601 / 2627) |
| `src/Server/Data/Repositories/IntroOfferRepository.cs`, `TaxDocumentRepository.cs` (+ interfaces) | Persistence |
| `src/Server/Services/IntroOfferResolver.cs` | Pure best-offer matching |
| `src/Server/Services/IntroOfferService.cs` (+ interface), `Controllers/IntroOffersController.cs` | Staff CRUD + preview |
| `src/Server/TaxDocuments/TaxDocumentService.cs` (+ `ITaxDocumentService`, `TaxDocumentIssueResult`) | Issue / mark paid / adjust, inside the caller's transaction |
| `src/Server/TaxDocuments/TaxDocumentNumberSource.cs` | `ITaxDocumentNumberSource` + SQL sequence implementation |
| `src/Server/TaxDocuments/TaxDocumentEmails.cs` (+ `ITaxDocumentEmails`) | The five document emails |
| `src/Server/TaxDocuments/TaxDocumentQueryService.cs` (+ interface), `TaxDocumentCsv.cs`, `Controllers/TaxDocumentsController.cs` | Document page, lists, register, CSV, issue missing |
| `src/Shared/DTOs/IntroOffers/*`, `src/Shared/DTOs/TaxDocuments/*` | DTOs |
| `src/Client/Services/IntroOfferApiService.cs`, `TaxDocumentApiService.cs` | Client APIs |
| `src/Client/Pages/TaxDocumentPage.razor`, `MyTaxInvoices.razor`, `Components/TaxDocuments/TaxDocumentList.razor` | Document page and lists |
| `src/Client/Pages/Staff/StaffIntroOffers.razor`, `StaffInvoices.razor` (rewritten) | Staff pages |
| Removed | `NominationBinding` entity / repository / service / controller / DTO / `BindingStatus`; v1 `InvoiceDto`, `InvoiceLineDto`, `AdminService.GetInvoicesAsync`, `GET api/admin/invoices` |

---

### Task 1: Domain — intro offers, tax documents, settings and subscription columns; remove NominationBinding

**Files:**
- Create: `src/Shared/Enums/IntroOfferType.cs`, `src/Shared/Enums/TaxDocumentKind.cs`, `src/Shared/Enums/TaxDocumentStatus.cs`
- Create: `src/Server/Data/Entities/IntroOffer.cs`, `src/Server/Data/Entities/TaxDocument.cs`
- Modify: `src/Server/Data/Entities/StallionSeasonSubscription.cs`, `src/Server/Data/Entities/PlatformSettings.cs`,
  `src/Server/Data/Entities/Purchase.cs:53`, `src/Server/Data/AppDbContext.cs` (lines 21, 33, 262–279, 382–406, 408–443, after 482),
  `src/Server/Program.cs:114`, `src/Server/Program.cs:137`
- Delete: `src/Server/Data/Entities/NominationBinding.cs`, `src/Server/Data/Repositories/INominationBindingRepository.cs`,
  `src/Server/Data/Repositories/NominationBindingRepository.cs`, `src/Server/Services/INominationBindingService.cs`,
  `src/Server/Services/NominationBindingService.cs`, `src/Server/Controllers/BindingsController.cs`,
  `src/Shared/DTOs/Bindings/NominationBindingDto.cs`, `src/Shared/Enums/BindingStatus.cs`,
  `tests/Server.Tests/Services/NominationBindingServiceTests.cs`
- Create: migration `V2Phase5aTaxDocuments` (generated, plus one hand-written `UPDATE`)
- Test: `tests/Server.Tests/Data/TaxDocumentModelTests.cs`

**Interfaces:**
- Produces: `enum IntroOfferType { Waiver, AmountOff }`; `enum TaxDocumentKind { ListingFeeInvoice, BuyerFeeInvoice, AdjustmentNote }`;
  `enum TaxDocumentStatus { Unpaid, Paid }`;
  `IntroOffer { Guid Id; string Name; IntroOfferType Type; decimal? AmountOffIncGst; Guid? StudFarmId; Guid? SeasonId; DateTime EndsAt; bool IsRetired; DateTime CreatedAt; Guid CreatedByUserId; DateTime? UpdatedAt; Guid? UpdatedByUserId; StudFarm? StudFarm; Season? Season }`;
  `TaxDocument : IHasConcurrencyStamp` (all spec columns, plus `TaxDocument? OriginalDocument`);
  `StallionSeasonSubscription.IntroOfferId (Guid?)`, `.StandardFeeIncGst (decimal)`, `.IntroOffer (IntroOffer?)`;
  `PlatformSettings.BusinessLegalName / BusinessAbn / BusinessAddress / BusinessEmail / BankAccountName / BankBsb / BankAccountNumber (string?)`, `.InvoicePaymentTermsDays (int)`;
  `AppDbContext.IntroOffers`, `AppDbContext.TaxDocuments`; SQL sequence `TaxDocumentNumbers` (bigint, start 1).
- Removes: `AppDbContext.NominationBindings`, `Purchase.NominationBinding`, everything named `NominationBinding*`, `BindingStatus`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Server.Tests/Data/TaxDocumentModelTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data;

public class TaxDocumentModelTests
{
    private readonly string _name = Guid.NewGuid().ToString();
    private AppDbContext Db() => DbContextFactory.Create(_name);

    [Fact]
    public void TheModel_NumbersDocumentsFromASequence_AndAllowsOneDocumentPerFee()
    {
        using var db = Db();
        // Sequences are relational annotations: read them from the design-time model.
        var sequence = db.GetService<IDesignTimeModel>().Model.FindSequence("TaxDocumentNumbers");
        sequence.Should().NotBeNull();
        sequence!.StartValue.Should().Be(1);
        sequence.IncrementBy.Should().Be(1);

        var doc = db.Model.FindEntityType(typeof(TaxDocument))!;
        doc.GetIndexes().Should().Contain(i => i.IsUnique && i.Properties.Single().Name == "Number");
        doc.GetIndexes().Should().Contain(i => i.IsUnique && i.Properties.Single().Name == "SubscriptionId"
            && i.GetFilter() == "[Kind] = 'ListingFeeInvoice'");
        doc.GetIndexes().Should().Contain(i => i.IsUnique && i.Properties.Single().Name == "PurchaseId"
            && i.GetFilter() == "[Kind] = 'BuyerFeeInvoice'");
        doc.GetIndexes().Should().Contain(i => i.IsUnique && i.Properties.Single().Name == "OriginalDocumentId"
            && i.GetFilter() == "[Kind] = 'AdjustmentNote'");
        doc.GetIndexes().Should().Contain(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "Kind", "IssuedAt" }));
    }

    [Fact]
    public async Task TwoContextsMarkingTheSameInvoicePaid_SecondOneConflicts()
    {
        using (var db = Db())
        {
            db.TaxDocuments.Add(new TaxDocument
            {
                Number = "SA-000001", Kind = TaxDocumentKind.ListingFeeInvoice, Status = TaxDocumentStatus.Unpaid,
                IssuedAt = DateTime.UtcNow, Description = "Listing fee", AmountIncGst = 990m, AmountExGst = 900m, GstAmount = 90m,
                SupplierName = "Stallions Australia Pty Ltd", SupplierAbn = "12345678901", SupplierAddress = "1 Racecourse Rd",
                RecipientName = "Arrowfield", RecipientEmail = "sales@arrowfield.example"
            });
            await db.SaveChangesAsync();
        }
        using var first = Db();
        using var second = Db();
        var a = await first.TaxDocuments.SingleAsync();
        var b = await second.TaxDocuments.SingleAsync();

        a.Status = TaxDocumentStatus.Paid;
        await first.SaveChangesAsync();
        b.Status = TaxDocumentStatus.Paid;

        await second.Invoking(db => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task ASubscription_KeepsItsIntroOffer_AndStandardFee()
    {
        Guid id;
        using (var db = Db())
        {
            var offer = new IntroOffer { Name = "Launch 2026", Type = IntroOfferType.AmountOff, AmountOffIncGst = 495m,
                EndsAt = DateTime.UtcNow.AddDays(30), CreatedByUserId = Guid.NewGuid() };
            db.IntroOffers.Add(offer);
            var sub = new StallionSeasonSubscription { StallionId = Guid.NewGuid(), SeasonId = Guid.NewGuid(),
                StudFarmId = Guid.NewGuid(), FeeIncGst = 495m, StandardFeeIncGst = 990m, IntroOfferId = offer.Id };
            db.StallionSeasonSubscriptions.Add(sub);
            await db.SaveChangesAsync();
            id = sub.Id;
        }

        using var read = Db();
        var loaded = await read.StallionSeasonSubscriptions.Include(s => s.IntroOffer).SingleAsync(s => s.Id == id);
        loaded.StandardFeeIncGst.Should().Be(990m);
        loaded.IntroOffer!.Name.Should().Be("Launch 2026");
    }

    [Fact]
    public async Task TheSettingsSeed_HasFourteenDayTerms_AndNoBusinessDetailsYet()
    {
        using var db = Db();
        await db.Database.EnsureCreatedAsync();

        var settings = await db.PlatformSettings.SingleAsync();
        settings.InvoicePaymentTermsDays.Should().Be(14);
        settings.BusinessLegalName.Should().BeNull();
        settings.BusinessAbn.Should().BeNull();
        settings.BankBsb.Should().BeNull();
    }

    [Fact]
    public void NominationBindings_AreGone()
    {
        using var db = Db();
        db.Model.GetEntityTypes().Select(t => t.ClrType.Name).Should().NotContain("NominationBinding");
    }
}
```

- [ ] **Step 2: Run red** — `dotnet test tests/Server.Tests --filter FullyQualifiedName~TaxDocumentModelTests` → compile errors (`TaxDocument`, `IntroOffer`, new properties).

- [ ] **Step 3: Enums**

```csharp
// src/Shared/Enums/IntroOfferType.cs
namespace Stallions.Shared.Enums;

/// <summary>An intro offer either waives the listing fee or takes a fixed amount (inc. GST) off it.</summary>
public enum IntroOfferType
{
    Waiver,
    AmountOff
}
```

```csharp
// src/Shared/Enums/TaxDocumentKind.cs
namespace Stallions.Shared.Enums;

public enum TaxDocumentKind
{
    /// <summary>Listing fee tax invoice, issued to the stud.</summary>
    ListingFeeInvoice,
    /// <summary>Buyer fee tax invoice, issued to the buyer.</summary>
    BuyerFeeInvoice,
    /// <summary>Reverses one invoice in full (refund or waiver).</summary>
    AdjustmentNote
}
```

```csharp
// src/Shared/Enums/TaxDocumentStatus.cs
namespace Stallions.Shared.Enums;

/// <summary>Adjustment notes are always Paid (settled).</summary>
public enum TaxDocumentStatus
{
    Unpaid,
    Paid
}
```

- [ ] **Step 4: Entities**

```csharp
// src/Server/Data/Entities/IntroOffer.cs
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

/// <summary>
/// A Staff-configured waiver or discount on the listing fee, applied automatically to subscriptions
/// created before EndsAt. Never deleted; once applied, only Name and EndsAt may change (or it is retired).
/// </summary>
public class IntroOffer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Shown on the tax invoice line: "Intro offer: {Name}".</summary>
    public string Name { get; set; } = string.Empty;
    public IntroOfferType Type { get; set; }
    /// <summary>Dollars off the standard listing fee (inc. GST) for AmountOff; null for Waiver.</summary>
    public decimal? AmountOffIncGst { get; set; }
    /// <summary>Null = all studs.</summary>
    public Guid? StudFarmId { get; set; }
    /// <summary>Null = any season.</summary>
    public Guid? SeasonId { get; set; }
    /// <summary>UTC. Applies to subscriptions created before this instant (end of the chosen Sydney day).</summary>
    public DateTime EndsAt { get; set; }
    public bool IsRetired { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    // Navigation properties
    public StudFarm? StudFarm { get; set; }
    public Season? Season { get; set; }
}
```

```csharp
// src/Server/Data/Entities/TaxDocument.cs
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

/// <summary>
/// An issued tax invoice or adjustment note. Numbered SA-000001… from the TaxDocumentNumbers sequence.
/// Never edited or deleted after issue — the only change is an Unpaid invoice becoming Paid. Supplier,
/// recipient and bank details are snapshots taken at issue. Adjustment notes hold the positive amounts
/// they reverse.
/// </summary>
public class TaxDocument : IHasConcurrencyStamp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = string.Empty;
    public TaxDocumentKind Kind { get; set; }
    public DateTime IssuedAt { get; set; }
    public TaxDocumentStatus Status { get; set; }
    public DateTime? PaidAt { get; set; }
    /// <summary>Unpaid listing fee invoices only: IssuedAt + payment terms.</summary>
    public DateTime? DueAt { get; set; }
    public string Description { get; set; } = string.Empty;

    // Listing fee invoices with an intro offer applied
    public decimal? StandardAmountIncGst { get; set; }
    public string? IntroOfferName { get; set; }
    public decimal? DiscountIncGst { get; set; }

    public decimal AmountIncGst { get; set; }
    public decimal AmountExGst { get; set; }
    public decimal GstAmount { get; set; }

    // Supplier snapshot (Stallions Australia's business details at issue)
    public string SupplierName { get; set; } = string.Empty;
    public string SupplierAbn { get; set; } = string.Empty;
    public string SupplierAddress { get; set; } = string.Empty;
    public string? SupplierEmail { get; set; }

    // Recipient snapshot (stud: farm name, ABN, address, contact email; buyer: name + email)
    public string RecipientName { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public string? RecipientAbn { get; set; }
    public string? RecipientAddress { get; set; }

    // Bank snapshot — unpaid invoices only
    public string? BankAccountName { get; set; }
    public string? BankBsb { get; set; }
    public string? BankAccountNumber { get; set; }

    public Guid? SubscriptionId { get; set; }
    public Guid? PurchaseId { get; set; }
    /// <summary>Adjustment notes only: the invoice reversed.</summary>
    public Guid? OriginalDocumentId { get; set; }
    /// <summary>Adjustment notes: "Buyer fee refunded — …" / "Listing fee waived — …".</summary>
    public string? Reason { get; set; }

    // The recipient's owner, for access checks and lists
    public Guid? StudFarmId { get; set; }
    public Guid? BuyerUserId { get; set; }

    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    // Navigation properties
    public TaxDocument? OriginalDocument { get; set; }
}
```

`src/Server/Data/Entities/StallionSeasonSubscription.cs` — after `public decimal GstAmount { get; set; }` (line 20) add:

```csharp
    /// <summary>The standard listing fee when this subscription was created (shown on the tax invoice).</summary>
    public decimal StandardFeeIncGst { get; set; }
    /// <summary>The intro offer applied, if any. Not set when Staff entered a fee override.</summary>
    public Guid? IntroOfferId { get; set; }
```

and with the navigation properties (after `public User CreatedBy { get; set; } = null!;`, line 40):

```csharp
    public IntroOffer? IntroOffer { get; set; }
```

`src/Server/Data/Entities/PlatformSettings.cs` — after `public int OfferExpiryDays { get; set; }` (line 15) add:

```csharp
    // Stallions Australia's business details, printed on every tax document. Issuing a document needs
    // legal name, ABN and address; an unpaid invoice also needs the bank details. ABN and BSB are digits only.
    public string? BusinessLegalName { get; set; }
    public string? BusinessAbn { get; set; }
    public string? BusinessAddress { get; set; }
    public string? BusinessEmail { get; set; }
    public string? BankAccountName { get; set; }
    public string? BankBsb { get; set; }
    public string? BankAccountNumber { get; set; }
    public int InvoicePaymentTermsDays { get; set; } = 14;
```

`src/Server/Data/Entities/Purchase.cs:53` — delete `public NominationBinding? NominationBinding { get; set; }`.

- [ ] **Step 5: `AppDbContext`**

Line 21 — delete `public DbSet<NominationBinding> NominationBindings => Set<NominationBinding>();`.
After line 33 (`public DbSet<Offer> Offers => Set<Offer>();`) add:

```csharp
    public DbSet<IntroOffer> IntroOffers => Set<IntroOffer>();
    public DbSet<TaxDocument> TaxDocuments => Set<TaxDocument>();
```

Lines 262–279 — delete the whole `// ── NominationBindings ──` block.

PlatformSettings block (lines 382–406): after `e.Property(s => s.MinimumBidIncrement).HasPrecision(12, 2);` add

```csharp
            e.Property(s => s.BusinessLegalName).HasMaxLength(200);
            e.Property(s => s.BusinessAbn).HasMaxLength(11);
            e.Property(s => s.BusinessAddress).HasMaxLength(500);
            e.Property(s => s.BusinessEmail).HasMaxLength(256);
            e.Property(s => s.BankAccountName).HasMaxLength(200);
            e.Property(s => s.BankBsb).HasMaxLength(6);
            e.Property(s => s.BankAccountNumber).HasMaxLength(10);
```

and in the `HasData` object, after `OfferExpiryDays = 7,` add `InvoicePaymentTermsDays = 14,`.

Subscription block (lines 408–443): after `e.Property(s => s.GstAmount).HasPrecision(12, 2);` add
`e.Property(s => s.StandardFeeIncGst).HasPrecision(12, 2);` and before the block's closing `});` add

```csharp

            e.HasOne(s => s.IntroOffer)
                .WithMany()
                .HasForeignKey(s => s.IntroOfferId)
                .OnDelete(DeleteBehavior.Restrict);
```

After the Outbound emails block (after line 482, before the closing `}` of `OnModelCreating`) add:

```csharp

        // ── Intro offers ─────────────────────────────────────────────────────
        modelBuilder.Entity<IntroOffer>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.Name).HasMaxLength(100).IsRequired();
            e.Property(o => o.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(o => o.AmountOffIncGst).HasPrecision(12, 2);
            e.HasIndex(o => new { o.IsRetired, o.EndsAt });
            e.HasOne(o => o.StudFarm).WithMany().HasForeignKey(o => o.StudFarmId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(o => o.Season).WithMany().HasForeignKey(o => o.SeasonId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(o => o.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(o => o.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        // ── Tax documents (numbered from a sequence; never edited except Unpaid → Paid) ──
        modelBuilder.HasSequence<long>("TaxDocumentNumbers").StartsAt(1).IncrementsBy(1);
        modelBuilder.Entity<TaxDocument>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Number).HasMaxLength(20).IsRequired();
            e.HasIndex(d => d.Number).IsUnique();
            e.Property(d => d.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(d => d.Description).HasMaxLength(300).IsRequired();
            e.Property(d => d.StandardAmountIncGst).HasPrecision(12, 2);
            e.Property(d => d.IntroOfferName).HasMaxLength(100);
            e.Property(d => d.DiscountIncGst).HasPrecision(12, 2);
            e.Property(d => d.AmountIncGst).HasPrecision(12, 2);
            e.Property(d => d.AmountExGst).HasPrecision(12, 2);
            e.Property(d => d.GstAmount).HasPrecision(12, 2);
            e.Property(d => d.SupplierName).HasMaxLength(200).IsRequired();
            e.Property(d => d.SupplierAbn).HasMaxLength(11).IsRequired();
            e.Property(d => d.SupplierAddress).HasMaxLength(500).IsRequired();
            e.Property(d => d.SupplierEmail).HasMaxLength(256);
            e.Property(d => d.RecipientName).HasMaxLength(200).IsRequired();
            e.Property(d => d.RecipientEmail).HasMaxLength(320).IsRequired();
            e.Property(d => d.RecipientAbn).HasMaxLength(14);
            e.Property(d => d.RecipientAddress).HasMaxLength(500);
            e.Property(d => d.BankAccountName).HasMaxLength(200);
            e.Property(d => d.BankBsb).HasMaxLength(6);
            e.Property(d => d.BankAccountNumber).HasMaxLength(10);
            e.Property(d => d.Reason).HasMaxLength(600);
            e.Property(d => d.ConcurrencyStamp).IsConcurrencyToken();

            // One invoice per fee and one adjustment note per invoice — a second issue (race, retry) hits these.
            e.HasIndex(d => d.SubscriptionId).IsUnique().HasFilter("[Kind] = 'ListingFeeInvoice'");
            e.HasIndex(d => d.PurchaseId).IsUnique().HasFilter("[Kind] = 'BuyerFeeInvoice'");
            e.HasIndex(d => d.OriginalDocumentId).IsUnique().HasFilter("[Kind] = 'AdjustmentNote'");
            e.HasIndex(d => new { d.Kind, d.IssuedAt });
            e.HasIndex(d => d.StudFarmId);
            e.HasIndex(d => d.BuyerUserId);

            e.HasOne<StallionSeasonSubscription>().WithMany().HasForeignKey(d => d.SubscriptionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Purchase>().WithMany().HasForeignKey(d => d.PurchaseId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(d => d.OriginalDocument).WithMany().HasForeignKey(d => d.OriginalDocumentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<StudFarm>().WithMany().HasForeignKey(d => d.StudFarmId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(d => d.BuyerUserId).OnDelete(DeleteBehavior.NoAction);
        });
```

- [ ] **Step 6: Remove NominationBinding code**

```bash
git rm src/Server/Data/Entities/NominationBinding.cs src/Server/Data/Repositories/INominationBindingRepository.cs \
  src/Server/Data/Repositories/NominationBindingRepository.cs src/Server/Services/INominationBindingService.cs \
  src/Server/Services/NominationBindingService.cs src/Server/Controllers/BindingsController.cs \
  src/Shared/DTOs/Bindings/NominationBindingDto.cs src/Shared/Enums/BindingStatus.cs \
  tests/Server.Tests/Services/NominationBindingServiceTests.cs
```

`src/Server/Program.cs` — delete line 114 (`builder.Services.AddScoped<INominationBindingRepository, NominationBindingRepository>();`)
and line 137 (`builder.Services.AddScoped<INominationBindingService, NominationBindingService>();`).
Then `grep -rn "NominationBinding\|BindingStatus" src tests --include=*.cs --include=*.razor` → only `Data/Migrations` matches.

- [ ] **Step 7: Run green** — the five tests pass.

- [ ] **Step 8: Migration** — `dotnet ef migrations add V2Phase5aTaxDocuments --project src/Server --startup-project src/Server --output-dir Data/Migrations`.
  In the generated `Up`, directly **after** the `AddColumn<decimal>(name: "StandardFeeIncGst", table: "StallionSeasonSubscriptions", …)` call add:

```csharp
            // Existing subscriptions: the fee charged was the standard fee (Waived rows hold 0).
            migrationBuilder.Sql("UPDATE [StallionSeasonSubscriptions] SET [StandardFeeIncGst] = [FeeIncGst];");
```

  Read the whole migration: it must only `CreateSequence` `TaxDocumentNumbers` (start 1), create `IntroOffers` and
  `TaxDocuments` (with the indexes and FKs above), add `StallionSeasonSubscriptions.StandardFeeIncGst` /
  `IntroOfferId` (+ index + FK), add the eight `PlatformSettings` columns, `UpdateData` the settings row
  (`InvoicePaymentTermsDays = 14`), and `DropTable("NominationBindings")`. `Down` must reverse each.
  Check `dotnet ef migrations has-pending-model-changes --project src/Server --startup-project src/Server` → no changes.
  Apply locally: `dotnet ef database update --project src/Server --startup-project src/Server --connection "Server=(localdb)\mssqllocaldb;Database=StallionsNomsDev;Trusted_Connection=True;MultipleActiveResultSets=true"`.
  Then in SSMS / `sqlcmd` against localdb: `SELECT NEXT VALUE FOR dbo.TaxDocumentNumbers` returns 1 — then
  `ALTER SEQUENCE dbo.TaxDocumentNumbers RESTART WITH 1` so the first real document is SA-000001.

- [ ] **Step 9: Full build (0 warnings) and full test run → green.**

- [ ] **Step 10: Commit**

```bash
git add src/Shared/Enums/IntroOfferType.cs src/Shared/Enums/TaxDocumentKind.cs src/Shared/Enums/TaxDocumentStatus.cs \
  src/Server/Data/Entities/IntroOffer.cs src/Server/Data/Entities/TaxDocument.cs src/Server/Data/Entities/StallionSeasonSubscription.cs \
  src/Server/Data/Entities/PlatformSettings.cs src/Server/Data/Entities/Purchase.cs src/Server/Data/AppDbContext.cs \
  src/Server/Program.cs src/Server/Data/Migrations tests/Server.Tests/Data/TaxDocumentModelTests.cs
git status --short
git commit -m "feat: phase 5a domain — intro offers, tax documents, business details; remove nomination bindings

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 2: Intro offers — resolver, Staff CRUD, preview

**Files:**
- Create: `src/Shared/DTOs/IntroOffers/IntroOfferDto.cs`, `SaveIntroOfferRequest.cs`, `IntroOfferPreviewDto.cs`
- Create: `src/Server/Services/IntroOfferResolver.cs`
- Create: `src/Server/Data/Repositories/IIntroOfferRepository.cs`, `src/Server/Data/Repositories/IntroOfferRepository.cs`
- Create: `src/Server/Services/IIntroOfferService.cs`, `src/Server/Services/IntroOfferService.cs`, `src/Server/Controllers/IntroOffersController.cs`
- Modify: `src/Server/Program.cs` (two registrations)
- Test: `tests/Server.Tests/Services/IntroOfferResolverTests.cs`, `tests/Server.Tests/Services/IntroOfferServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 `IntroOffer`, `IntroOfferType`, `AppDbContext.IntroOffers`, `StallionSeasonSubscription.IntroOfferId`; existing `SydneyTime.EndOfDayUtc(DateOnly)`, `SydneyTime.Today(DateTime)`.
- Produces:
  `sealed record IntroOfferMatch(IntroOffer? Offer, decimal StandardFeeIncGst, decimal FeeIncGst)`;
  `static class IntroOfferResolver { bool Matches(IntroOffer, Guid studFarmId, Guid seasonId, DateTime now); decimal FeeUnder(IntroOffer, decimal standardFeeIncGst); IntroOfferMatch Resolve(Guid studFarmId, Guid seasonId, decimal standardFeeIncGst, DateTime now, IEnumerable<IntroOffer> candidates) }`;
  `IIntroOfferRepository { Task<IntroOffer?> GetByIdAsync(Guid); Task<IReadOnlyList<IntroOffer>> GetAllAsync(); Task<IReadOnlyList<IntroOffer>> GetCandidatesAsync(Guid studFarmId, Guid seasonId, DateTime now); Task<IReadOnlyDictionary<Guid,int>> GetTimesAppliedAsync(); Task<int> CountTimesAppliedAsync(Guid id); Task AddAsync(IntroOffer); Task UpdateAsync(IntroOffer) }`;
  `IIntroOfferService { GetAllAsync(); CreateAsync(SaveIntroOfferRequest); UpdateAsync(Guid, SaveIntroOfferRequest); RetireAsync(Guid); PreviewAsync(Guid studFarmId, Guid seasonId) }` (all `ServiceResult<…>`);
  DTOs `IntroOfferDto`, `SaveIntroOfferRequest { Name, Type, AmountOffIncGst?, StudFarmId?, SeasonId?, DateOnly EndsOn }`, `IntroOfferPreviewDto { StandardFeeIncGst, FeeIncGst, IntroOfferId?, IntroOfferName? }` in `Stallions.Shared.DTOs.IntroOffers`;
  routes `GET/POST api/intro-offers`, `PUT api/intro-offers/{id}`, `POST api/intro-offers/{id}/retire`, `GET api/intro-offers/preview?studFarmId&seasonId` (all StaffOnly).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Server.Tests/Services/IntroOfferResolverTests.cs
using FluentAssertions;
using Stallions.Server.Data.Entities;
using Stallions.Server.Services;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class IntroOfferResolverTests
{
    private static readonly DateTime Now = new(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Farm = Guid.NewGuid();
    private static readonly Guid Season = Guid.NewGuid();

    private static IntroOffer Offer(IntroOfferType type = IntroOfferType.AmountOff, decimal off = 495m, Guid? farm = null,
        Guid? season = null, DateTime? endsAt = null, bool retired = false, DateTime? createdAt = null, string name = "Offer") => new()
    {
        Name = name, Type = type, AmountOffIncGst = type == IntroOfferType.Waiver ? null : off,
        StudFarmId = farm, SeasonId = season, EndsAt = endsAt ?? Now.AddDays(30), IsRetired = retired,
        CreatedAt = createdAt ?? Now.AddDays(-1)
    };

    private static IntroOfferMatch Resolve(params IntroOffer[] offers) =>
        IntroOfferResolver.Resolve(Farm, Season, 990m, Now, offers);

    [Fact]
    public void NoOffers_LeavesTheStandardFee()
    {
        var match = Resolve();

        match.Offer.Should().BeNull();
        match.FeeIncGst.Should().Be(990m);
        match.StandardFeeIncGst.Should().Be(990m);
    }

    [Fact]
    public void AWaiver_MakesTheFeeZero() =>
        Resolve(Offer(IntroOfferType.Waiver)).FeeIncGst.Should().Be(0m);

    [Fact]
    public void AmountOff_TakesThoseDollarsOff() =>
        Resolve(Offer(off: 495m)).FeeIncGst.Should().Be(495m);

    [Theory]
    [InlineData(990)]
    [InlineData(1500)]
    public void AmountOffAtOrAboveTheStandardFee_MakesItZero(int off) =>
        Resolve(Offer(off: off)).FeeIncGst.Should().Be(0m);

    [Fact]
    public void AnOfferForThisStud_Applies_ButNotOneForAnotherStud()
    {
        Resolve(Offer(farm: Farm)).Offer.Should().NotBeNull();
        Resolve(Offer(farm: Guid.NewGuid())).Offer.Should().BeNull();
    }

    [Fact]
    public void AnOfferForThisSeason_Applies_ButNotOneForAnotherSeason()
    {
        Resolve(Offer(season: Season)).Offer.Should().NotBeNull();
        Resolve(Offer(season: Guid.NewGuid())).Offer.Should().BeNull();
    }

    [Fact]
    public void EndsAt_IsExclusive()
    {
        Resolve(Offer(endsAt: Now)).Offer.Should().BeNull();
        Resolve(Offer(endsAt: Now.AddTicks(1))).Offer.Should().NotBeNull();
    }

    [Fact]
    public void ARetiredOffer_NeverApplies() =>
        Resolve(Offer(IntroOfferType.Waiver, retired: true)).Offer.Should().BeNull();

    [Fact]
    public void TheLowestFeeWins()
    {
        var small = Offer(off: 100m, name: "Small");
        var big = Offer(off: 600m, name: "Big");

        var match = Resolve(small, big);

        match.Offer!.Name.Should().Be("Big");
        match.FeeIncGst.Should().Be(390m);
    }

    [Fact]
    public void ATie_GoesToTheOfferEndingSoonest()
    {
        var later = Offer(IntroOfferType.Waiver, endsAt: Now.AddDays(60), name: "Later");
        var sooner = Offer(off: 2000m, endsAt: Now.AddDays(10), name: "Sooner");

        Resolve(later, sooner).Offer!.Name.Should().Be("Sooner");
    }

    [Fact]
    public void ATieOnTheEndDate_GoesToTheEarliestCreated()
    {
        var newer = Offer(IntroOfferType.Waiver, createdAt: Now.AddDays(-1), name: "Newer");
        var older = Offer(IntroOfferType.Waiver, createdAt: Now.AddDays(-5), name: "Older");

        Resolve(newer, older).Offer!.Name.Should().Be("Older");
    }
}
```

```csharp
// tests/Server.Tests/Services/IntroOfferServiceTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.DTOs.IntroOffers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

/// <summary>Real repositories on the in-memory provider (seeded settings: standard listing fee $990).</summary>
public class IntroOfferServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly TestClock _clock = new() { Now = new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero) };
    private readonly Mock<IUserService> _users = new();
    private readonly User _staff = new() { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active, ObjectId = "staff", Email = "staff@x", DisplayName = "Staff" };
    private readonly StudFarm _farm = new() { UserId = Guid.NewGuid(), Name = "Arrowfield" };
    private readonly Season _season = new() { Name = "2026 Season", IsOpen = true };

    public IntroOfferServiceTests()
    {
        _db = DbContextFactory.Create(Guid.NewGuid().ToString());
        _db.Database.EnsureCreated();
        _db.Users.Add(_staff);
        _db.StudFarms.Add(_farm);
        _db.Seasons.Add(_season);
        _db.SaveChanges();
        SignIn(_staff);
    }

    public void Dispose() => _db.Dispose();

    private void SignIn(User user) => _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(user);

    private IntroOfferService CreateSut() => new(new IntroOfferRepository(_db), new StudFarmRepository(_db),
        new SeasonRepository(_db), new PlatformSettingsRepository(_db), new AuditLogRepository(_db), _users.Object, _clock);

    private static SaveIntroOfferRequest Request(string type = "AmountOff", decimal? off = 495m, Guid? farm = null,
        Guid? season = null, DateOnly? endsOn = null, string name = "Launch 2026") => new()
    {
        Name = name, Type = type, AmountOffIncGst = off, StudFarmId = farm, SeasonId = season,
        EndsOn = endsOn ?? new DateOnly(2026, 12, 31)
    };

    private async Task<IntroOfferDto> CreateAsync(SaveIntroOfferRequest request)
    {
        var r = await CreateSut().CreateAsync(request);
        r.Succeeded.Should().BeTrue(r.Error);
        return r.Value!;
    }

    private void ApplyTo(Guid offerId)
    {
        _db.StallionSeasonSubscriptions.Add(new StallionSeasonSubscription
        {
            StallionId = Guid.NewGuid(), SeasonId = _season.Id, StudFarmId = _farm.Id, IntroOfferId = offerId,
            FeeIncGst = 495m, FeeExGst = 450m, GstAmount = 45m, StandardFeeIncGst = 990m, CreatedByUserId = _staff.Id
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Create_SavesTheOffer_EndingAtTheEndOfThatSydneyDay_AndAuditsIt()
    {
        var dto = await CreateAsync(Request(farm: _farm.Id, season: _season.Id));

        // 31 Dec 2026 is daylight time (UTC+11): 23:59:59 Sydney = 12:59:59 UTC.
        dto.EndsAt.Should().Be(new DateTime(2026, 12, 31, 12, 59, 59, DateTimeKind.Utc));
        dto.EndsOn.Should().Be(new DateOnly(2026, 12, 31));
        dto.State.Should().Be("Active");
        dto.StudFarmName.Should().Be("Arrowfield");
        dto.SeasonName.Should().Be("2026 Season");
        dto.Type.Should().Be("AmountOff");
        dto.AmountOffIncGst.Should().Be(495m);
        var audit = await _db.AuditLogs.SingleAsync(a => a.EntityId == dto.Id);
        audit.EntityType.Should().Be("IntroOffer");
        audit.Action.Should().Be("CreateIntroOffer");
        audit.UserId.Should().Be(_staff.Id);
        audit.Details.Should().Contain("Launch 2026");
    }

    [Fact]
    public async Task Create_AWaiver_StoresNoAmount()
    {
        var dto = await CreateAsync(Request(type: "waiver", off: 300m));

        dto.Type.Should().Be("Waiver");
        dto.AmountOffIncGst.Should().BeNull();
    }

    [Theory]
    [InlineData("", "AmountOff", 495.0, "A name is required.")]
    [InlineData("Launch", "Discount", 495.0, "Type must be Waiver or AmountOff.")]
    [InlineData("Launch", "AmountOff", 0.0, "Enter the dollars off the listing fee (more than $0).")]
    [InlineData("Launch", "AmountOff", 10.005, "The amount is in dollars and cents.")]
    public async Task Create_RejectsBadInput(string name, string type, double off, string error)
    {
        var r = await CreateSut().CreateAsync(Request(name: name, type: type, off: (decimal)off));

        r.HttpStatusCode.Should().Be(400);
        r.Error.Should().Be(error);
    }

    [Fact]
    public async Task Create_EndingYesterday_IsRejected_ButEndingTodayIsAllowed()
    {
        // The clock is 11 Oct 2026 11:00 in Sydney.
        (await CreateSut().CreateAsync(Request(endsOn: new DateOnly(2026, 10, 10)))).Error
            .Should().Be("The end date must be in the future.");
        (await CreateSut().CreateAsync(Request(endsOn: new DateOnly(2026, 10, 11)))).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Create_ForAnUnknownStudOrSeason_IsRejected()
    {
        (await CreateSut().CreateAsync(Request(farm: Guid.NewGuid()))).Error.Should().Be("Stud farm not found.");
        (await CreateSut().CreateAsync(Request(season: Guid.NewGuid()))).Error.Should().Be("Season not found.");
    }

    [Fact]
    public async Task EveryAction_AsNonStaff_IsForbidden()
    {
        var offer = await CreateAsync(Request());
        SignIn(new User { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active });

        (await CreateSut().CreateAsync(Request())).HttpStatusCode.Should().Be(403);
        (await CreateSut().UpdateAsync(offer.Id, Request())).HttpStatusCode.Should().Be(403);
        (await CreateSut().RetireAsync(offer.Id)).HttpStatusCode.Should().Be(403);
        (await CreateSut().GetAllAsync()).HttpStatusCode.Should().Be(403);
        (await CreateSut().PreviewAsync(_farm.Id, _season.Id)).HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Update_BeforeItIsApplied_CanChangeTheTerms()
    {
        var offer = await CreateAsync(Request());

        var r = await CreateSut().UpdateAsync(offer.Id, Request(type: "Waiver", off: null, farm: _farm.Id));

        r.Succeeded.Should().BeTrue(r.Error);
        r.Value!.Type.Should().Be("Waiver");
        r.Value.StudFarmId.Should().Be(_farm.Id);
        (await _db.AuditLogs.CountAsync(a => a.EntityId == offer.Id && a.Action == "UpdateIntroOffer")).Should().Be(1);
    }

    [Fact]
    public async Task Update_AfterItIsApplied_OnlyNameAndEndDateMayChange()
    {
        var offer = await CreateAsync(Request());
        ApplyTo(offer.Id);

        var changeAmount = await CreateSut().UpdateAsync(offer.Id, Request(off: 300m));
        changeAmount.HttpStatusCode.Should().Be(409);

        var changeScope = await CreateSut().UpdateAsync(offer.Id, Request(season: _season.Id));
        changeScope.HttpStatusCode.Should().Be(409);

        var rename = await CreateSut().UpdateAsync(offer.Id, Request(name: "Launch 2026 (extended)", endsOn: new DateOnly(2027, 1, 31)));
        rename.Succeeded.Should().BeTrue(rename.Error);
        rename.Value!.Name.Should().Be("Launch 2026 (extended)");
        rename.Value.EndsOn.Should().Be(new DateOnly(2027, 1, 31));
        rename.Value.AmountOffIncGst.Should().Be(495m);
    }

    [Fact]
    public async Task Retire_StopsTheOfferApplying_AndIsAudited_AndLocksIt()
    {
        var offer = await CreateAsync(Request());
        (await CreateSut().PreviewAsync(_farm.Id, _season.Id)).Value!.FeeIncGst.Should().Be(495m);

        var r = await CreateSut().RetireAsync(offer.Id);

        r.Value!.State.Should().Be("Retired");
        (await CreateSut().PreviewAsync(_farm.Id, _season.Id)).Value!.FeeIncGst.Should().Be(990m);
        (await _db.AuditLogs.CountAsync(a => a.EntityId == offer.Id && a.Action == "RetireIntroOffer")).Should().Be(1);
        (await CreateSut().UpdateAsync(offer.Id, Request(name: "Back again"))).Error.Should().Be("A retired offer can't be changed.");
    }

    [Fact]
    public async Task GetAll_ShowsStateAndTimesApplied()
    {
        var active = await CreateAsync(Request(name: "Active one"));
        var ending = await CreateAsync(Request(name: "Ending one", endsOn: new DateOnly(2026, 10, 12)));
        ApplyTo(active.Id);
        ApplyTo(active.Id);
        _clock.Advance(TimeSpan.FromDays(3));

        var all = (await CreateSut().GetAllAsync()).Value!;

        all.Single(o => o.Id == active.Id).TimesApplied.Should().Be(2);
        all.Single(o => o.Id == active.Id).State.Should().Be("Active");
        all.Single(o => o.Id == ending.Id).State.Should().Be("Ended");
        all.Single(o => o.Id == ending.Id).TimesApplied.Should().Be(0);
    }

    [Fact]
    public async Task Preview_PicksTheBestOffer()
    {
        await CreateAsync(Request(name: "Everyone $200 off", off: 200m));
        await CreateAsync(Request(name: "Arrowfield free", type: "Waiver", off: null, farm: _farm.Id));

        var preview = (await CreateSut().PreviewAsync(_farm.Id, _season.Id)).Value!;

        preview.StandardFeeIncGst.Should().Be(990m);
        preview.FeeIncGst.Should().Be(0m);
        preview.IntroOfferName.Should().Be("Arrowfield free");
    }

    [Fact]
    public async Task Preview_ForAnUnknownStudOrSeason_IsNotFound()
    {
        (await CreateSut().PreviewAsync(Guid.NewGuid(), _season.Id)).HttpStatusCode.Should().Be(404);
        (await CreateSut().PreviewAsync(_farm.Id, Guid.NewGuid())).HttpStatusCode.Should().Be(404);
    }
}
```

- [ ] **Step 2: Run red** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~IntroOffer"` → compile errors.

- [ ] **Step 3: DTOs**

```csharp
// src/Shared/DTOs/IntroOffers/IntroOfferDto.cs
namespace Stallions.Shared.DTOs.IntroOffers;

public class IntroOfferDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;          // Waiver | AmountOff
    public decimal? AmountOffIncGst { get; set; }
    public Guid? StudFarmId { get; set; }                     // null = all studs
    public string? StudFarmName { get; set; }
    public Guid? SeasonId { get; set; }                       // null = any season
    public string? SeasonName { get; set; }
    public DateTime EndsAt { get; set; }                      // UTC
    public DateOnly EndsOn { get; set; }                      // the Sydney date it ends (inclusive)
    public bool IsRetired { get; set; }
    public string State { get; set; } = string.Empty;         // Active | Ended | Retired
    public int TimesApplied { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

```csharp
// src/Shared/DTOs/IntroOffers/SaveIntroOfferRequest.cs
namespace Stallions.Shared.DTOs.IntroOffers;

// Staff-only. Create or edit. Once an offer has been applied, only Name and EndsOn may change.
public class SaveIntroOfferRequest
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;          // Waiver | AmountOff
    public decimal? AmountOffIncGst { get; set; }             // required (> 0) for AmountOff; ignored for Waiver
    public Guid? StudFarmId { get; set; }
    public Guid? SeasonId { get; set; }
    /// <summary>The last Sydney day the offer applies (to subscriptions created by 23:59:59 that day).</summary>
    public DateOnly EndsOn { get; set; }
}
```

```csharp
// src/Shared/DTOs/IntroOffers/IntroOfferPreviewDto.cs
namespace Stallions.Shared.DTOs.IntroOffers;

/// <summary>What a new subscription for this stud and season would cost now, with the best intro offer.</summary>
public class IntroOfferPreviewDto
{
    public decimal StandardFeeIncGst { get; set; }
    public decimal FeeIncGst { get; set; }
    public Guid? IntroOfferId { get; set; }
    public string? IntroOfferName { get; set; }
}
```

- [ ] **Step 4: Resolver**

```csharp
// src/Server/Services/IntroOfferResolver.cs
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

/// <summary>The offer applied (or null) and the resulting listing fee.</summary>
public sealed record IntroOfferMatch(IntroOffer? Offer, decimal StandardFeeIncGst, decimal FeeIncGst);

/// <summary>
/// Picks the intro offer for a new subscription: the matching offer giving the lowest fee; a tie goes to
/// the offer ending soonest, then the earliest created (then Id, so the result is always the same).
/// </summary>
public static class IntroOfferResolver
{
    public static bool Matches(IntroOffer offer, Guid studFarmId, Guid seasonId, DateTime now) =>
        !offer.IsRetired
        && now < offer.EndsAt
        && (offer.StudFarmId == null || offer.StudFarmId == studFarmId)
        && (offer.SeasonId == null || offer.SeasonId == seasonId);

    public static decimal FeeUnder(IntroOffer offer, decimal standardFeeIncGst) => offer.Type == IntroOfferType.Waiver
        ? 0m
        : Math.Max(0m, standardFeeIncGst - (offer.AmountOffIncGst ?? 0m));

    public static IntroOfferMatch Resolve(Guid studFarmId, Guid seasonId, decimal standardFeeIncGst, DateTime now,
        IEnumerable<IntroOffer> candidates)
    {
        var best = candidates
            .Where(o => Matches(o, studFarmId, seasonId, now))
            .Select(o => (Offer: o, Fee: FeeUnder(o, standardFeeIncGst)))
            .OrderBy(x => x.Fee)
            .ThenBy(x => x.Offer.EndsAt)
            .ThenBy(x => x.Offer.CreatedAt)
            .ThenBy(x => x.Offer.Id)
            .FirstOrDefault();

        return best.Offer == null
            ? new IntroOfferMatch(null, standardFeeIncGst, standardFeeIncGst)
            : new IntroOfferMatch(best.Offer, standardFeeIncGst, best.Fee);
    }
}
```

- [ ] **Step 5: Repository**

```csharp
// src/Server/Data/Repositories/IIntroOfferRepository.cs
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IIntroOfferRepository
{
    /// <summary>With StudFarm and Season.</summary>
    Task<IntroOffer?> GetByIdAsync(Guid id);
    /// <summary>All offers including retired, with StudFarm and Season; live ones first.</summary>
    Task<IReadOnlyList<IntroOffer>> GetAllAsync();
    /// <summary>Offers that could apply now to this stud and season: not retired, not ended, in scope.</summary>
    Task<IReadOnlyList<IntroOffer>> GetCandidatesAsync(Guid studFarmId, Guid seasonId, DateTime now);
    /// <summary>Offer id → number of subscriptions it was applied to.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetTimesAppliedAsync();
    Task<int> CountTimesAppliedAsync(Guid id);
    Task AddAsync(IntroOffer offer);
    Task UpdateAsync(IntroOffer offer);
}
```

```csharp
// src/Server/Data/Repositories/IntroOfferRepository.cs
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class IntroOfferRepository : IIntroOfferRepository
{
    private readonly AppDbContext _db;
    public IntroOfferRepository(AppDbContext db) => _db = db;

    private IQueryable<IntroOffer> WithDetails() =>
        _db.IntroOffers.Include(o => o.StudFarm).Include(o => o.Season);

    public async Task<IntroOffer?> GetByIdAsync(Guid id) =>
        await WithDetails().FirstOrDefaultAsync(o => o.Id == id);

    public async Task<IReadOnlyList<IntroOffer>> GetAllAsync() =>
        await WithDetails().OrderBy(o => o.IsRetired).ThenByDescending(o => o.EndsAt).ToListAsync();

    public async Task<IReadOnlyList<IntroOffer>> GetCandidatesAsync(Guid studFarmId, Guid seasonId, DateTime now) =>
        await _db.IntroOffers
            .Where(o => !o.IsRetired && o.EndsAt > now
                && (o.StudFarmId == null || o.StudFarmId == studFarmId)
                && (o.SeasonId == null || o.SeasonId == seasonId))
            .ToListAsync();

    public async Task<IReadOnlyDictionary<Guid, int>> GetTimesAppliedAsync() =>
        await _db.StallionSeasonSubscriptions
            .Where(s => s.IntroOfferId != null)
            .GroupBy(s => s.IntroOfferId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count);

    public Task<int> CountTimesAppliedAsync(Guid id) =>
        _db.StallionSeasonSubscriptions.CountAsync(s => s.IntroOfferId == id);

    public async Task AddAsync(IntroOffer offer)
    {
        _db.IntroOffers.Add(offer);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(IntroOffer offer)
    {
        _db.IntroOffers.Update(offer);
        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 6: Service and controller**

```csharp
// src/Server/Services/IIntroOfferService.cs
using Stallions.Shared.DTOs.IntroOffers;

namespace Stallions.Server.Services;

/// <summary>Staff-only management of intro offers (fee settings). Every change is audited.</summary>
public interface IIntroOfferService
{
    Task<ServiceResult<IReadOnlyList<IntroOfferDto>>> GetAllAsync();
    Task<ServiceResult<IntroOfferDto>> CreateAsync(SaveIntroOfferRequest request);
    Task<ServiceResult<IntroOfferDto>> UpdateAsync(Guid id, SaveIntroOfferRequest request);
    Task<ServiceResult<IntroOfferDto>> RetireAsync(Guid id);
    /// <summary>The fee a new subscription for this stud and season would get right now.</summary>
    Task<ServiceResult<IntroOfferPreviewDto>> PreviewAsync(Guid studFarmId, Guid seasonId);
}
```

```csharp
// src/Server/Services/IntroOfferService.cs
using System.Text.Json;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Infrastructure;
using Stallions.Shared.DTOs.IntroOffers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public class IntroOfferService : IIntroOfferService
{
    private const string AuditEntity = "IntroOffer";
    private const string StaffOnlyMessage = "Only Stallions Australia staff can manage intro offers.";
    private const decimal MaxAmountOffIncGst = 1_000_000m;

    private readonly IIntroOfferRepository _offers;
    private readonly IStudFarmRepository _farms;
    private readonly ISeasonRepository _seasons;
    private readonly IPlatformSettingsRepository _settings;
    private readonly IAuditLogRepository _audit;
    private readonly IUserService _users;
    private readonly TimeProvider _clock;

    public IntroOfferService(IIntroOfferRepository offers, IStudFarmRepository farms, ISeasonRepository seasons,
        IPlatformSettingsRepository settings, IAuditLogRepository audit, IUserService users, TimeProvider clock)
    {
        _offers = offers; _farms = farms; _seasons = seasons; _settings = settings; _audit = audit; _users = users; _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    /// <summary>The validated, normalised values of a create / edit request.</summary>
    private sealed record Validated(string Name, IntroOfferType Type, decimal? AmountOffIncGst, Guid? StudFarmId,
        Guid? SeasonId, DateTime EndsAt);

    public async Task<ServiceResult<IReadOnlyList<IntroOfferDto>>> GetAllAsync()
    {
        if (await GetStaffCallerAsync() == null)
            return ServiceResult<IReadOnlyList<IntroOfferDto>>.Forbidden(StaffOnlyMessage);

        var offers = await _offers.GetAllAsync();
        var applied = await _offers.GetTimesAppliedAsync();
        var now = Now;
        return ServiceResult<IReadOnlyList<IntroOfferDto>>.Ok(
            offers.Select(o => ToDto(o, applied.TryGetValue(o.Id, out var n) ? n : 0, now)).ToList());
    }

    public async Task<ServiceResult<IntroOfferDto>> CreateAsync(SaveIntroOfferRequest request)
    {
        var staff = await GetStaffCallerAsync();
        if (staff == null) return ServiceResult<IntroOfferDto>.Forbidden(StaffOnlyMessage);

        var (values, error) = await ValidateAsync(request);
        if (values == null) return ServiceResult<IntroOfferDto>.BadRequest(error!);

        var now = Now;
        if (values.EndsAt <= now) return ServiceResult<IntroOfferDto>.BadRequest("The end date must be in the future.");

        var offer = new IntroOffer
        {
            Name = values.Name, Type = values.Type, AmountOffIncGst = values.AmountOffIncGst,
            StudFarmId = values.StudFarmId, SeasonId = values.SeasonId, EndsAt = values.EndsAt,
            CreatedAt = now, CreatedByUserId = staff.Id
        };
        await _offers.AddAsync(offer);
        await _audit.LogAsync(AuditEntity, offer.Id, "CreateIntroOffer", staff.Id,
            JsonSerializer.Serialize(new { Old = (object?)null, New = Snapshot(offer) }));

        return ServiceResult<IntroOfferDto>.Created(ToDto(await _offers.GetByIdAsync(offer.Id) ?? offer, 0, now));
    }

    public async Task<ServiceResult<IntroOfferDto>> UpdateAsync(Guid id, SaveIntroOfferRequest request)
    {
        var staff = await GetStaffCallerAsync();
        if (staff == null) return ServiceResult<IntroOfferDto>.Forbidden(StaffOnlyMessage);

        var offer = await _offers.GetByIdAsync(id);
        if (offer == null) return ServiceResult<IntroOfferDto>.NotFound("Intro offer not found.");
        if (offer.IsRetired) return ServiceResult<IntroOfferDto>.BadRequest("A retired offer can't be changed.");

        var (values, error) = await ValidateAsync(request);
        if (values == null) return ServiceResult<IntroOfferDto>.BadRequest(error!);

        // Recorded subscriptions name this offer: its terms must stay what they were when applied.
        var timesApplied = await _offers.CountTimesAppliedAsync(id);
        var termsChanged = values.Type != offer.Type || values.AmountOffIncGst != offer.AmountOffIncGst
                           || values.StudFarmId != offer.StudFarmId || values.SeasonId != offer.SeasonId;
        if (timesApplied > 0 && termsChanged)
            return ServiceResult<IntroOfferDto>.Conflict(
                $"This offer has been applied to {timesApplied} subscription(s), so only its name and end date can change.");

        var old = Snapshot(offer);
        offer.Name = values.Name;
        offer.Type = values.Type;
        offer.AmountOffIncGst = values.AmountOffIncGst;
        offer.StudFarmId = values.StudFarmId;
        offer.SeasonId = values.SeasonId;
        offer.EndsAt = values.EndsAt;
        offer.UpdatedAt = Now;
        offer.UpdatedByUserId = staff.Id;
        await _offers.UpdateAsync(offer);
        await _audit.LogAsync(AuditEntity, offer.Id, "UpdateIntroOffer", staff.Id,
            JsonSerializer.Serialize(new { Old = old, New = Snapshot(offer) }));

        return ServiceResult<IntroOfferDto>.Ok(ToDto(await _offers.GetByIdAsync(id) ?? offer, timesApplied, Now));
    }

    public async Task<ServiceResult<IntroOfferDto>> RetireAsync(Guid id)
    {
        var staff = await GetStaffCallerAsync();
        if (staff == null) return ServiceResult<IntroOfferDto>.Forbidden(StaffOnlyMessage);

        var offer = await _offers.GetByIdAsync(id);
        if (offer == null) return ServiceResult<IntroOfferDto>.NotFound("Intro offer not found.");
        var timesApplied = await _offers.CountTimesAppliedAsync(id);
        if (offer.IsRetired) return ServiceResult<IntroOfferDto>.Ok(ToDto(offer, timesApplied, Now));

        var old = Snapshot(offer);
        offer.IsRetired = true;
        offer.UpdatedAt = Now;
        offer.UpdatedByUserId = staff.Id;
        await _offers.UpdateAsync(offer);
        await _audit.LogAsync(AuditEntity, offer.Id, "RetireIntroOffer", staff.Id,
            JsonSerializer.Serialize(new { Old = old, New = Snapshot(offer) }));

        return ServiceResult<IntroOfferDto>.Ok(ToDto(offer, timesApplied, Now));
    }

    public async Task<ServiceResult<IntroOfferPreviewDto>> PreviewAsync(Guid studFarmId, Guid seasonId)
    {
        if (await GetStaffCallerAsync() == null) return ServiceResult<IntroOfferPreviewDto>.Forbidden(StaffOnlyMessage);
        if (await _farms.GetByIdAsync(studFarmId) == null) return ServiceResult<IntroOfferPreviewDto>.NotFound("Stud farm not found.");
        if (await _seasons.GetByIdAsync(seasonId) == null) return ServiceResult<IntroOfferPreviewDto>.NotFound("Season not found.");

        var now = Now;
        var standard = (await _settings.GetAsync()).StandardListingFeeIncGst;
        var match = IntroOfferResolver.Resolve(studFarmId, seasonId, standard, now,
            await _offers.GetCandidatesAsync(studFarmId, seasonId, now));
        return ServiceResult<IntroOfferPreviewDto>.Ok(new IntroOfferPreviewDto
        {
            StandardFeeIncGst = standard,
            FeeIncGst = match.FeeIncGst,
            IntroOfferId = match.Offer?.Id,
            IntroOfferName = match.Offer?.Name
        });
    }

    private async Task<(Validated? Values, string? Error)> ValidateAsync(SaveIntroOfferRequest r)
    {
        var name = r.Name?.Trim() ?? string.Empty;
        if (name.Length == 0) return (null, "A name is required.");
        if (name.Length > 100) return (null, "The name can be at most 100 characters.");

        var typeName = Enum.GetNames<IntroOfferType>()
            .FirstOrDefault(n => string.Equals(n, r.Type?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (typeName == null) return (null, "Type must be Waiver or AmountOff.");
        var type = Enum.Parse<IntroOfferType>(typeName);

        decimal? amount = null;
        if (type == IntroOfferType.AmountOff)
        {
            if (r.AmountOffIncGst is not { } a || a <= 0) return (null, "Enter the dollars off the listing fee (more than $0).");
            if (decimal.Round(a, 2) != a) return (null, "The amount is in dollars and cents.");
            if (a > MaxAmountOffIncGst) return (null, "The amount can't be more than $1,000,000.");
            amount = a;
        }

        if (r.StudFarmId is { } farmId && await _farms.GetByIdAsync(farmId) == null) return (null, "Stud farm not found.");
        if (r.SeasonId is { } seasonId && await _seasons.GetByIdAsync(seasonId) == null) return (null, "Season not found.");

        return (new Validated(name, type, amount, r.StudFarmId, r.SeasonId, SydneyTime.EndOfDayUtc(r.EndsOn)), null);
    }

    // Controllers enforce StaffOnly; checked again here because intro offers are fee settings.
    private async Task<User?> GetStaffCallerAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        return caller?.Role == UserRole.Staff ? caller : null;
    }

    private static object Snapshot(IntroOffer o) => new
    {
        o.Name, Type = o.Type.ToString(), o.AmountOffIncGst, o.StudFarmId, o.SeasonId, o.EndsAt, o.IsRetired
    };

    private static IntroOfferDto ToDto(IntroOffer o, int timesApplied, DateTime now) => new()
    {
        Id = o.Id,
        Name = o.Name,
        Type = o.Type.ToString(),
        AmountOffIncGst = o.AmountOffIncGst,
        StudFarmId = o.StudFarmId,
        StudFarmName = o.StudFarm?.Name,
        SeasonId = o.SeasonId,
        SeasonName = o.Season?.Name,
        EndsAt = o.EndsAt,
        EndsOn = SydneyTime.Today(o.EndsAt),
        IsRetired = o.IsRetired,
        State = o.IsRetired ? "Retired" : o.EndsAt <= now ? "Ended" : "Active",
        TimesApplied = timesApplied,
        CreatedAt = o.CreatedAt
    };
}
```

```csharp
// src/Server/Controllers/IntroOffersController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.IntroOffers;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/intro-offers")]
[Authorize(Policy = "StaffOnly")]
public class IntroOffersController : ControllerBase
{
    private readonly IIntroOfferService _offers;
    public IntroOffersController(IIntroOfferService offers) => _offers = offers;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var r = await _offers.GetAllAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("preview")]
    public async Task<IActionResult> Preview([FromQuery] Guid studFarmId, [FromQuery] Guid seasonId)
    {
        var r = await _offers.PreviewAsync(studFarmId, seasonId);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveIntroOfferRequest request)
    {
        var r = await _offers.CreateAsync(request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveIntroOfferRequest request)
    {
        var r = await _offers.UpdateAsync(id, request);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/retire")]
    public async Task<IActionResult> Retire(Guid id)
    {
        var r = await _offers.RetireAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
}
```

`src/Server/Program.cs` — after `builder.Services.AddScoped<IProcessedPaymentEventRepository, ProcessedPaymentEventRepository>();` add
`builder.Services.AddScoped<IIntroOfferRepository, IntroOfferRepository>();`; after
`builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();` add
`builder.Services.AddScoped<IIntroOfferService, IntroOfferService>();`.

- [ ] **Step 7: Run green** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~IntroOffer"`; then full build (0 warnings) and full test run.

- [ ] **Step 8: Commit**

```bash
git add src/Shared/DTOs/IntroOffers src/Server/Services/IntroOfferResolver.cs src/Server/Services/IIntroOfferService.cs \
  src/Server/Services/IntroOfferService.cs src/Server/Data/Repositories/IIntroOfferRepository.cs \
  src/Server/Data/Repositories/IntroOfferRepository.cs src/Server/Controllers/IntroOffersController.cs src/Server/Program.cs \
  tests/Server.Tests/Services/IntroOfferResolverTests.cs tests/Server.Tests/Services/IntroOfferServiceTests.cs
git status --short
git commit -m "feat: intro offers — resolver, Staff CRUD with lock-after-applied, fee preview

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Subscriptions apply intro offers; a $0 result is waived without a payment page

**Files:**
- Modify: `src/Server/Services/SubscriptionService.cs` (constructor lines 18–48; `CreateAsync` lines 50–110; `ActivateAsync` lines 185–271; `MapToDto` lines 330–349)
- Modify: `src/Server/Data/Repositories/SubscriptionRepository.cs` (`WithDetails`, lines 139–143)
- Modify: `src/Shared/DTOs/Subscriptions/SubscriptionDto.cs`, `src/Shared/DTOs/Subscriptions/CreateSubscriptionRequest.cs` (comment), `src/Shared/DTOs/Payments/PaymentRedirectDto.cs`
- Modify (fixture only): `tests/Server.Tests/Services/SubscriptionServiceTests.cs` (`CreateSut`, constructor)
- Test: `tests/Server.Tests/Services/SubscriptionServiceTests.cs` (add tests)

**Interfaces:**
- Consumes: Task 2 `IntroOfferResolver`, `IIntroOfferRepository.GetCandidatesAsync`, `IntroOfferRepository`.
- Produces: `SubscriptionService` constructor
  `(ISubscriptionRepository, IStallionRepository, ISeasonRepository, IStudFarmRepository, IPlatformSettingsRepository, IAuditLogRepository, IUserService, IPaymentProvider, IIntroOfferRepository, TimeProvider, ILogger<SubscriptionService>)`;
  `SubscriptionDto.StandardFeeIncGst (decimal)`, `.IntroOfferName (string?)`; `PaymentRedirectDto.ActivatedWithoutPayment (bool)`.

- [ ] **Step 1: Write the failing tests**

Fixture changes in `SubscriptionServiceTests`: add fields

```csharp
    private readonly TestClock _clock = new() { Now = DateTimeOffset.UtcNow };
```

and replace `CreateSut()` with

```csharp
    private SubscriptionService CreateSut() => new(
        new SubscriptionRepository(_db),
        new StallionRepository(_db),
        new SeasonRepository(_db),
        new StudFarmRepository(_db),
        new PlatformSettingsRepository(_db),
        new AuditLogRepository(_db),
        _users.Object,
        _provider.Object,
        new IntroOfferRepository(_db),
        _clock,
        NullLogger<SubscriptionService>.Instance);

    private IntroOffer AddOffer(string name, IntroOfferType type, decimal? off = null, Guid? farmId = null, int endsInDays = 30)
    {
        var offer = new IntroOffer
        {
            Name = name, Type = type, AmountOffIncGst = off, StudFarmId = farmId,
            EndsAt = _clock.UtcNow.AddDays(endsInDays), CreatedAt = _clock.UtcNow, CreatedByUserId = _staff.Id
        };
        _db.IntroOffers.Add(offer);
        _db.SaveChanges();
        return offer;
    }
```

Add the tests (new region `// ── Intro offers ──`):

```csharp
    // ── Intro offers ────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_WithNoOverride_AppliesTheBestIntroOffer()
    {
        AddOffer("Everyone $100 off", IntroOfferType.AmountOff, 100m);
        var launch = AddOffer("Launch 2026", IntroOfferType.AmountOff, 495m, _farmA.Id);

        var dto = await CreatePendingAsync(_stallionA);

        (dto.FeeIncGst, dto.FeeExGst, dto.GstAmount).Should().Be((495m, 450m, 45m));
        dto.StandardFeeIncGst.Should().Be(990m);
        dto.IntroOfferName.Should().Be("Launch 2026");
        dto.Status.Should().Be("Pending");
        (await _db.StallionSeasonSubscriptions.SingleAsync()).IntroOfferId.Should().Be(launch.Id);
    }

    [Fact]
    public async Task Create_WithAnOverride_IgnoresOffers()
    {
        AddOffer("Launch 2026", IntroOfferType.AmountOff, 495m);

        var dto = await CreatePendingAsync(_stallionA, feeOverride: 700m);

        dto.FeeIncGst.Should().Be(700m);
        dto.StandardFeeIncGst.Should().Be(990m);
        dto.IntroOfferName.Should().BeNull();
    }

    [Fact]
    public async Task Create_WhenTheOfferWaivesTheFee_IsWaivedAtOnce()
    {
        AddOffer("Founding studs", IntroOfferType.Waiver);

        var dto = await CreatePendingAsync(_stallionA);

        dto.Status.Should().Be("Waived");
        dto.PaymentMethod.Should().Be("Waived");
        dto.WaiverReason.Should().Be("Intro offer: Founding studs");
        dto.FeeIncGst.Should().Be(0m);
        dto.PaidAt.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public async Task Create_Audit_RecordsTheIntroOffer()
    {
        var offer = AddOffer("Launch 2026", IntroOfferType.AmountOff, 495m);

        var dto = await CreatePendingAsync(_stallionA);

        (await _db.AuditLogs.SingleAsync(a => a.EntityId == dto.Id)).Details.Should().Contain(offer.Id.ToString());
    }

    [Fact]
    public async Task Activate_AppliesTheOffer_ToTheNewSubscription()
    {
        AddOffer("Launch 2026", IntroOfferType.AmountOff, 495m);
        PaymentPageReturns("https://pay/fee");

        await ActivateAs(_studUserA, _stallionA);

        var sub = await _db.StallionSeasonSubscriptions.SingleAsync();
        (sub.FeeIncGst, sub.StandardFeeIncGst).Should().Be((495m, 990m));
        _provider.Verify(p => p.CreateListingFeeSessionAsync(sub.Id, 495m,
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Activate_WhenTheOfferWaivesTheFee_ActivatesWithoutAPaymentPage()
    {
        AddOffer("Founding studs", IntroOfferType.AmountOff, 2000m, _farmA.Id);

        var result = await ActivateAs(_studUserA, _stallionA);

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.ActivatedWithoutPayment.Should().BeTrue();
        result.Value.Url.Should().BeEmpty();
        var sub = await _db.StallionSeasonSubscriptions.SingleAsync();
        sub.Status.Should().Be(SubscriptionStatus.Waived);
        sub.WaiverReason.Should().Be("Intro offer: Founding studs");
        _provider.Verify(p => p.CreateListingFeeSessionAsync(It.IsAny<Guid>(), It.IsAny<decimal>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Activate_ReusesAStaffPendingSubscription_EvenIfABetterOfferExistsNow()
    {
        await CreatePendingAsync(_stallionA);                  // $990, no offer yet
        AddOffer("Founding studs", IntroOfferType.Waiver);     // created afterwards
        PaymentPageReturns("https://pay/fee");

        var result = await ActivateAs(_studUserA, _stallionA);

        result.Value!.ActivatedWithoutPayment.Should().BeFalse();
        _provider.Verify(p => p.CreateListingFeeSessionAsync(It.IsAny<Guid>(), 990m,
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Activate_IgnoresAnEndedOffer()
    {
        AddOffer("Old offer", IntroOfferType.Waiver, endsInDays: -1);
        PaymentPageReturns("https://pay/fee");

        await ActivateAs(_studUserA, _stallionA);

        (await _db.StallionSeasonSubscriptions.SingleAsync()).FeeIncGst.Should().Be(990m);
    }
```

Add `using Stallions.Shared.Enums;` is already present; `IntroOffer` comes from `Stallions.Server.Data.Entities` (present).

- [ ] **Step 2: Run red** — `dotnet test tests/Server.Tests --filter FullyQualifiedName~SubscriptionServiceTests` → compile errors.

- [ ] **Step 3: DTOs**

`src/Shared/DTOs/Payments/PaymentRedirectDto.cs` (whole file):

```csharp
// src/Shared/DTOs/Payments/PaymentRedirectDto.cs
namespace Stallions.Shared.DTOs.Payments;

/// <summary>
/// Where to send the browser next: the provider's hosted page. When an intro offer made the fee $0 the
/// stallion is already active, ActivatedWithoutPayment is true and Url is empty.
/// </summary>
public class PaymentRedirectDto
{
    public string Url { get; set; } = string.Empty;
    public bool ActivatedWithoutPayment { get; set; }
}
```

`src/Shared/DTOs/Subscriptions/SubscriptionDto.cs` — after `public decimal GstAmount { get; set; }` add:

```csharp
    public decimal StandardFeeIncGst { get; set; }               // the standard fee when created
    public string? IntroOfferName { get; set; }                  // the intro offer applied, if any
```

`src/Shared/DTOs/Subscriptions/CreateSubscriptionRequest.cs` — replace the two comment lines above the class with:

```csharp
// Staff-only. Without FeeIncGstOverride the standard listing fee applies, with the best intro offer
// (a $0 result is waived at once). An override is used as-is and no intro offer is recorded.
```

- [ ] **Step 4: Repository include**

`src/Server/Data/Repositories/SubscriptionRepository.cs` lines 139–143 become:

```csharp
    private IQueryable<StallionSeasonSubscription> WithDetails() =>
        _db.StallionSeasonSubscriptions
            .Include(s => s.Stallion)
            .Include(s => s.Season)
            .Include(s => s.StudFarm)
            .Include(s => s.IntroOffer);
```

- [ ] **Step 5: `SubscriptionService`**

Constructor (lines 18–48) — add fields and parameters:

```csharp
    private readonly ISubscriptionRepository _repo;
    private readonly IStallionRepository _stallionRepo;
    private readonly ISeasonRepository _seasonRepo;
    private readonly IStudFarmRepository _farmRepo;
    private readonly IPlatformSettingsRepository _settingsRepo;
    private readonly IAuditLogRepository _auditRepo;
    private readonly IUserService _users;
    private readonly IPaymentProvider _provider;
    private readonly IIntroOfferRepository _introOffers;
    private readonly TimeProvider _clock;
    private readonly ILogger<SubscriptionService> _log;

    public SubscriptionService(
        ISubscriptionRepository repo,
        IStallionRepository stallionRepo,
        ISeasonRepository seasonRepo,
        IStudFarmRepository farmRepo,
        IPlatformSettingsRepository settingsRepo,
        IAuditLogRepository auditRepo,
        IUserService users,
        IPaymentProvider provider,
        IIntroOfferRepository introOffers,
        TimeProvider clock,
        ILogger<SubscriptionService> log)
    {
        _repo = repo;
        _stallionRepo = stallionRepo;
        _seasonRepo = seasonRepo;
        _farmRepo = farmRepo;
        _settingsRepo = settingsRepo;
        _auditRepo = auditRepo;
        _users = users;
        _provider = provider;
        _introOffers = introOffers;
        _clock = clock;
        _log = log;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
```

Add two private helpers (above `GetForStudFarmAsync`):

```csharp
    /// <summary>The standard fee with the best intro offer for this stud and season, as of now.</summary>
    private async Task<IntroOfferMatch> ResolveFeeAsync(Guid studFarmId, Guid seasonId)
    {
        var now = Now;
        var standard = (await _settingsRepo.GetAsync()).StandardListingFeeIncGst;
        return IntroOfferResolver.Resolve(studFarmId, seasonId, standard, now,
            await _introOffers.GetCandidatesAsync(studFarmId, seasonId, now));
    }

    /// <summary>A new subscription at the resolved fee; a $0 intro offer waives it immediately.</summary>
    private StallionSeasonSubscription NewSubscription(Guid stallionId, Guid seasonId, Guid studFarmId,
        IntroOfferMatch match, Guid createdByUserId, string? notes)
    {
        var fee = GstBreakdown.FromIncGst(match.FeeIncGst);
        var subscription = new StallionSeasonSubscription
        {
            StallionId = stallionId,
            SeasonId = seasonId,
            StudFarmId = studFarmId,
            FeeIncGst = fee.IncGst,
            FeeExGst = fee.ExGst,
            GstAmount = fee.Gst,
            StandardFeeIncGst = match.StandardFeeIncGst,
            IntroOfferId = match.Offer?.Id,
            Status = SubscriptionStatus.Pending,
            Notes = notes,
            CreatedByUserId = createdByUserId
        };
        if (match.Offer != null && match.FeeIncGst == 0m)
        {
            subscription.Status = SubscriptionStatus.Waived;
            subscription.PaymentMethod = SubscriptionPaymentMethod.Waived;
            subscription.WaiverReason = $"Intro offer: {match.Offer.Name}";
            subscription.PaidAt = Now;
        }
        return subscription;
    }
```

In `CreateAsync` replace lines 72–86 (from `var feeIncGst = …` through the closing `};` of the object initializer) with:

```csharp
        // An override is used as-is (no intro offer recorded); otherwise the best intro offer applies.
        var standardFee = (await _settingsRepo.GetAsync()).StandardListingFeeIncGst;
        var match = request.FeeIncGstOverride is { } overrideFee
            ? new IntroOfferMatch(null, standardFee, overrideFee)
            : await ResolveFeeAsync(stallion.StudFarmId, season.Id);

        var subscription = NewSubscription(stallion.Id, season.Id, stallion.StudFarmId, match, staff.Id,
            string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim());
```

and the audit call (lines 99–106) becomes:

```csharp
        await _auditRepo.LogAsync(AuditEntity, subscription.Id, "CreateSubscription", staff.Id,
            JsonSerializer.Serialize(new
            {
                subscription.StallionId,
                subscription.SeasonId,
                subscription.FeeIncGst,
                subscription.StandardFeeIncGst,
                subscription.IntroOfferId,
                Status = subscription.Status.ToString(),
                FeeOverridden = request.FeeIncGstOverride.HasValue
            }));
```

In `ActivateAsync` replace lines 210–246 (the `if (subscription == null) { … }` block and the following "already active" check) with:

```csharp
        if (subscription == null)
        {
            // Self-serve: the standard fee with the best intro offer. A Pending subscription Staff created
            // is reused instead (below), so its amount and notes are kept even if an offer started since.
            var match = await ResolveFeeAsync(farm.Id, season.Id);
            subscription = NewSubscription(stallion.Id, season.Id, farm.Id, match, caller.Id, notes: null);
            var created = true;
            try
            {
                await _repo.AddAsync(subscription);
            }
            catch (DbUpdateException)
            {
                // A concurrent click created it first — use that one.
                _repo.Detach(subscription);
                created = false;
                subscription = await _repo.GetByStallionAndSeasonAsync(stallion.Id, season.Id);
                if (subscription == null) throw;
            }

            if (created)
            {
                await _auditRepo.LogAsync(AuditEntity, subscription.Id, "CreateSubscription", caller.Id,
                    JsonSerializer.Serialize(new
                    {
                        subscription.StallionId, subscription.SeasonId, subscription.FeeIncGst,
                        subscription.StandardFeeIncGst, subscription.IntroOfferId,
                        Status = subscription.Status.ToString(), SelfServe = true
                    }));
                if (subscription.Status == SubscriptionStatus.Waived)
                    return ServiceResult<PaymentRedirectDto>.Ok(new PaymentRedirectDto { ActivatedWithoutPayment = true });
            }
        }

        if (subscription.Status is SubscriptionStatus.Paid or SubscriptionStatus.Waived)
            return ServiceResult<PaymentRedirectDto>.BadRequest($"{stallion.Name} is already active for {season.Name}.");
```

`MapToDto` (lines 330–349) — after `GstAmount = s.GstAmount,` add:

```csharp
        StandardFeeIncGst = s.StandardFeeIncGst,
        IntroOfferName = s.IntroOffer?.Name,
```

`src/Server/Program.cs` needs no change (`TimeProvider` and `IIntroOfferRepository` are registered).

- [ ] **Step 6: Run green** — `SubscriptionServiceTests` (old and new) pass; full build (0 warnings) and full test run.

- [ ] **Step 7: Commit**

```bash
git add src/Server/Services/SubscriptionService.cs src/Server/Data/Repositories/SubscriptionRepository.cs \
  src/Shared/DTOs/Subscriptions/SubscriptionDto.cs src/Shared/DTOs/Subscriptions/CreateSubscriptionRequest.cs \
  src/Shared/DTOs/Payments/PaymentRedirectDto.cs tests/Server.Tests/Services/SubscriptionServiceTests.cs
git status --short
git commit -m "feat: listing fee subscriptions apply intro offers; a \$0 offer waives without a payment page

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 4: Business details — Staff settings API and form

**Files:**
- Create: `src/Shared/BusinessNumbers.cs`, `src/Shared/DTOs/Settings/BusinessDetailsDto.cs`, `src/Shared/DTOs/Settings/UpdateBusinessDetailsRequest.cs`
- Modify: `src/Server/Data/Entities/PlatformSettings.cs` (two computed properties)
- Modify: `src/Server/Services/IPlatformSettingsService.cs`, `src/Server/Services/PlatformSettingsService.cs`, `src/Server/Controllers/PlatformSettingsController.cs`
- Modify: `src/Client/Services/PlatformSettingsApiService.cs`, `src/Client/Pages/Staff/StaffSettings.razor`
- Test: `tests/Server.Tests/Shared/BusinessNumbersTests.cs`, `tests/Server.Tests/Services/PlatformSettingsServiceTests.cs` (add tests),
  `tests/Client.Tests/Pages/StaffSettingsTests.cs` (fixture: `Render` takes an optional `BusinessDetailsDto`; add tests)

**Interfaces:**
- Consumes: Task 1 `PlatformSettings` business columns.
- Produces: `static class BusinessNumbers { string Compact(string?); bool IsAbn(string?); bool IsBsb(string?); string FormatAbn(string?); string FormatBsb(string?) }` (namespace `Stallions.Shared`);
  `PlatformSettings.HasBusinessDetails`, `.HasBankDetails` (get-only, not mapped);
  `IPlatformSettingsService.GetBusinessDetailsAsync()`, `.UpdateBusinessDetailsAsync(UpdateBusinessDetailsRequest)` → `ServiceResult<BusinessDetailsDto>`;
  `GET/PUT api/settings/business-details` (StaffOnly);
  `BusinessDetailsDto { LegalName, Abn, Address, Email, BankAccountName, BankBsb, BankAccountNumber, InvoicePaymentTermsDays, CanIssueTaxDocuments, CanIssueUnpaidInvoices }`;
  client `PlatformSettingsApiService.GetBusinessDetailsAsync()`, `.UpdateBusinessDetailsAsync(UpdateBusinessDetailsRequest)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Server.Tests/Shared/BusinessNumbersTests.cs
using FluentAssertions;
using Stallions.Shared;

namespace Stallions.Server.Tests.Shared;

public class BusinessNumbersTests
{
    [Theory]
    [InlineData(" 12 345 678 901 ", "12345678901")]
    [InlineData("12-345-678-901", "12345678901")]
    [InlineData("062-000", "062000")]
    [InlineData(null, "")]
    public void Compact_RemovesSpacesAndHyphens(string? input, string expected) =>
        BusinessNumbers.Compact(input).Should().Be(expected);

    [Theory]
    [InlineData("12345678901", true)]
    [InlineData("1234567890", false)]
    [InlineData("123456789012", false)]
    [InlineData("1234567890A", false)]
    [InlineData(null, false)]
    public void IsAbn_NeedsExactlyElevenDigits(string? abn, bool expected) =>
        BusinessNumbers.IsAbn(abn).Should().Be(expected);

    [Theory]
    [InlineData("062000", true)]
    [InlineData("06200", false)]
    [InlineData("06A000", false)]
    public void IsBsb_NeedsExactlySixDigits(string bsb, bool expected) =>
        BusinessNumbers.IsBsb(bsb).Should().Be(expected);

    [Fact]
    public void Format_GroupsTheDigits()
    {
        BusinessNumbers.FormatAbn("12345678901").Should().Be("12 345 678 901");
        BusinessNumbers.FormatAbn("12 345 678 901").Should().Be("12 345 678 901");
        BusinessNumbers.FormatBsb("062000").Should().Be("062-000");
    }

    [Fact]
    public void Format_LeavesAnythingElseAsItIs()
    {
        BusinessNumbers.FormatAbn("not an abn").Should().Be("not an abn");
        BusinessNumbers.FormatAbn(null).Should().Be("");
        BusinessNumbers.FormatBsb("12").Should().Be("12");
    }
}
```

Add to `PlatformSettingsServiceTests` (it already has `_repo`, `_audit`, `_users`, `CreateSut()`, `Staff()`, `Current()`):

```csharp
    private static UpdateBusinessDetailsRequest Business(string? abn = "12 345 678 901", string? bsb = "062-000",
        int terms = 14, string? legalName = "Stallions Australia Pty Ltd") => new()
    {
        LegalName = legalName, Abn = abn, Address = "1 Racecourse Road\nScone NSW 2337",
        Email = "accounts@stallions.example", BankAccountName = "Stallions Australia Pty Ltd", BankBsb = bsb,
        BankAccountNumber = "1234 5678", InvoicePaymentTermsDays = terms
    };

    private PlatformSettings SignedInStaffWithSettings(out User staff)
    {
        staff = Staff();
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(staff);
        var settings = Current();
        _repo.Setup(r => r.GetAsync()).ReturnsAsync(settings);
        return settings;
    }

    [Fact]
    public async Task UpdateBusinessDetails_AcceptsSpacesAndHyphens_AndStoresDigitsOnly()
    {
        var settings = SignedInStaffWithSettings(out var staff);

        var r = await CreateSut().UpdateBusinessDetailsAsync(Business(abn: " 12 345 678 901 "));

        r.Succeeded.Should().BeTrue(r.Error);
        settings.BusinessAbn.Should().Be("12345678901");
        settings.BankBsb.Should().Be("062000");
        settings.BankAccountNumber.Should().Be("12345678");
        settings.BusinessAddress.Should().Be("1 Racecourse Road\nScone NSW 2337");
        settings.InvoicePaymentTermsDays.Should().Be(14);
        r.Value!.CanIssueTaxDocuments.Should().BeTrue();
        r.Value.CanIssueUnpaidInvoices.Should().BeTrue();
        _repo.Verify(x => x.UpdateAsync(settings), Times.Once);
        _audit.Verify(a => a.LogAsync("PlatformSettings", PlatformSettings.SingletonId, "UpdateBusinessDetails", staff.Id,
            It.Is<string?>(d => d != null && d.Contains("12345678901") && d.Contains("Old"))), Times.Once);
    }

    [Theory]
    [InlineData("1234567890")]
    [InlineData("123456789012")]
    [InlineData("1234567890A")]
    [InlineData("12-345-678-90")]
    public async Task UpdateBusinessDetails_RejectsABadAbn(string abn)
    {
        SignedInStaffWithSettings(out _);

        var r = await CreateSut().UpdateBusinessDetailsAsync(Business(abn: abn));

        r.HttpStatusCode.Should().Be(400);
        r.Error.Should().Be("ABN must be 11 digits.");
        _repo.Verify(x => x.UpdateAsync(It.IsAny<PlatformSettings>()), Times.Never);
    }

    [Theory]
    [InlineData("06200")]
    [InlineData("0620000")]
    [InlineData("06A-000")]
    public async Task UpdateBusinessDetails_RejectsABadBsb(string bsb)
    {
        SignedInStaffWithSettings(out _);

        var r = await CreateSut().UpdateBusinessDetailsAsync(Business(bsb: bsb));

        r.Error.Should().Be("BSB must be 6 digits.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public async Task UpdateBusinessDetails_RejectsPaymentTermsOutOfRange(int terms)
    {
        SignedInStaffWithSettings(out _);

        var r = await CreateSut().UpdateBusinessDetailsAsync(Business(terms: terms));

        r.Error.Should().Be("Payment terms must be between 1 and 90 days.");
    }

    [Fact]
    public async Task UpdateBusinessDetails_BlankValues_AreStoredAsNull_AndCannotIssue()
    {
        var settings = SignedInStaffWithSettings(out _);

        var r = await CreateSut().UpdateBusinessDetailsAsync(Business(abn: "  ", bsb: "", legalName: "   "));

        settings.BusinessLegalName.Should().BeNull();
        settings.BusinessAbn.Should().BeNull();
        settings.BankBsb.Should().BeNull();
        r.Value!.CanIssueTaxDocuments.Should().BeFalse();
        r.Value.CanIssueUnpaidInvoices.Should().BeFalse();
    }

    [Fact]
    public async Task BusinessDetails_AsNonStaff_AreForbidden()
    {
        _users.Setup(u => u.GetOrCreateCurrentUserAsync())
            .ReturnsAsync(new User { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.Active });

        (await CreateSut().GetBusinessDetailsAsync()).HttpStatusCode.Should().Be(403);
        (await CreateSut().UpdateBusinessDetailsAsync(Business())).HttpStatusCode.Should().Be(403);
        _repo.Verify(x => x.UpdateAsync(It.IsAny<PlatformSettings>()), Times.Never);
    }
```

`StaffSettingsTests` — fixture change: `Render` takes the business details to return:

```csharp
    private IRenderedComponent<StaffSettings> Render(BusinessDetailsDto? business = null)
    {
        this.AddTestAuthorization().SetAuthorized("staff@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        _settingsMock.Setup(s => s.GetAsync()).ReturnsAsync(new PlatformSettingsDto
        {
            BuyerFeeIncGst = 150m, StandardListingFeeIncGst = 990m, MinimumBidIncrement = 25m,
            ChargeGracePeriodHours = 2, OfferExpiryDays = 7
        });
        _settingsMock.Setup(s => s.GetBusinessDetailsAsync()).ReturnsAsync(business ?? new BusinessDetailsDto
        {
            LegalName = "Stallions Australia Pty Ltd", Abn = "12345678901", Address = "1 Racecourse Road",
            InvoicePaymentTermsDays = 14, CanIssueTaxDocuments = true
        });
        Services.AddSingleton(_settingsMock.Object);

        var userApiMock = new Mock<UserApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        userApiMock.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
        {
            Id = Guid.NewGuid(), DisplayName = "Staff", Email = "staff@example.com",
            Role = "Staff", Status = "Active"
        });
        Services.AddSingleton(new UserStateService(userApiMock.Object));

        return RenderComponent<StaffSettings>();
    }
```

New tests:

```csharp
    [Fact]
    public void BusinessDetails_ShowTheAbnGrouped()
    {
        var cut = Render();

        cut.WaitForAssertion(() => cut.Find("#biz-abn").GetAttribute("value").Should().Be("12 345 678 901"));
    }

    [Fact]
    public void BusinessDetails_WithABadAbn_ShowsAnErrorAndDoesNotSave()
    {
        var cut = Render();
        cut.WaitForAssertion(() => cut.Find("#biz-abn"));

        cut.Find("#biz-abn").Change("1234");
        cut.FindAll("button").Single(b => b.TextContent.Contains("Save business details")).Click();

        cut.Markup.Should().Contain("ABN must be 11 digits.");
        _settingsMock.Verify(s => s.UpdateBusinessDetailsAsync(It.IsAny<UpdateBusinessDetailsRequest>()), Times.Never);
    }

    [Fact]
    public void BusinessDetails_Save_SendsTheForm()
    {
        _settingsMock.Setup(s => s.UpdateBusinessDetailsAsync(It.IsAny<UpdateBusinessDetailsRequest>()))
            .ReturnsAsync(new BusinessDetailsDto { Abn = "12345678901", BankBsb = "062000", InvoicePaymentTermsDays = 14,
                CanIssueTaxDocuments = true, CanIssueUnpaidInvoices = true });
        var cut = Render();
        cut.WaitForAssertion(() => cut.Find("#bank-bsb"));

        cut.Find("#bank-bsb").Change("062-000");
        cut.FindAll("button").Single(b => b.TextContent.Contains("Save business details")).Click();

        _settingsMock.Verify(s => s.UpdateBusinessDetailsAsync(It.Is<UpdateBusinessDetailsRequest>(r =>
            r.BankBsb == "062-000" && r.Abn == "12 345 678 901" && r.InvoicePaymentTermsDays == 14)), Times.Once);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Business details saved."));
    }

    [Fact]
    public void BusinessDetails_Incomplete_WarnThatNoTaxInvoiceCanBeIssued()
    {
        var cut = Render(new BusinessDetailsDto { InvoicePaymentTermsDays = 14 });

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Tax invoices can't be issued until the legal name, ABN and address are set."));
    }
```

- [ ] **Step 2: Run red** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~BusinessNumbers|FullyQualifiedName~PlatformSettingsServiceTests"` and
  `dotnet test tests/Client.Tests --filter FullyQualifiedName~StaffSettingsTests` → compile errors.

- [ ] **Step 3: Shared helper and DTOs**

```csharp
// src/Shared/BusinessNumbers.cs
namespace Stallions.Shared;

/// <summary>
/// Australian Business Numbers and BSBs: typed with spaces or hyphens, stored as digits only, shown grouped
/// (12 345 678 901, 062-000). Validation checks the digit count only.
/// </summary>
public static class BusinessNumbers
{
    public static string Compact(string? value) =>
        new((value ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());

    public static bool IsAbn(string? compact) => compact is { Length: 11 } && compact.All(char.IsAsciiDigit);

    public static bool IsBsb(string? compact) => compact is { Length: 6 } && compact.All(char.IsAsciiDigit);

    public static string FormatAbn(string? abn)
    {
        var c = Compact(abn);
        return IsAbn(c) ? $"{c[..2]} {c[2..5]} {c[5..8]} {c[8..]}" : abn ?? string.Empty;
    }

    public static string FormatBsb(string? bsb)
    {
        var c = Compact(bsb);
        return IsBsb(c) ? $"{c[..3]}-{c[3..]}" : bsb ?? string.Empty;
    }
}
```

```csharp
// src/Shared/DTOs/Settings/BusinessDetailsDto.cs
namespace Stallions.Shared.DTOs.Settings;

/// <summary>Staff-only: Stallions Australia's details printed on tax documents. ABN and BSB are digits only.</summary>
public class BusinessDetailsDto
{
    public string? LegalName { get; set; }
    public string? Abn { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public string? BankAccountName { get; set; }
    public string? BankBsb { get; set; }
    public string? BankAccountNumber { get; set; }
    public int InvoicePaymentTermsDays { get; set; }
    /// <summary>Legal name, ABN and address are set.</summary>
    public bool CanIssueTaxDocuments { get; set; }
    /// <summary>…and the bank details too (needed for an unpaid invoice).</summary>
    public bool CanIssueUnpaidInvoices { get; set; }
}
```

```csharp
// src/Shared/DTOs/Settings/UpdateBusinessDetailsRequest.cs
namespace Stallions.Shared.DTOs.Settings;

// Staff-only (PUT api/settings/business-details). Blank values clear a field. ABN: 11 digits; BSB: 6 digits;
// spaces and hyphens are ignored. Payment terms 1–90 days.
public class UpdateBusinessDetailsRequest
{
    public string? LegalName { get; set; }
    public string? Abn { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public string? BankAccountName { get; set; }
    public string? BankBsb { get; set; }
    public string? BankAccountNumber { get; set; }
    public int InvoicePaymentTermsDays { get; set; }
}
```

`src/Server/Data/Entities/PlatformSettings.cs` — after `public int InvoicePaymentTermsDays { get; set; } = 14;` add:

```csharp

    /// <summary>A tax document can be issued (get-only: not a column).</summary>
    public bool HasBusinessDetails => !string.IsNullOrWhiteSpace(BusinessLegalName)
                                      && !string.IsNullOrWhiteSpace(BusinessAbn)
                                      && !string.IsNullOrWhiteSpace(BusinessAddress);

    /// <summary>An unpaid invoice can show where to pay.</summary>
    public bool HasBankDetails => !string.IsNullOrWhiteSpace(BankAccountName)
                                  && !string.IsNullOrWhiteSpace(BankBsb)
                                  && !string.IsNullOrWhiteSpace(BankAccountNumber);
```

- [ ] **Step 4: Service and controller**

`src/Server/Services/IPlatformSettingsService.cs` — add:

```csharp
    /// <summary>Staff-only: Stallions Australia's business details (includes bank details).</summary>
    Task<ServiceResult<BusinessDetailsDto>> GetBusinessDetailsAsync();
    Task<ServiceResult<BusinessDetailsDto>> UpdateBusinessDetailsAsync(UpdateBusinessDetailsRequest request);
```

`src/Server/Services/PlatformSettingsService.cs` — add `using Stallions.Shared;` and these members (before `private static object Snapshot`):

```csharp
    private const string BusinessStaffOnlyMessage = "Only Stallions Australia staff can manage the business details.";

    public async Task<ServiceResult<BusinessDetailsDto>> GetBusinessDetailsAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.Staff)
            return ServiceResult<BusinessDetailsDto>.Forbidden(BusinessStaffOnlyMessage);
        return ServiceResult<BusinessDetailsDto>.Ok(MapBusiness(await _repo.GetAsync()));
    }

    public async Task<ServiceResult<BusinessDetailsDto>> UpdateBusinessDetailsAsync(UpdateBusinessDetailsRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.Staff)
            return ServiceResult<BusinessDetailsDto>.Forbidden(BusinessStaffOnlyMessage);

        var legalName = Text(request.LegalName);
        var address = Text(request.Address);
        var email = Text(request.Email);
        var accountName = Text(request.BankAccountName);
        var abn = Digits(request.Abn);
        var bsb = Digits(request.BankBsb);
        var account = Digits(request.BankAccountNumber);

        if (legalName?.Length > 200) return ServiceResult<BusinessDetailsDto>.BadRequest("The legal name can be at most 200 characters.");
        if (abn != null && !BusinessNumbers.IsAbn(abn)) return ServiceResult<BusinessDetailsDto>.BadRequest("ABN must be 11 digits.");
        if (address?.Length > 500) return ServiceResult<BusinessDetailsDto>.BadRequest("The address can be at most 500 characters.");
        if (email != null && (email.Length > 256 || !email.Contains('@')))
            return ServiceResult<BusinessDetailsDto>.BadRequest("Enter a valid email address.");
        if (accountName?.Length > 200) return ServiceResult<BusinessDetailsDto>.BadRequest("The account name can be at most 200 characters.");
        if (bsb != null && !BusinessNumbers.IsBsb(bsb)) return ServiceResult<BusinessDetailsDto>.BadRequest("BSB must be 6 digits.");
        if (account != null && (account.Length is < 4 or > 10 || !account.All(char.IsAsciiDigit)))
            return ServiceResult<BusinessDetailsDto>.BadRequest("Account number must be 4 to 10 digits.");
        if (request.InvoicePaymentTermsDays is < 1 or > 90)
            return ServiceResult<BusinessDetailsDto>.BadRequest("Payment terms must be between 1 and 90 days.");

        var settings = await _repo.GetAsync();
        var old = BusinessSnapshot(settings);
        settings.BusinessLegalName = legalName;
        settings.BusinessAbn = abn;
        settings.BusinessAddress = address;
        settings.BusinessEmail = email;
        settings.BankAccountName = accountName;
        settings.BankBsb = bsb;
        settings.BankAccountNumber = account;
        settings.InvoicePaymentTermsDays = request.InvoicePaymentTermsDays;
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedByUserId = caller.Id;
        await _repo.UpdateAsync(settings);

        await _auditRepo.LogAsync("PlatformSettings", settings.Id, "UpdateBusinessDetails", caller.Id,
            JsonSerializer.Serialize(new { Old = old, New = BusinessSnapshot(settings) }));

        return ServiceResult<BusinessDetailsDto>.Ok(MapBusiness(settings));
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Digits(string? value)
    {
        var compact = BusinessNumbers.Compact(value);
        return compact.Length == 0 ? null : compact;
    }

    private static object BusinessSnapshot(PlatformSettings s) => new
    {
        s.BusinessLegalName, s.BusinessAbn, s.BusinessAddress, s.BusinessEmail,
        s.BankAccountName, s.BankBsb, s.BankAccountNumber, s.InvoicePaymentTermsDays
    };

    private static BusinessDetailsDto MapBusiness(PlatformSettings s) => new()
    {
        LegalName = s.BusinessLegalName,
        Abn = s.BusinessAbn,
        Address = s.BusinessAddress,
        Email = s.BusinessEmail,
        BankAccountName = s.BankAccountName,
        BankBsb = s.BankBsb,
        BankAccountNumber = s.BankAccountNumber,
        InvoicePaymentTermsDays = s.InvoicePaymentTermsDays,
        CanIssueTaxDocuments = s.HasBusinessDetails,
        CanIssueUnpaidInvoices = s.HasBusinessDetails && s.HasBankDetails
    };
```

`src/Server/Controllers/PlatformSettingsController.cs` — add:

```csharp
    // Staff only: includes bank details, which GET api/settings (any signed-in user) must not expose.
    [HttpGet("business-details")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> GetBusinessDetails()
    {
        var r = await _settings.GetBusinessDetailsAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPut("business-details")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> UpdateBusinessDetails([FromBody] UpdateBusinessDetailsRequest request)
    {
        var r = await _settings.UpdateBusinessDetailsAsync(request);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
```

- [ ] **Step 5: Client**

`src/Client/Pages/Staff/StaffSettings.razor` — add `@using Stallions.Shared` after line 3 (for `BusinessNumbers`; not in `_Imports.razor`, because `ListingDetail.razor` already has that `@using`).

`src/Client/Services/PlatformSettingsApiService.cs` — add:

```csharp
    public virtual async Task<BusinessDetailsDto> GetBusinessDetailsAsync()
    {
        var r = await _http.GetAsync("api/settings/business-details");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<BusinessDetailsDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<BusinessDetailsDto> UpdateBusinessDetailsAsync(UpdateBusinessDetailsRequest request)
    {
        var r = await _http.PutAsJsonAsync("api/settings/business-details", request);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<BusinessDetailsDto>()
               ?? throw new ApiException(500, "Empty response.");
    }
```

`src/Client/Pages/Staff/StaffSettings.razor`:

After line 85 (the `</div>` closing `<div class="admin-form settings-form">`) and before line 86 (`}`), add:

```razor

    <div class="admin-form settings-form">
        <div class="form-section">
            <div class="form-section-title">Business details (tax invoices)</div>
            <p class="form-hint">
                Printed on every tax invoice and adjustment note. Changes apply to documents issued after you save.
            </p>
            @if (_businessStatus is { CanIssueTaxDocuments: false })
            {
                <div class="alert alert-warning">Tax invoices can't be issued until the legal name, ABN and address are set.</div>
            }
            else if (_businessStatus is { CanIssueUnpaidInvoices: false })
            {
                <div class="alert alert-warning">Add the bank details to issue unpaid tax invoices for listing fees.</div>
            }
            <div class="form-group">
                <label class="form-label" for="biz-legal-name">Legal name</label>
                <input id="biz-legal-name" class="form-input" maxlength="200" @bind="_business.LegalName" />
            </div>
            <div class="form-group">
                <label class="form-label" for="biz-abn">ABN</label>
                <input id="biz-abn" class="form-input" inputmode="numeric" placeholder="12 345 678 901" @bind="_business.Abn" />
            </div>
            <div class="form-group">
                <label class="form-label" for="biz-address">Address</label>
                <textarea id="biz-address" class="form-control" rows="3" maxlength="500" @bind="_business.Address"></textarea>
            </div>
            <div class="form-group">
                <label class="form-label" for="biz-email">Accounts email</label>
                <input id="biz-email" type="email" class="form-input" maxlength="256" @bind="_business.Email" />
            </div>
        </div>
        <div class="form-section">
            <div class="form-section-title">Bank details for unpaid invoices</div>
            <div class="form-group">
                <label class="form-label" for="bank-account-name">Account name</label>
                <input id="bank-account-name" class="form-input" maxlength="200" @bind="_business.BankAccountName" />
            </div>
            <div class="form-group">
                <label class="form-label" for="bank-bsb">BSB</label>
                <input id="bank-bsb" class="form-input" inputmode="numeric" placeholder="062-000" @bind="_business.BankBsb" />
            </div>
            <div class="form-group">
                <label class="form-label" for="bank-account-number">Account number</label>
                <input id="bank-account-number" class="form-input" inputmode="numeric" @bind="_business.BankAccountNumber" />
            </div>
            <div class="form-group">
                <label class="form-label" for="invoice-terms">Payment terms (days)</label>
                <input id="invoice-terms" type="number" class="form-input" min="1" max="90" step="1"
                       @bind="_business.InvoicePaymentTermsDays" />
                <p class="form-hint">An unpaid listing fee invoice is due this many days after it is issued.</p>
            </div>
        </div>

        @if (_businessError is not null) { <div class="alert alert-danger">@_businessError</div> }
        @if (_businessSaved) { <div class="alert alert-success">Business details saved.</div> }

        <div class="form-actions">
            <button class="btn btn-gold" @onclick="SaveBusinessAsync" disabled="@_businessBusy">
                @(_businessBusy ? "Saving…" : "Save business details")
            </button>
        </div>
    </div>
```

In `@code`: add fields

```csharp
    private BusinessDetailsDto? _businessStatus;
    private UpdateBusinessDetailsRequest _business = new();
    private bool _businessBusy;
    private bool _businessSaved;
    private string? _businessError;
```

In `OnInitializedAsync`, inside the `try` after `ResetForm();` add:

```csharp
            ResetBusiness(await SettingsApi.GetBusinessDetailsAsync());
```

and add these methods:

```csharp
    private void ResetBusiness(BusinessDetailsDto? d)
    {
        _businessStatus = d;
        _business = new UpdateBusinessDetailsRequest
        {
            LegalName = d?.LegalName,
            Abn = BusinessNumbers.FormatAbn(d?.Abn),
            Address = d?.Address,
            Email = d?.Email,
            BankAccountName = d?.BankAccountName,
            BankBsb = BusinessNumbers.FormatBsb(d?.BankBsb),
            BankAccountNumber = d?.BankAccountNumber,
            InvoicePaymentTermsDays = d?.InvoicePaymentTermsDays ?? 0
        };
    }

    // Mirrors the server's checks (which remain the authority).
    private string? ValidateBusiness()
    {
        var abn = BusinessNumbers.Compact(_business.Abn);
        if (abn.Length > 0 && !BusinessNumbers.IsAbn(abn)) return "ABN must be 11 digits.";
        var bsb = BusinessNumbers.Compact(_business.BankBsb);
        if (bsb.Length > 0 && !BusinessNumbers.IsBsb(bsb)) return "BSB must be 6 digits.";
        if (_business.InvoicePaymentTermsDays is < 1 or > 90) return "Payment terms must be between 1 and 90 days.";
        return null;
    }

    private async Task SaveBusinessAsync()
    {
        _businessSaved = false;
        _businessError = ValidateBusiness();
        if (_businessError is not null) return;
        _businessBusy = true;
        try
        {
            ResetBusiness(await SettingsApi.UpdateBusinessDetailsAsync(_business));
            _businessSaved = true;
        }
        catch (Exception ex) { _businessError = ex is ApiException ae ? ae.Message : "Failed to save the business details."; }
        finally { _businessBusy = false; }
    }
```

- [ ] **Step 6: Run green** — the server and client tests above; full build (0 warnings) and full test run.

- [ ] **Step 7: Commit**

```bash
git add src/Shared/BusinessNumbers.cs src/Shared/DTOs/Settings src/Server/Data/Entities/PlatformSettings.cs \
  src/Server/Services/IPlatformSettingsService.cs src/Server/Services/PlatformSettingsService.cs \
  src/Server/Controllers/PlatformSettingsController.cs src/Client/Services/PlatformSettingsApiService.cs \
  src/Client/Pages/Staff/StaffSettings.razor tests/Server.Tests/Shared/BusinessNumbersTests.cs \
  tests/Server.Tests/Services/PlatformSettingsServiceTests.cs tests/Client.Tests/Pages/StaffSettingsTests.cs
git status --short
git commit -m "feat: Staff-managed business details for tax documents (ABN, address, bank, terms)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: `TaxDocumentService` — numbering, snapshots, idempotency, emails

**Files:**
- Create: `src/Server/Data/DbErrors.cs`
- Create: `src/Server/Data/Repositories/ITaxDocumentRepository.cs`, `src/Server/Data/Repositories/TaxDocumentRepository.cs`
- Create: `src/Server/TaxDocuments/TaxDocumentNumberSource.cs`, `src/Server/TaxDocuments/TaxDocumentService.cs`, `src/Server/TaxDocuments/TaxDocumentEmails.cs`
- Modify: `src/Server/Program.cs` (`using Stallions.Server.TaxDocuments;` + four registrations)
- Create (test helpers): `tests/Server.Tests/Helpers/FakeTaxDocumentNumberSource.cs`, `tests/Server.Tests/Helpers/TaxTestData.cs`
- Test: `tests/Server.Tests/TaxDocuments/TaxDocumentServiceTests.cs`, `tests/Server.Tests/TaxDocuments/TaxDocumentEmailsTests.cs`,
  `tests/Server.Tests/Data/Repositories/TaxDocumentRepositoryTests.cs`

**Interfaces:**
- Consumes: Task 1 `TaxDocument`, sequence; Task 4 `PlatformSettings.HasBusinessDetails/HasBankDetails`, `BusinessNumbers`; Task 2 `IIntroOfferRepository.GetByIdAsync`.
- Produces:
  `static class DbErrors { bool IsUniqueViolation(DbUpdateException) }`;
  `ITaxDocumentRepository { GetByIdAsync(Guid); GetListingFeeInvoiceAsync(Guid subscriptionId); GetBuyerFeeInvoiceAsync(Guid purchaseId); GetAdjustmentNoteForAsync(Guid originalDocumentId); GetAdjustmentNotesForAsync(IEnumerable<Guid>); GetBySubscriptionIdsAsync(IEnumerable<Guid>); GetByPurchaseIdsAsync(IEnumerable<Guid>); GetForBuyerAsync(Guid); GetForStudFarmAsync(Guid); SearchAsync(TaxDocumentKind?, TaxDocumentStatus?, DateTime? issuedFromUtc, DateTime? issuedBeforeUtc); Task<bool> TryAddAsync(TaxDocument); Task<bool> TryUpdateAsync(TaxDocument) }` (`TaxDocumentRepository` has `virtual` lookups, `TryAddAsync`, `TryUpdateAsync`);
  `interface ITaxDocumentNumberSource { Task<long> NextAsync(); }`, `SqlTaxDocumentNumberSource`;
  `enum TaxDocumentIssueStatus { Issued, MarkedPaid, Existing, BusinessDetailsMissing, NotNeeded }`;
  `sealed record TaxDocumentIssueResult(TaxDocumentIssueStatus Status, TaxDocument? Document)` with static `Missing`, `NotNeeded`;
  `ITaxDocumentService { Task<bool> CanIssueAsync(bool unpaidInvoice = false); Task<TaxDocumentIssueResult> IssueListingFeeInvoiceAsync(StallionSeasonSubscription subscription, bool paid, Guid? userId, DateTime? paidAt = null); Task<TaxDocumentIssueResult> MarkListingFeeInvoicePaidAsync(StallionSeasonSubscription subscription, DateTime paidAt, Guid? userId); Task<TaxDocumentIssueResult> SettleListingFeeAsync(StallionSeasonSubscription subscription, DateTime paidAt, Guid? userId); Task<TaxDocumentIssueResult> IssueBuyerFeeInvoiceAsync(Purchase purchase); Task<TaxDocumentIssueResult> IssueAdjustmentNoteAsync(TaxDocument original, string reason, Guid? userId); Task RecordPendingAsync(string entityType, Guid entityId, TaxDocumentKind kind) }`;
  `TaxDocumentService.PendingAction = "TaxDocumentPending"`, `TaxDocumentService.BusinessDetailsMissingMessage = "Complete the business details in Settings first."`, `TaxDocumentService.FormatNumber(long)`;
  `ITaxDocumentEmails { ListingFeeTaxInvoiceAsync(TaxDocument); ListingFeeInvoiceDueAsync(TaxDocument); ListingFeeReceiptAsync(TaxDocument); BuyerFeeTaxInvoiceAsync(TaxDocument); AdjustmentNoteAsync(TaxDocument note, TaxDocument original) }`;
  test helpers `FakeTaxDocumentNumberSource` (`StartAt(long)`), `TaxTestData.CompleteBusinessDetails(AppDbContext, bool withBank = true)`, `TaxTestData.ClearBusinessDetails(AppDbContext)`.

`TaxDocumentService` never starts a transaction: every caller already runs inside `ITransactionRunner.RunAsync`
(or, in the webhook, inside the processor's transaction), so document, audit row, email and money state commit together.
A unique-index violation inside that transaction rolls back only EF's savepoint for that `SaveChanges` (with MARS on,
EF skips savepoints, but SQL Server undoes just the failed INSERT statement — error 2601 doesn't doom the transaction),
so the caller's transaction stays usable and the service returns the winner's document.

- [ ] **Step 1: Test helpers**

```csharp
// tests/Server.Tests/Helpers/FakeTaxDocumentNumberSource.cs
using Stallions.Server.TaxDocuments;

namespace Stallions.Server.Tests.Helpers;

/// <summary>The in-memory provider can't run SQL sequences: a counter stands in for TaxDocumentNumbers.</summary>
public class FakeTaxDocumentNumberSource : ITaxDocumentNumberSource
{
    private long _last;
    public void StartAt(long first) => _last = first - 1;
    public Task<long> NextAsync() => Task.FromResult(Interlocked.Increment(ref _last));
}
```

```csharp
// tests/Server.Tests/Helpers/TaxTestData.cs
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Tests.Helpers;

/// <summary>Business details on the seeded settings row (call Database.EnsureCreated() first).</summary>
public static class TaxTestData
{
    public static PlatformSettings CompleteBusinessDetails(AppDbContext db, bool withBank = true)
    {
        var s = db.PlatformSettings.Single();
        s.BusinessLegalName = "Stallions Australia Pty Ltd";
        s.BusinessAbn = "12345678901";
        s.BusinessAddress = "1 Racecourse Road, Scone NSW 2337";
        s.BusinessEmail = "accounts@stallions.example";
        s.BankAccountName = withBank ? "Stallions Australia Pty Ltd" : null;
        s.BankBsb = withBank ? "062000" : null;
        s.BankAccountNumber = withBank ? "12345678" : null;
        db.SaveChanges();
        return s;
    }

    public static void ClearBusinessDetails(AppDbContext db)
    {
        var s = db.PlatformSettings.Single();
        s.BusinessLegalName = null;
        s.BusinessAbn = null;
        s.BusinessAddress = null;
        s.BusinessEmail = null;
        s.BankAccountName = null;
        s.BankBsb = null;
        s.BankAccountNumber = null;
        db.SaveChanges();
    }
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/Server.Tests/Data/Repositories/TaxDocumentRepositoryTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data.Repositories;

public class TaxDocumentRepositoryTests
{
    private static readonly DateTime T0 = new(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Makes every save fail the way SQL Server does (a SqlException with this number).</summary>
    private sealed class FailingSaves(int sqlNumber) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("insert failed", SqlExceptionFactory.WithNumber(sqlNumber));
    }

    private static AppDbContext FailingDb(int sqlNumber) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .AddInterceptors(new FailingSaves(sqlNumber))
        .Options);

    private static TaxDocument Doc(string number, TaxDocumentKind kind = TaxDocumentKind.ListingFeeInvoice,
        TaxDocumentStatus status = TaxDocumentStatus.Paid, DateTime? issuedAt = null, Guid? buyer = null, Guid? farm = null) => new()
    {
        Number = number, Kind = kind, Status = status, IssuedAt = issuedAt ?? T0, Description = "Fee",
        AmountIncGst = 990m, AmountExGst = 900m, GstAmount = 90m, SupplierName = "SA", SupplierAbn = "12345678901",
        SupplierAddress = "Scone", RecipientName = "R", RecipientEmail = "r@x", BuyerUserId = buyer, StudFarmId = farm
    };

    [Fact]
    public async Task TryAdd_OnAUniqueViolation_ReturnsFalse_AndStopsTracking()
    {
        using var db = FailingDb(2601);
        var doc = Doc("SA-000001");

        (await new TaxDocumentRepository(db).TryAddAsync(doc)).Should().BeFalse();

        db.Entry(doc).State.Should().Be(EntityState.Detached);
    }

    [Fact]
    public async Task TryAdd_OnAnyOtherFailure_Throws()
    {
        using var db = FailingDb(547);

        var act = () => new TaxDocumentRepository(db).TryAddAsync(Doc("SA-000001"));

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Lists_AreScopedToTheirOwner_NewestFirst()
    {
        using var db = DbContextFactory.Create(Guid.NewGuid().ToString());
        var buyer = Guid.NewGuid();
        var farm = Guid.NewGuid();
        var repo = new TaxDocumentRepository(db);
        await repo.TryAddAsync(Doc("SA-000001", TaxDocumentKind.BuyerFeeInvoice, buyer: buyer, issuedAt: T0));
        await repo.TryAddAsync(Doc("SA-000002", TaxDocumentKind.BuyerFeeInvoice, buyer: buyer, issuedAt: T0.AddDays(1)));
        await repo.TryAddAsync(Doc("SA-000003", TaxDocumentKind.BuyerFeeInvoice, buyer: Guid.NewGuid()));
        await repo.TryAddAsync(Doc("SA-000004", farm: farm));

        (await repo.GetForBuyerAsync(buyer)).Select(d => d.Number).Should().Equal("SA-000002", "SA-000001");
        (await repo.GetForStudFarmAsync(farm)).Select(d => d.Number).Should().Equal("SA-000004");
    }

    [Fact]
    public async Task Search_FiltersByKindStatusAndIssueTime()
    {
        using var db = DbContextFactory.Create(Guid.NewGuid().ToString());
        var repo = new TaxDocumentRepository(db);
        await repo.TryAddAsync(Doc("SA-000001", issuedAt: T0));
        await repo.TryAddAsync(Doc("SA-000002", status: TaxDocumentStatus.Unpaid, issuedAt: T0.AddDays(1)));
        await repo.TryAddAsync(Doc("SA-000003", TaxDocumentKind.BuyerFeeInvoice, issuedAt: T0.AddDays(2)));

        (await repo.SearchAsync(TaxDocumentKind.ListingFeeInvoice, null, null, null)).Should().HaveCount(2);
        (await repo.SearchAsync(null, TaxDocumentStatus.Unpaid, null, null)).Single().Number.Should().Be("SA-000002");
        (await repo.SearchAsync(null, null, T0.AddDays(1), T0.AddDays(2))).Single().Number.Should().Be("SA-000002");
    }
}
```

```csharp
// tests/Server.Tests/TaxDocuments/TaxDocumentServiceTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.TaxDocuments;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.TaxDocuments;

public class TaxDocumentServiceTests : IDisposable
{
    private readonly string _name = Guid.NewGuid().ToString();
    private readonly AppDbContext _db;
    private readonly TestClock _clock = new() { Now = new DateTimeOffset(2026, 10, 11, 2, 0, 0, TimeSpan.Zero) };
    private readonly FakeTaxDocumentNumberSource _numbers = new();
    private readonly Mock<ITaxDocumentEmails> _emails = new();
    private readonly User _owner = new() { ObjectId = "o", Email = "owner@arrowfield.example", DisplayName = "Owner", Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };
    private readonly User _buyer = new() { ObjectId = "b", Email = "jane@example.com", DisplayName = "Jane Buyer", Role = UserRole.Buyer, Status = UserStatus.Active };
    private readonly StudFarm _farm;
    private readonly Season _season = new() { Name = "2026 Season", IsOpen = true };

    public TaxDocumentServiceTests()
    {
        _db = DbContextFactory.Create(_name);
        _db.Database.EnsureCreated();
        TaxTestData.CompleteBusinessDetails(_db);
        _farm = new StudFarm { UserId = _owner.Id, Name = "Arrowfield", ABN = "98 765 432 109", Address = "Scone NSW",
            ContactEmail = "sales@arrowfield.example" };
        _db.AddRange(_owner, _buyer, _farm, _season);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private TaxDocumentService CreateSut(AppDbContext? db = null, ITaxDocumentRepository? repo = null)
    {
        db ??= _db;
        return new TaxDocumentService(repo ?? new TaxDocumentRepository(db), _numbers, new PlatformSettingsRepository(db),
            new StudFarmRepository(db), new UserRepository(db), new IntroOfferRepository(db), _emails.Object,
            new AuditLogRepository(db), _clock, NullLogger<TaxDocumentService>.Instance);
    }

    /// <summary>A subscription for a new stallion "Dundeel", loaded with Stallion, Season and StudFarm.</summary>
    private async Task<StallionSeasonSubscription> SubscriptionAsync(decimal fee = 990m, IntroOffer? offer = null)
    {
        var stallion = new Stallion { Name = "Dundeel", StudFarmId = _farm.Id };
        _db.Stallions.Add(stallion);
        var split = GstBreakdown.FromIncGst(fee);
        var sub = new StallionSeasonSubscription
        {
            StallionId = stallion.Id, SeasonId = _season.Id, StudFarmId = _farm.Id, FeeIncGst = split.IncGst,
            FeeExGst = split.ExGst, GstAmount = split.Gst, StandardFeeIncGst = 990m, IntroOfferId = offer?.Id,
            CreatedByUserId = _owner.Id
        };
        _db.StallionSeasonSubscriptions.Add(sub);
        await _db.SaveChangesAsync();
        return (await new SubscriptionRepository(_db).GetByIdAsync(sub.Id))!;
    }

    /// <summary>A completed sale of a Snitzel nomination to Jane Buyer, loaded as the charger loads it.</summary>
    private async Task<Purchase> SaleAsync(bool fromOffer = false)
    {
        var listing = AuctionTestData.Auction(_db, _clock.UtcNow.AddDays(-1), status: ListingStatus.Sold);
        var purchase = new Purchase
        {
            ListingId = listing.Id, BuyerUserId = _buyer.Id, BidId = fromOffer ? null : Guid.NewGuid(),
            OfferId = fromOffer ? Guid.NewGuid() : null, TotalPriceIncGst = 20000m, BuyerFeeIncGst = 150m,
            BuyerFeeExGst = 136.36m, BuyerFeeGst = 13.64m, BalancePayableToStudIncGst = 19850m,
            Status = PurchaseStatus.Completed, PaidAt = _clock.UtcNow, PaymentReference = "pi_1"
        };
        _db.Purchases.Add(purchase);
        await _db.SaveChangesAsync();
        return (await new PurchaseRepository(_db).GetForChargeAsync(purchase.Id))!;
    }

    /// <summary>Its first look misses the other instance's document; its insert then hits the unique index.</summary>
    private sealed class RacingRepository(AppDbContext db) : TaxDocumentRepository(db)
    {
        private bool _looked;

        public override async Task<TaxDocument?> GetListingFeeInvoiceAsync(Guid subscriptionId)
        {
            if (!_looked) { _looked = true; return null; }
            return await base.GetListingFeeInvoiceAsync(subscriptionId);
        }

        public override Task<bool> TryAddAsync(TaxDocument document) => Task.FromResult(false);
    }

    [Fact]
    public async Task ListingFeeInvoice_Paid_HasTheNumberGstSplitAndSnapshots()
    {
        var sub = await SubscriptionAsync();

        var r = await CreateSut().IssueListingFeeInvoiceAsync(sub, paid: true, userId: _owner.Id, paidAt: _clock.UtcNow);

        r.Status.Should().Be(TaxDocumentIssueStatus.Issued);
        var doc = await _db.TaxDocuments.SingleAsync();
        doc.Number.Should().Be("SA-000001");
        doc.Kind.Should().Be(TaxDocumentKind.ListingFeeInvoice);
        doc.Status.Should().Be(TaxDocumentStatus.Paid);
        doc.IssuedAt.Should().Be(_clock.UtcNow);
        doc.PaidAt.Should().Be(_clock.UtcNow);
        doc.DueAt.Should().BeNull();
        (doc.AmountIncGst, doc.AmountExGst, doc.GstAmount).Should().Be((990m, 900m, 90m));
        doc.Description.Should().Be("Listing fee — Dundeel, 2026 Season");
        (doc.SupplierName, doc.SupplierAbn, doc.SupplierAddress, doc.SupplierEmail).Should().Be(
            ("Stallions Australia Pty Ltd", "12345678901", "1 Racecourse Road, Scone NSW 2337", "accounts@stallions.example"));
        (doc.RecipientName, doc.RecipientEmail, doc.RecipientAbn, doc.RecipientAddress).Should().Be(
            ("Arrowfield", "sales@arrowfield.example", "98 765 432 109", "Scone NSW"));
        doc.BankBsb.Should().BeNull();
        doc.StudFarmId.Should().Be(_farm.Id);
        doc.SubscriptionId.Should().Be(sub.Id);
        doc.BuyerUserId.Should().BeNull();
        doc.StandardAmountIncGst.Should().BeNull();
        _emails.Verify(e => e.ListingFeeTaxInvoiceAsync(It.Is<TaxDocument>(d => d.Id == doc.Id)), Times.Once);
        var audit = await _db.AuditLogs.SingleAsync(a => a.EntityId == doc.Id);
        (audit.EntityType, audit.Action, audit.UserId).Should().Be(("TaxDocument", "TaxDocumentIssued", _owner.Id));
    }

    [Fact]
    public async Task ListingFeeInvoice_Unpaid_IsDueAfterTheTerms_WithTheBankDetails()
    {
        var sub = await SubscriptionAsync();

        await CreateSut().IssueListingFeeInvoiceAsync(sub, paid: false, userId: null);

        var doc = await _db.TaxDocuments.SingleAsync();
        doc.Status.Should().Be(TaxDocumentStatus.Unpaid);
        doc.PaidAt.Should().BeNull();
        doc.DueAt.Should().Be(_clock.UtcNow.AddDays(14));
        (doc.BankAccountName, doc.BankBsb, doc.BankAccountNumber).Should().Be(("Stallions Australia Pty Ltd", "062000", "12345678"));
        _emails.Verify(e => e.ListingFeeInvoiceDueAsync(It.Is<TaxDocument>(d => d.Id == doc.Id)), Times.Once);
        _emails.Verify(e => e.ListingFeeTaxInvoiceAsync(It.IsAny<TaxDocument>()), Times.Never);
    }

    [Fact]
    public async Task ListingFeeInvoice_WithAnIntroOffer_ShowsTheStandardFeeOfferAndDiscount()
    {
        var offer = new IntroOffer { Name = "Launch 2026", Type = IntroOfferType.AmountOff, AmountOffIncGst = 495m,
            EndsAt = _clock.UtcNow.AddDays(30), CreatedByUserId = _owner.Id };
        _db.IntroOffers.Add(offer);
        await _db.SaveChangesAsync();
        var sub = await SubscriptionAsync(fee: 495m, offer: offer);

        await CreateSut().IssueListingFeeInvoiceAsync(sub, paid: true, userId: null);

        var doc = await _db.TaxDocuments.SingleAsync();
        (doc.StandardAmountIncGst, doc.IntroOfferName, doc.DiscountIncGst).Should().Be((990m, "Launch 2026", 495m));
        (doc.AmountIncGst, doc.AmountExGst, doc.GstAmount).Should().Be((495m, 450m, 45m));
    }

    [Fact]
    public async Task Numbers_AreSequentialAndZeroPadded()
    {
        _numbers.StartAt(123);

        var first = (await CreateSut().IssueListingFeeInvoiceAsync(await SubscriptionAsync(), true, null)).Document!;
        var second = (await CreateSut().IssueListingFeeInvoiceAsync(await SubscriptionAsync(), true, null)).Document!;

        first.Number.Should().Be("SA-000123");
        second.Number.Should().Be("SA-000124");
        TaxDocumentService.FormatNumber(1_234_567).Should().Be("SA-1234567");
    }

    [Fact]
    public async Task IssuingTwice_ReturnsTheSameDocument_AndSendsOneEmail()
    {
        var sub = await SubscriptionAsync();
        var first = await CreateSut().IssueListingFeeInvoiceAsync(sub, paid: true, userId: null);

        var second = await CreateSut().IssueListingFeeInvoiceAsync(sub, paid: true, userId: null);

        second.Status.Should().Be(TaxDocumentIssueStatus.Existing);
        second.Document!.Id.Should().Be(first.Document!.Id);
        (await _db.TaxDocuments.CountAsync()).Should().Be(1);
        _emails.Verify(e => e.ListingFeeTaxInvoiceAsync(It.IsAny<TaxDocument>()), Times.Once);
    }

    [Fact]
    public async Task Issue_LosingTheInsertRace_ReturnsTheWinnersDocument_AndSendsNoEmail()
    {
        var sub = await SubscriptionAsync();
        var winner = (await CreateSut().IssueListingFeeInvoiceAsync(sub, paid: true, userId: null)).Document!;
        _emails.Invocations.Clear();

        var r = await CreateSut(repo: new RacingRepository(_db)).IssueListingFeeInvoiceAsync(sub, paid: true, userId: null);

        r.Status.Should().Be(TaxDocumentIssueStatus.Existing);
        r.Document!.Id.Should().Be(winner.Id);
        (await _db.TaxDocuments.CountAsync()).Should().Be(1);
        _emails.Verify(e => e.ListingFeeTaxInvoiceAsync(It.IsAny<TaxDocument>()), Times.Never);
        (await _db.AuditLogs.CountAsync(a => a.Action == "TaxDocumentIssued")).Should().Be(1);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("abn")]
    [InlineData("address")]
    public async Task MissingBusinessDetails_IssueNothing(string missing)
    {
        var settings = _db.PlatformSettings.Single();
        if (missing == "name") settings.BusinessLegalName = " ";
        if (missing == "abn") settings.BusinessAbn = null;
        if (missing == "address") settings.BusinessAddress = null;
        await _db.SaveChangesAsync();

        var r = await CreateSut().IssueListingFeeInvoiceAsync(await SubscriptionAsync(), paid: true, userId: null);

        r.Status.Should().Be(TaxDocumentIssueStatus.BusinessDetailsMissing);
        (await _db.TaxDocuments.AnyAsync()).Should().BeFalse();
        _emails.Verify(e => e.ListingFeeTaxInvoiceAsync(It.IsAny<TaxDocument>()), Times.Never);
        (await CreateSut().CanIssueAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task AnUnpaidInvoice_NeedsTheBankDetails_ButAPaidOneDoesNot()
    {
        TaxTestData.CompleteBusinessDetails(_db, withBank: false);

        (await CreateSut().IssueListingFeeInvoiceAsync(await SubscriptionAsync(), paid: false, userId: null)).Status
            .Should().Be(TaxDocumentIssueStatus.BusinessDetailsMissing);
        (await CreateSut().IssueListingFeeInvoiceAsync(await SubscriptionAsync(), paid: true, userId: null)).Status
            .Should().Be(TaxDocumentIssueStatus.Issued);
        (await CreateSut().CanIssueAsync(unpaidInvoice: true)).Should().BeFalse();
        (await CreateSut().CanIssueAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task AZeroFee_NeedsNoDocument()
    {
        var r = await CreateSut().IssueListingFeeInvoiceAsync(await SubscriptionAsync(fee: 0m), paid: true, userId: null);

        r.Status.Should().Be(TaxDocumentIssueStatus.NotNeeded);
        (await _db.TaxDocuments.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task MarkPaid_MarksTheUnpaidInvoicePaid_AndSendsAReceipt()
    {
        var sub = await SubscriptionAsync();
        await CreateSut().IssueListingFeeInvoiceAsync(sub, paid: false, userId: null);
        _clock.Advance(TimeSpan.FromDays(3));

        var r = await CreateSut().MarkListingFeeInvoicePaidAsync(sub, _clock.UtcNow, _owner.Id);

        r.Status.Should().Be(TaxDocumentIssueStatus.MarkedPaid);
        var doc = await _db.TaxDocuments.SingleAsync();
        doc.Status.Should().Be(TaxDocumentStatus.Paid);
        doc.PaidAt.Should().Be(_clock.UtcNow);
        _emails.Verify(e => e.ListingFeeReceiptAsync(It.Is<TaxDocument>(d => d.Id == doc.Id)), Times.Once);
        (await _db.AuditLogs.CountAsync(a => a.EntityId == doc.Id && a.Action == "TaxInvoicePaid")).Should().Be(1);
    }

    [Fact]
    public async Task MarkPaid_WhenAnotherInstancePaidItFirst_IsIdempotent()
    {
        var sub = await SubscriptionAsync();
        await CreateSut().IssueListingFeeInvoiceAsync(sub, paid: false, userId: null);
        using var other = DbContextFactory.Create(_name);
        await other.TaxDocuments.SingleAsync();                                  // the second instance read it while Unpaid
        await CreateSut().MarkListingFeeInvoicePaidAsync(sub, _clock.UtcNow, null); // the first instance pays it

        var r = await CreateSut(other).MarkListingFeeInvoicePaidAsync(sub, _clock.UtcNow, null);

        r.Status.Should().Be(TaxDocumentIssueStatus.Existing);
        r.Document!.Status.Should().Be(TaxDocumentStatus.Paid);
        _emails.Verify(e => e.ListingFeeReceiptAsync(It.IsAny<TaxDocument>()), Times.Once);
    }

    [Fact]
    public async Task MarkPaid_WithNoInvoice_DoesNothing()
    {
        (await CreateSut().MarkListingFeeInvoicePaidAsync(await SubscriptionAsync(), _clock.UtcNow, null)).Status
            .Should().Be(TaxDocumentIssueStatus.NotNeeded);
    }

    [Fact]
    public async Task Settle_WithNoInvoice_IssuesAPaidOne_AndWithAnUnpaidOne_MarksItPaid()
    {
        var fresh = await SubscriptionAsync();
        (await CreateSut().SettleListingFeeAsync(fresh, _clock.UtcNow, null)).Status.Should().Be(TaxDocumentIssueStatus.Issued);
        _emails.Verify(e => e.ListingFeeTaxInvoiceAsync(It.IsAny<TaxDocument>()), Times.Once);

        var invoiced = await SubscriptionAsync();
        await CreateSut().IssueListingFeeInvoiceAsync(invoiced, paid: false, userId: null);
        (await CreateSut().SettleListingFeeAsync(invoiced, _clock.UtcNow, null)).Status.Should().Be(TaxDocumentIssueStatus.MarkedPaid);
        _emails.Verify(e => e.ListingFeeReceiptAsync(It.IsAny<TaxDocument>()), Times.Once);
        (await _db.TaxDocuments.CountAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData(false, "Buyer fee — nomination to Snitzel, 2026 Season (auction)")]
    [InlineData(true, "Buyer fee — nomination to Snitzel, 2026 Season (accepted offer)")]
    public async Task BuyerFeeInvoice_IsAPaidInvoiceToTheBuyer(bool fromOffer, string description)
    {
        var sale = await SaleAsync(fromOffer);

        var r = await CreateSut().IssueBuyerFeeInvoiceAsync(sale);

        r.Status.Should().Be(TaxDocumentIssueStatus.Issued);
        var doc = await _db.TaxDocuments.SingleAsync();
        doc.Kind.Should().Be(TaxDocumentKind.BuyerFeeInvoice);
        doc.Status.Should().Be(TaxDocumentStatus.Paid);
        doc.Description.Should().Be(description);
        (doc.AmountIncGst, doc.AmountExGst, doc.GstAmount).Should().Be((150m, 136.36m, 13.64m));
        (doc.RecipientName, doc.RecipientEmail).Should().Be(("Jane Buyer", "jane@example.com"));
        doc.BuyerUserId.Should().Be(_buyer.Id);
        doc.StudFarmId.Should().BeNull();
        doc.PurchaseId.Should().Be(sale.Id);
        _emails.Verify(e => e.BuyerFeeTaxInvoiceAsync(It.Is<TaxDocument>(d => d.Id == doc.Id)), Times.Once);
    }

    [Fact]
    public async Task AdjustmentNote_ReversesTheWholeInvoice_AndLinksIt()
    {
        var sale = await SaleAsync();
        var invoice = (await CreateSut().IssueBuyerFeeInvoiceAsync(sale)).Document!;
        _clock.Advance(TimeSpan.FromDays(1));

        var r = await CreateSut().IssueAdjustmentNoteAsync(invoice, "Buyer fee refunded — duplicate charge", _owner.Id);

        r.Status.Should().Be(TaxDocumentIssueStatus.Issued);
        var note = r.Document!;
        note.Number.Should().Be("SA-000002");
        note.Kind.Should().Be(TaxDocumentKind.AdjustmentNote);
        note.Status.Should().Be(TaxDocumentStatus.Paid);
        (note.AmountIncGst, note.AmountExGst, note.GstAmount).Should().Be((150m, 136.36m, 13.64m));
        note.OriginalDocumentId.Should().Be(invoice.Id);
        note.PurchaseId.Should().Be(sale.Id);
        note.BuyerUserId.Should().Be(_buyer.Id);
        (note.RecipientName, note.RecipientEmail).Should().Be(("Jane Buyer", "jane@example.com"));
        note.Reason.Should().Be("Buyer fee refunded — duplicate charge");
        note.IssuedAt.Should().Be(_clock.UtcNow);
        _emails.Verify(e => e.AdjustmentNoteAsync(It.Is<TaxDocument>(d => d.Id == note.Id),
            It.Is<TaxDocument>(d => d.Id == invoice.Id)), Times.Once);
    }

    [Fact]
    public async Task AdjustmentNote_Twice_ReturnsTheExistingNote()
    {
        var invoice = (await CreateSut().IssueBuyerFeeInvoiceAsync(await SaleAsync())).Document!;
        var first = await CreateSut().IssueAdjustmentNoteAsync(invoice, "Buyer fee refunded", null);

        var second = await CreateSut().IssueAdjustmentNoteAsync(invoice, "Buyer fee refunded", null);

        second.Status.Should().Be(TaxDocumentIssueStatus.Existing);
        second.Document!.Id.Should().Be(first.Document!.Id);
        _emails.Verify(e => e.AdjustmentNoteAsync(It.IsAny<TaxDocument>(), It.IsAny<TaxDocument>()), Times.Once);
    }

    [Fact]
    public async Task RecordPending_AuditsTaxDocumentPending()
    {
        var id = Guid.NewGuid();

        await CreateSut().RecordPendingAsync("Purchase", id, TaxDocumentKind.BuyerFeeInvoice);

        var audit = await _db.AuditLogs.SingleAsync(a => a.EntityId == id);
        (audit.EntityType, audit.Action).Should().Be(("Purchase", TaxDocumentService.PendingAction));
        audit.Details.Should().Contain("BuyerFeeInvoice");
    }

    [Fact]
    public async Task TheStudsInvoice_GoesToTheOwnersEmail_WhenTheFarmHasNoContactEmail()
    {
        _farm.ContactEmail = null;
        await _db.SaveChangesAsync();

        await CreateSut().IssueListingFeeInvoiceAsync(await SubscriptionAsync(), paid: true, userId: null);

        (await _db.TaxDocuments.SingleAsync()).RecipientEmail.Should().Be("owner@arrowfield.example");
    }
}
```

```csharp
// tests/Server.Tests/TaxDocuments/TaxDocumentEmailsTests.cs
using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Email;
using Stallions.Server.Options;
using Stallions.Server.TaxDocuments;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.TaxDocuments;

public class TaxDocumentEmailsTests
{
    private readonly List<(OutgoingEmail Email, string? EntityType, Guid? EntityId)> _queued = new();

    private TaxDocumentEmails CreateSut()
    {
        var outbox = new Mock<IEmailOutbox>();
        outbox.Setup(o => o.EnqueueAsync(It.IsAny<OutgoingEmail>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .Callback<OutgoingEmail, string?, Guid?>((e, t, id) => _queued.Add((e, t, id)))
            .Returns(Task.CompletedTask);
        return new TaxDocumentEmails(outbox.Object,
            Microsoft.Extensions.Options.Options.Create(new DisclosureOptions { BuyerFeeExplanation = "CONFIGURED fee wording." }),
            Microsoft.Extensions.Options.Options.Create(new EmailOptions { PublicBaseUrl = "https://app.example/" }));
    }

    private static readonly DateTime Issued = new(2026, 10, 11, 2, 0, 0, DateTimeKind.Utc);

    private static TaxDocument Invoice(TaxDocumentKind kind = TaxDocumentKind.ListingFeeInvoice) => new()
    {
        Id = Guid.NewGuid(), Number = "SA-000123", Kind = kind, Status = TaxDocumentStatus.Paid, IssuedAt = Issued,
        PaidAt = Issued, Description = "Listing fee — Dundeel, 2026 Season", AmountIncGst = 990m, AmountExGst = 900m,
        GstAmount = 90m, SupplierName = "Stallions Australia Pty Ltd", SupplierAbn = "12345678901",
        SupplierAddress = "1 Racecourse Road", RecipientName = "Arrowfield", RecipientEmail = "sales@arrowfield.example"
    };

    [Fact]
    public async Task ListingFeeTaxInvoice_CarriesTheKeyDetails_AndALink()
    {
        var doc = Invoice();

        await CreateSut().ListingFeeTaxInvoiceAsync(doc);

        var (email, type, id) = _queued.Single();
        email.Template.Should().Be("ListingFeeTaxInvoice");
        email.ToAddress.Should().Be("sales@arrowfield.example");
        email.Subject.Should().Contain("SA-000123");
        email.TextBody.Should().Contain("Tax invoice: SA-000123").And.Contain("Date: 11 Oct 2026")
            .And.Contain("Stallions Australia Pty Ltd, ABN 12 345 678 901").And.Contain("Amount (excl. GST): $900.00")
            .And.Contain("GST: $90.00").And.Contain("Total (incl. GST): $990.00")
            .And.Contain($"https://app.example/tax-documents/{doc.Id}");
        (type, id).Should().Be(("TaxDocument", doc.Id));
    }

    [Fact]
    public async Task ListingFeeTaxInvoice_WithAnIntroOffer_ShowsTheDiscount()
    {
        var doc = Invoice();
        doc.StandardAmountIncGst = 990m;
        doc.IntroOfferName = "Launch 2026";
        doc.DiscountIncGst = 495m;
        doc.AmountIncGst = 495m;

        await CreateSut().ListingFeeTaxInvoiceAsync(doc);

        _queued.Single().Email.TextBody.Should().Contain("Standard listing fee: $990.00")
            .And.Contain("Intro offer: Launch 2026: −$495.00");
    }

    [Fact]
    public async Task InvoiceDue_ShowsTheDueDate_BankDetails_AndPaymentReference()
    {
        var doc = Invoice();
        doc.Status = TaxDocumentStatus.Unpaid;
        doc.PaidAt = null;
        doc.DueAt = Issued.AddDays(14);
        doc.BankAccountName = "Stallions Australia Pty Ltd";
        doc.BankBsb = "062000";
        doc.BankAccountNumber = "12345678";

        await CreateSut().ListingFeeInvoiceDueAsync(doc);

        var email = _queued.Single().Email;
        email.Template.Should().Be("ListingFeeInvoiceDue");
        email.Subject.Should().Be("Tax invoice SA-000123 — amount due $990.00 by 25 Oct 2026");
        email.TextBody.Should().Contain("Account name: Stallions Australia Pty Ltd").And.Contain("BSB: 062-000")
            .And.Contain("Account number: 12345678").And.Contain("Please use SA-000123 as your payment reference.");
    }

    [Fact]
    public async Task Receipt_SaysThePaymentWasReceived()
    {
        await CreateSut().ListingFeeReceiptAsync(Invoice());

        var email = _queued.Single().Email;
        email.Template.Should().Be("ListingFeeReceipt");
        email.TextBody.Should().Contain("We've received your payment of $990.00 for tax invoice SA-000123");
    }

    [Fact]
    public async Task BuyerFeeTaxInvoice_IncludesTheConfiguredWording()
    {
        var doc = Invoice(TaxDocumentKind.BuyerFeeInvoice);
        doc.RecipientEmail = "jane@example.com";

        await CreateSut().BuyerFeeTaxInvoiceAsync(doc);

        var email = _queued.Single().Email;
        email.Template.Should().Be("BuyerFeeTaxInvoice");
        email.ToAddress.Should().Be("jane@example.com");
        email.TextBody.Should().Contain("CONFIGURED fee wording.");
    }

    [Fact]
    public async Task AdjustmentNote_ReferencesTheOriginalInvoice()
    {
        var original = Invoice(TaxDocumentKind.BuyerFeeInvoice);
        var note = Invoice(TaxDocumentKind.AdjustmentNote);
        note.Number = "SA-000130";
        note.Reason = "Buyer fee refunded — duplicate charge";

        await CreateSut().AdjustmentNoteAsync(note, original);

        var email = _queued.Single().Email;
        email.Template.Should().Be("AdjustmentNote");
        email.Subject.Should().Be("Adjustment note SA-000130 for tax invoice SA-000123");
        email.TextBody.Should().Contain("Adjustment note: SA-000130")
            .And.Contain("This adjustment note reverses tax invoice SA-000123 dated 11 Oct 2026 in full.")
            .And.Contain("Reason: Buyer fee refunded — duplicate charge");
    }

    [Fact]
    public async Task RecipientText_IsHtmlEncoded()
    {
        var doc = Invoice();
        doc.RecipientName = "<b>Arrowfield</b>";

        await CreateSut().ListingFeeTaxInvoiceAsync(doc);

        _queued.Single().Email.HtmlBody.Should().Contain("&lt;b&gt;Arrowfield&lt;/b&gt;").And.NotContain("<b>Arrowfield</b>");
    }
}
```

- [ ] **Step 3: Run red** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~TaxDocument"` → compile errors.

- [ ] **Step 4: Unique-violation helper and repository**

```csharp
// src/Server/Data/DbErrors.cs
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Stallions.Server.Data;

public static class DbErrors
{
    /// <summary>SQL Server: 2601 = duplicate key in a unique index, 2627 = unique constraint violation.</summary>
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
```

```csharp
// src/Server/Data/Repositories/ITaxDocumentRepository.cs
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public interface ITaxDocumentRepository
{
    /// <summary>With OriginalDocument.</summary>
    Task<TaxDocument?> GetByIdAsync(Guid id);
    Task<TaxDocument?> GetListingFeeInvoiceAsync(Guid subscriptionId);
    Task<TaxDocument?> GetBuyerFeeInvoiceAsync(Guid purchaseId);
    Task<TaxDocument?> GetAdjustmentNoteForAsync(Guid originalDocumentId);
    Task<IReadOnlyList<TaxDocument>> GetAdjustmentNotesForAsync(IEnumerable<Guid> originalDocumentIds);
    /// <summary>Every document (any kind) for these subscriptions.</summary>
    Task<IReadOnlyList<TaxDocument>> GetBySubscriptionIdsAsync(IEnumerable<Guid> subscriptionIds);
    /// <summary>Every document (any kind) for these sale records.</summary>
    Task<IReadOnlyList<TaxDocument>> GetByPurchaseIdsAsync(IEnumerable<Guid> purchaseIds);
    /// <summary>The buyer's documents, newest first, with OriginalDocument.</summary>
    Task<IReadOnlyList<TaxDocument>> GetForBuyerAsync(Guid buyerUserId);
    /// <summary>The stud farm's documents, newest first, with OriginalDocument.</summary>
    Task<IReadOnlyList<TaxDocument>> GetForStudFarmAsync(Guid studFarmId);
    /// <summary>Newest first, with OriginalDocument. issuedFromUtc inclusive, issuedBeforeUtc exclusive.</summary>
    Task<IReadOnlyList<TaxDocument>> SearchAsync(TaxDocumentKind? kind, TaxDocumentStatus? status,
        DateTime? issuedFromUtc, DateTime? issuedBeforeUtc);
    /// <summary>Inserts the document; false (and no longer tracked) when a unique index says it already exists.</summary>
    Task<bool> TryAddAsync(TaxDocument document);
    /// <summary>Saves a status change; false (and no longer tracked) when another instance changed it first.</summary>
    Task<bool> TryUpdateAsync(TaxDocument document);
}
```

```csharp
// src/Server/Data/Repositories/TaxDocumentRepository.cs
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public class TaxDocumentRepository : ITaxDocumentRepository
{
    private readonly AppDbContext _db;
    public TaxDocumentRepository(AppDbContext db) => _db = db;

    private IQueryable<TaxDocument> WithOriginal() => _db.TaxDocuments.Include(d => d.OriginalDocument);

    public async Task<TaxDocument?> GetByIdAsync(Guid id) =>
        await WithOriginal().FirstOrDefaultAsync(d => d.Id == id);

    public virtual async Task<TaxDocument?> GetListingFeeInvoiceAsync(Guid subscriptionId) =>
        await _db.TaxDocuments.FirstOrDefaultAsync(d =>
            d.SubscriptionId == subscriptionId && d.Kind == TaxDocumentKind.ListingFeeInvoice);

    public virtual async Task<TaxDocument?> GetBuyerFeeInvoiceAsync(Guid purchaseId) =>
        await _db.TaxDocuments.FirstOrDefaultAsync(d =>
            d.PurchaseId == purchaseId && d.Kind == TaxDocumentKind.BuyerFeeInvoice);

    public virtual async Task<TaxDocument?> GetAdjustmentNoteForAsync(Guid originalDocumentId) =>
        await _db.TaxDocuments.FirstOrDefaultAsync(d =>
            d.OriginalDocumentId == originalDocumentId && d.Kind == TaxDocumentKind.AdjustmentNote);

    public async Task<IReadOnlyList<TaxDocument>> GetAdjustmentNotesForAsync(IEnumerable<Guid> originalDocumentIds)
    {
        var ids = originalDocumentIds.ToList();
        return await _db.TaxDocuments
            .Where(d => d.Kind == TaxDocumentKind.AdjustmentNote && d.OriginalDocumentId != null
                        && ids.Contains(d.OriginalDocumentId.Value))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<TaxDocument>> GetBySubscriptionIdsAsync(IEnumerable<Guid> subscriptionIds)
    {
        var ids = subscriptionIds.ToList();
        return await _db.TaxDocuments
            .Where(d => d.SubscriptionId != null && ids.Contains(d.SubscriptionId.Value))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<TaxDocument>> GetByPurchaseIdsAsync(IEnumerable<Guid> purchaseIds)
    {
        var ids = purchaseIds.ToList();
        return await _db.TaxDocuments
            .Where(d => d.PurchaseId != null && ids.Contains(d.PurchaseId.Value))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<TaxDocument>> GetForBuyerAsync(Guid buyerUserId) =>
        await WithOriginal().Where(d => d.BuyerUserId == buyerUserId)
            .OrderByDescending(d => d.IssuedAt).ThenByDescending(d => d.Number).ToListAsync();

    public async Task<IReadOnlyList<TaxDocument>> GetForStudFarmAsync(Guid studFarmId) =>
        await WithOriginal().Where(d => d.StudFarmId == studFarmId)
            .OrderByDescending(d => d.IssuedAt).ThenByDescending(d => d.Number).ToListAsync();

    public async Task<IReadOnlyList<TaxDocument>> SearchAsync(TaxDocumentKind? kind, TaxDocumentStatus? status,
        DateTime? issuedFromUtc, DateTime? issuedBeforeUtc)
    {
        var q = WithOriginal();
        if (kind.HasValue) q = q.Where(d => d.Kind == kind.Value);
        if (status.HasValue) q = q.Where(d => d.Status == status.Value);
        if (issuedFromUtc.HasValue) q = q.Where(d => d.IssuedAt >= issuedFromUtc.Value);
        if (issuedBeforeUtc.HasValue) q = q.Where(d => d.IssuedAt < issuedBeforeUtc.Value);
        return await q.OrderByDescending(d => d.IssuedAt).ThenByDescending(d => d.Number).ToListAsync();
    }

    public virtual async Task<bool> TryAddAsync(TaxDocument document)
    {
        _db.TaxDocuments.Add(document);
        try
        {
            await _db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // Inside the caller's transaction EF rolled back to its savepoint; forget the rejected row.
            _db.Entry(document).State = EntityState.Detached;
            return false;
        }
    }

    public virtual async Task<bool> TryUpdateAsync(TaxDocument document)
    {
        try
        {
            _db.TaxDocuments.Update(document);
            await _db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.Entry(document).State = EntityState.Detached;
            return false;
        }
    }
}
```

- [ ] **Step 5: Number source, service and emails**

```csharp
// src/Server/TaxDocuments/TaxDocumentNumberSource.cs
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data;

namespace Stallions.Server.TaxDocuments;

/// <summary>The next tax document number. Tests use a counter (the in-memory provider has no sequences).</summary>
public interface ITaxDocumentNumberSource
{
    Task<long> NextAsync();
}

/// <summary>
/// SQL sequence dbo.TaxDocumentNumbers: safe across app instances. Runs on the context's connection, so it
/// joins the caller's transaction; a rolled-back transaction leaves a gap, which is acceptable.
/// </summary>
public class SqlTaxDocumentNumberSource : ITaxDocumentNumberSource
{
    private readonly AppDbContext _db;
    public SqlTaxDocumentNumberSource(AppDbContext db) => _db = db;

    // ToListAsync (not SingleAsync) so EF sends the SQL as written: NEXT VALUE FOR can't sit in a subquery.
    public async Task<long> NextAsync() =>
        (await _db.Database.SqlQuery<long>($"SELECT NEXT VALUE FOR [dbo].[TaxDocumentNumbers] AS [Value]").ToListAsync()).Single();
}
```

```csharp
// src/Server/TaxDocuments/TaxDocumentService.cs
using System.Text.Json;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared;
using Stallions.Shared.Enums;

namespace Stallions.Server.TaxDocuments;

public enum TaxDocumentIssueStatus
{
    /// <summary>A new document was issued and emailed.</summary>
    Issued,
    /// <summary>An Unpaid invoice was marked Paid and a receipt emailed.</summary>
    MarkedPaid,
    /// <summary>The document already existed (or was already Paid): nothing new was sent.</summary>
    Existing,
    /// <summary>Legal name, ABN or address (or, for an unpaid invoice, the bank details) are not set.</summary>
    BusinessDetailsMissing,
    /// <summary>Nothing to issue (a $0 fee, or no invoice to mark paid).</summary>
    NotNeeded
}

public sealed record TaxDocumentIssueResult(TaxDocumentIssueStatus Status, TaxDocument? Document)
{
    public static TaxDocumentIssueResult Missing { get; } = new(TaxDocumentIssueStatus.BusinessDetailsMissing, null);
    public static TaxDocumentIssueResult NotNeeded { get; } = new(TaxDocumentIssueStatus.NotNeeded, null);
}

/// <summary>
/// Issues tax invoices and adjustment notes. Every method runs inside the caller's transaction (it never
/// starts one), so the document, its audit row, its email and the money state commit together. Each issue
/// is idempotent: an existing document is returned and nothing new is sent.
/// </summary>
public interface ITaxDocumentService
{
    /// <summary>Legal name, ABN and address are set (and, for an unpaid invoice, the bank details).</summary>
    Task<bool> CanIssueAsync(bool unpaidInvoice = false);

    /// <summary>The subscription must have Stallion, Season and StudFarm loaded.</summary>
    Task<TaxDocumentIssueResult> IssueListingFeeInvoiceAsync(StallionSeasonSubscription subscription, bool paid,
        Guid? userId, DateTime? paidAt = null);

    /// <summary>Marks the subscription's Unpaid invoice Paid and emails a receipt; NotNeeded if it has no invoice.</summary>
    Task<TaxDocumentIssueResult> MarkListingFeeInvoicePaidAsync(StallionSeasonSubscription subscription,
        DateTime paidAt, Guid? userId);

    /// <summary>The listing fee was paid (card or Staff): mark the Unpaid invoice Paid, or issue a Paid one.</summary>
    Task<TaxDocumentIssueResult> SettleListingFeeAsync(StallionSeasonSubscription subscription, DateTime paidAt, Guid? userId);

    /// <summary>The purchase must have Buyer and the listing's Stallion and Season loaded.</summary>
    Task<TaxDocumentIssueResult> IssueBuyerFeeInvoiceAsync(Purchase purchase);

    /// <summary>Reverses the original invoice in full.</summary>
    Task<TaxDocumentIssueResult> IssueAdjustmentNoteAsync(TaxDocument original, string reason, Guid? userId);

    /// <summary>An automatic issue couldn't happen (business details incomplete): log and audit TaxDocumentPending.</summary>
    Task RecordPendingAsync(string entityType, Guid entityId, TaxDocumentKind kind);
}

public class TaxDocumentService : ITaxDocumentService
{
    public const string PendingAction = "TaxDocumentPending";
    public const string BusinessDetailsMissingMessage = "Complete the business details in Settings first.";
    private const string AuditEntity = "TaxDocument";

    private readonly ITaxDocumentRepository _documents;
    private readonly ITaxDocumentNumberSource _numbers;
    private readonly IPlatformSettingsRepository _settings;
    private readonly IStudFarmRepository _farms;
    private readonly IUserRepository _users;
    private readonly IIntroOfferRepository _offers;
    private readonly ITaxDocumentEmails _emails;
    private readonly IAuditLogRepository _audit;
    private readonly TimeProvider _clock;
    private readonly ILogger<TaxDocumentService> _log;

    public TaxDocumentService(ITaxDocumentRepository documents, ITaxDocumentNumberSource numbers,
        IPlatformSettingsRepository settings, IStudFarmRepository farms, IUserRepository users, IIntroOfferRepository offers,
        ITaxDocumentEmails emails, IAuditLogRepository audit, TimeProvider clock, ILogger<TaxDocumentService> log)
    {
        _documents = documents; _numbers = numbers; _settings = settings; _farms = farms; _users = users;
        _offers = offers; _emails = emails; _audit = audit; _clock = clock; _log = log;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public static string FormatNumber(long value) => $"SA-{value:D6}";

    public async Task<bool> CanIssueAsync(bool unpaidInvoice = false)
    {
        var settings = await _settings.GetAsync();
        return settings.HasBusinessDetails && (!unpaidInvoice || settings.HasBankDetails);
    }

    public async Task<TaxDocumentIssueResult> IssueListingFeeInvoiceAsync(StallionSeasonSubscription subscription,
        bool paid, Guid? userId, DateTime? paidAt = null)
    {
        if (subscription.FeeIncGst <= 0) return TaxDocumentIssueResult.NotNeeded;
        if (await _documents.GetListingFeeInvoiceAsync(subscription.Id) is { } existing)
            return new TaxDocumentIssueResult(TaxDocumentIssueStatus.Existing, existing);

        var settings = await _settings.GetAsync();
        if (!settings.HasBusinessDetails || (!paid && !settings.HasBankDetails)) return TaxDocumentIssueResult.Missing;

        var now = Now;
        var farm = subscription.StudFarm ?? await _farms.GetByIdAsync(subscription.StudFarmId)
                   ?? throw new InvalidOperationException($"Stud farm {subscription.StudFarmId} not found.");
        var document = NewDocument(TaxDocumentKind.ListingFeeInvoice, settings, now, subscription.FeeIncGst);
        document.Description = Truncate(
            $"Listing fee — {subscription.Stallion?.Name ?? "stallion"}, {subscription.Season?.Name ?? "season"}", 300);
        document.SubscriptionId = subscription.Id;
        document.StudFarmId = farm.Id;
        document.RecipientName = farm.Name;
        document.RecipientEmail = await StudEmailAsync(farm);
        document.RecipientAbn = string.IsNullOrWhiteSpace(farm.ABN) ? null : farm.ABN.Trim();
        document.RecipientAddress = string.IsNullOrWhiteSpace(farm.Address) ? null : farm.Address.Trim();

        if (subscription.IntroOfferId is { } offerId && await _offers.GetByIdAsync(offerId) is { } offer)
        {
            document.StandardAmountIncGst = subscription.StandardFeeIncGst;
            document.IntroOfferName = offer.Name;
            document.DiscountIncGst = subscription.StandardFeeIncGst - subscription.FeeIncGst;
        }

        if (paid)
        {
            document.Status = TaxDocumentStatus.Paid;
            document.PaidAt = paidAt ?? subscription.PaidAt ?? now;
        }
        else
        {
            document.Status = TaxDocumentStatus.Unpaid;
            document.DueAt = now.AddDays(settings.InvoicePaymentTermsDays);
            document.BankAccountName = settings.BankAccountName;
            document.BankBsb = settings.BankBsb;
            document.BankAccountNumber = settings.BankAccountNumber;
        }

        var result = await AddAsync(document, () => _documents.GetListingFeeInvoiceAsync(subscription.Id), userId);
        if (result.Status == TaxDocumentIssueStatus.Issued)
        {
            if (paid) await _emails.ListingFeeTaxInvoiceAsync(document);
            else await _emails.ListingFeeInvoiceDueAsync(document);
        }
        return result;
    }

    public async Task<TaxDocumentIssueResult> MarkListingFeeInvoicePaidAsync(StallionSeasonSubscription subscription,
        DateTime paidAt, Guid? userId)
    {
        var invoice = await _documents.GetListingFeeInvoiceAsync(subscription.Id);
        if (invoice == null) return TaxDocumentIssueResult.NotNeeded;
        if (invoice.Status == TaxDocumentStatus.Paid) return new TaxDocumentIssueResult(TaxDocumentIssueStatus.Existing, invoice);

        invoice.Status = TaxDocumentStatus.Paid;
        invoice.PaidAt = paidAt;
        if (!await _documents.TryUpdateAsync(invoice))
        {
            // Staff and the card webhook paid it at the same time: the other one won; it is Paid now.
            _log.LogInformation("Tax invoice {Number} was marked paid concurrently; keeping the first", invoice.Number);
            return new TaxDocumentIssueResult(TaxDocumentIssueStatus.Existing,
                await _documents.GetListingFeeInvoiceAsync(subscription.Id));
        }

        await _audit.LogAsync(AuditEntity, invoice.Id, "TaxInvoicePaid", userId,
            JsonSerializer.Serialize(new { invoice.Number, invoice.PaidAt, invoice.AmountIncGst }));
        await _emails.ListingFeeReceiptAsync(invoice);
        return new TaxDocumentIssueResult(TaxDocumentIssueStatus.MarkedPaid, invoice);
    }

    public async Task<TaxDocumentIssueResult> SettleListingFeeAsync(StallionSeasonSubscription subscription,
        DateTime paidAt, Guid? userId)
    {
        var marked = await MarkListingFeeInvoicePaidAsync(subscription, paidAt, userId);
        return marked.Status == TaxDocumentIssueStatus.NotNeeded
            ? await IssueListingFeeInvoiceAsync(subscription, paid: true, userId, paidAt)
            : marked;
    }

    public async Task<TaxDocumentIssueResult> IssueBuyerFeeInvoiceAsync(Purchase purchase)
    {
        if (purchase.BuyerFeeIncGst <= 0) return TaxDocumentIssueResult.NotNeeded;
        if (await _documents.GetBuyerFeeInvoiceAsync(purchase.Id) is { } existing)
            return new TaxDocumentIssueResult(TaxDocumentIssueStatus.Existing, existing);

        var settings = await _settings.GetAsync();
        if (!settings.HasBusinessDetails) return TaxDocumentIssueResult.Missing;

        var now = Now;
        var buyer = purchase.Buyer ?? await _users.GetByIdAsync(purchase.BuyerUserId)
                    ?? throw new InvalidOperationException($"Buyer {purchase.BuyerUserId} not found.");
        var listing = purchase.Listing;
        var source = purchase.OfferId != null ? "accepted offer" : "auction";
        var document = NewDocument(TaxDocumentKind.BuyerFeeInvoice, settings, now, purchase.BuyerFeeIncGst);
        document.Description = Truncate(
            $"Buyer fee — nomination to {listing?.Stallion?.Name ?? "stallion"}, {listing?.Season?.Name ?? "season"} ({source})", 300);
        document.PurchaseId = purchase.Id;
        document.BuyerUserId = buyer.Id;
        document.RecipientName = buyer.DisplayName;
        document.RecipientEmail = buyer.Email;
        document.Status = TaxDocumentStatus.Paid;
        document.PaidAt = purchase.PaidAt ?? now;

        var result = await AddAsync(document, () => _documents.GetBuyerFeeInvoiceAsync(purchase.Id), userId: null);
        if (result.Status == TaxDocumentIssueStatus.Issued) await _emails.BuyerFeeTaxInvoiceAsync(document);
        return result;
    }

    public async Task<TaxDocumentIssueResult> IssueAdjustmentNoteAsync(TaxDocument original, string reason, Guid? userId)
    {
        if (original.Kind == TaxDocumentKind.AdjustmentNote)
            throw new ArgumentException("An adjustment note can't itself be adjusted.", nameof(original));
        if (await _documents.GetAdjustmentNoteForAsync(original.Id) is { } existing)
            return new TaxDocumentIssueResult(TaxDocumentIssueStatus.Existing, existing);

        var settings = await _settings.GetAsync();
        if (!settings.HasBusinessDetails) return TaxDocumentIssueResult.Missing;

        var now = Now;
        var note = NewDocument(TaxDocumentKind.AdjustmentNote, settings, now, original.AmountIncGst);
        note.Description = Truncate($"Adjustment to {original.Number}: {original.Description}", 300);
        note.Status = TaxDocumentStatus.Paid;
        note.PaidAt = now;
        note.SubscriptionId = original.SubscriptionId;
        note.PurchaseId = original.PurchaseId;
        note.OriginalDocumentId = original.Id;
        note.Reason = Truncate(reason.Trim(), 600);
        note.StudFarmId = original.StudFarmId;
        note.BuyerUserId = original.BuyerUserId;
        note.RecipientName = original.RecipientName;
        note.RecipientEmail = original.RecipientEmail;
        note.RecipientAbn = original.RecipientAbn;
        note.RecipientAddress = original.RecipientAddress;

        var result = await AddAsync(note, () => _documents.GetAdjustmentNoteForAsync(original.Id), userId);
        if (result.Status == TaxDocumentIssueStatus.Issued) await _emails.AdjustmentNoteAsync(note, original);
        return result;
    }

    public async Task RecordPendingAsync(string entityType, Guid entityId, TaxDocumentKind kind)
    {
        _log.LogError("No {Kind} was issued for {EntityType} {EntityId}: the business details are incomplete. " +
                      "Staff can issue it from Invoices once Settings are complete.", kind, entityType, entityId);
        await _audit.LogAsync(entityType, entityId, PendingAction, null, JsonSerializer.Serialize(new { Kind = kind.ToString() }));
    }

    /// <summary>Numbers and inserts the document; a unique-index hit means another instance issued it first.</summary>
    private async Task<TaxDocumentIssueResult> AddAsync(TaxDocument document, Func<Task<TaxDocument?>> findExisting, Guid? userId)
    {
        document.Number = FormatNumber(await _numbers.NextAsync());
        if (!await _documents.TryAddAsync(document))
        {
            var winner = await findExisting()
                ?? throw new InvalidOperationException($"Tax document {document.Number} hit a unique index but no existing document was found.");
            _log.LogInformation("{Kind} was issued concurrently as {Number}; number {Unused} not used",
                document.Kind, winner.Number, document.Number);
            return new TaxDocumentIssueResult(TaxDocumentIssueStatus.Existing, winner);
        }

        await _audit.LogAsync(AuditEntity, document.Id, "TaxDocumentIssued", userId, JsonSerializer.Serialize(new
        {
            document.Number, Kind = document.Kind.ToString(), Status = document.Status.ToString(),
            document.AmountIncGst, document.AmountExGst, document.GstAmount,
            document.SubscriptionId, document.PurchaseId, document.OriginalDocumentId
        }));
        _log.LogInformation("Issued {Kind} {Number} for {AmountIncGst}", document.Kind, document.Number, document.AmountIncGst);
        return new TaxDocumentIssueResult(TaxDocumentIssueStatus.Issued, document);
    }

    private static TaxDocument NewDocument(TaxDocumentKind kind, PlatformSettings settings, DateTime now, decimal amountIncGst)
    {
        var amount = GstBreakdown.FromIncGst(amountIncGst);
        return new TaxDocument
        {
            Kind = kind,
            IssuedAt = now,
            AmountIncGst = amount.IncGst,
            AmountExGst = amount.ExGst,
            GstAmount = amount.Gst,
            SupplierName = settings.BusinessLegalName!.Trim(),
            SupplierAbn = settings.BusinessAbn!,
            SupplierAddress = settings.BusinessAddress!.Trim(),
            SupplierEmail = string.IsNullOrWhiteSpace(settings.BusinessEmail) ? null : settings.BusinessEmail.Trim()
        };
    }

    // The stud's contact email, falling back to the stud admin's own email.
    private async Task<string> StudEmailAsync(StudFarm farm)
    {
        if (!string.IsNullOrWhiteSpace(farm.ContactEmail)) return farm.ContactEmail.Trim();
        var owner = farm.User ?? await _users.GetByIdAsync(farm.UserId);
        if (string.IsNullOrWhiteSpace(owner?.Email))
            _log.LogWarning("Stud farm {StudFarmId} has no contact or owner email for its tax invoice", farm.Id);
        return owner?.Email ?? string.Empty;
    }

    private static string Truncate(string value, int max) => value.Length > max ? value[..max] : value;
}
```

```csharp
// src/Server/TaxDocuments/TaxDocumentEmails.cs
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;
using Stallions.Server.Data.Entities;
using Stallions.Server.Email;
using Stallions.Server.Infrastructure;
using Stallions.Server.Options;
using Stallions.Shared;
using Stallions.Shared.Enums;

namespace Stallions.Server.TaxDocuments;

/// <summary>The tax document emails, queued through the outbox in the caller's transaction.</summary>
public interface ITaxDocumentEmails
{
    /// <summary>To the stud: a Paid listing fee invoice (card payment or Staff mark paid).</summary>
    Task ListingFeeTaxInvoiceAsync(TaxDocument invoice);
    /// <summary>To the stud: an Unpaid listing fee invoice, with the due date and bank details.</summary>
    Task ListingFeeInvoiceDueAsync(TaxDocument invoice);
    /// <summary>To the stud: their Unpaid invoice has been paid.</summary>
    Task ListingFeeReceiptAsync(TaxDocument invoice);
    /// <summary>To the buyer: the buyer fee tax invoice.</summary>
    Task BuyerFeeTaxInvoiceAsync(TaxDocument invoice);
    /// <summary>To the original invoice's recipient.</summary>
    Task AdjustmentNoteAsync(TaxDocument note, TaxDocument original);
}

/// <summary>
/// Plain, factual wording (Staff-editable wording comes in 5b); amounts inc. GST with the ex-GST and GST split;
/// dates as Sydney dates. Buyer-fee policy wording comes from DisclosureOptions.
/// </summary>
public class TaxDocumentEmails : ITaxDocumentEmails
{
    private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");

    private readonly IEmailOutbox _outbox;
    private readonly DisclosureOptions _disclosure;
    private readonly string _baseUrl;

    public TaxDocumentEmails(IEmailOutbox outbox, IOptions<DisclosureOptions> disclosure, IOptions<EmailOptions> email)
    {
        _outbox = outbox;
        _disclosure = disclosure.Value;
        _baseUrl = email.Value.PublicBaseUrl.TrimEnd('/');
    }

    public Task ListingFeeTaxInvoiceAsync(TaxDocument d) =>
        QueueAsync(d, "ListingFeeTaxInvoice", $"Tax invoice {d.Number}: {d.Description}",
            Para("Thank you — your listing fee has been paid. Your tax invoice is below."),
            Details(d),
            Para("Status: Paid"),
            DocumentLink(d, "View or print your tax invoice"));

    public Task ListingFeeInvoiceDueAsync(TaxDocument d) =>
        QueueAsync(d, "ListingFeeInvoiceDue", $"Tax invoice {d.Number} — amount due {Money(d.AmountIncGst)} by {Date(d.DueAt)}",
            Para($"Here is your tax invoice for the listing fee. Please pay {Money(d.AmountIncGst)} by {Date(d.DueAt)}."),
            Details(d),
            Rows(("Account name", d.BankAccountName ?? string.Empty),
                 ("BSB", BusinessNumbers.FormatBsb(d.BankBsb)),
                 ("Account number", d.BankAccountNumber ?? string.Empty)),
            Para($"Please use {d.Number} as your payment reference."),
            DocumentLink(d, "View or print your tax invoice"));

    public Task ListingFeeReceiptAsync(TaxDocument d) =>
        QueueAsync(d, "ListingFeeReceipt", $"Receipt: tax invoice {d.Number} paid",
            Para($"We've received your payment of {Money(d.AmountIncGst)} for tax invoice {d.Number}. Thank you."),
            Details(d),
            Para($"Paid: {Date(d.PaidAt)}"),
            DocumentLink(d, "View or print your tax invoice"));

    public Task BuyerFeeTaxInvoiceAsync(TaxDocument d) =>
        QueueAsync(d, "BuyerFeeTaxInvoice", $"Tax invoice {d.Number}: buyer fee",
            Para("The buyer fee has been charged to your saved card. Your tax invoice is below."),
            Details(d),
            Para(_disclosure.BuyerFeeExplanation),
            DocumentLink(d, "View or print your tax invoice"));

    public Task AdjustmentNoteAsync(TaxDocument note, TaxDocument original) =>
        QueueAsync(note, "AdjustmentNote", $"Adjustment note {note.Number} for tax invoice {original.Number}",
            Para($"This adjustment note reverses tax invoice {original.Number} dated {Date(original.IssuedAt)} in full."),
            Para($"Reason: {note.Reason}"),
            Details(note),
            DocumentLink(note, "View or print your adjustment note"));

    // ── Building blocks ────────────────────────────────────────────────────────

    private sealed record Block(string Html, string Text);

    private async Task QueueAsync(TaxDocument d, string template, string subject, params Block[] blocks)
    {
        var html = "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\">"
                   + string.Concat(blocks.Select(b => b.Html))
                   + "<p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>";
        var text = string.Join("\n\n", blocks.Select(b => b.Text)) + "\n\nStallions Australia";
        var trimmedSubject = subject.Length > 300 ? subject[..300] : subject;
        await _outbox.EnqueueAsync(new OutgoingEmail(d.RecipientEmail, trimmedSubject, html, text, template), "TaxDocument", d.Id);
    }

    private static Block Para(string text) => new($"<p>{Enc(text)}</p>", text);

    private Block DocumentLink(TaxDocument d, string label)
    {
        var url = $"{_baseUrl}/tax-documents/{d.Id}";
        return new($"<p><a href=\"{Enc(url)}\">{Enc(label)}</a></p>", $"{label}: {url}");
    }

    /// <summary>The document's key details: heading and number, date, supplier and ABN, recipient, amounts.</summary>
    private static Block Details(TaxDocument d)
    {
        var rows = new List<(string, string)>
        {
            (d.Kind == TaxDocumentKind.AdjustmentNote ? "Adjustment note" : "Tax invoice", d.Number),
            ("Date", Date(d.IssuedAt)),
            ("Supplier", $"{d.SupplierName}, ABN {BusinessNumbers.FormatAbn(d.SupplierAbn)}"),
            ("To", d.RecipientName),
            ("Description", d.Description)
        };
        if (d.StandardAmountIncGst is { } standard && d.DiscountIncGst is { } discount)
        {
            rows.Add(("Standard listing fee", Money(standard)));
            rows.Add(($"Intro offer: {d.IntroOfferName}", "−" + Money(discount)));
        }
        rows.Add(("Amount (excl. GST)", Money(d.AmountExGst)));
        rows.Add(("GST", Money(d.GstAmount)));
        rows.Add(("Total (incl. GST)", Money(d.AmountIncGst)));
        return Rows(rows.ToArray());
    }

    private static Block Rows(params (string Label, string Value)[] rows)
    {
        var html = "<table style=\"border-collapse:collapse\">"
                   + string.Concat(rows.Select(r =>
                       $"<tr><td style=\"padding:4px 16px 4px 0\">{Enc(r.Label)}</td><td style=\"padding:4px 0;text-align:right\"><strong>{Enc(r.Value)}</strong></td></tr>"))
                   + "</table>";
        var text = string.Join("\n", rows.Select(r => $"{r.Label}: {r.Value}"));
        return new(html, text);
    }

    private static string Money(decimal amount) => amount.ToString("C2", Au);
    private static string Date(DateTime? utc) => utc is { } v ? SydneyTime.Today(v).ToString("d MMM yyyy", Au) : string.Empty;
    private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
```

`src/Server/Program.cs` — add `using Stallions.Server.TaxDocuments;`; after the `IIntroOfferRepository` registration add
`builder.Services.AddScoped<ITaxDocumentRepository, TaxDocumentRepository>();` and
`builder.Services.AddScoped<ITaxDocumentNumberSource, SqlTaxDocumentNumberSource>();`; after the `IIntroOfferService`
registration add `builder.Services.AddScoped<ITaxDocumentEmails, TaxDocumentEmails>();` and
`builder.Services.AddScoped<ITaxDocumentService, TaxDocumentService>();`.

- [ ] **Step 6: Run green** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~TaxDocument"`; full build (0 warnings) and full test run.

- [ ] **Step 7: Commit**

```bash
git add src/Server/Data/DbErrors.cs src/Server/Data/Repositories/ITaxDocumentRepository.cs src/Server/Data/Repositories/TaxDocumentRepository.cs \
  src/Server/TaxDocuments src/Server/Program.cs tests/Server.Tests/Helpers/FakeTaxDocumentNumberSource.cs \
  tests/Server.Tests/Helpers/TaxTestData.cs tests/Server.Tests/TaxDocuments tests/Server.Tests/Data/Repositories/TaxDocumentRepositoryTests.cs
git status --short
git commit -m "feat: TaxDocumentService — sequential numbering, snapshots, idempotent issue, document emails

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 6: Listing fee invoices — card webhook, Staff issue invoice, mark paid, waive

**Files:**
- Modify: `src/Server/Payments/PaymentEventProcessor.cs` (constructor lines 17–34; `MarkListingFeePaidAsync` lines 187–197)
- Modify: `src/Server/Services/SubscriptionService.cs` (constructor; `MarkPaidAsync` lines 112–149; `WaiveAsync` lines 151–183;
  `GetForStudFarmAsync` / `GetAllAsync`; `MapToDto`), `src/Server/Services/ISubscriptionService.cs`, `src/Server/Controllers/SubscriptionsController.cs`
- Modify: `src/Shared/DTOs/Subscriptions/SubscriptionDto.cs`
- Modify (fixtures only): `tests/Server.Tests/Payments/PaymentEventProcessorTests.cs` (field, constructor, `CreateSut`),
  `tests/Server.Tests/Services/SubscriptionServiceTests.cs` (fields, constructor, `CreateSut`)
- Test: `tests/Server.Tests/Payments/PaymentEventProcessorTests.cs` (add tests), `tests/Server.Tests/Payments/ListingFeeTaxInvoiceFlowTests.cs` (new),
  `tests/Server.Tests/Services/SubscriptionServiceTests.cs` (add tests)

**Interfaces:**
- Consumes: Task 5 `ITaxDocumentService` (`CanIssueAsync`, `IssueListingFeeInvoiceAsync`, `SettleListingFeeAsync`, `IssueAdjustmentNoteAsync`,
  `RecordPendingAsync`), `TaxDocumentService.BusinessDetailsMissingMessage`, `ITaxDocumentRepository.GetListingFeeInvoiceAsync / GetBySubscriptionIdsAsync`.
- Produces: `PaymentEventProcessor` constructor
  `(IProcessedPaymentEventRepository, ISavedCardRepository, IUserRepository, ISubscriptionRepository, IPurchaseRepository, IPaymentProvider, IAuditLogRepository, ITaxDocumentService, ITransactionRunner, ILogger<PaymentEventProcessor>)`;
  `SubscriptionService` constructor
  `(ISubscriptionRepository, IStallionRepository, ISeasonRepository, IStudFarmRepository, IPlatformSettingsRepository, IAuditLogRepository, IUserService, IPaymentProvider, IIntroOfferRepository, ITaxDocumentService, ITaxDocumentRepository, ITransactionRunner, TimeProvider, ILogger<SubscriptionService>)`;
  `ISubscriptionService.IssueInvoiceAsync(Guid id)` → `ServiceResult<SubscriptionDto>`; `POST api/subscriptions/{id}/issue-invoice` (StaffOnly);
  `SubscriptionDto.TaxInvoiceId (Guid?)`, `.TaxInvoiceNumber`, `.TaxInvoiceStatus`, `.AdjustmentNoteNumber` (string?).

- [ ] **Step 1: Write the failing tests**

`PaymentEventProcessorTests` fixture — add `using Stallions.Server.TaxDocuments;`, a field and its default, and pass it:

```csharp
    private readonly Mock<ITaxDocumentService> _taxDocuments = new();
```

In the constructor, after `_provider.SetupGet(p => p.Name).Returns("Fake");` add:

```csharp
        _taxDocuments.Setup(t => t.SettleListingFeeAsync(It.IsAny<StallionSeasonSubscription>(), It.IsAny<DateTime>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new TaxDocumentIssueResult(TaxDocumentIssueStatus.Issued, new TaxDocument()));
```

`CreateSut()` becomes:

```csharp
    private PaymentEventProcessor CreateSut() => new(
        new ProcessedPaymentEventRepository(_db), new SavedCardRepository(_db), new UserRepository(_db),
        new SubscriptionRepository(_db), new PurchaseRepository(_db), _provider.Object, new AuditLogRepository(_db),
        _taxDocuments.Object, new InlineTransactionRunner(), NullLogger<PaymentEventProcessor>.Instance);
```

New tests in `PaymentEventProcessorTests`:

```csharp
    [Fact]
    public async Task ListingFeePaid_SettlesTheTaxInvoice()
    {
        var sub = PendingSubscription(990m);

        await CreateSut().ProcessAsync(new ListingFeePaidEvent("evt_p", sub.Id, 99000, "aud", "pi_1"));

        _taxDocuments.Verify(t => t.SettleListingFeeAsync(It.Is<StallionSeasonSubscription>(s => s.Id == sub.Id
            && s.Status == SubscriptionStatus.Paid), It.IsAny<DateTime>(), null), Times.Once);
        _taxDocuments.Verify(t => t.RecordPendingAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<TaxDocumentKind>()), Times.Never);
    }

    [Fact]
    public async Task ListingFeePaid_WhenNoInvoiceCanBeIssued_StillPays_AndRecordsItAsPending()
    {
        var sub = PendingSubscription(990m);
        _taxDocuments.Setup(t => t.SettleListingFeeAsync(It.IsAny<StallionSeasonSubscription>(), It.IsAny<DateTime>(), It.IsAny<Guid?>()))
            .ReturnsAsync(TaxDocumentIssueResult.Missing);

        var outcome = await CreateSut().ProcessAsync(new ListingFeePaidEvent("evt_p", sub.Id, 99000, "aud", "pi_1"));

        outcome.Should().Be(PaymentEventOutcome.Processed);
        (await _db.StallionSeasonSubscriptions.SingleAsync()).Status.Should().Be(SubscriptionStatus.Paid);
        _taxDocuments.Verify(t => t.RecordPendingAsync("StallionSeasonSubscription", sub.Id, TaxDocumentKind.ListingFeeInvoice), Times.Once);
    }

    [Fact]
    public async Task ListingFeePaid_ThatIsRejected_IssuesNoInvoice()
    {
        var sub = PendingSubscription(990m);

        await CreateSut().ProcessAsync(new ListingFeePaidEvent("evt_p", sub.Id, 1, "aud", "pi_1"));

        _taxDocuments.Verify(t => t.SettleListingFeeAsync(It.IsAny<StallionSeasonSubscription>(), It.IsAny<DateTime>(), It.IsAny<Guid?>()), Times.Never);
    }
```

```csharp
// tests/Server.Tests/Payments/ListingFeeTaxInvoiceFlowTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Payments;
using Stallions.Server.TaxDocuments;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Payments;

/// <summary>The card webhook with the real TaxDocumentService, end to end on the in-memory provider.</summary>
public class ListingFeeTaxInvoiceFlowTests : IDisposable
{
    private readonly AppDbContext _db = DbContextFactory.Create(Guid.NewGuid().ToString());
    private readonly Mock<IPaymentProvider> _provider = new();
    private readonly Mock<ITaxDocumentEmails> _emails = new();
    private readonly TestClock _clock = new() { Now = DateTimeOffset.UtcNow };
    private readonly FakeTaxDocumentNumberSource _numbers = new();
    private readonly User _owner = new() { ObjectId = "o", Email = "owner@arrowfield.example", DisplayName = "Owner", Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };

    public ListingFeeTaxInvoiceFlowTests()
    {
        _db.Database.EnsureCreated();
        TaxTestData.CompleteBusinessDetails(_db);
        _provider.SetupGet(p => p.Name).Returns("Fake");
        _db.Users.Add(_owner);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private TaxDocumentService TaxDocuments() => new(new TaxDocumentRepository(_db), _numbers, new PlatformSettingsRepository(_db),
        new StudFarmRepository(_db), new UserRepository(_db), new IntroOfferRepository(_db), _emails.Object,
        new AuditLogRepository(_db), _clock, NullLogger<TaxDocumentService>.Instance);

    private PaymentEventProcessor Processor() => new(
        new ProcessedPaymentEventRepository(_db), new SavedCardRepository(_db), new UserRepository(_db),
        new SubscriptionRepository(_db), new PurchaseRepository(_db), _provider.Object, new AuditLogRepository(_db),
        TaxDocuments(), new InlineTransactionRunner(), NullLogger<PaymentEventProcessor>.Instance);

    private async Task<StallionSeasonSubscription> PendingSubscriptionAsync()
    {
        var farm = new StudFarm { UserId = _owner.Id, Name = "Arrowfield", ContactEmail = "sales@arrowfield.example" };
        var stallion = new Stallion { Name = "Dundeel", StudFarmId = farm.Id };
        var season = new Season { Name = "2026 Season" };
        _db.AddRange(farm, stallion, season);
        var sub = new StallionSeasonSubscription
        {
            StallionId = stallion.Id, SeasonId = season.Id, StudFarmId = farm.Id, FeeIncGst = 990m, FeeExGst = 900m,
            GstAmount = 90m, StandardFeeIncGst = 990m, Status = SubscriptionStatus.Pending, CreatedByUserId = _owner.Id
        };
        _db.StallionSeasonSubscriptions.Add(sub);
        await _db.SaveChangesAsync();
        return sub;
    }

    private static ListingFeePaidEvent Paid(Guid subscriptionId) => new("evt_1", subscriptionId, 99000, "aud", "pi_1");

    [Fact]
    public async Task CardPayment_IssuesAPaidTaxInvoiceToTheStud()
    {
        var sub = await PendingSubscriptionAsync();

        (await Processor().ProcessAsync(Paid(sub.Id))).Should().Be(PaymentEventOutcome.Processed);

        var doc = await _db.TaxDocuments.SingleAsync();
        (doc.Kind, doc.Status, doc.SubscriptionId).Should().Be((TaxDocumentKind.ListingFeeInvoice, TaxDocumentStatus.Paid, sub.Id));
        doc.Description.Should().Be("Listing fee — Dundeel, 2026 Season");
        doc.RecipientEmail.Should().Be("sales@arrowfield.example");
        _emails.Verify(e => e.ListingFeeTaxInvoiceAsync(It.IsAny<TaxDocument>()), Times.Once);
    }

    [Fact]
    public async Task CardPayment_ForAnInvoicedSubscription_MarksThatInvoicePaid_AndSendsAReceipt()
    {
        var sub = await PendingSubscriptionAsync();
        await TaxDocuments().IssueListingFeeInvoiceAsync((await new SubscriptionRepository(_db).GetByIdAsync(sub.Id))!,
            paid: false, userId: null);

        await Processor().ProcessAsync(Paid(sub.Id));

        var doc = await _db.TaxDocuments.SingleAsync();
        doc.Status.Should().Be(TaxDocumentStatus.Paid);
        _emails.Verify(e => e.ListingFeeReceiptAsync(It.IsAny<TaxDocument>()), Times.Once);
        _emails.Verify(e => e.ListingFeeTaxInvoiceAsync(It.IsAny<TaxDocument>()), Times.Never);
    }

    [Fact]
    public async Task ListingFeePaid_WithoutBusinessDetails_StillRecordsThePayment_AndAuditsPending()
    {
        TaxTestData.ClearBusinessDetails(_db);
        var sub = await PendingSubscriptionAsync();

        var outcome = await Processor().ProcessAsync(Paid(sub.Id));

        outcome.Should().Be(PaymentEventOutcome.Processed);
        (await _db.StallionSeasonSubscriptions.SingleAsync()).Status.Should().Be(SubscriptionStatus.Paid);
        (await _db.TaxDocuments.AnyAsync()).Should().BeFalse();
        (await _db.AuditLogs.SingleAsync(a => a.Action == TaxDocumentService.PendingAction)).EntityId.Should().Be(sub.Id);
    }
}
```

`SubscriptionServiceTests` fixture — add `using Stallions.Server.TaxDocuments;`, fields

```csharp
    private readonly Mock<ITaxDocumentEmails> _taxEmails = new();
    private readonly FakeTaxDocumentNumberSource _numbers = new();
```

in the constructor, directly after `_db.Database.EnsureCreated();`, add `TaxTestData.CompleteBusinessDetails(_db);`, and replace `CreateSut()`:

```csharp
    private TaxDocumentService TaxDocuments() => new(new TaxDocumentRepository(_db), _numbers,
        new PlatformSettingsRepository(_db), new StudFarmRepository(_db), new UserRepository(_db),
        new IntroOfferRepository(_db), _taxEmails.Object, new AuditLogRepository(_db), _clock,
        NullLogger<TaxDocumentService>.Instance);

    private SubscriptionService CreateSut() => new(
        new SubscriptionRepository(_db),
        new StallionRepository(_db),
        new SeasonRepository(_db),
        new StudFarmRepository(_db),
        new PlatformSettingsRepository(_db),
        new AuditLogRepository(_db),
        _users.Object,
        _provider.Object,
        new IntroOfferRepository(_db),
        TaxDocuments(),
        new TaxDocumentRepository(_db),
        new InlineTransactionRunner(),
        _clock,
        NullLogger<SubscriptionService>.Instance);
```

New tests (region `// ── Tax invoices ──`):

```csharp
    // ── Tax invoices ────────────────────────────────────────────────────────

    private async Task<SubscriptionDto> IssueInvoiceAsync(SubscriptionDto created)
    {
        SignInAs(_staff);
        var r = await CreateSut().IssueInvoiceAsync(created.Id);
        r.Succeeded.Should().BeTrue(r.Error);
        return r.Value!;
    }

    [Fact]
    public async Task IssueInvoice_IssuesAnUnpaidInvoice_AndEmailsTheStud()
    {
        var created = await CreatePendingAsync(_stallionA);

        var dto = await IssueInvoiceAsync(created);

        dto.Status.Should().Be("Pending");
        dto.TaxInvoiceNumber.Should().Be("SA-000001");
        dto.TaxInvoiceStatus.Should().Be("Unpaid");
        var doc = await _db.TaxDocuments.SingleAsync();
        doc.RecipientEmail.Should().Be("a@x"); // Farm A has no contact email: its admin's email
        doc.DueAt.Should().NotBeNull();
        _taxEmails.Verify(e => e.ListingFeeInvoiceDueAsync(It.IsAny<TaxDocument>()), Times.Once);
    }

    [Fact]
    public async Task IssueInvoice_Twice_IsAConflict()
    {
        var created = await CreatePendingAsync(_stallionA);
        await IssueInvoiceAsync(created);

        var again = await CreateSut().IssueInvoiceAsync(created.Id);

        again.HttpStatusCode.Should().Be(409);
        again.Error.Should().Contain("SA-000001");
    }

    [Fact]
    public async Task IssueInvoice_ForAWaivedSubscription_IsBadRequest()
    {
        var created = await CreatePendingAsync(_stallionA);
        await CreateSut().WaiveAsync(created.Id, new WaiveSubscriptionRequest { Reason = "Founding stud" });

        (await CreateSut().IssueInvoiceAsync(created.Id)).HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task IssueInvoice_WithoutBankDetails_IsRejected()
    {
        var created = await CreatePendingAsync(_stallionA);
        TaxTestData.CompleteBusinessDetails(_db, withBank: false);

        var r = await CreateSut().IssueInvoiceAsync(created.Id);

        r.HttpStatusCode.Should().Be(400);
        r.Error.Should().Be("Complete the business details in Settings first.");
        (await _db.TaxDocuments.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task IssueInvoice_AsStudAdmin_IsForbidden()
    {
        var created = await CreatePendingAsync(_stallionA);
        SignInAs(_studUserA);

        (await CreateSut().IssueInvoiceAsync(created.Id)).HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task MarkPaid_WithNoInvoice_IssuesAPaidInvoice()
    {
        var created = await CreatePendingAsync(_stallionA);

        var r = await CreateSut().MarkPaidAsync(created.Id, new MarkSubscriptionPaidRequest { PaymentMethod = "BankTransfer" });

        r.Value!.TaxInvoiceStatus.Should().Be("Paid");
        (await _db.TaxDocuments.SingleAsync()).Status.Should().Be(TaxDocumentStatus.Paid);
        _taxEmails.Verify(e => e.ListingFeeTaxInvoiceAsync(It.IsAny<TaxDocument>()), Times.Once);
    }

    [Fact]
    public async Task MarkPaid_WithAnUnpaidInvoice_MarksItPaid_AndSendsAReceipt()
    {
        var created = await CreatePendingAsync(_stallionA);
        await IssueInvoiceAsync(created);

        await CreateSut().MarkPaidAsync(created.Id, new MarkSubscriptionPaidRequest { PaymentMethod = "Invoice", PaymentReference = "SA-000001" });

        var doc = await _db.TaxDocuments.SingleAsync();
        doc.Status.Should().Be(TaxDocumentStatus.Paid);
        _taxEmails.Verify(e => e.ListingFeeReceiptAsync(It.IsAny<TaxDocument>()), Times.Once);
    }

    [Fact]
    public async Task MarkPaid_WithoutBusinessDetails_IsRejected_AndLeavesItPending()
    {
        var created = await CreatePendingAsync(_stallionA);
        TaxTestData.ClearBusinessDetails(_db);

        var r = await CreateSut().MarkPaidAsync(created.Id, new MarkSubscriptionPaidRequest { PaymentMethod = "Invoice" });

        r.Error.Should().Be("Complete the business details in Settings first.");
        (await _db.StallionSeasonSubscriptions.SingleAsync()).Status.Should().Be(SubscriptionStatus.Pending);
        (await _db.TaxDocuments.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Waive_WithAnUnpaidInvoice_IssuesAnAdjustmentNoteToTheStud()
    {
        var created = await CreatePendingAsync(_stallionA);
        await IssueInvoiceAsync(created);

        var r = await CreateSut().WaiveAsync(created.Id, new WaiveSubscriptionRequest { Reason = "Founding stud" });

        r.Value!.Status.Should().Be("Waived");
        r.Value.AdjustmentNoteNumber.Should().Be("SA-000002");
        var note = await _db.TaxDocuments.SingleAsync(d => d.Kind == TaxDocumentKind.AdjustmentNote);
        note.Reason.Should().Be("Listing fee waived — Founding stud");
        note.AmountIncGst.Should().Be(990m);
        _taxEmails.Verify(e => e.AdjustmentNoteAsync(It.IsAny<TaxDocument>(), It.IsAny<TaxDocument>()), Times.Once);
    }

    [Fact]
    public async Task Waive_WithoutAnInvoice_NeedsNoBusinessDetails()
    {
        var created = await CreatePendingAsync(_stallionA);
        TaxTestData.ClearBusinessDetails(_db);

        (await CreateSut().WaiveAsync(created.Id, new WaiveSubscriptionRequest { Reason = "Founding stud" })).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Waive_WithAnInvoiceButNoBusinessDetails_IsRejected()
    {
        var created = await CreatePendingAsync(_stallionA);
        await IssueInvoiceAsync(created);
        TaxTestData.ClearBusinessDetails(_db);

        var r = await CreateSut().WaiveAsync(created.Id, new WaiveSubscriptionRequest { Reason = "Founding stud" });

        r.Error.Should().Be("Complete the business details in Settings first.");
        (await _db.StallionSeasonSubscriptions.SingleAsync()).Status.Should().Be(SubscriptionStatus.Pending);
    }

    [Fact]
    public async Task GetAll_ShowsEachSubscriptionsTaxInvoice()
    {
        var invoiced = await CreatePendingAsync(_stallionA);
        await IssueInvoiceAsync(invoiced);
        await CreatePendingAsync(_stallionB);

        var all = (await CreateSut().GetAllAsync(null, null)).Value!;

        all.Single(s => s.Id == invoiced.Id).TaxInvoiceNumber.Should().Be("SA-000001");
        all.Single(s => s.StallionId == _stallionB.Id).TaxInvoiceNumber.Should().BeNull();
    }
```

- [ ] **Step 2: Run red** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~PaymentEventProcessorTests|FullyQualifiedName~ListingFeeTaxInvoiceFlowTests|FullyQualifiedName~SubscriptionServiceTests"` → compile errors.

- [ ] **Step 3: Webhook**

`src/Server/Payments/PaymentEventProcessor.cs` — add `using Stallions.Server.TaxDocuments;`; add the field
`private readonly ITaxDocumentService _taxDocuments;`; the constructor (lines 27–34) becomes:

```csharp
    public PaymentEventProcessor(
        IProcessedPaymentEventRepository processed, ISavedCardRepository cards, IUserRepository users,
        ISubscriptionRepository subscriptions, IPurchaseRepository purchases, IPaymentProvider provider,
        IAuditLogRepository audit, ITaxDocumentService taxDocuments, ITransactionRunner transactions,
        ILogger<PaymentEventProcessor> log)
    {
        _processed = processed; _cards = cards; _users = users; _subscriptions = subscriptions;
        _purchases = purchases; _provider = provider; _audit = audit; _taxDocuments = taxDocuments;
        _transactions = transactions; _log = log;
    }
```

In `MarkListingFeePaidAsync`, between the `ListingFeePaidByCard` audit call (lines 195–196) and `return new HandlerResult(PaymentEventOutcome.Processed);` add:

```csharp

        // The tax invoice commits with the payment (same transaction). Missing business details must
        // never fail the payment: it is recorded as pending for Staff to issue later.
        var invoice = await _taxDocuments.SettleListingFeeAsync(subscription, subscription.PaidAt!.Value, null);
        if (invoice.Status == TaxDocumentIssueStatus.BusinessDetailsMissing)
            await _taxDocuments.RecordPendingAsync("StallionSeasonSubscription", subscription.Id, TaxDocumentKind.ListingFeeInvoice);
```

- [ ] **Step 4: DTO, interface, controller**

`src/Shared/DTOs/Subscriptions/SubscriptionDto.cs` — after `public string? IntroOfferName { get; set; }` add:

```csharp
    public Guid? TaxInvoiceId { get; set; }                      // the listing fee tax invoice, once issued
    public string? TaxInvoiceNumber { get; set; }
    public string? TaxInvoiceStatus { get; set; }                // Unpaid | Paid
    public string? AdjustmentNoteNumber { get; set; }            // set when the invoice was reversed (waived)
```

`src/Server/Services/ISubscriptionService.cs` — add:

```csharp
    /// <summary>Staff: issue an unpaid tax invoice for a Pending subscription (stud pays by bank transfer).</summary>
    Task<ServiceResult<SubscriptionDto>> IssueInvoiceAsync(Guid id);
```

`src/Server/Controllers/SubscriptionsController.cs` — add after `MarkPaid`:

```csharp
    [HttpPost("{id:guid}/issue-invoice")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> IssueInvoice(Guid id)
    {
        var r = await _subscriptions.IssueInvoiceAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
```

- [ ] **Step 5: `SubscriptionService`**

Add usings `using Stallions.Server.Data;` and `using Stallions.Server.TaxDocuments;`. Add fields
`private readonly ITaxDocumentService _taxDocuments; private readonly ITaxDocumentRepository _taxDocumentRepo; private readonly ITransactionRunner _transactions;`
and the constructor parameters `ITaxDocumentService taxDocuments, ITaxDocumentRepository taxDocumentRepo, ITransactionRunner transactions`
between `IIntroOfferRepository introOffers` and `TimeProvider clock`, assigning each.

Replace `MarkPaidAsync` (lines 112–149) with:

```csharp
    public async Task<ServiceResult<SubscriptionDto>> MarkPaidAsync(Guid id, MarkSubscriptionPaidRequest request)
    {
        var staff = await GetStaffCallerAsync();
        if (staff == null)
            return ServiceResult<SubscriptionDto>.Forbidden(StaffOnlyMessage);

        var method = ParsePaidMethod(request.PaymentMethod);
        if (method == null)
            return ServiceResult<SubscriptionDto>.BadRequest(
                "Payment method must be Card, Invoice or BankTransfer.");

        return await _transactions.RunAsync(async () =>
        {
            var subscription = await _repo.GetByIdAsync(id);
            if (subscription == null)
                return ServiceResult<SubscriptionDto>.NotFound("Subscription not found.");

            if (subscription.Status != SubscriptionStatus.Pending)
                return ServiceResult<SubscriptionDto>.BadRequest(
                    $"Only pending subscriptions can be marked paid (this one is {subscription.Status}).");

            // The tax document first: if it can't be issued, nothing has been written.
            var paidAt = DateTime.UtcNow;
            var invoice = await _taxDocuments.SettleListingFeeAsync(subscription, paidAt, staff.Id);
            if (invoice.Status == TaxDocumentIssueStatus.BusinessDetailsMissing)
                return ServiceResult<SubscriptionDto>.BadRequest(TaxDocumentService.BusinessDetailsMissingMessage);

            subscription.Status = SubscriptionStatus.Paid;
            subscription.PaymentMethod = method;
            subscription.PaymentReference = string.IsNullOrWhiteSpace(request.PaymentReference)
                ? null : request.PaymentReference.Trim();
            subscription.PaidAt = paidAt;
            subscription.PendingCheckoutUrl = null;
            subscription.PendingCheckoutExpiresAt = null;
            await _repo.UpdateAsync(subscription);

            await _auditRepo.LogAsync(AuditEntity, subscription.Id, "MarkSubscriptionPaid", staff.Id,
                JsonSerializer.Serialize(new
                {
                    PaymentMethod = method.ToString(),
                    subscription.PaymentReference,
                    subscription.FeeIncGst,
                    TaxInvoiceNumber = invoice.Document?.Number
                }));

            return ServiceResult<SubscriptionDto>.Ok(MapToDto(subscription, includeStaffFields: true,
                invoice.Document is { } doc ? new List<TaxDocument> { doc } : null));
        });
    }
```

Replace `WaiveAsync` (lines 151–183) with:

```csharp
    public async Task<ServiceResult<SubscriptionDto>> WaiveAsync(Guid id, WaiveSubscriptionRequest request)
    {
        var staff = await GetStaffCallerAsync();
        if (staff == null)
            return ServiceResult<SubscriptionDto>.Forbidden(StaffOnlyMessage);

        if (string.IsNullOrWhiteSpace(request.Reason))
            return ServiceResult<SubscriptionDto>.BadRequest("A reason is required to waive the listing fee.");

        return await _transactions.RunAsync(async () =>
        {
            var subscription = await _repo.GetByIdAsync(id);
            if (subscription == null)
                return ServiceResult<SubscriptionDto>.NotFound("Subscription not found.");

            if (subscription.Status != SubscriptionStatus.Pending)
                return ServiceResult<SubscriptionDto>.BadRequest(
                    $"Only pending subscriptions can be waived (this one is {subscription.Status}).");

            var reason = request.Reason.Trim();
            // An Unpaid tax invoice already sent for this fee is reversed by an adjustment note to the stud.
            var documents = new List<TaxDocument>();
            if (await _taxDocumentRepo.GetListingFeeInvoiceAsync(subscription.Id) is { } invoice)
            {
                var note = await _taxDocuments.IssueAdjustmentNoteAsync(invoice, $"Listing fee waived — {reason}", staff.Id);
                if (note.Status == TaxDocumentIssueStatus.BusinessDetailsMissing)
                    return ServiceResult<SubscriptionDto>.BadRequest(TaxDocumentService.BusinessDetailsMissingMessage);
                documents.Add(invoice);
                documents.Add(note.Document!);
            }

            var previousFee = subscription.FeeIncGst;
            subscription.FeeIncGst = 0m;
            subscription.FeeExGst = 0m;
            subscription.GstAmount = 0m;
            subscription.Status = SubscriptionStatus.Waived;
            subscription.PaymentMethod = SubscriptionPaymentMethod.Waived;
            subscription.WaiverReason = reason;
            subscription.PendingCheckoutUrl = null;
            subscription.PendingCheckoutExpiresAt = null;
            await _repo.UpdateAsync(subscription);

            await _auditRepo.LogAsync(AuditEntity, subscription.Id, "WaiveSubscription", staff.Id,
                JsonSerializer.Serialize(new
                {
                    PreviousFeeIncGst = previousFee,
                    subscription.WaiverReason,
                    AdjustmentNoteNumber = documents.FirstOrDefault(d => d.Kind == TaxDocumentKind.AdjustmentNote)?.Number
                }));

            return ServiceResult<SubscriptionDto>.Ok(MapToDto(subscription, includeStaffFields: true, documents));
        });
    }
```

Add `IssueInvoiceAsync` (after `WaiveAsync`):

```csharp
    public async Task<ServiceResult<SubscriptionDto>> IssueInvoiceAsync(Guid id)
    {
        var staff = await GetStaffCallerAsync();
        if (staff == null)
            return ServiceResult<SubscriptionDto>.Forbidden(StaffOnlyMessage);
        if (!await _taxDocuments.CanIssueAsync(unpaidInvoice: true))
            return ServiceResult<SubscriptionDto>.BadRequest(TaxDocumentService.BusinessDetailsMissingMessage);

        return await _transactions.RunAsync(async () =>
        {
            var subscription = await _repo.GetByIdAsync(id);
            if (subscription == null)
                return ServiceResult<SubscriptionDto>.NotFound("Subscription not found.");
            if (subscription.Status != SubscriptionStatus.Pending)
                return ServiceResult<SubscriptionDto>.BadRequest(
                    $"Only pending subscriptions can be invoiced (this one is {subscription.Status}).");
            if (subscription.FeeIncGst <= 0)
                return ServiceResult<SubscriptionDto>.BadRequest("This subscription has no fee to invoice.");

            var result = await _taxDocuments.IssueListingFeeInvoiceAsync(subscription, paid: false, staff.Id);
            return result.Status switch
            {
                TaxDocumentIssueStatus.Issued => ServiceResult<SubscriptionDto>.Ok(
                    MapToDto(subscription, includeStaffFields: true, new List<TaxDocument> { result.Document! })),
                TaxDocumentIssueStatus.Existing => ServiceResult<SubscriptionDto>.Conflict(
                    $"Tax invoice {result.Document!.Number} has already been issued for this subscription."),
                _ => ServiceResult<SubscriptionDto>.BadRequest(TaxDocumentService.BusinessDetailsMissingMessage)
            };
        });
    }
```

In `GetForStudFarmAsync` replace the final `return` with:

```csharp
        var documents = await _taxDocumentRepo.GetBySubscriptionIdsAsync(subscriptions.Select(s => s.Id));
        return ServiceResult<IReadOnlyList<SubscriptionDto>>.Ok(
            subscriptions.Select(s => MapToDto(s, includeStaffFields: false, documents)).ToList());
```

and in `GetAllAsync` replace the final `return` with:

```csharp
        var documents = await _taxDocumentRepo.GetBySubscriptionIdsAsync(subscriptions.Select(s => s.Id));
        return ServiceResult<IReadOnlyList<SubscriptionDto>>.Ok(
            subscriptions.Select(s => MapToDto(s, includeStaffFields: true, documents)).ToList());
```

Replace `MapToDto` with:

```csharp
    private static SubscriptionDto MapToDto(StallionSeasonSubscription s, bool includeStaffFields,
        IReadOnlyCollection<TaxDocument>? documents = null)
    {
        var invoice = documents?.FirstOrDefault(d => d.SubscriptionId == s.Id && d.Kind == TaxDocumentKind.ListingFeeInvoice);
        var note = documents?.FirstOrDefault(d => d.SubscriptionId == s.Id && d.Kind == TaxDocumentKind.AdjustmentNote);
        return new SubscriptionDto
        {
            Id = s.Id,
            StallionId = s.StallionId,
            StallionName = s.Stallion?.Name ?? string.Empty,
            SeasonId = s.SeasonId,
            SeasonName = s.Season?.Name ?? string.Empty,
            StudFarmId = s.StudFarmId,
            StudFarmName = s.StudFarm?.Name ?? string.Empty,
            FeeIncGst = s.FeeIncGst,
            FeeExGst = s.FeeExGst,
            GstAmount = s.GstAmount,
            StandardFeeIncGst = s.StandardFeeIncGst,
            IntroOfferName = s.IntroOffer?.Name,
            Status = s.Status.ToString(),
            PaymentMethod = s.PaymentMethod?.ToString(),
            PaymentReference = s.PaymentReference,
            PaidAt = s.PaidAt,
            WaiverReason = s.WaiverReason,
            Notes = includeStaffFields ? s.Notes : null,
            CreatedAt = s.CreatedAt,
            TaxInvoiceId = invoice?.Id,
            TaxInvoiceNumber = invoice?.Number,
            TaxInvoiceStatus = invoice?.Status.ToString(),
            AdjustmentNoteNumber = note?.Number
        };
    }
```

- [ ] **Step 6: Run green** — the three test classes (old and new tests); full build (0 warnings) and full test run.

- [ ] **Step 7: Commit**

```bash
git add src/Server/Payments/PaymentEventProcessor.cs src/Server/Services/SubscriptionService.cs src/Server/Services/ISubscriptionService.cs \
  src/Server/Controllers/SubscriptionsController.cs src/Shared/DTOs/Subscriptions/SubscriptionDto.cs \
  tests/Server.Tests/Payments/PaymentEventProcessorTests.cs tests/Server.Tests/Payments/ListingFeeTaxInvoiceFlowTests.cs \
  tests/Server.Tests/Services/SubscriptionServiceTests.cs
git status --short
git commit -m "feat: listing fee tax invoices — card payment, Staff issue invoice, mark paid and waive

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Buyer fee invoices and refunds — charger, win email link, adjustment note on refund, transactions

**Files:**
- Modify: `src/Server/Sales/BuyerFeeCharger.cs` (constructor lines 40–59; success branch lines 193–216)
- Modify: `src/Server/Email/AuctionEmails.cs` (constructor lines 21–35; `WonAndChargedAsync` lines 59–75)
- Modify: `src/Server/Services/PurchaseService.cs` (constructor lines 12–27; `RefundAsync` lines 97–123), `src/Server/Services/IPurchaseService.cs`,
  `src/Server/Controllers/PurchasesController.cs:40–46`
- Modify: `src/Server/Services/AdminService.cs` (constructor lines 12–42; `GetTransactionsAsync` lines 66–84), `src/Shared/DTOs/Admin/TransactionDto.cs`
- Create: `src/Shared/DTOs/Checkout/RefundBuyerFeeRequest.cs`, `src/Shared/DTOs/Checkout/RefundResultDto.cs`
- Modify (fixtures only): `tests/Server.Tests/Auctions/AuctionCloserFixture.cs`, `tests/Server.Tests/Email/AuctionEmailsTests.cs` (`CreateSut`),
  `tests/Server.Tests/Services/PurchaseServiceTests.cs` (`CreateSut`, and the three `RefundAsync(id)` calls → `RefundAsync(id, new RefundBuyerFeeRequest())`),
  `tests/Server.Tests/Services/AdminServiceTests.cs` (field, constructor, `CreateSut`)
- Test: `tests/Server.Tests/Sales/BuyerFeeChargerTaxInvoiceTests.cs` (new); add tests to `AuctionEmailsTests`, `PurchaseServiceTests`, `AdminServiceTests`

**Interfaces:**
- Consumes: Task 5 `ITaxDocumentService.IssueBuyerFeeInvoiceAsync / IssueAdjustmentNoteAsync / RecordPendingAsync`, `ITaxDocumentRepository.GetBuyerFeeInvoiceAsync / GetByPurchaseIdsAsync`.
- Produces: `BuyerFeeCharger` constructor
  `(IPurchaseRepository, IListingRepository, IBidRepository, ISavedCardRepository, IPlatformSettingsRepository, IAuditLogRepository, IAuctionEmails, ITaxDocumentService, IPaymentProvider, ITransactionRunner, TimeProvider, ILogger<BuyerFeeCharger>)`;
  `AuctionEmails` constructor `(IEmailOutbox, IUserRepository, ITaxDocumentRepository, IOptions<DisclosureOptions>, IOptions<EmailOptions>, ILogger<AuctionEmails>)`;
  `PurchaseService` constructor `(IPurchaseRepository, IAuditLogRepository, IUserService, IListingRepository, IBidRepository, ITaxDocumentRepository, ITaxDocumentService, ITransactionRunner)`;
  `IPurchaseService.RefundAsync(Guid id, RefundBuyerFeeRequest request)` → `ServiceResult<RefundResultDto>`; `POST api/purchases/{id}/refund` body `{ note }` → 200 `RefundResultDto`;
  `RefundBuyerFeeRequest { string? Note }`, `RefundResultDto { Guid? AdjustmentNoteId; string? AdjustmentNoteNumber; string Message }` (namespace `Stallions.Shared.DTOs.Checkout`);
  `AdminService` constructor gains a trailing `ITaxDocumentRepository`; `TransactionDto.TaxInvoiceId (Guid?)`, `.TaxInvoiceNumber`, `.AdjustmentNoteNumber`;
  `AuctionCloserFixture.TaxDocuments` (`Mock<ITaxDocumentService>`).

Every existing `AuctionCloser*`, `BuyerFeeChargerOfferTests` and `AuctionEmailsTests` test passes unchanged — only fixtures change.

- [ ] **Step 1: Write the failing tests**

`AuctionCloserFixture` — add `using Stallions.Server.TaxDocuments;`, the property and its default, and pass it to the charger:

```csharp
    public Mock<ITaxDocumentService> TaxDocuments { get; } = new();
```

In the constructor, after `ChargesSucceed();` add:

```csharp
        TaxDocuments.Setup(t => t.IssueBuyerFeeInvoiceAsync(It.IsAny<Purchase>()))
            .ReturnsAsync(new TaxDocumentIssueResult(TaxDocumentIssueStatus.Issued, new TaxDocument()));
```

and in `CreateCharger` the `new BuyerFeeCharger(...)` call becomes:

```csharp
        return new BuyerFeeCharger(
            new PurchaseRepository(db), listings ?? new ListingRepository(db), new BidRepository(db), new SavedCardRepository(db),
            settings.Object, Audit.Object, Emails.Object, TaxDocuments.Object, Provider.Object, new ClearingTransactionRunner(db), Clock,
            NullLogger<BuyerFeeCharger>.Instance);
```

```csharp
// tests/Server.Tests/Sales/BuyerFeeChargerTaxInvoiceTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Sales;
using Stallions.Server.TaxDocuments;
using Stallions.Server.Tests.Auctions;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Sales;

public class BuyerFeeChargerTaxInvoiceTests
{
    private readonly AuctionCloserFixture _f = new();

    /// <summary>An accepted $9,000 offer on an open Make an Offer listing, with its Pending sale record.</summary>
    private Guid AcceptedOfferSale() => _f.Seed(db =>
    {
        var auction = AuctionTestData.Auction(db, _f.Clock.UtcNow.AddDays(-2), status: ListingStatus.Unsold);
        var listing = new MakeAnOfferListing
        {
            StallionId = auction.StallionId, SeasonId = auction.SeasonId, StudFarmId = auction.StudFarmId,
            ListingType = ListingType.MakeAnOffer, Status = ListingStatus.Active, BuyerFeeIncGst = 150m,
            SourceAuctionListingId = auction.Id, ExpiresAt = _f.Clock.UtcNow.AddDays(30), PublishedAt = _f.Clock.UtcNow
        };
        db.MakeAnOfferListings.Add(listing);
        var buyer = AuctionTestData.Buyer(db, "Offer Buyer");
        AuctionTestData.Card(db, buyer);
        var offer = new Offer { ListingId = listing.Id, BuyerUserId = buyer.Id, AmountIncGst = 9000m,
            Status = OfferStatus.Accepted, CreatedAt = _f.Clock.UtcNow, ExpiresAt = _f.Clock.UtcNow.AddDays(7) };
        db.Offers.Add(offer);
        var purchase = new Purchase
        {
            ListingId = listing.Id, BuyerUserId = buyer.Id, OfferId = offer.Id, TotalPriceIncGst = 9000m,
            BuyerFeeIncGst = 150m, BuyerFeeExGst = 136.36m, BuyerFeeGst = 13.64m, BalancePayableToStudIncGst = 8850m,
            Status = PurchaseStatus.Pending, CreatedAt = _f.Clock.UtcNow
        };
        db.Purchases.Add(purchase);
        db.SaveChanges();
        return purchase.Id;
    });

    [Fact]
    public async Task Paid_IssuesTheBuyerFeeInvoice_OnceTheSaleIsCompleted_BeforeTheWinEmail()
    {
        var purchaseId = AcceptedOfferSale();
        var calls = new List<string>();
        PurchaseStatus? statusAtIssue = null;
        _f.TaxDocuments.Setup(t => t.IssueBuyerFeeInvoiceAsync(It.IsAny<Purchase>()))
            .Callback<Purchase>(p => { statusAtIssue = p.Status; calls.Add("invoice"); })
            .ReturnsAsync(new TaxDocumentIssueResult(TaxDocumentIssueStatus.Issued, new TaxDocument()));
        _f.Emails.Setup(e => e.WonAndChargedAsync(It.IsAny<Listing>(), It.IsAny<Purchase>(), It.IsAny<User>()))
            .Callback(() => calls.Add("win email"))
            .Returns(Task.CompletedTask);

        (await _f.CreateCharger().ChargeAsync(purchaseId)).Should().Be(ChargeOutcome.Paid);

        statusAtIssue.Should().Be(PurchaseStatus.Completed);
        calls.Should().Equal("invoice", "win email");
        _f.TaxDocuments.Verify(t => t.IssueBuyerFeeInvoiceAsync(It.Is<Purchase>(p => p.Id == purchaseId)), Times.Once);
        _f.TaxDocuments.Verify(t => t.RecordPendingAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<TaxDocumentKind>()), Times.Never);
    }

    [Fact]
    public async Task Paid_WithoutBusinessDetails_StillCompletesTheSale_AndRecordsPending()
    {
        var purchaseId = AcceptedOfferSale();
        _f.TaxDocuments.Setup(t => t.IssueBuyerFeeInvoiceAsync(It.IsAny<Purchase>())).ReturnsAsync(TaxDocumentIssueResult.Missing);

        (await _f.CreateCharger().ChargeAsync(purchaseId)).Should().Be(ChargeOutcome.Paid);

        (await _f.Read().Purchases.SingleAsync(p => p.Id == purchaseId)).Status.Should().Be(PurchaseStatus.Completed);
        _f.TaxDocuments.Verify(t => t.RecordPendingAsync("Purchase", purchaseId, TaxDocumentKind.BuyerFeeInvoice), Times.Once);
        _f.Emails.Verify(e => e.WonAndChargedAsync(It.IsAny<Listing>(), It.IsAny<Purchase>(), It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task Declined_IssuesNoInvoice()
    {
        var purchaseId = AcceptedOfferSale();
        _f.ChargesDecline();

        (await _f.CreateCharger().ChargeAsync(purchaseId)).Should().Be(ChargeOutcome.PaymentFailed);

        _f.TaxDocuments.Verify(t => t.IssueBuyerFeeInvoiceAsync(It.IsAny<Purchase>()), Times.Never);
    }

    [Fact]
    public async Task FinalFailure_IssuesNoInvoice()
    {
        var purchaseId = AcceptedOfferSale();
        _f.ChargesDecline();
        await _f.CreateCharger().ChargeAsync(purchaseId);
        _f.Clock.Advance(TimeSpan.FromHours(2));

        (await _f.CreateCharger().ChargeAsync(purchaseId)).Should().Be(ChargeOutcome.NoSale);

        _f.TaxDocuments.Verify(t => t.IssueBuyerFeeInvoiceAsync(It.IsAny<Purchase>()), Times.Never);
    }
}
```

`AuctionEmailsTests` — fixture: add the field and pass it:

```csharp
    private readonly Mock<ITaxDocumentRepository> _taxDocuments = new();
```

```csharp
        return new AuctionEmails(outbox.Object, _users.Object, _taxDocuments.Object,
            Microsoft.Extensions.Options.Options.Create(new DisclosureOptions
            {
                BuyerFeeExplanation = "CONFIGURED fee wording.",
                StudFarmBalanceArrangement = "CONFIGURED balance wording."
            }),
            Microsoft.Extensions.Options.Options.Create(new EmailOptions { PublicBaseUrl = "https://app.example/" }),
            NullLogger<AuctionEmails>.Instance);
```

New tests:

```csharp
    [Fact]
    public async Task WonAndCharged_LinksTheBuyerFeeTaxInvoice_WhenOneWasIssued()
    {
        var sale = Sale();
        var invoice = new TaxDocument { Id = Guid.NewGuid(), Number = "SA-000124" };
        _taxDocuments.Setup(t => t.GetBuyerFeeInvoiceAsync(sale.Id)).ReturnsAsync(invoice);

        await CreateSut().WonAndChargedAsync(Listing(), sale, Winner);

        _queued.Single().TextBody.Should()
            .Contain($"Your tax invoice SA-000124 is available here: https://app.example/tax-documents/{invoice.Id}");
    }

    [Fact]
    public async Task WonAndCharged_WithoutAnInvoice_HasNoInvoiceLine()
    {
        await CreateSut().WonAndChargedAsync(Listing(), Sale(), Winner);

        _queued.Single().TextBody.Should().NotContain("tax invoice");
    }
```

`PurchaseServiceTests` — fixture: add `using Stallions.Server.TaxDocuments;`, `using Stallions.Server.Tests.Helpers;`,
`using Stallions.Shared.DTOs.Checkout;`, fields and `CreateSut`:

```csharp
    private readonly Mock<ITaxDocumentRepository> _taxDocumentRepo = new();
    private readonly Mock<ITaxDocumentService> _taxDocuments = new();

    private PurchaseService CreateSut() =>
        new(_purchases.Object, _audit.Object, _users.Object, _listings.Object, _bids.Object,
            _taxDocumentRepo.Object, _taxDocuments.Object, new InlineTransactionRunner());
```

and change the three existing calls `CreateSut().RefundAsync(purchase.Id)` / `CreateSut().RefundAsync(Guid.NewGuid())` to pass
`new RefundBuyerFeeRequest()` as the second argument. New tests:

```csharp
    private (User Staff, Purchase Sale) StaffAndCompletedSale()
    {
        var staff = new User { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active };
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(staff);
        var sale = new Purchase
        {
            Id = Guid.NewGuid(), Status = PurchaseStatus.Completed, TotalPriceIncGst = 10000m, BuyerFeeIncGst = 150m,
            BalancePayableToStudIncGst = 9850m, PaymentProvider = "Stripe", PaymentReference = "pi_1"
        };
        _purchases.Setup(r => r.GetByIdAsync(sale.Id)).ReturnsAsync(sale);
        return (staff, sale);
    }

    [Fact]
    public async Task Refund_WithATaxInvoice_IssuesAnAdjustmentNote_AndSaysSo()
    {
        var (staff, sale) = StaffAndCompletedSale();
        var invoice = new TaxDocument { Id = Guid.NewGuid(), Number = "SA-000124", Kind = TaxDocumentKind.BuyerFeeInvoice };
        _taxDocumentRepo.Setup(r => r.GetBuyerFeeInvoiceAsync(sale.Id)).ReturnsAsync(invoice);
        var note = new TaxDocument { Id = Guid.NewGuid(), Number = "SA-000130", Kind = TaxDocumentKind.AdjustmentNote };
        _taxDocuments.Setup(t => t.IssueAdjustmentNoteAsync(invoice, "Buyer fee refunded — Duplicate charge", staff.Id))
            .ReturnsAsync(new TaxDocumentIssueResult(TaxDocumentIssueStatus.Issued, note));

        var r = await CreateSut().RefundAsync(sale.Id, new RefundBuyerFeeRequest { Note = "  Duplicate charge " });

        r.Succeeded.Should().BeTrue(r.Error);
        r.Value!.AdjustmentNoteNumber.Should().Be("SA-000130");
        r.Value.AdjustmentNoteId.Should().Be(note.Id);
        r.Value.Message.Should().Be("Refund recorded. Adjustment note SA-000130 has been emailed to the buyer.");
        sale.Status.Should().Be(PurchaseStatus.Refunded);
        _audit.Verify(a => a.LogAsync("Purchase", sale.Id, "PurchaseRefunded", staff.Id,
            It.Is<string?>(d => d != null && d.Contains("SA-000130"))), Times.Once);
    }

    [Fact]
    public async Task Refund_WithoutBusinessDetails_IsRejected_AndNothingChanges()
    {
        var (_, sale) = StaffAndCompletedSale();
        _taxDocumentRepo.Setup(r => r.GetBuyerFeeInvoiceAsync(sale.Id)).ReturnsAsync(new TaxDocument { Number = "SA-000124" });
        _taxDocuments.Setup(t => t.IssueAdjustmentNoteAsync(It.IsAny<TaxDocument>(), It.IsAny<string>(), It.IsAny<Guid?>()))
            .ReturnsAsync(TaxDocumentIssueResult.Missing);

        var r = await CreateSut().RefundAsync(sale.Id, new RefundBuyerFeeRequest());

        r.HttpStatusCode.Should().Be(400);
        r.Error.Should().Be("Complete the business details in Settings first.");
        sale.Status.Should().Be(PurchaseStatus.Completed);
        _purchases.Verify(p => p.UpdateAsync(It.IsAny<Purchase>()), Times.Never);
    }

    [Fact]
    public async Task Refund_OfASaleWithNoTaxInvoice_IsRecordedWithoutANote()
    {
        var (_, sale) = StaffAndCompletedSale();

        var r = await CreateSut().RefundAsync(sale.Id, new RefundBuyerFeeRequest { Note = "Goodwill" });

        r.Value!.AdjustmentNoteNumber.Should().BeNull();
        r.Value.Message.Should().Be("Refund recorded. This sale has no tax invoice, so no adjustment note was issued.");
        sale.Status.Should().Be(PurchaseStatus.Refunded);
        _taxDocuments.Verify(t => t.IssueAdjustmentNoteAsync(It.IsAny<TaxDocument>(), It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
    }

    [Fact]
    public async Task Refund_WithoutANote_UsesThePlainReason()
    {
        var (staff, sale) = StaffAndCompletedSale();
        var invoice = new TaxDocument { Number = "SA-000124" };
        _taxDocumentRepo.Setup(r => r.GetBuyerFeeInvoiceAsync(sale.Id)).ReturnsAsync(invoice);
        _taxDocuments.Setup(t => t.IssueAdjustmentNoteAsync(invoice, "Buyer fee refunded", staff.Id))
            .ReturnsAsync(new TaxDocumentIssueResult(TaxDocumentIssueStatus.Issued, new TaxDocument { Number = "SA-000130" }));

        (await CreateSut().RefundAsync(sale.Id, new RefundBuyerFeeRequest { Note = "   " })).Succeeded.Should().BeTrue();

        _taxDocuments.Verify(t => t.IssueAdjustmentNoteAsync(invoice, "Buyer fee refunded", staff.Id), Times.Once);
    }

    [Fact]
    public async Task Refund_WithANoteOver500Characters_IsRejected()
    {
        var (_, sale) = StaffAndCompletedSale();

        var r = await CreateSut().RefundAsync(sale.Id, new RefundBuyerFeeRequest { Note = new string('x', 501) });

        r.Error.Should().Be("The note can be at most 500 characters.");
    }
```

`AdminServiceTests` — fixture: add `using Stallions.Shared.Enums;` (present), the field, a constructor and the `CreateSut` argument:

```csharp
    private readonly Mock<ITaxDocumentRepository> _taxDocRepoMock = new();

    public AdminServiceTests() =>
        _taxDocRepoMock.Setup(r => r.GetByPurchaseIdsAsync(It.IsAny<IEnumerable<Guid>>())).ReturnsAsync(new List<TaxDocument>());
```

(`CreateSut()` gains `_taxDocRepoMock.Object` as its last argument.) New test:

```csharp
    [Fact]
    public async Task GetTransactions_ShowsTheBuyerFeeInvoiceAndAnyAdjustmentNote()
    {
        var farm = new StudFarm { Id = Guid.NewGuid(), Name = "Arrowfield" };
        var refunded = CompletedSale(farm, 10000m, 150m);
        var plain = CompletedSale(farm, 20000m, 150m);
        _purchaseRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Purchase> { refunded, plain });
        var invoiceId = Guid.NewGuid();
        _taxDocRepoMock.Setup(r => r.GetByPurchaseIdsAsync(It.IsAny<IEnumerable<Guid>>())).ReturnsAsync(new List<TaxDocument>
        {
            new() { Id = invoiceId, PurchaseId = refunded.Id, Kind = TaxDocumentKind.BuyerFeeInvoice, Number = "SA-000124" },
            new() { PurchaseId = refunded.Id, Kind = TaxDocumentKind.AdjustmentNote, Number = "SA-000130" }
        });

        var result = (await CreateSut().GetTransactionsAsync()).Value!;

        var t = result.Single(x => x.PurchaseId == refunded.Id);
        (t.TaxInvoiceId, t.TaxInvoiceNumber, t.AdjustmentNoteNumber).Should().Be((invoiceId, "SA-000124", "SA-000130"));
        result.Single(x => x.PurchaseId == plain.Id).TaxInvoiceNumber.Should().BeNull();
    }
```

- [ ] **Step 2: Run red** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~BuyerFeeCharger|FullyQualifiedName~AuctionEmailsTests|FullyQualifiedName~PurchaseServiceTests|FullyQualifiedName~AdminServiceTests"` → compile errors.

- [ ] **Step 3: DTOs**

```csharp
// src/Shared/DTOs/Checkout/RefundBuyerFeeRequest.cs
namespace Stallions.Shared.DTOs.Checkout;

// Staff-only. Records a buyer fee refund already made in the payment provider's dashboard.
public class RefundBuyerFeeRequest
{
    /// <summary>Optional, up to 500 characters; printed on the adjustment note after "Buyer fee refunded — ".</summary>
    public string? Note { get; set; }
}
```

```csharp
// src/Shared/DTOs/Checkout/RefundResultDto.cs
namespace Stallions.Shared.DTOs.Checkout;

public class RefundResultDto
{
    public Guid? AdjustmentNoteId { get; set; }
    public string? AdjustmentNoteNumber { get; set; }
    /// <summary>What happened, for Staff (including when no adjustment note could be issued).</summary>
    public string Message { get; set; } = string.Empty;
}
```

`src/Shared/DTOs/Admin/TransactionDto.cs` — after `public string Status { get; set; } = string.Empty;` add:

```csharp
    public Guid? TaxInvoiceId { get; set; }
    public string? TaxInvoiceNumber { get; set; }
    public string? AdjustmentNoteNumber { get; set; }
```

- [ ] **Step 4: Charger**

`src/Server/Sales/BuyerFeeCharger.cs` — add `using Stallions.Server.TaxDocuments;`, the field `private readonly ITaxDocumentService _taxDocuments;`,
and the constructor (lines 52–59) becomes:

```csharp
    public BuyerFeeCharger(
        IPurchaseRepository purchases, IListingRepository listings, IBidRepository bids, ISavedCardRepository cards,
        IPlatformSettingsRepository settings, IAuditLogRepository audit, IAuctionEmails emails, ITaxDocumentService taxDocuments,
        IPaymentProvider provider, ITransactionRunner transactions, TimeProvider clock, ILogger<BuyerFeeCharger> log)
    {
        _purchases = purchases; _listings = listings; _bids = bids; _cards = cards; _settings = settings;
        _audit = audit; _emails = emails; _taxDocuments = taxDocuments; _provider = provider;
        _transactions = transactions; _clock = clock; _log = log;
    }
```

In the success branch, between the `BuyerFeeCharged` audit call (lines 209–212) and
`await _emails.WonAndChargedAsync(listing, purchase, purchase.Buyer);` (line 213) add:

```csharp
            // The buyer fee tax invoice commits with the sale, before the win email (which links it).
            // Missing business details must never undo a successful charge: Staff issue it later.
            var invoice = await _taxDocuments.IssueBuyerFeeInvoiceAsync(purchase);
            if (invoice.Status == TaxDocumentIssueStatus.BusinessDetailsMissing)
                await _taxDocuments.RecordPendingAsync("Purchase", purchase.Id, TaxDocumentKind.BuyerFeeInvoice);
```

- [ ] **Step 5: Win email link**

`src/Server/Email/AuctionEmails.cs` — add `private readonly ITaxDocumentRepository _taxDocuments;`; the constructor (lines 27–35) becomes:

```csharp
    public AuctionEmails(IEmailOutbox outbox, IUserRepository users, ITaxDocumentRepository taxDocuments,
        IOptions<DisclosureOptions> disclosure, IOptions<EmailOptions> email, ILogger<AuctionEmails> log)
    {
        _outbox = outbox;
        _users = users;
        _taxDocuments = taxDocuments;
        _disclosure = disclosure.Value;
        _baseUrl = email.Value.PublicBaseUrl.TrimEnd('/');
        _log = log;
    }
```

Replace `WonAndChargedAsync` (lines 59–75) with:

```csharp
    public async Task WonAndChargedAsync(Listing listing, Purchase purchase, User winner)
    {
        // The charger issues the buyer fee tax invoice just before this email, in the same transaction.
        var invoice = await _taxDocuments.GetBuyerFeeInvoiceAsync(purchase.Id);
        var fromOffer = FromOffer(purchase);
        var blocks = new List<Block>
        {
            fromOffer
                ? Para($"Your offer of {Money(purchase.TotalPriceIncGst)} for the {Name(listing)} nomination ({Season(listing)}) was accepted by {Farm(listing)}.")
                : Para($"Your bid of {Money(purchase.TotalPriceIncGst)} won the auction for the {Name(listing)} nomination ({Season(listing)}), offered by {Farm(listing)}."),
            fromOffer
                ? Amounts(purchase, "Price (your accepted offer)", "Balance you pay the stud directly")
                : Amounts(purchase, "Price (your winning bid)", "Balance you pay the stud directly"),
            Para($"The buyer fee of {Money(purchase.BuyerFeeIncGst)} has been charged to your saved card."),
            Para(_disclosure.BuyerFeeExplanation),
            Para(_disclosure.StudFarmBalanceArrangement),
            Para(StudContact(listing))
        };
        if (invoice != null)
            blocks.Add(Link($"/tax-documents/{invoice.Id}", $"Your tax invoice {invoice.Number} is available here"));
        blocks.Add(Link("/my-purchases", "View your sale record"));

        await (fromOffer
            ? QueueAsync(winner.Email, "OfferAcceptedAndCharged", $"Your offer for the {Name(listing)} nomination was accepted", listing, blocks.ToArray())
            : QueueAsync(winner.Email, "WonAndCharged", $"You won the {Name(listing)} nomination", listing, blocks.ToArray()));
    }
```

- [ ] **Step 6: Refund**

`src/Server/Services/IPurchaseService.cs` — `Task<ServiceResult> RefundAsync(Guid id);` becomes
`Task<ServiceResult<RefundResultDto>> RefundAsync(Guid id, RefundBuyerFeeRequest request);`.

`src/Server/Services/PurchaseService.cs` — add usings `using Stallions.Server.Data;` and `using Stallions.Server.TaxDocuments;`;
constructor (lines 12–27) becomes:

```csharp
    private readonly IPurchaseRepository _purchaseRepo;
    private readonly IAuditLogRepository _auditRepo;
    private readonly IUserService _users;
    private readonly IListingRepository _listingRepo;
    private readonly IBidRepository _bidRepo;
    private readonly ITaxDocumentRepository _taxDocumentRepo;
    private readonly ITaxDocumentService _taxDocuments;
    private readonly ITransactionRunner _transactions;

    public PurchaseService(IPurchaseRepository purchaseRepo, IAuditLogRepository auditRepo, IUserService users,
        IListingRepository listingRepo, IBidRepository bidRepo, ITaxDocumentRepository taxDocumentRepo,
        ITaxDocumentService taxDocuments, ITransactionRunner transactions)
    {
        _purchaseRepo = purchaseRepo;
        _auditRepo = auditRepo;
        _users = users;
        _listingRepo = listingRepo;
        _bidRepo = bidRepo;
        _taxDocumentRepo = taxDocumentRepo;
        _taxDocuments = taxDocuments;
        _transactions = transactions;
    }
```

Replace `RefundAsync` (lines 97–123) with:

```csharp
    public async Task<ServiceResult<RefundResultDto>> RefundAsync(Guid id, RefundBuyerFeeRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<RefundResultDto>.Forbidden();
        if (caller.Role != UserRole.Staff) return ServiceResult<RefundResultDto>.Forbidden("Only Staff can process refunds.");

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note?.Length > 500) return ServiceResult<RefundResultDto>.BadRequest("The note can be at most 500 characters.");

        return await _transactions.RunAsync(async () =>
        {
            var purchase = await _purchaseRepo.GetByIdAsync(id);
            if (purchase == null)
                return ServiceResult<RefundResultDto>.NotFound("Sale record not found.");
            if (purchase.Status != PurchaseStatus.Completed)
                return ServiceResult<RefundResultDto>.BadRequest("Only paid sale records can be refunded.");

            // The adjustment note first: if it can't be issued, nothing has been written.
            TaxDocument? adjustment = null;
            if (await _taxDocumentRepo.GetBuyerFeeInvoiceAsync(purchase.Id) is { } invoice)
            {
                var result = await _taxDocuments.IssueAdjustmentNoteAsync(invoice,
                    note == null ? "Buyer fee refunded" : $"Buyer fee refunded — {note}", caller.Id);
                if (result.Status == TaxDocumentIssueStatus.BusinessDetailsMissing)
                    return ServiceResult<RefundResultDto>.BadRequest(TaxDocumentService.BusinessDetailsMissingMessage);
                adjustment = result.Document;
            }

            // Records a refund Staff have made in the payment provider's dashboard — no money moves here.
            purchase.RefundAmount = purchase.BuyerFeeIncGst;
            purchase.RefundedAt = DateTime.UtcNow;
            purchase.Status = PurchaseStatus.Refunded;
            await _purchaseRepo.UpdateAsync(purchase);

            await _auditRepo.LogAsync("Purchase", purchase.Id, "PurchaseRefunded", caller.Id, JsonSerializer.Serialize(new
            {
                purchase.RefundAmount, purchase.PaymentProvider, purchase.PaymentReference,
                Note = note, AdjustmentNoteNumber = adjustment?.Number
            }));

            return ServiceResult<RefundResultDto>.Ok(new RefundResultDto
            {
                AdjustmentNoteId = adjustment?.Id,
                AdjustmentNoteNumber = adjustment?.Number,
                Message = adjustment != null
                    ? $"Refund recorded. Adjustment note {adjustment.Number} has been emailed to the buyer."
                    : "Refund recorded. This sale has no tax invoice, so no adjustment note was issued."
            });
        });
    }
```

`src/Server/Controllers/PurchasesController.cs` lines 40–46 become (add `using Stallions.Shared.DTOs.Checkout;`):

```csharp
    [HttpPost("purchases/{id:guid}/refund")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> Refund(Guid id, [FromBody] RefundBuyerFeeRequest? request)
    {
        var r = await _purchases.RefundAsync(id, request ?? new RefundBuyerFeeRequest());
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
```

- [ ] **Step 7: Transactions show their documents**

`src/Server/Services/AdminService.cs` — add the field `private readonly ITaxDocumentRepository _taxDocumentRepo;`, a trailing
constructor parameter `ITaxDocumentRepository taxDocumentRepo` (assign it), and replace `GetTransactionsAsync` (lines 66–84) with:

```csharp
    public async Task<ServiceResult<IReadOnlyList<TransactionDto>>> GetTransactionsAsync()
    {
        var purchases = await _purchaseRepo.GetAllAsync();
        var documents = await _taxDocumentRepo.GetByPurchaseIdsAsync(purchases.Select(p => p.Id));
        var dtos = purchases.Select(p =>
        {
            var invoice = documents.FirstOrDefault(d => d.PurchaseId == p.Id && d.Kind == TaxDocumentKind.BuyerFeeInvoice);
            var note = documents.FirstOrDefault(d => d.PurchaseId == p.Id && d.Kind == TaxDocumentKind.AdjustmentNote);
            return new TransactionDto
            {
                PurchaseId = p.Id,
                StallionName = p.Listing?.Stallion?.Name ?? string.Empty,
                BuyerDisplayName = p.Buyer?.DisplayName ?? string.Empty,
                StudFarmName = p.Listing?.StudFarm?.Name ?? string.Empty,
                TotalPriceIncGst = p.TotalPriceIncGst,
                BuyerFeeIncGst = p.BuyerFeeIncGst,
                BuyerFeeExGst = p.BuyerFeeExGst,
                BuyerFeeGst = p.BuyerFeeGst,
                BalancePayableToStudIncGst = p.BalancePayableToStudIncGst,
                PaidAt = p.PaidAt,
                Status = p.Status.ToString(),
                TaxInvoiceId = invoice?.Id,
                TaxInvoiceNumber = invoice?.Number,
                AdjustmentNoteNumber = note?.Number
            };
        }).ToList();
        return ServiceResult<IReadOnlyList<TransactionDto>>.Ok(dtos);
    }
```

- [ ] **Step 8: Run green** — the four test classes (old and new tests), every `AuctionCloser*` / `BuyerFeeChargerOfferTests` test unchanged;
  full build (0 warnings) and full test run.

- [ ] **Step 9: Commit**

```bash
git add src/Server/Sales/BuyerFeeCharger.cs src/Server/Email/AuctionEmails.cs src/Server/Services/PurchaseService.cs \
  src/Server/Services/IPurchaseService.cs src/Server/Controllers/PurchasesController.cs src/Server/Services/AdminService.cs \
  src/Shared/DTOs/Checkout/RefundBuyerFeeRequest.cs src/Shared/DTOs/Checkout/RefundResultDto.cs src/Shared/DTOs/Admin/TransactionDto.cs \
  tests/Server.Tests/Auctions/AuctionCloserFixture.cs tests/Server.Tests/Sales/BuyerFeeChargerTaxInvoiceTests.cs \
  tests/Server.Tests/Email/AuctionEmailsTests.cs tests/Server.Tests/Services/PurchaseServiceTests.cs tests/Server.Tests/Services/AdminServiceTests.cs
git status --short
git commit -m "feat: buyer fee tax invoices on every sale; refunds issue an adjustment note

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 8: Tax documents API — document access, buyer and stud lists, Staff register, CSV, issue missing

**Files:**
- Create: `src/Shared/DTOs/TaxDocuments/TaxDocumentDto.cs`, `TaxDocumentSummaryDto.cs`, `TaxDocumentTotalsDto.cs`, `TaxDocumentRegisterDto.cs`,
  `TaxDocumentFilter.cs`, `IssueMissingResultDto.cs`
- Create: `src/Server/TaxDocuments/TaxDocumentQueryService.cs` (+ `ITaxDocumentQueryService`), `src/Server/TaxDocuments/TaxDocumentCsv.cs`,
  `src/Server/Controllers/TaxDocumentsController.cs`
- Modify: `src/Server/Data/Repositories/IAuditLogRepository.cs`, `src/Server/Data/Repositories/AuditLogRepository.cs` (one query),
  `src/Server/Infrastructure/SydneyTime.cs` (`StartOfDayUtc`), `src/Server/Program.cs` (one registration)
- Test: `tests/Server.Tests/TaxDocuments/TaxDocumentQueryServiceTests.cs`, `tests/Server.Tests/TaxDocuments/TaxDocumentCsvTests.cs`,
  `tests/Server.Tests/Controllers/TaxDocumentsControllerTests.cs`, `tests/Server.Tests/Infrastructure/SydneyTimeTests.cs` (add a test)

**Interfaces:**
- Consumes: Task 5 `ITaxDocumentRepository`, `ITaxDocumentService` (`CanIssueAsync`, `IssueListingFeeInvoiceAsync`, `IssueBuyerFeeInvoiceAsync`),
  `TaxDocumentService.PendingAction / BusinessDetailsMissingMessage`; `BusinessNumbers`; `DisclosureOptions.BuyerFeeExplanation`.
- Produces: `IAuditLogRepository.GetEntityIdsWithActionAsync(string entityType, string action)` → `Task<IReadOnlyList<Guid>>`;
  `SydneyTime.StartOfDayUtc(DateOnly)`;
  `ITaxDocumentQueryService { GetAsync(Guid); GetMineAsync(); GetForStudAsync(); GetRegisterAsync(TaxDocumentFilter); ExportCsvAsync(TaxDocumentFilter); IssueMissingAsync() }`;
  `static class TaxDocumentCsv { string Build(IEnumerable<TaxDocumentSummaryDto>); string Text(string?) }`;
  routes `GET api/tax-documents/{id}` ([Authorize], 404 unless recipient owner or Staff), `GET api/tax-documents/mine` (BuyerOnly),
  `GET api/tax-documents/stud` (StudFarmAdminOnly), `GET api/tax-documents?kind&status&from&to` (StaffOnly),
  `GET api/tax-documents/export.csv?…` (StaffOnly), `POST api/tax-documents/issue-missing` (StaffOnly);
  DTOs in `Stallions.Shared.DTOs.TaxDocuments` (below).

- [ ] **Step 1: Write the failing tests**

Add to `tests/Server.Tests/Infrastructure/SydneyTimeTests.cs`:

```csharp
    [Fact]
    public void StartOfDay_IsMidnightInSydney_InUtc() =>
        // 11 Oct 2026 is daylight time (UTC+11): 00:00 Sydney = 13:00 UTC the day before.
        SydneyTime.StartOfDayUtc(new DateOnly(2026, 10, 11))
            .Should().Be(new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc));
```

```csharp
// tests/Server.Tests/TaxDocuments/TaxDocumentCsvTests.cs
using FluentAssertions;
using Stallions.Server.TaxDocuments;
using Stallions.Shared.DTOs.TaxDocuments;

namespace Stallions.Server.Tests.TaxDocuments;

public class TaxDocumentCsvTests
{
    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+61 2 6545 0000", "'+61 2 6545 0000")]
    [InlineData("-5 nominations", "'-5 nominations")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tTabbed", "'\tTabbed")]
    [InlineData("Arrowfield, Scone", "\"Arrowfield, Scone\"")]
    [InlineData("Say \"hi\"", "\"Say \"\"hi\"\"\"")]
    [InlineData("=HYPERLINK(\"x\",\"y\")", "\"'=HYPERLINK(\"\"x\"\",\"\"y\"\")\"")]
    [InlineData("Plain", "Plain")]
    [InlineData(null, "")]
    public void Text_NeutralisesFormulas_AndQuotesWhenNeeded(string? value, string expected) =>
        TaxDocumentCsv.Text(value).Should().Be(expected);

    [Fact]
    public void Build_WritesAHeader_SignedNumericAmounts_AndOneLinePerDocument()
    {
        var rows = new[]
        {
            new TaxDocumentSummaryDto
            {
                Number = "SA-000124", Kind = "BuyerFeeInvoice", Status = "Paid", IssuedOn = new DateOnly(2026, 10, 11),
                PaidOn = new DateOnly(2026, 10, 11), RecipientName = "Jane Buyer", RecipientEmail = "jane@example.com",
                Description = "Buyer fee — nomination to Dundeel, 2026 Season (auction)", AmountIncGst = 150m,
                AmountExGst = 136.36m, GstAmount = 13.64m, ReversedByNumber = "SA-000130"
            },
            new TaxDocumentSummaryDto
            {
                Number = "SA-000130", Kind = "AdjustmentNote", Status = "Paid", IssuedOn = new DateOnly(2026, 10, 12),
                PaidOn = new DateOnly(2026, 10, 12), RecipientName = "Jane Buyer", RecipientEmail = "jane@example.com",
                Description = "Adjustment to SA-000124", AmountIncGst = 150m, AmountExGst = 136.36m, GstAmount = 13.64m,
                OriginalNumber = "SA-000124", Reason = "Buyer fee refunded"
            }
        };

        var lines = TaxDocumentCsv.Build(rows).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lines.Should().HaveCount(3);
        lines[0].Should().Be("Number,Kind,Issued,Status,Due,Paid,Recipient,Recipient email,Description,Amount inc GST,Amount ex GST,GST,Adjusts,Reversed by,Reason");
        lines[1].Should().Be("SA-000124,BuyerFeeInvoice,2026-10-11,Paid,,2026-10-11,Jane Buyer,jane@example.com,Buyer fee — nomination to Dundeel, 2026 Season (auction),150.00,136.36,13.64,,SA-000130,"
            .Replace("Buyer fee — nomination to Dundeel, 2026 Season (auction)", "\"Buyer fee — nomination to Dundeel, 2026 Season (auction)\""));
        lines[2].Should().EndWith(",-150.00,-136.36,-13.64,SA-000124,,Buyer fee refunded");
    }
}
```

```csharp
// tests/Server.Tests/TaxDocuments/TaxDocumentQueryServiceTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Options;
using Stallions.Server.Services;
using Stallions.Server.TaxDocuments;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared;
using Stallions.Shared.DTOs.TaxDocuments;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.TaxDocuments;

public class TaxDocumentQueryServiceTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 11, 2, 0, 0, DateTimeKind.Utc); // 1 pm, 11 Oct in Sydney

    private readonly AppDbContext _db = DbContextFactory.Create(Guid.NewGuid().ToString());
    private readonly Mock<IUserService> _users = new();
    private readonly TestClock _clock = new() { Now = new DateTimeOffset(T0) };
    private readonly User _staff = NewUser(UserRole.Staff, "staff");
    private readonly User _buyerA = NewUser(UserRole.Buyer, "jane");
    private readonly User _buyerB = NewUser(UserRole.Buyer, "bob");
    private readonly User _studA = NewUser(UserRole.StudFarmAdmin, "arrowfield");
    private readonly User _studB = NewUser(UserRole.StudFarmAdmin, "coolmore");
    private readonly StudFarm _farmA;
    private readonly StudFarm _farmB;

    public TaxDocumentQueryServiceTests()
    {
        _db.Database.EnsureCreated();
        _farmA = new StudFarm { UserId = _studA.Id, Name = "Arrowfield", ContactEmail = "sales@arrowfield.example" };
        _farmB = new StudFarm { UserId = _studB.Id, Name = "Coolmore" };
        _db.AddRange(_staff, _buyerA, _buyerB, _studA, _studB, _farmA, _farmB);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static User NewUser(UserRole role, string name) => new()
    {
        Id = Guid.NewGuid(), ObjectId = name, Email = $"{name}@example.com", DisplayName = name, Role = role, Status = UserStatus.Active
    };

    private void SignIn(User user) => _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(user);

    private TaxDocumentService TaxDocuments() => new(new TaxDocumentRepository(_db), new FakeTaxDocumentNumberSource(),
        new PlatformSettingsRepository(_db), new StudFarmRepository(_db), new UserRepository(_db), new IntroOfferRepository(_db),
        new Mock<ITaxDocumentEmails>().Object, new AuditLogRepository(_db), _clock, NullLogger<TaxDocumentService>.Instance);

    private TaxDocumentQueryService CreateSut() => new(new TaxDocumentRepository(_db), TaxDocuments(),
        new SubscriptionRepository(_db), new PurchaseRepository(_db), new StudFarmRepository(_db), new AuditLogRepository(_db),
        _users.Object, new InlineTransactionRunner(),
        Microsoft.Extensions.Options.Options.Create(new DisclosureOptions { BuyerFeeExplanation = "CONFIGURED fee wording." }),
        NullLogger<TaxDocumentQueryService>.Instance);

    private TaxDocument AddDoc(string number, TaxDocumentKind kind, Guid? buyer = null, Guid? farm = null,
        TaxDocumentStatus status = TaxDocumentStatus.Paid, decimal amount = 990m, DateTime? issuedAt = null,
        TaxDocument? original = null, string recipient = "Arrowfield")
    {
        var split = GstBreakdown.FromIncGst(amount);
        var d = new TaxDocument
        {
            Number = number, Kind = kind, Status = status, IssuedAt = issuedAt ?? T0,
            PaidAt = status == TaxDocumentStatus.Paid ? issuedAt ?? T0 : null,
            DueAt = status == TaxDocumentStatus.Unpaid ? (issuedAt ?? T0).AddDays(14) : null,
            Description = kind == TaxDocumentKind.BuyerFeeInvoice ? "Buyer fee — nomination to Dundeel, 2026 Season (auction)" : "Listing fee — Dundeel, 2026 Season",
            AmountIncGst = split.IncGst, AmountExGst = split.ExGst, GstAmount = split.Gst,
            SupplierName = "Stallions Australia Pty Ltd", SupplierAbn = "12345678901", SupplierAddress = "1 Racecourse Road",
            RecipientName = recipient, RecipientEmail = "r@example.com", RecipientAbn = farm != null ? "98765432109" : null,
            BuyerUserId = buyer, StudFarmId = farm, OriginalDocumentId = original?.Id,
            Reason = original != null ? "Buyer fee refunded" : null
        };
        _db.TaxDocuments.Add(d);
        _db.SaveChanges();
        return d;
    }

    // ── One document ────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_TheBuyersOwnInvoice_IsShown_WithTheConfiguredFeeNote()
    {
        var doc = AddDoc("SA-000001", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerA.Id, amount: 150m, recipient: "jane");
        SignIn(_buyerA);

        var r = await CreateSut().GetAsync(doc.Id);

        r.Succeeded.Should().BeTrue(r.Error);
        var dto = r.Value!;
        (dto.Number, dto.Kind, dto.Status).Should().Be(("SA-000001", "BuyerFeeInvoice", "Paid"));
        dto.SupplierAbn.Should().Be("12 345 678 901");
        dto.IssuedOn.Should().Be(new DateOnly(2026, 10, 11));
        dto.FeeNote.Should().Be("CONFIGURED fee wording.");
        (dto.AmountIncGst, dto.AmountExGst, dto.GstAmount).Should().Be((150m, 136.36m, 13.64m));
    }

    [Fact]
    public async Task Get_AnotherBuyersDocument_IsNotFound()
    {
        var doc = AddDoc("SA-000001", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerA.Id, amount: 150m);
        SignIn(_buyerB);

        (await CreateSut().GetAsync(doc.Id)).HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Get_TheStudsOwnInvoice_IsShown_WithItsAbnGrouped()
    {
        var doc = AddDoc("SA-000001", TaxDocumentKind.ListingFeeInvoice, farm: _farmA.Id, status: TaxDocumentStatus.Unpaid);
        SignIn(_studA);

        var dto = (await CreateSut().GetAsync(doc.Id)).Value!;

        dto.RecipientAbn.Should().Be("98 765 432 109");
        dto.DueOn.Should().Be(new DateOnly(2026, 10, 25));
        dto.FeeNote.Should().BeNull();
    }

    [Fact]
    public async Task Get_AnotherStudsDocument_IsNotFound()
    {
        var doc = AddDoc("SA-000001", TaxDocumentKind.ListingFeeInvoice, farm: _farmA.Id);
        SignIn(_studB);

        (await CreateSut().GetAsync(doc.Id)).HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Get_AStudCannotSeeTheBuyerFeeInvoiceForItsSale()
    {
        var doc = AddDoc("SA-000001", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerA.Id, amount: 150m);
        SignIn(_studA);

        (await CreateSut().GetAsync(doc.Id)).HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Get_StaffSeeEveryDocument_AndAnUnknownIdIsNotFound()
    {
        var buyerDoc = AddDoc("SA-000001", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerA.Id, amount: 150m);
        var studDoc = AddDoc("SA-000002", TaxDocumentKind.ListingFeeInvoice, farm: _farmA.Id);
        SignIn(_staff);

        (await CreateSut().GetAsync(buyerDoc.Id)).Succeeded.Should().BeTrue();
        (await CreateSut().GetAsync(studDoc.Id)).Succeeded.Should().BeTrue();
        (await CreateSut().GetAsync(Guid.NewGuid())).HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Get_AReversedInvoice_NamesItsNote_AndTheNoteNamesTheOriginal()
    {
        var invoice = AddDoc("SA-000001", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerA.Id, amount: 150m);
        var note = AddDoc("SA-000002", TaxDocumentKind.AdjustmentNote, buyer: _buyerA.Id, amount: 150m,
            issuedAt: T0.AddDays(1), original: invoice);
        SignIn(_buyerA);

        (await CreateSut().GetAsync(invoice.Id)).Value!.ReversedByNumber.Should().Be("SA-000002");
        var noteDto = (await CreateSut().GetAsync(note.Id)).Value!;
        (noteDto.OriginalNumber, noteDto.OriginalIssuedOn, noteDto.Reason).Should().Be(
            ("SA-000001", new DateOnly(2026, 10, 11), "Buyer fee refunded"));
    }

    // ── Lists ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Mine_AndStud_ListOnlyTheCallersOwnDocuments()
    {
        AddDoc("SA-000001", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerA.Id, amount: 150m);
        AddDoc("SA-000002", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerB.Id, amount: 150m);
        AddDoc("SA-000003", TaxDocumentKind.ListingFeeInvoice, farm: _farmA.Id);
        AddDoc("SA-000004", TaxDocumentKind.ListingFeeInvoice, farm: _farmB.Id);

        SignIn(_buyerA);
        (await CreateSut().GetMineAsync()).Value!.Select(d => d.Number).Should().Equal("SA-000001");
        SignIn(_studA);
        (await CreateSut().GetForStudAsync()).Value!.Select(d => d.Number).Should().Equal("SA-000003");
        (await CreateSut().GetMineAsync()).HttpStatusCode.Should().Be(403);
    }

    // ── Staff register ──────────────────────────────────────────────────────

    [Fact]
    public async Task Register_SubtractsAdjustmentNotes_AndShowsUnpaidSeparately()
    {
        AddDoc("SA-000001", TaxDocumentKind.ListingFeeInvoice, farm: _farmA.Id);                                   // paid 990
        AddDoc("SA-000002", TaxDocumentKind.ListingFeeInvoice, farm: _farmA.Id, status: TaxDocumentStatus.Unpaid); // owed 990
        var waived = AddDoc("SA-000003", TaxDocumentKind.ListingFeeInvoice, farm: _farmB.Id,
            status: TaxDocumentStatus.Unpaid, amount: 495m);                                                        // reversed
        AddDoc("SA-000004", TaxDocumentKind.AdjustmentNote, farm: _farmB.Id, amount: 495m, original: waived);
        var buyerFee = AddDoc("SA-000005", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerA.Id, amount: 150m);
        AddDoc("SA-000006", TaxDocumentKind.AdjustmentNote, buyer: _buyerA.Id, amount: 150m, original: buyerFee);
        SignIn(_staff);

        var register = (await CreateSut().GetRegisterAsync(new TaxDocumentFilter())).Value!;

        var t = register.Totals;
        (t.InvoiceCount, t.InvoicesIncGst, t.InvoicesExGst, t.InvoicesGst).Should().Be((4, 2625m, 2386.36m, 238.64m));
        (t.AdjustmentCount, t.AdjustmentsIncGst, t.AdjustmentsExGst, t.AdjustmentsGst).Should().Be((2, 645m, 586.36m, 58.64m));
        (t.NetIncGst, t.NetExGst, t.NetGst).Should().Be((1980m, 1800m, 180m));
        (t.UnpaidCount, t.UnpaidIncGst).Should().Be((1, 990m));
        register.Documents.Should().HaveCount(6);
        register.Documents.Single(d => d.Number == "SA-000003").ReversedByNumber.Should().Be("SA-000004");
        register.Documents.Single(d => d.Number == "SA-000006").OriginalNumber.Should().Be("SA-000005");
    }

    [Fact]
    public async Task Register_FiltersByKindStatusAndSydneyDate()
    {
        AddDoc("SA-000001", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerA.Id, amount: 150m,
            issuedAt: new DateTime(2026, 10, 10, 13, 30, 0, DateTimeKind.Utc));   // 12:30 am, 11 Oct in Sydney
        AddDoc("SA-000002", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerA.Id, amount: 150m,
            issuedAt: new DateTime(2026, 10, 10, 12, 30, 0, DateTimeKind.Utc));   // 11:30 pm, 10 Oct in Sydney
        AddDoc("SA-000003", TaxDocumentKind.ListingFeeInvoice, farm: _farmA.Id, status: TaxDocumentStatus.Unpaid);
        SignIn(_staff);

        var oct11 = new TaxDocumentFilter { From = new DateOnly(2026, 10, 11), To = new DateOnly(2026, 10, 11), Kind = "buyerfeeinvoice" };
        (await CreateSut().GetRegisterAsync(oct11)).Value!.Documents.Select(d => d.Number).Should().Equal("SA-000001");
        (await CreateSut().GetRegisterAsync(new TaxDocumentFilter { Status = "Unpaid" })).Value!.Documents
            .Select(d => d.Number).Should().Equal("SA-000003");
    }

    [Fact]
    public async Task Register_RejectsAnUnknownKind_OrADateRangeBackwards()
    {
        SignIn(_staff);

        (await CreateSut().GetRegisterAsync(new TaxDocumentFilter { Kind = "Receipt" })).HttpStatusCode.Should().Be(400);
        (await CreateSut().GetRegisterAsync(new TaxDocumentFilter { From = new DateOnly(2026, 10, 12), To = new DateOnly(2026, 10, 11) }))
            .HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Register_Export_AndIssueMissing_AreStaffOnly()
    {
        SignIn(_buyerA);

        (await CreateSut().GetRegisterAsync(new TaxDocumentFilter())).HttpStatusCode.Should().Be(403);
        (await CreateSut().ExportCsvAsync(new TaxDocumentFilter())).HttpStatusCode.Should().Be(403);
        (await CreateSut().IssueMissingAsync()).HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Export_EscapesFormulaText_AndSignsAdjustmentNotes()
    {
        var invoice = AddDoc("SA-000001", TaxDocumentKind.BuyerFeeInvoice, buyer: _buyerA.Id, amount: 150m, recipient: "=SUM(A1:A9)");
        AddDoc("SA-000002", TaxDocumentKind.AdjustmentNote, buyer: _buyerA.Id, amount: 150m, original: invoice, recipient: "@evil");
        SignIn(_staff);

        var csv = (await CreateSut().ExportCsvAsync(new TaxDocumentFilter())).Value!;

        csv.Should().Contain(",'=SUM(A1:A9),").And.Contain(",'@evil,").And.Contain(",-150.00,-136.36,-13.64,SA-000001,");
    }

    // ── Pending documents ───────────────────────────────────────────────────

    private async Task<(Guid SubscriptionId, Guid PurchaseId)> FeesRecordedWithoutDocumentsAsync()
    {
        var stallion = new Stallion { Name = "Dundeel", StudFarmId = _farmA.Id };
        var season = new Season { Name = "2026 Season" };
        _db.AddRange(stallion, season);
        var sub = new StallionSeasonSubscription
        {
            StallionId = stallion.Id, SeasonId = season.Id, StudFarmId = _farmA.Id, FeeIncGst = 990m, FeeExGst = 900m,
            GstAmount = 90m, StandardFeeIncGst = 990m, Status = SubscriptionStatus.Paid,
            PaymentMethod = SubscriptionPaymentMethod.Card, PaidAt = T0, CreatedByUserId = _studA.Id
        };
        _db.StallionSeasonSubscriptions.Add(sub);
        var listing = AuctionTestData.Auction(_db, T0.AddDays(-1), status: ListingStatus.Sold);
        var purchase = new Purchase
        {
            ListingId = listing.Id, BuyerUserId = _buyerA.Id, BidId = Guid.NewGuid(), TotalPriceIncGst = 20000m,
            BuyerFeeIncGst = 150m, BuyerFeeExGst = 136.36m, BuyerFeeGst = 13.64m, BalancePayableToStudIncGst = 19850m,
            Status = PurchaseStatus.Completed, PaidAt = T0
        };
        _db.Purchases.Add(purchase);
        await _db.SaveChangesAsync();
        var audit = new AuditLogRepository(_db);
        await audit.LogAsync("StallionSeasonSubscription", sub.Id, TaxDocumentService.PendingAction, null, "{}");
        await audit.LogAsync("Purchase", purchase.Id, TaxDocumentService.PendingAction, null, "{}");
        return (sub.Id, purchase.Id);
    }

    [Fact]
    public async Task PendingFees_AreCounted_AndIssueMissingIssuesThem_OnceTheBusinessDetailsAreComplete()
    {
        var (subscriptionId, purchaseId) = await FeesRecordedWithoutDocumentsAsync();
        SignIn(_staff);

        (await CreateSut().GetRegisterAsync(new TaxDocumentFilter())).Value!.PendingCount.Should().Be(2);
        (await CreateSut().IssueMissingAsync()).Error.Should().Be("Complete the business details in Settings first.");

        TaxTestData.CompleteBusinessDetails(_db);
        var result = (await CreateSut().IssueMissingAsync()).Value!;

        (result.Issued, result.Remaining).Should().Be((2, 0));
        (await _db.TaxDocuments.SingleAsync(d => d.SubscriptionId == subscriptionId)).Status.Should().Be(TaxDocumentStatus.Paid);
        (await _db.TaxDocuments.SingleAsync(d => d.PurchaseId == purchaseId)).Kind.Should().Be(TaxDocumentKind.BuyerFeeInvoice);
        (await CreateSut().GetRegisterAsync(new TaxDocumentFilter())).Value!.PendingCount.Should().Be(0);
        (await _db.AuditLogs.CountAsync(a => a.Action == "IssueMissingTaxDocuments" && a.UserId == _staff.Id)).Should().Be(1);
    }

    [Fact]
    public async Task PendingFees_ThatAlreadyHaveADocument_AreNotCounted()
    {
        var (subscriptionId, _) = await FeesRecordedWithoutDocumentsAsync();
        var existing = AddDoc("SA-000009", TaxDocumentKind.ListingFeeInvoice, farm: _farmA.Id);
        existing.SubscriptionId = subscriptionId;
        await _db.SaveChangesAsync();
        SignIn(_staff);

        (await CreateSut().GetRegisterAsync(new TaxDocumentFilter())).Value!.PendingCount.Should().Be(1);
    }
}
```

```csharp
// tests/Server.Tests/Controllers/TaxDocumentsControllerTests.cs
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Stallions.Server.Controllers;
using Stallions.Server.Services;
using Stallions.Server.TaxDocuments;
using Stallions.Shared.DTOs.TaxDocuments;

namespace Stallions.Server.Tests.Controllers;

public class TaxDocumentsControllerTests
{
    private readonly Mock<ITaxDocumentQueryService> _documents = new();

    [Fact]
    public async Task Export_ReturnsAUtf8CsvFileWithABom()
    {
        _documents.Setup(d => d.ExportCsvAsync(It.IsAny<TaxDocumentFilter>())).ReturnsAsync(ServiceResult<string>.Ok("Number\r\n"));

        var result = await new TaxDocumentsController(_documents.Object).Export(new TaxDocumentFilter());

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("text/csv; charset=utf-8");
        file.FileDownloadName.Should().Be("tax-documents.csv");
        file.FileContents.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
    }

    [Fact]
    public async Task Get_SomeoneElsesDocument_Is404()
    {
        _documents.Setup(d => d.GetAsync(It.IsAny<Guid>())).ReturnsAsync(ServiceResult<TaxDocumentDto>.NotFound("Tax document not found."));

        var result = await new TaxDocumentsController(_documents.Object).Get(Guid.NewGuid());

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(404);
    }
}
```

- [ ] **Step 2: Run red** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~TaxDocument|FullyQualifiedName~SydneyTimeTests"` → compile errors.

- [ ] **Step 3: DTOs**

```csharp
// src/Shared/DTOs/TaxDocuments/TaxDocumentDto.cs
namespace Stallions.Shared.DTOs.TaxDocuments;

/// <summary>One tax invoice or adjustment note, as printed. ABN / BSB already grouped; dates are Sydney dates.</summary>
public class TaxDocumentDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;          // ListingFeeInvoice | BuyerFeeInvoice | AdjustmentNote
    public string Status { get; set; } = string.Empty;        // Unpaid | Paid
    public DateTime IssuedAt { get; set; }
    public DateOnly IssuedOn { get; set; }
    public DateOnly? DueOn { get; set; }
    public DateOnly? PaidOn { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal? StandardAmountIncGst { get; set; }
    public string? IntroOfferName { get; set; }
    public decimal? DiscountIncGst { get; set; }
    public decimal AmountIncGst { get; set; }
    public decimal AmountExGst { get; set; }
    public decimal GstAmount { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string SupplierAbn { get; set; } = string.Empty;
    public string SupplierAddress { get; set; } = string.Empty;
    public string? SupplierEmail { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public string? RecipientAbn { get; set; }
    public string? RecipientAddress { get; set; }
    public string? BankAccountName { get; set; }
    public string? BankBsb { get; set; }
    public string? BankAccountNumber { get; set; }
    public Guid? OriginalDocumentId { get; set; }
    public string? OriginalNumber { get; set; }
    public DateOnly? OriginalIssuedOn { get; set; }
    public string? Reason { get; set; }
    /// <summary>Buyer fee invoices: the configured explanation that the fee forms part of the price.</summary>
    public string? FeeNote { get; set; }
    public Guid? ReversedById { get; set; }
    public string? ReversedByNumber { get; set; }
}
```

```csharp
// src/Shared/DTOs/TaxDocuments/TaxDocumentSummaryDto.cs
namespace Stallions.Shared.DTOs.TaxDocuments;

/// <summary>A row in a list of documents. Amounts are positive; an AdjustmentNote reduces the totals.</summary>
public class TaxDocumentSummaryDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateOnly IssuedOn { get; set; }
    public DateOnly? DueOn { get; set; }
    public DateOnly? PaidOn { get; set; }
    public string Description { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public decimal AmountIncGst { get; set; }
    public decimal AmountExGst { get; set; }
    public decimal GstAmount { get; set; }
    public string? OriginalNumber { get; set; }
    public string? ReversedByNumber { get; set; }
    public string? Reason { get; set; }
}
```

```csharp
// src/Shared/DTOs/TaxDocuments/TaxDocumentTotalsDto.cs
namespace Stallions.Shared.DTOs.TaxDocuments;

/// <summary>BAS totals for the filtered documents. Net = invoices − adjustment notes. Unpaid excludes reversed invoices.</summary>
public class TaxDocumentTotalsDto
{
    public int InvoiceCount { get; set; }
    public decimal InvoicesIncGst { get; set; }
    public decimal InvoicesExGst { get; set; }
    public decimal InvoicesGst { get; set; }
    public int AdjustmentCount { get; set; }
    public decimal AdjustmentsIncGst { get; set; }
    public decimal AdjustmentsExGst { get; set; }
    public decimal AdjustmentsGst { get; set; }
    public decimal NetIncGst { get; set; }
    public decimal NetExGst { get; set; }
    public decimal NetGst { get; set; }
    public int UnpaidCount { get; set; }
    public decimal UnpaidIncGst { get; set; }
}
```

```csharp
// src/Shared/DTOs/TaxDocuments/TaxDocumentRegisterDto.cs
namespace Stallions.Shared.DTOs.TaxDocuments;

public class TaxDocumentRegisterDto
{
    public List<TaxDocumentSummaryDto> Documents { get; set; } = new();
    public TaxDocumentTotalsDto Totals { get; set; } = new();
    /// <summary>Fees recorded without a document because the business details were incomplete.</summary>
    public int PendingCount { get; set; }
}
```

```csharp
// src/Shared/DTOs/TaxDocuments/TaxDocumentFilter.cs
namespace Stallions.Shared.DTOs.TaxDocuments;

/// <summary>Staff register filters (query string). Dates are Sydney calendar dates, both inclusive.</summary>
public class TaxDocumentFilter
{
    public string? Kind { get; set; }      // ListingFeeInvoice | BuyerFeeInvoice | AdjustmentNote
    public string? Status { get; set; }    // Unpaid | Paid
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}
```

```csharp
// src/Shared/DTOs/TaxDocuments/IssueMissingResultDto.cs
namespace Stallions.Shared.DTOs.TaxDocuments;

public class IssueMissingResultDto
{
    public int Issued { get; set; }
    public int Remaining { get; set; }
}
```

- [ ] **Step 4: Supporting changes**

`src/Server/Infrastructure/SydneyTime.cs` — after `EndOfDayUtc` add:

```csharp
    /// <summary>00:00:00 on that date in Sydney, as UTC.</summary>
    public static DateTime StartOfDayUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), Zone);
```

`src/Server/Data/Repositories/IAuditLogRepository.cs` — add:

```csharp
    /// <summary>Distinct ids of the entities of this type that have an audit row with this action.</summary>
    Task<IReadOnlyList<Guid>> GetEntityIdsWithActionAsync(string entityType, string action);
```

`src/Server/Data/Repositories/AuditLogRepository.cs` — add:

```csharp
    public async Task<IReadOnlyList<Guid>> GetEntityIdsWithActionAsync(string entityType, string action) =>
        await _db.AuditLogs
            .Where(a => a.EntityType == entityType && a.Action == action)
            .Select(a => a.EntityId)
            .Distinct()
            .ToListAsync();
```

- [ ] **Step 5: CSV, query service and controller**

```csharp
// src/Server/TaxDocuments/TaxDocumentCsv.cs
using System.Globalization;
using System.Text;
using Stallions.Shared.DTOs.TaxDocuments;
using Stallions.Shared.Enums;

namespace Stallions.Server.TaxDocuments;

/// <summary>
/// The register as CSV: one line per document; adjustment-note amounts negative; amounts written as plain
/// numbers. Text that a spreadsheet would run as a formula (starting = + - @ tab or CR) is prefixed with '.
/// </summary>
public static class TaxDocumentCsv
{
    private static readonly string[] Header =
    {
        "Number", "Kind", "Issued", "Status", "Due", "Paid", "Recipient", "Recipient email", "Description",
        "Amount inc GST", "Amount ex GST", "GST", "Adjusts", "Reversed by", "Reason"
    };

    public static string Build(IEnumerable<TaxDocumentSummaryDto> rows)
    {
        var csv = new StringBuilder();
        csv.Append(string.Join(",", Header)).Append("\r\n");
        foreach (var r in rows)
        {
            var sign = r.Kind == nameof(TaxDocumentKind.AdjustmentNote) ? -1m : 1m;
            csv.Append(string.Join(",", new[]
            {
                Text(r.Number), Text(r.Kind), Date(r.IssuedOn), Text(r.Status), Date(r.DueOn), Date(r.PaidOn),
                Text(r.RecipientName), Text(r.RecipientEmail), Text(r.Description),
                Amount(sign * r.AmountIncGst), Amount(sign * r.AmountExGst), Amount(sign * r.GstAmount),
                Text(r.OriginalNumber), Text(r.ReversedByNumber), Text(r.Reason)
            })).Append("\r\n");
        }
        return csv.ToString();
    }

    public static string Text(string? value)
    {
        var v = value ?? string.Empty;
        if (v.Length > 0 && "=+-@\t\r".IndexOf(v[0]) >= 0) v = "'" + v;
        return v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
}
```

```csharp
// src/Server/TaxDocuments/TaxDocumentQueryService.cs
using System.Text.Json;
using Microsoft.Extensions.Options;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Infrastructure;
using Stallions.Server.Options;
using Stallions.Server.Services;
using Stallions.Shared;
using Stallions.Shared.DTOs.TaxDocuments;
using Stallions.Shared.Enums;

namespace Stallions.Server.TaxDocuments;

/// <summary>Reading tax documents: the document page, buyer and stud lists, the Staff register, CSV, issue missing.</summary>
public interface ITaxDocumentQueryService
{
    /// <summary>The recipient's owner (buyer / stud admins of the farm) or Staff; anyone else gets 404.</summary>
    Task<ServiceResult<TaxDocumentDto>> GetAsync(Guid id);
    Task<ServiceResult<IReadOnlyList<TaxDocumentSummaryDto>>> GetMineAsync();
    Task<ServiceResult<IReadOnlyList<TaxDocumentSummaryDto>>> GetForStudAsync();
    Task<ServiceResult<TaxDocumentRegisterDto>> GetRegisterAsync(TaxDocumentFilter filter);
    Task<ServiceResult<string>> ExportCsvAsync(TaxDocumentFilter filter);
    /// <summary>Issues the documents for fees recorded with a TaxDocumentPending audit row and still none.</summary>
    Task<ServiceResult<IssueMissingResultDto>> IssueMissingAsync();
}

public class TaxDocumentQueryService : ITaxDocumentQueryService
{
    private const string StaffOnlyMessage = "Only Stallions Australia staff can use the tax document register.";
    private const string NotFoundMessage = "Tax document not found.";

    private readonly ITaxDocumentRepository _documents;
    private readonly ITaxDocumentService _taxDocuments;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IPurchaseRepository _purchases;
    private readonly IStudFarmRepository _farms;
    private readonly IAuditLogRepository _audit;
    private readonly IUserService _users;
    private readonly ITransactionRunner _transactions;
    private readonly DisclosureOptions _disclosure;
    private readonly ILogger<TaxDocumentQueryService> _log;

    public TaxDocumentQueryService(ITaxDocumentRepository documents, ITaxDocumentService taxDocuments,
        ISubscriptionRepository subscriptions, IPurchaseRepository purchases, IStudFarmRepository farms,
        IAuditLogRepository audit, IUserService users, ITransactionRunner transactions,
        IOptions<DisclosureOptions> disclosure, ILogger<TaxDocumentQueryService> log)
    {
        _documents = documents; _taxDocuments = taxDocuments; _subscriptions = subscriptions; _purchases = purchases;
        _farms = farms; _audit = audit; _users = users; _transactions = transactions; _disclosure = disclosure.Value; _log = log;
    }

    public async Task<ServiceResult<TaxDocumentDto>> GetAsync(Guid id)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        var document = await _documents.GetByIdAsync(id);
        // Not found and not yours look the same, so a document id reveals nothing.
        if (caller == null || document == null || !await CanSeeAsync(caller, document))
            return ServiceResult<TaxDocumentDto>.NotFound(NotFoundMessage);

        var reversedBy = document.Kind == TaxDocumentKind.AdjustmentNote
            ? null
            : await _documents.GetAdjustmentNoteForAsync(document.Id);
        return ServiceResult<TaxDocumentDto>.Ok(ToDto(document, reversedBy));
    }

    public async Task<ServiceResult<IReadOnlyList<TaxDocumentSummaryDto>>> GetMineAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.Buyer)
            return ServiceResult<IReadOnlyList<TaxDocumentSummaryDto>>.Forbidden("Only buyers have buyer fee tax invoices.");
        return ServiceResult<IReadOnlyList<TaxDocumentSummaryDto>>.Ok(await SummariesAsync(await _documents.GetForBuyerAsync(caller.Id)));
    }

    public async Task<ServiceResult<IReadOnlyList<TaxDocumentSummaryDto>>> GetForStudAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.StudFarmAdmin)
            return ServiceResult<IReadOnlyList<TaxDocumentSummaryDto>>.Forbidden("Only stud farm admins have listing fee tax invoices.");
        var farm = await _farms.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult<IReadOnlyList<TaxDocumentSummaryDto>>.NotFound("No stud farm found for the current user.");
        return ServiceResult<IReadOnlyList<TaxDocumentSummaryDto>>.Ok(await SummariesAsync(await _documents.GetForStudFarmAsync(farm.Id)));
    }

    public async Task<ServiceResult<TaxDocumentRegisterDto>> GetRegisterAsync(TaxDocumentFilter filter)
    {
        if (!await IsStaffAsync()) return ServiceResult<TaxDocumentRegisterDto>.Forbidden(StaffOnlyMessage);

        var (rows, error) = await SearchAsync(filter);
        if (error != null) return ServiceResult<TaxDocumentRegisterDto>.BadRequest(error);

        var reversedBy = await ReversalsAsync(rows);
        var (pendingSubscriptions, pendingPurchases) = await FindPendingAsync();
        return ServiceResult<TaxDocumentRegisterDto>.Ok(new TaxDocumentRegisterDto
        {
            Documents = rows.Select(d => ToSummary(d, reversedBy)).ToList(),
            Totals = Totals(rows, reversedBy),
            PendingCount = pendingSubscriptions.Count + pendingPurchases.Count
        });
    }

    public async Task<ServiceResult<string>> ExportCsvAsync(TaxDocumentFilter filter)
    {
        if (!await IsStaffAsync()) return ServiceResult<string>.Forbidden(StaffOnlyMessage);

        var (rows, error) = await SearchAsync(filter);
        if (error != null) return ServiceResult<string>.BadRequest(error);

        var reversedBy = await ReversalsAsync(rows);
        return ServiceResult<string>.Ok(TaxDocumentCsv.Build(rows.Select(d => ToSummary(d, reversedBy))));
    }

    public async Task<ServiceResult<IssueMissingResultDto>> IssueMissingAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.Staff)
            return ServiceResult<IssueMissingResultDto>.Forbidden(StaffOnlyMessage);
        if (!await _taxDocuments.CanIssueAsync())
            return ServiceResult<IssueMissingResultDto>.BadRequest(TaxDocumentService.BusinessDetailsMissingMessage);

        var (subscriptionIds, purchaseIds) = await FindPendingAsync();
        var issued = 0;
        foreach (var id in subscriptionIds)
        {
            var done = await _transactions.RunAsync(async () =>
            {
                var subscription = await _subscriptions.GetByIdAsync(id);
                if (subscription is not { Status: SubscriptionStatus.Paid }) return false;
                var result = await _taxDocuments.IssueListingFeeInvoiceAsync(subscription, paid: true, caller.Id, subscription.PaidAt);
                return result.Status == TaxDocumentIssueStatus.Issued;
            });
            if (done) issued++;
        }
        foreach (var id in purchaseIds)
        {
            var done = await _transactions.RunAsync(async () =>
            {
                var purchase = await _purchases.GetForChargeAsync(id);
                if (purchase is not { Status: PurchaseStatus.Completed }) return false;
                return (await _taxDocuments.IssueBuyerFeeInvoiceAsync(purchase)).Status == TaxDocumentIssueStatus.Issued;
            });
            if (done) issued++;
        }

        var (stillSubscriptions, stillPurchases) = await FindPendingAsync();
        var remaining = stillSubscriptions.Count + stillPurchases.Count;
        await _audit.LogAsync("TaxDocument", Guid.Empty, "IssueMissingTaxDocuments", caller.Id,
            JsonSerializer.Serialize(new { Issued = issued, Remaining = remaining }));
        _log.LogInformation("Staff {UserId} issued {Issued} missing tax documents; {Remaining} remain", caller.Id, issued, remaining);
        return ServiceResult<IssueMissingResultDto>.Ok(new IssueMissingResultDto { Issued = issued, Remaining = remaining });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<bool> IsStaffAsync() => (await _users.GetOrCreateCurrentUserAsync())?.Role == UserRole.Staff;

    private async Task<bool> CanSeeAsync(User caller, TaxDocument document) => caller.Role switch
    {
        UserRole.Staff => true,
        UserRole.Buyer => document.BuyerUserId == caller.Id,
        UserRole.StudFarmAdmin => document.StudFarmId != null
                                  && (await _farms.GetByUserIdAsync(caller.Id))?.Id == document.StudFarmId,
        _ => false
    };

    private async Task<(IReadOnlyList<TaxDocument> Rows, string? Error)> SearchAsync(TaxDocumentFilter filter)
    {
        TaxDocumentKind? kind = null;
        if (!string.IsNullOrWhiteSpace(filter.Kind))
        {
            var name = Enum.GetNames<TaxDocumentKind>().FirstOrDefault(n => string.Equals(n, filter.Kind.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name == null) return (Array.Empty<TaxDocument>(), $"'{filter.Kind}' is not a tax document kind.");
            kind = Enum.Parse<TaxDocumentKind>(name);
        }
        TaxDocumentStatus? status = null;
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var name = Enum.GetNames<TaxDocumentStatus>().FirstOrDefault(n => string.Equals(n, filter.Status.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name == null) return (Array.Empty<TaxDocument>(), $"'{filter.Status}' is not a tax document status.");
            status = Enum.Parse<TaxDocumentStatus>(name);
        }
        if (filter.From is { } from && filter.To is { } to && from > to)
            return (Array.Empty<TaxDocument>(), "The from date must be on or before the to date.");

        var fromUtc = filter.From is { } f ? SydneyTime.StartOfDayUtc(f) : (DateTime?)null;
        var beforeUtc = filter.To is { } t ? SydneyTime.StartOfDayUtc(t.AddDays(1)) : (DateTime?)null;
        return (await _documents.SearchAsync(kind, status, fromUtc, beforeUtc), null);
    }

    /// <summary>Invoice id → the number of the adjustment note that reversed it.</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> ReversalsAsync(IReadOnlyList<TaxDocument> rows)
    {
        var invoiceIds = rows.Where(d => d.Kind != TaxDocumentKind.AdjustmentNote).Select(d => d.Id).ToList();
        if (invoiceIds.Count == 0) return new Dictionary<Guid, string>();
        var notes = await _documents.GetAdjustmentNotesForAsync(invoiceIds);
        return notes.ToDictionary(n => n.OriginalDocumentId!.Value, n => n.Number);
    }

    private async Task<IReadOnlyList<TaxDocumentSummaryDto>> SummariesAsync(IReadOnlyList<TaxDocument> rows)
    {
        var reversedBy = await ReversalsAsync(rows);
        return rows.Select(d => ToSummary(d, reversedBy)).ToList();
    }

    /// <summary>Fees with a TaxDocumentPending audit row that still have no document and still need one.</summary>
    private async Task<(List<Guid> Subscriptions, List<Guid> Purchases)> FindPendingAsync()
    {
        var subscriptionIds = await _audit.GetEntityIdsWithActionAsync("StallionSeasonSubscription", TaxDocumentService.PendingAction);
        var purchaseIds = await _audit.GetEntityIdsWithActionAsync("Purchase", TaxDocumentService.PendingAction);
        var subscriptionDocs = await _documents.GetBySubscriptionIdsAsync(subscriptionIds);
        var purchaseDocs = await _documents.GetByPurchaseIdsAsync(purchaseIds);

        var subscriptions = new List<Guid>();
        foreach (var id in subscriptionIds.Where(id =>
                     !subscriptionDocs.Any(d => d.SubscriptionId == id && d.Kind == TaxDocumentKind.ListingFeeInvoice)))
        {
            if (await _subscriptions.GetByIdAsync(id) is { Status: SubscriptionStatus.Paid, FeeIncGst: > 0m })
                subscriptions.Add(id);
        }

        var purchases = new List<Guid>();
        foreach (var id in purchaseIds.Where(id =>
                     !purchaseDocs.Any(d => d.PurchaseId == id && d.Kind == TaxDocumentKind.BuyerFeeInvoice)))
        {
            if (await _purchases.GetByIdAsync(id) is { Status: PurchaseStatus.Completed })
                purchases.Add(id);
        }
        return (subscriptions, purchases);
    }

    internal static TaxDocumentTotalsDto Totals(IReadOnlyList<TaxDocument> rows, IReadOnlyDictionary<Guid, string> reversedBy)
    {
        var invoices = rows.Where(d => d.Kind != TaxDocumentKind.AdjustmentNote).ToList();
        var notes = rows.Where(d => d.Kind == TaxDocumentKind.AdjustmentNote).ToList();
        var unpaid = invoices.Where(d => d.Status == TaxDocumentStatus.Unpaid && !reversedBy.ContainsKey(d.Id)).ToList();
        var totals = new TaxDocumentTotalsDto
        {
            InvoiceCount = invoices.Count,
            InvoicesIncGst = invoices.Sum(d => d.AmountIncGst),
            InvoicesExGst = invoices.Sum(d => d.AmountExGst),
            InvoicesGst = invoices.Sum(d => d.GstAmount),
            AdjustmentCount = notes.Count,
            AdjustmentsIncGst = notes.Sum(d => d.AmountIncGst),
            AdjustmentsExGst = notes.Sum(d => d.AmountExGst),
            AdjustmentsGst = notes.Sum(d => d.GstAmount),
            UnpaidCount = unpaid.Count,
            UnpaidIncGst = unpaid.Sum(d => d.AmountIncGst)
        };
        totals.NetIncGst = totals.InvoicesIncGst - totals.AdjustmentsIncGst;
        totals.NetExGst = totals.InvoicesExGst - totals.AdjustmentsExGst;
        totals.NetGst = totals.InvoicesGst - totals.AdjustmentsGst;
        return totals;
    }

    private static DateOnly? SydneyDate(DateTime? utc) => utc is { } value ? SydneyTime.Today(value) : null;

    private TaxDocumentDto ToDto(TaxDocument d, TaxDocument? reversedBy) => new()
    {
        Id = d.Id,
        Number = d.Number,
        Kind = d.Kind.ToString(),
        Status = d.Status.ToString(),
        IssuedAt = d.IssuedAt,
        IssuedOn = SydneyTime.Today(d.IssuedAt),
        DueOn = SydneyDate(d.DueAt),
        PaidOn = SydneyDate(d.PaidAt),
        Description = d.Description,
        StandardAmountIncGst = d.StandardAmountIncGst,
        IntroOfferName = d.IntroOfferName,
        DiscountIncGst = d.DiscountIncGst,
        AmountIncGst = d.AmountIncGst,
        AmountExGst = d.AmountExGst,
        GstAmount = d.GstAmount,
        SupplierName = d.SupplierName,
        SupplierAbn = BusinessNumbers.FormatAbn(d.SupplierAbn),
        SupplierAddress = d.SupplierAddress,
        SupplierEmail = d.SupplierEmail,
        RecipientName = d.RecipientName,
        RecipientEmail = d.RecipientEmail,
        RecipientAbn = d.RecipientAbn == null ? null : BusinessNumbers.FormatAbn(d.RecipientAbn),
        RecipientAddress = d.RecipientAddress,
        BankAccountName = d.BankAccountName,
        BankBsb = d.BankBsb == null ? null : BusinessNumbers.FormatBsb(d.BankBsb),
        BankAccountNumber = d.BankAccountNumber,
        OriginalDocumentId = d.OriginalDocumentId,
        OriginalNumber = d.OriginalDocument?.Number,
        OriginalIssuedOn = SydneyDate(d.OriginalDocument?.IssuedAt),
        Reason = d.Reason,
        FeeNote = d.Kind == TaxDocumentKind.BuyerFeeInvoice ? _disclosure.BuyerFeeExplanation : null,
        ReversedById = reversedBy?.Id,
        ReversedByNumber = reversedBy?.Number
    };

    private static TaxDocumentSummaryDto ToSummary(TaxDocument d, IReadOnlyDictionary<Guid, string> reversedBy) => new()
    {
        Id = d.Id,
        Number = d.Number,
        Kind = d.Kind.ToString(),
        Status = d.Status.ToString(),
        IssuedOn = SydneyTime.Today(d.IssuedAt),
        DueOn = SydneyDate(d.DueAt),
        PaidOn = SydneyDate(d.PaidAt),
        Description = d.Description,
        RecipientName = d.RecipientName,
        RecipientEmail = d.RecipientEmail,
        AmountIncGst = d.AmountIncGst,
        AmountExGst = d.AmountExGst,
        GstAmount = d.GstAmount,
        OriginalNumber = d.OriginalDocument?.Number,
        ReversedByNumber = reversedBy.TryGetValue(d.Id, out var number) ? number : null,
        Reason = d.Reason
    };
}
```

```csharp
// src/Server/Controllers/TaxDocumentsController.cs
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.TaxDocuments;
using Stallions.Shared.DTOs.TaxDocuments;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/tax-documents")]
public class TaxDocumentsController : ControllerBase
{
    private readonly ITaxDocumentQueryService _documents;
    public TaxDocumentsController(ITaxDocumentQueryService documents) => _documents = documents;

    // Any signed-in user; the service returns 404 unless the caller owns the recipient or is Staff.
    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Get(Guid id)
    {
        var r = await _documents.GetAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("mine")]
    [Authorize(Policy = "BuyerOnly")]
    public async Task<IActionResult> GetMine()
    {
        var r = await _documents.GetMineAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("stud")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> GetForStud()
    {
        var r = await _documents.GetForStudAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> GetRegister([FromQuery] TaxDocumentFilter filter)
    {
        var r = await _documents.GetRegisterAsync(filter);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("export.csv")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> Export([FromQuery] TaxDocumentFilter filter)
    {
        var r = await _documents.ExportCsvAsync(filter);
        if (!r.Succeeded) return StatusCode(r.HttpStatusCode, r.Error);
        // UTF-8 with a BOM so Excel shows "—" and other non-ASCII text correctly.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(r.Value!)).ToArray();
        return File(bytes, "text/csv; charset=utf-8", "tax-documents.csv");
    }

    [HttpPost("issue-missing")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> IssueMissing()
    {
        var r = await _documents.IssueMissingAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
}
```

`src/Server/Program.cs` — after the `ITaxDocumentService` registration add
`builder.Services.AddScoped<ITaxDocumentQueryService, TaxDocumentQueryService>();`.

- [ ] **Step 6: Run green** — the new test classes and `SydneyTimeTests`; full build (0 warnings) and full test run.

- [ ] **Step 7: Commit**

```bash
git add src/Shared/DTOs/TaxDocuments src/Server/TaxDocuments/TaxDocumentQueryService.cs src/Server/TaxDocuments/TaxDocumentCsv.cs \
  src/Server/Controllers/TaxDocumentsController.cs src/Server/Data/Repositories/IAuditLogRepository.cs \
  src/Server/Data/Repositories/AuditLogRepository.cs src/Server/Infrastructure/SydneyTime.cs src/Server/Program.cs \
  tests/Server.Tests/TaxDocuments/TaxDocumentQueryServiceTests.cs tests/Server.Tests/TaxDocuments/TaxDocumentCsvTests.cs \
  tests/Server.Tests/Controllers/TaxDocumentsControllerTests.cs tests/Server.Tests/Infrastructure/SydneyTimeTests.cs
git status --short
git commit -m "feat: tax documents API — 404 access rules, buyer and stud lists, Staff register with BAS totals, CSV, issue missing

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 9: Client — tax document page, buyer's tax invoices, stud's tax invoices, activation by intro offer

**Files:**
- Create: `src/Client/Services/TaxDocumentApiService.cs`
- Create: `src/Client/Pages/TaxDocumentPage.razor`, `src/Client/Pages/TaxDocumentPage.razor.css`, `src/Client/Pages/MyTaxInvoices.razor`,
  `src/Client/Components/TaxDocuments/TaxDocumentList.razor`
- Modify: `src/Client/Program.cs` (register the service), `src/Client/_Imports.razor` (two usings), `src/Client/Layout/NavBar.razor` (lines 28–32 and 68–72),
  `src/Client/wwwroot/css/app.css` (print rules, appended), `src/Client/Services/SubscriptionApiService.cs` (`ActivateAsync`, lines 51–59),
  `src/Client/Pages/Admin/AdminStallions.razor` (inject; tax invoices section after line 128; `Load` lines 271–301; `ActivateAsync` lines 313–320)
- Modify (fixture + one setup): `tests/Client.Tests/Pages/AdminStallionsTests.cs` (`RenderAt` registers a `TaxDocumentApiService` mock;
  `Activate_SendsTheBrowserToThePaymentPage` returns a `PaymentRedirectDto`)
- Test: `tests/Client.Tests/Services/TaxDocumentApiServiceTests.cs`, `tests/Client.Tests/Pages/TaxDocumentPageTests.cs`,
  `tests/Client.Tests/Pages/MyTaxInvoicesTests.cs`, `tests/Client.Tests/Pages/AdminStallionsTests.cs` (add tests)

**Interfaces:**
- Consumes: Task 8 routes and `Stallions.Shared.DTOs.TaxDocuments` DTOs; Task 3 `PaymentRedirectDto.ActivatedWithoutPayment`.
- Produces: `TaxDocumentApiService { virtual GetAsync(Guid) → TaxDocumentDto? (null on 404); GetMineAsync(); GetForStudAsync(); GetRegisterAsync(TaxDocumentFilter); ExportCsvAsync(TaxDocumentFilter) → string; IssueMissingAsync(); static string BuildQuery(TaxDocumentFilter) }`;
  `SubscriptionApiService.ActivateAsync(Guid)` now returns `PaymentRedirectDto`;
  component `TaxDocumentList` (`[Parameter] IReadOnlyList<TaxDocumentSummaryDto> Documents`, `static string KindText(string)`, `static string StatusText(TaxDocumentSummaryDto)`, `static string SignedTotal(TaxDocumentSummaryDto)`);
  pages `/tax-documents/{id}` and `/account/tax-invoices`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Client.Tests/Services/TaxDocumentApiServiceTests.cs
using System.Net;
using FluentAssertions;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.TaxDocuments;

namespace Stallions.Client.Tests.Services;

public class TaxDocumentApiServiceTests
{
    [Fact]
    public void BuildQuery_IncludesOnlyTheFiltersSet()
    {
        TaxDocumentApiService.BuildQuery(new TaxDocumentFilter()).Should().BeEmpty();
        TaxDocumentApiService.BuildQuery(new TaxDocumentFilter
        {
            Kind = "BuyerFeeInvoice", Status = "Paid", From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 31)
        }).Should().Be("?kind=BuyerFeeInvoice&status=Paid&from=2026-10-01&to=2026-10-31");
    }

    [Fact]
    public async Task Get_WhenTheApiSays404_ReturnsNull()
    {
        var http = new HttpClient(new FakeHttpMessageHandler(HttpStatusCode.NotFound, "Tax document not found."))
            { BaseAddress = new Uri("https://localhost/") };

        (await new TaxDocumentApiService(http).GetAsync(Guid.NewGuid())).Should().BeNull();
    }
}
```

```csharp
// tests/Client.Tests/Pages/TaxDocumentPageTests.cs
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.TaxDocuments;

namespace Stallions.Client.Tests.Pages;

public class TaxDocumentPageTests : TestContext
{
    private readonly Mock<TaxDocumentApiService> _api =
        new(MockBehavior.Loose, new HttpClient { BaseAddress = new Uri("https://localhost/") });

    private static TaxDocumentDto ListingFeeInvoice() => new()
    {
        Id = Guid.NewGuid(), Number = "SA-000123", Kind = "ListingFeeInvoice", Status = "Paid",
        IssuedOn = new DateOnly(2026, 10, 11), PaidOn = new DateOnly(2026, 10, 11),
        Description = "Listing fee — Dundeel, 2026 Season", AmountIncGst = 495m, AmountExGst = 450m, GstAmount = 45m,
        StandardAmountIncGst = 990m, IntroOfferName = "Launch 2026", DiscountIncGst = 495m,
        SupplierName = "Stallions Australia Pty Ltd", SupplierAbn = "12 345 678 901", SupplierAddress = "1 Racecourse Road",
        RecipientName = "Arrowfield", RecipientEmail = "sales@arrowfield.example", RecipientAbn = "98 765 432 109"
    };

    private IRenderedComponent<TaxDocumentPage> Render(TaxDocumentDto? doc)
    {
        this.AddTestAuthorization().SetAuthorized("user@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        var id = doc?.Id ?? Guid.NewGuid();
        _api.Setup(a => a.GetAsync(id)).ReturnsAsync(doc);
        Services.AddSingleton(_api.Object);
        return RenderComponent<TaxDocumentPage>(p => p.Add(c => c.Id, id));
    }

    [Fact]
    public void APaidInvoice_ShowsTheTaxInvoiceDetails_AndTheIntroOffer()
    {
        var cut = Render(ListingFeeInvoice());

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("SA-000123"));
        cut.Find("h1").TextContent.Should().Be("Tax invoice");
        cut.Markup.Should().Contain("ABN 12 345 678 901").And.Contain("ABN 98 765 432 109")
            .And.Contain("Intro offer: Launch 2026").And.Contain("−$495.00").And.Contain("$990.00")
            .And.Contain("Amount (excl. GST)").And.Contain("$450.00").And.Contain("GST").And.Contain("$45.00")
            .And.Contain("Total (incl. GST)").And.Contain("11 Oct 2026").And.Contain("Paid")
            .And.NotContain("payment reference");
    }

    [Fact]
    public void AnUnpaidInvoice_ShowsTheDueDateBankDetailsAndPaymentReference()
    {
        var doc = ListingFeeInvoice();
        doc.Status = "Unpaid";
        doc.PaidOn = null;
        doc.DueOn = new DateOnly(2026, 10, 25);
        doc.BankAccountName = "Stallions Australia Pty Ltd";
        doc.BankBsb = "062-000";
        doc.BankAccountNumber = "12345678";

        var cut = Render(doc);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Unpaid"));
        cut.Markup.Should().Contain("25 Oct 2026").And.Contain("062-000").And.Contain("12345678")
            .And.Contain("Please use SA-000123 as your payment reference.");
    }

    [Fact]
    public void AnAdjustmentNote_ShowsWhatItAdjusts_AndWhy()
    {
        var doc = ListingFeeInvoice();
        doc.Kind = "AdjustmentNote";
        doc.Number = "SA-000130";
        doc.StandardAmountIncGst = null;
        doc.DiscountIncGst = null;
        doc.OriginalDocumentId = Guid.NewGuid();
        doc.OriginalNumber = "SA-000123";
        doc.OriginalIssuedOn = new DateOnly(2026, 10, 11);
        doc.Reason = "Listing fee waived — Founding stud";

        var cut = Render(doc);

        cut.WaitForAssertion(() => cut.Find("h1").TextContent.Should().Be("Adjustment note"));
        cut.Markup.Should().Contain("Adjusts tax invoice").And.Contain("SA-000123").And.Contain("dated 11 Oct 2026")
            .And.Contain("Listing fee waived — Founding stud");
        cut.Find($"a[href='/tax-documents/{doc.OriginalDocumentId}']").Should().NotBeNull();
    }

    [Fact]
    public void ABuyerFeeInvoice_ShowsTheConfiguredFeeNote()
    {
        var doc = ListingFeeInvoice();
        doc.Kind = "BuyerFeeInvoice";
        doc.StandardAmountIncGst = null;
        doc.DiscountIncGst = null;
        doc.FeeNote = "CONFIGURED fee wording.";

        var cut = Render(doc);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("CONFIGURED fee wording."));
    }

    [Fact]
    public void ADocumentThatIsNotTheCallers_ShowsNotAvailable()
    {
        var cut = Render(null);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("This tax document isn't available."));
    }

    [Fact]
    public void ThePrintButton_OpensTheBrowsersPrintDialog()
    {
        var cut = Render(ListingFeeInvoice());
        cut.WaitForAssertion(() => cut.Find("button.tax-doc-print"));

        cut.Find("button.tax-doc-print").Click();

        JSInterop.VerifyInvoke("print");
    }
}
```

```csharp
// tests/Client.Tests/Pages/MyTaxInvoicesTests.cs
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.TaxDocuments;

namespace Stallions.Client.Tests.Pages;

public class MyTaxInvoicesTests : TestContext
{
    [Fact]
    public void ListsTheBuyersDocuments_WithLinks_AndAdjustmentsShownNegative()
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        var invoiceId = Guid.NewGuid();
        var api = new Mock<TaxDocumentApiService>(MockBehavior.Loose, new HttpClient { BaseAddress = new Uri("https://localhost/") });
        api.Setup(a => a.GetMineAsync()).ReturnsAsync(new List<TaxDocumentSummaryDto>
        {
            new() { Id = Guid.NewGuid(), Number = "SA-000130", Kind = "AdjustmentNote", Status = "Paid", IssuedOn = new DateOnly(2026, 10, 12),
                Description = "Adjustment to SA-000124", AmountIncGst = 150m, OriginalNumber = "SA-000124" },
            new() { Id = invoiceId, Number = "SA-000124", Kind = "BuyerFeeInvoice", Status = "Paid", IssuedOn = new DateOnly(2026, 10, 11),
                Description = "Buyer fee — nomination to Dundeel, 2026 Season (auction)", AmountIncGst = 150m, ReversedByNumber = "SA-000130" }
        });
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<MyTaxInvoices>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("SA-000124"));
        cut.Find($"a[href='/tax-documents/{invoiceId}']").TextContent.Should().Be("SA-000124");
        cut.Markup.Should().Contain("Buyer fee invoice").And.Contain("Adjustment note").And.Contain("−$150.00")
            .And.Contain("Reversed by SA-000130");
    }

    [Fact]
    public void WithNoDocuments_ShowsAnEmptyState()
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        var api = new Mock<TaxDocumentApiService>(MockBehavior.Loose, new HttpClient { BaseAddress = new Uri("https://localhost/") });
        api.Setup(a => a.GetMineAsync()).ReturnsAsync(new List<TaxDocumentSummaryDto>());
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<MyTaxInvoices>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No tax invoices yet"));
    }
}
```

`AdminStallionsTests` — fixture: add `using Stallions.Shared.DTOs.Payments;` and `using Stallions.Shared.DTOs.TaxDocuments;`, fields

```csharp
    private readonly Mock<TaxDocumentApiService> _taxDocs = new(MockBehavior.Loose, Http());
    private readonly List<TaxDocumentSummaryDto> _studDocuments = [];
```

and in `RenderAt`, after `Services.AddSingleton(_subs.Object);` add:

```csharp
        _taxDocs.Setup(t => t.GetForStudAsync()).ReturnsAsync(_studDocuments);
        Services.AddSingleton(_taxDocs.Object);
```

In `Activate_SendsTheBrowserToThePaymentPage` change the setup to
`_subs.Setup(s => s.ActivateAsync(_snitzel.Id)).ReturnsAsync(new PaymentRedirectDto { Url = "https://checkout.example/pay" });`.
New tests:

```csharp
    [Fact]
    public void Activate_WhenAnIntroOfferWaivesTheFee_ShowsItActive_WithoutLeavingThePage()
    {
        _subs.SetupSequence(s => s.GetMineAsync())
            .ReturnsAsync([])
            .ReturnsAsync([Subscription("Waived", 0m)]);
        _subs.Setup(s => s.ActivateAsync(_snitzel.Id)).ReturnsAsync(new PaymentRedirectDto { ActivatedWithoutPayment = true });
        var cut = RenderAt(null);

        cut.FindAll("button").Single(b => b.TextContent.Contains("Activate for")).Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Snitzel is active for 2026 Season — intro offer applied.")
            .And.NotContain("Activate for"));
        Services.GetRequiredService<NavigationManager>().Uri.Should().NotContain("checkout");
    }

    [Fact]
    public void TheStudsTaxInvoices_AreListed_WithUnpaidOnesShowingTheirDueDate()
    {
        var id = Guid.NewGuid();
        _studDocuments.Add(new TaxDocumentSummaryDto
        {
            Id = id, Number = "SA-000123", Kind = "ListingFeeInvoice", Status = "Unpaid", IssuedOn = new DateOnly(2026, 10, 11),
            DueOn = new DateOnly(2026, 10, 25), Description = "Listing fee — Snitzel, 2026 Season", AmountIncGst = 990m
        });

        var cut = Render();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("SA-000123"));
        cut.Find($"a[href='/tax-documents/{id}']").Should().NotBeNull();
        cut.Markup.Should().Contain("Unpaid — due 25 Oct 2026").And.Contain("Listing fee invoice");
    }
```

- [ ] **Step 2: Run red** — `dotnet test tests/Client.Tests --filter "FullyQualifiedName~TaxDocument|FullyQualifiedName~MyTaxInvoices|FullyQualifiedName~AdminStallionsTests"` → compile errors.

- [ ] **Step 3: API service and registration**

```csharp
// src/Client/Services/TaxDocumentApiService.cs
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.TaxDocuments;

namespace Stallions.Client.Services;

/// <summary>Tax invoices and adjustment notes: one document, the buyer's and stud's lists, and the Staff register.</summary>
public class TaxDocumentApiService
{
    private readonly HttpClient _http;
    public TaxDocumentApiService(HttpClient http) => _http = http;

    /// <summary>Null when the document doesn't exist or isn't the caller's (the API answers 404 for both).</summary>
    public virtual async Task<TaxDocumentDto?> GetAsync(Guid id)
    {
        var r = await _http.GetAsync($"api/tax-documents/{id}");
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<TaxDocumentDto>();
    }

    public virtual Task<List<TaxDocumentSummaryDto>> GetMineAsync() => GetListAsync("api/tax-documents/mine");

    public virtual Task<List<TaxDocumentSummaryDto>> GetForStudAsync() => GetListAsync("api/tax-documents/stud");

    public virtual async Task<TaxDocumentRegisterDto> GetRegisterAsync(TaxDocumentFilter filter)
    {
        var r = await _http.GetAsync("api/tax-documents" + BuildQuery(filter));
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<TaxDocumentRegisterDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public virtual async Task<string> ExportCsvAsync(TaxDocumentFilter filter)
    {
        var r = await _http.GetAsync("api/tax-documents/export.csv" + BuildQuery(filter));
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadAsStringAsync();
    }

    public virtual async Task<IssueMissingResultDto> IssueMissingAsync()
    {
        var r = await _http.PostAsync("api/tax-documents/issue-missing", null);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<IssueMissingResultDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    public static string BuildQuery(TaxDocumentFilter filter)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(filter.Kind)) query.Add($"kind={Uri.EscapeDataString(filter.Kind)}");
        if (!string.IsNullOrEmpty(filter.Status)) query.Add($"status={Uri.EscapeDataString(filter.Status)}");
        if (filter.From is { } from) query.Add($"from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        if (filter.To is { } to) query.Add($"to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        return query.Count == 0 ? string.Empty : "?" + string.Join("&", query);
    }

    private async Task<List<TaxDocumentSummaryDto>> GetListAsync(string url)
    {
        var r = await _http.GetAsync(url);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<List<TaxDocumentSummaryDto>>() ?? [];
    }
}
```

`src/Client/Program.cs` — after the `OfferApiService` registration add:

```csharp
builder.Services.AddHttpClient<TaxDocumentApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
```

`src/Client/_Imports.razor` — add `@using Stallions.Shared.DTOs.TaxDocuments` and `@using Stallions.Client.Components.TaxDocuments`.

`src/Client/Services/SubscriptionApiService.cs` — replace `ActivateAsync` (lines 51–59) with:

```csharp
    /// <summary>
    /// Starts the listing-fee payment for one of the stud's stallions. Url is the page to send the browser to,
    /// unless an intro offer made the fee $0 (ActivatedWithoutPayment).
    /// </summary>
    public virtual async Task<Stallions.Shared.DTOs.Payments.PaymentRedirectDto> ActivateAsync(Guid stallionId)
    {
        var r = await _http.PostAsJsonAsync("api/subscriptions/activate", new ActivateStallionRequest { StallionId = stallionId });
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<Stallions.Shared.DTOs.Payments.PaymentRedirectDto>()
               ?? throw new ApiException(500, "Empty response.");
    }
```

- [ ] **Step 4: Shared list component**

```razor
@* src/Client/Components/TaxDocuments/TaxDocumentList.razor *@
@* A list of tax documents (buyer's or stud's). Adjustment notes are shown as negative amounts. *@

<table class="admin-table tax-doc-list">
    <thead>
        <tr>
            <th>Number</th>
            <th>Date</th>
            <th>Description</th>
            <th>Type</th>
            <th class="num">Total (incl. GST)</th>
            <th>Status</th>
        </tr>
    </thead>
    <tbody>
        @foreach (var d in Documents)
        {
            <tr>
                <td><a href="/tax-documents/@d.Id">@d.Number</a></td>
                <td>@d.IssuedOn.ToString("d MMM yyyy", Au)</td>
                <td>@d.Description</td>
                <td>@KindText(d.Kind)</td>
                <td class="num">@SignedTotal(d)</td>
                <td>@StatusText(d)</td>
            </tr>
        }
    </tbody>
</table>

@code {
    [Parameter, EditorRequired] public IReadOnlyList<TaxDocumentSummaryDto> Documents { get; set; } = [];

    private static readonly System.Globalization.CultureInfo Au = System.Globalization.CultureInfo.GetCultureInfo("en-AU");

    public static string KindText(string kind) => kind switch
    {
        "ListingFeeInvoice" => "Listing fee invoice",
        "BuyerFeeInvoice" => "Buyer fee invoice",
        "AdjustmentNote" => "Adjustment note",
        _ => kind
    };

    public static string SignedTotal(TaxDocumentSummaryDto d) =>
        (d.Kind == "AdjustmentNote" ? "−" : "") + d.AmountIncGst.ToString("C2", Au);

    public static string StatusText(TaxDocumentSummaryDto d) =>
        d.ReversedByNumber is { } note ? $"Reversed by {note}"
        : d.Kind == "AdjustmentNote" ? $"Adjusts {d.OriginalNumber}"
        : d.Status == "Unpaid" ? (d.DueOn is { } due ? $"Unpaid — due {due.ToString("d MMM yyyy", Au)}" : "Unpaid")
        : "Paid";
}
```

- [ ] **Step 5: Document page**

```razor
@* src/Client/Pages/TaxDocumentPage.razor *@
@page "/tax-documents/{Id:guid}"
@attribute [Authorize]
@inject TaxDocumentApiService TaxDocumentApi
@inject IJSRuntime JS

<PageTitle>@(_doc is null ? "Tax document" : $"{Heading} {_doc.Number}") — Stallions Australia</PageTitle>

<div class="container tax-doc-page">
    @if (_error is not null)
    {
        <ErrorMessage Message="@_error" OnRetry="Load" />
    }
    else if (_notFound)
    {
        <EmptyState Message="This tax document isn't available." Icon="🧾" />
    }
    else if (_doc is null)
    {
        <LoadingSpinner />
    }
    else
    {
        <div class="tax-doc-actions no-print">
            <button class="btn btn-gold tax-doc-print" @onclick="PrintAsync">Print / Save as PDF</button>
        </div>

        <article class="tax-doc">
            <header class="tax-doc-header">
                <div>
                    <h1 class="tax-doc-title">@Heading</h1>
                    <div class="tax-doc-number">@_doc.Number</div>
                </div>
                <span class="badge @(_doc.Status == "Paid" ? "badge-won" : "badge-ending")">@_doc.Status</span>
            </header>

            <dl class="tax-doc-meta">
                <dt>Issued</dt><dd>@Date(_doc.IssuedOn)</dd>
                @if (_doc.Status == "Unpaid" && _doc.DueOn is { } due)
                {
                    <dt>Due</dt><dd>@Date(due)</dd>
                }
                @if (_doc.Kind != "AdjustmentNote" && _doc.PaidOn is { } paid)
                {
                    <dt>Paid</dt><dd>@Date(paid)</dd>
                }
            </dl>

            <section class="tax-doc-parties">
                <div>
                    <h2>From</h2>
                    <p><strong>@_doc.SupplierName</strong><br />ABN @_doc.SupplierAbn</p>
                    <p class="tax-doc-address">@_doc.SupplierAddress</p>
                    @if (_doc.SupplierEmail is not null) { <p>@_doc.SupplierEmail</p> }
                </div>
                <div>
                    <h2>To</h2>
                    <p>
                        <strong>@_doc.RecipientName</strong>
                        @if (_doc.RecipientAbn is not null) { <br /><text>ABN @_doc.RecipientAbn</text> }
                    </p>
                    @if (_doc.RecipientAddress is not null) { <p class="tax-doc-address">@_doc.RecipientAddress</p> }
                    <p>@_doc.RecipientEmail</p>
                </div>
            </section>

            @if (_doc.Kind == "AdjustmentNote")
            {
                <p class="tax-doc-adjusts">
                    Adjusts tax invoice <a href="/tax-documents/@_doc.OriginalDocumentId">@_doc.OriginalNumber</a>
                    dated @Date(_doc.OriginalIssuedOn). Reason: @_doc.Reason
                </p>
            }

            <table class="tax-doc-lines">
                <thead>
                    <tr><th>Description</th><th class="num">Amount (incl. GST)</th></tr>
                </thead>
                <tbody>
                    @if (_doc.StandardAmountIncGst is { } standard && _doc.DiscountIncGst is { } discount)
                    {
                        <tr><td>@_doc.Description — standard fee</td><td class="num">@Money(standard)</td></tr>
                        <tr><td>Intro offer: @_doc.IntroOfferName</td><td class="num">−@Money(discount)</td></tr>
                        <tr><td><strong>Fee payable</strong></td><td class="num">@Money(_doc.AmountIncGst)</td></tr>
                    }
                    else
                    {
                        <tr><td>@_doc.Description</td><td class="num">@Money(_doc.AmountIncGst)</td></tr>
                    }
                </tbody>
                <tfoot>
                    <tr><td>Amount (excl. GST)</td><td class="num">@Money(_doc.AmountExGst)</td></tr>
                    <tr><td>GST</td><td class="num">@Money(_doc.GstAmount)</td></tr>
                    <tr class="tax-doc-total"><td>Total (incl. GST)</td><td class="num">@Money(_doc.AmountIncGst)</td></tr>
                </tfoot>
            </table>

            @if (!string.IsNullOrWhiteSpace(_doc.FeeNote))
            {
                <p class="tax-doc-note">@_doc.FeeNote</p>
            }

            @if (_doc.Status == "Unpaid")
            {
                <section class="tax-doc-pay">
                    <h2>How to pay</h2>
                    <dl class="tax-doc-meta">
                        <dt>Account name</dt><dd>@_doc.BankAccountName</dd>
                        <dt>BSB</dt><dd>@_doc.BankBsb</dd>
                        <dt>Account number</dt><dd>@_doc.BankAccountNumber</dd>
                    </dl>
                    <p>Please use @_doc.Number as your payment reference.</p>
                </section>
            }

            @if (_doc.ReversedByNumber is not null)
            {
                <p class="tax-doc-note">
                    This tax invoice was reversed by adjustment note
                    <a href="/tax-documents/@_doc.ReversedById">@_doc.ReversedByNumber</a>.
                </p>
            }
        </article>
    }
</div>

@code {
    [Parameter] public Guid Id { get; set; }

    private TaxDocumentDto? _doc;
    private bool _notFound;
    private string? _error;

    private static readonly System.Globalization.CultureInfo Au = System.Globalization.CultureInfo.GetCultureInfo("en-AU");

    private string Heading => _doc?.Kind == "AdjustmentNote" ? "Adjustment note" : "Tax invoice";
    private static string Money(decimal amount) => amount.ToString("C2", Au);
    private static string Date(DateOnly? date) => date?.ToString("d MMM yyyy", Au) ?? string.Empty;

    // The id changes when following the link between an invoice and its adjustment note.
    protected override async Task OnParametersSetAsync() => await Load();

    private async Task Load()
    {
        _error = null;
        _notFound = false;
        _doc = null;
        try
        {
            _doc = await TaxDocumentApi.GetAsync(Id);
            _notFound = _doc is null;
        }
        catch (ApiException) { _error = "Failed to load the tax document. Please try again."; }
        catch (Exception ex) when (ex is HttpRequestException or AccessTokenNotAvailableException)
        {
            _error = ex is AccessTokenNotAvailableException
                ? "Your session has expired — please sign in again."
                : "We couldn't reach the server. Check your connection and try again.";
        }
    }

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");
}
```

```css
/* src/Client/Pages/TaxDocumentPage.razor.css */
.tax-doc-page { padding-block: var(--space-10); max-width: 820px; }
.tax-doc-actions { display: flex; justify-content: flex-end; margin-bottom: var(--space-4); }
.tax-doc { background: var(--white); border: 1px solid var(--border, #e5e7eb); border-radius: 8px; padding: var(--space-8); }
.tax-doc-header { display: flex; justify-content: space-between; align-items: flex-start; gap: var(--space-4); margin-bottom: var(--space-6); }
.tax-doc-title { font-size: var(--font-size-2xl, 1.75rem); margin: 0; }
.tax-doc-number { color: var(--text-secondary); font-size: var(--font-size-lg); }
.tax-doc-meta { display: grid; grid-template-columns: max-content 1fr; gap: var(--space-1) var(--space-4); margin: 0 0 var(--space-6); }
.tax-doc-meta dt { color: var(--text-secondary); }
.tax-doc-meta dd { margin: 0; }
.tax-doc-parties { display: grid; grid-template-columns: 1fr; gap: var(--space-6); margin-bottom: var(--space-6); }
.tax-doc-parties h2, .tax-doc-pay h2 { font-size: var(--font-size-sm); text-transform: uppercase; letter-spacing: 0.06em; color: var(--text-secondary); margin: 0 0 var(--space-2); }
.tax-doc-address { white-space: pre-line; }
.tax-doc-lines { width: 100%; border-collapse: collapse; margin-bottom: var(--space-6); }
.tax-doc-lines th, .tax-doc-lines td { padding: var(--space-2) 0; border-bottom: 1px solid var(--border, #e5e7eb); text-align: left; }
.tax-doc-lines .num { text-align: right; white-space: nowrap; }
.tax-doc-total td { font-weight: 700; border-bottom: 2px solid var(--navy); }
.tax-doc-note, .tax-doc-adjusts { color: var(--text-secondary); }

@media (min-width: 640px) {
    .tax-doc-parties { grid-template-columns: 1fr 1fr; }
}
```

`src/Client/wwwroot/css/app.css` — append (global: scoped CSS can't reach the nav bar and footer, and `::deep` doesn't work inside `@media`):

```css
/* ── Printing a tax document: only the document itself ─────────────────── */
@media print {
  .navbar, .footer, .no-print { display: none !important; }
  body, .content-area { background: #fff !important; }
  .tax-doc { border: none !important; padding: 0 !important; }
}
```

- [ ] **Step 6: Buyer's list and navigation**

```razor
@* src/Client/Pages/MyTaxInvoices.razor *@
@page "/account/tax-invoices"
@attribute [Authorize]
@inject TaxDocumentApiService TaxDocumentApi

<PageTitle>My tax invoices — Stallions Australia</PageTitle>

<div class="container" style="padding-block: var(--space-10)">
    <h1 class="page-title">My tax invoices</h1>
    <p class="text-secondary text-sm">Tax invoices for the buyer fees you've paid, and any adjustment notes.</p>

    @if (_error is not null)
    {
        <ErrorMessage Message="@_error" OnRetry="Load" />
    }
    else if (_documents is null)
    {
        <LoadingSpinner />
    }
    else if (_documents.Count == 0)
    {
        <EmptyState Message="No tax invoices yet" Icon="🧾" />
    }
    else
    {
        <TaxDocumentList Documents="_documents" />
    }
</div>

@code {
    private List<TaxDocumentSummaryDto>? _documents;
    private string? _error;

    protected override async Task OnInitializedAsync() => await Load();

    private async Task Load()
    {
        _error = null;
        _documents = null;
        try { _documents = await TaxDocumentApi.GetMineAsync() ?? []; }
        catch (ApiException) { _error = "Failed to load your tax invoices. Please try again."; }
        catch (Exception ex) when (ex is HttpRequestException or AccessTokenNotAvailableException)
        {
            _error = ex is AccessTokenNotAvailableException
                ? "Your session has expired — please sign in again."
                : "We couldn't reach the server. Check your connection and try again.";
        }
    }
}
```

`src/Client/Layout/NavBar.razor` — in the desktop buyer block (lines 28–32) and the drawer buyer block (lines 68–72), add a link after "My Offers":

```razor
                        <NavLink href="/account/tax-invoices" class="nav-link">Tax invoices</NavLink>
```

(drawer: `<NavLink href="/account/tax-invoices" class="drawer-link" @onclick="CloseDrawer">Tax invoices</NavLink>`).

- [ ] **Step 7: Stud — tax invoices section and activation by intro offer**

`src/Client/Pages/Admin/AdminStallions.razor`:

Add `@inject TaxDocumentApiService TaxDocumentApi` after line 5.

After line 128 (the `}` closing the "Your Stable" `else` block) add:

```razor

    @* ── Tax invoices (listing fees) ─────────────────────────────────────── *@
    <h2 style="font-size:var(--font-size-lg);margin:var(--space-8) 0 var(--space-4)">Tax invoices</h2>
    @if (_taxDocuments.Count == 0)
    {
        <p style="color:var(--text-muted)">No tax invoices yet. Listing fee tax invoices appear here once issued.</p>
    }
    else
    {
        <TaxDocumentList Documents="_taxDocuments" />
    }
```

In `@code` add the field `private List<TaxDocumentSummaryDto> _taxDocuments = [];` and in `Load()`, after
`_subscriptions = await SubscriptionApi.GetMineAsync() ?? [];` add:

```csharp
            _taxDocuments = await TaxDocumentApi.GetForStudAsync() ?? [];
```

Replace `ActivateAsync` (lines 313–320) with:

```csharp
    private async Task ActivateAsync(Guid stallionId)
    {
        _activating = stallionId; _activateError = null; _paymentNotice = null;
        try
        {
            var result = await SubscriptionApi.ActivateAsync(stallionId);
            if (result.ActivatedWithoutPayment)
            {
                // An intro offer made the fee $0: the stallion is already active, no payment page.
                var name = _stallions.FirstOrDefault(s => s.Id == stallionId)?.Name ?? "Your stallion";
                _paymentNotice = $"{name} is active for {_openSeason?.Name} — intro offer applied.";
                _subscriptions = await SubscriptionApi.GetMineAsync() ?? [];
                _activating = null;
                return;
            }
            // Stripe returns an absolute URL, the dev fake a relative one — both need a full page load.
            // The buttons stay disabled on success: the browser is leaving the page.
            Nav.NavigateTo(result.Url, forceLoad: true);
        }
        catch (ApiException ex) { _activateError = ex.Message; _activating = null; }
    }
```

- [ ] **Step 8: Run green** — the client tests above and every existing `AdminStallionsTests` test; full build (0 warnings) and full test run.

- [ ] **Step 9: Commit**

```bash
git add src/Client/Services/TaxDocumentApiService.cs src/Client/Services/SubscriptionApiService.cs src/Client/Program.cs \
  src/Client/_Imports.razor src/Client/Pages/TaxDocumentPage.razor src/Client/Pages/TaxDocumentPage.razor.css \
  src/Client/Pages/MyTaxInvoices.razor src/Client/Components/TaxDocuments src/Client/Layout/NavBar.razor \
  src/Client/wwwroot/css/app.css src/Client/Pages/Admin/AdminStallions.razor \
  tests/Client.Tests/Services/TaxDocumentApiServiceTests.cs tests/Client.Tests/Pages/TaxDocumentPageTests.cs \
  tests/Client.Tests/Pages/MyTaxInvoicesTests.cs tests/Client.Tests/Pages/AdminStallionsTests.cs
git status --short
git commit -m "feat: client — printable tax document page, buyer and stud tax invoice lists, activation by intro offer

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 10: Client Staff — Intro offers page; Listing Fees page shows the offer, issues tax invoices

**Files:**
- Create: `src/Client/Services/IntroOfferApiService.cs`, `src/Client/Pages/Staff/StaffIntroOffers.razor`
- Modify: `src/Client/Program.cs` (register), `src/Client/_Imports.razor` (`@using Stallions.Shared.DTOs.IntroOffers`),
  `src/Client/Layout/StaffLayout.razor` (new link after line 34), `src/Client/Services/SubscriptionApiService.cs` (`IssueInvoiceAsync`),
  `src/Client/Pages/Staff/StaffSubscriptions.razor` (lines 4–8, 17–20, 56–99, 128–148, 161–196, `@code`)
- Test: `tests/Client.Tests/Pages/StaffIntroOffersTests.cs`, `tests/Client.Tests/Pages/StaffSubscriptionsTests.cs`

**Interfaces:**
- Consumes: Task 2 `api/intro-offers` routes and DTOs; Task 6 `POST api/subscriptions/{id}/issue-invoice`, `SubscriptionDto.TaxInvoice*`, `.AdjustmentNoteNumber`;
  Task 3 `SubscriptionDto.StandardFeeIncGst`, `.IntroOfferName`.
- Produces: `IntroOfferApiService { virtual GetAllAsync(); CreateAsync(SaveIntroOfferRequest); UpdateAsync(Guid, SaveIntroOfferRequest); RetireAsync(Guid); PreviewAsync(Guid studFarmId, Guid seasonId) }`;
  `SubscriptionApiService.IssueInvoiceAsync(Guid)` → `SubscriptionDto`; page `/staff/intro-offers`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Client.Tests/Pages/StaffIntroOffersTests.cs
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Staff;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Admin;
using Stallions.Shared.DTOs.IntroOffers;
using Stallions.Shared.DTOs.Seasons;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class StaffIntroOffersTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };
    private readonly Mock<IntroOfferApiService> _api = new(MockBehavior.Loose, Http());

    private static IntroOfferDto Launch(int timesApplied = 0) => new()
    {
        Id = Guid.NewGuid(), Name = "Launch 2026", Type = "AmountOff", AmountOffIncGst = 495m,
        EndsOn = new DateOnly(2026, 12, 31), State = "Active", TimesApplied = timesApplied
    };

    private IRenderedComponent<StaffIntroOffers> Render(params IntroOfferDto[] offers)
    {
        this.AddTestAuthorization().SetAuthorized("staff@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.Setup(a => a.GetAllAsync()).ReturnsAsync(offers.ToList());
        _api.Setup(a => a.CreateAsync(It.IsAny<SaveIntroOfferRequest>())).ReturnsAsync(new IntroOfferDto());
        Services.AddSingleton(_api.Object);

        var staffApi = new Mock<StaffApiService>(MockBehavior.Loose, Http());
        staffApi.Setup(s => s.GetStudFarmsAsync()).ReturnsAsync([new StudFarmSummaryDto { Id = Guid.NewGuid(), Name = "Arrowfield" }]);
        staffApi.Setup(s => s.GetSeasonsAsync()).ReturnsAsync([new SeasonDto { Id = Guid.NewGuid(), Name = "2026 Season", IsOpen = true }]);
        Services.AddSingleton(staffApi.Object);

        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "Staff", Email = "staff@example.com", Role = "Staff", Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));

        return RenderComponent<StaffIntroOffers>();
    }

    private static void Click(IRenderedComponent<StaffIntroOffers> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == text).Click();

    [Fact]
    public void Lists_OffersWithTheirTermsScopeStateAndUse()
    {
        var retired = new IntroOfferDto
        {
            Id = Guid.NewGuid(), Name = "Founding studs", Type = "Waiver", StudFarmName = "Arrowfield", SeasonName = "2026 Season",
            EndsOn = new DateOnly(2026, 11, 30), State = "Retired", IsRetired = true, TimesApplied = 3
        };

        var cut = Render(Launch(), retired);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Launch 2026"));
        cut.Markup.Should().Contain("$495.00 off").And.Contain("Fee waived").And.Contain("All studs · Any season")
            .And.Contain("Arrowfield · 2026 Season").And.Contain("31 Dec 2026").And.Contain("Retired");
        cut.FindAll("button").Count(b => b.TextContent.Trim() == "Edit").Should().Be(1);
    }

    [Fact]
    public void Create_AmountOffWithoutAnAmount_ShowsAnError_AndDoesNotSave()
    {
        var cut = Render();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No intro offers yet."));
        Click(cut, "+ New intro offer");

        cut.Find("#offer-name").Change("Launch");
        Click(cut, "Save intro offer");

        cut.Markup.Should().Contain("Enter the dollars off the listing fee (more than $0).");
        _api.Verify(a => a.CreateAsync(It.IsAny<SaveIntroOfferRequest>()), Times.Never);
    }

    [Fact]
    public void Create_AWaiver_SendsTheRequest()
    {
        var cut = Render();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No intro offers yet."));
        Click(cut, "+ New intro offer");

        cut.Find("#offer-name").Change("Founding studs");
        cut.Find("#offer-type").Change("Waiver");
        cut.Find("#offer-ends").Change("2026-12-31");
        Click(cut, "Save intro offer");

        _api.Verify(a => a.CreateAsync(It.Is<SaveIntroOfferRequest>(r => r.Name == "Founding studs" && r.Type == "Waiver"
            && r.AmountOffIncGst == null && r.StudFarmId == null && r.SeasonId == null
            && r.EndsOn == new DateOnly(2026, 12, 31))), Times.Once);
    }

    [Fact]
    public void Edit_AnAppliedOffer_LocksItsTerms()
    {
        var cut = Render(Launch(timesApplied: 2));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Launch 2026"));

        Click(cut, "Edit");

        cut.Markup.Should().Contain("only the name and end date can change");
        cut.Find("#offer-type").HasAttribute("disabled").Should().BeTrue();
        cut.Find("#offer-amount").HasAttribute("disabled").Should().BeTrue();
        cut.Find("#offer-name").HasAttribute("disabled").Should().BeFalse();
        cut.Find("#offer-ends").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Retire_AsksFirst_ThenRetires()
    {
        var offer = Launch();
        _api.Setup(a => a.RetireAsync(offer.Id)).ReturnsAsync(offer);
        var cut = Render(offer);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Launch 2026"));

        Click(cut, "Retire");

        cut.Markup.Should().Contain("Retire Launch 2026?");
        _api.Verify(a => a.RetireAsync(It.IsAny<Guid>()), Times.Never);

        Click(cut, "Retire offer");

        _api.Verify(a => a.RetireAsync(offer.Id), Times.Once);
    }
}
```

```csharp
// tests/Client.Tests/Pages/StaffSubscriptionsTests.cs
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Staff;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Admin;
using Stallions.Shared.DTOs.IntroOffers;
using Stallions.Shared.DTOs.Seasons;
using Stallions.Shared.DTOs.Settings;
using Stallions.Shared.DTOs.Stallions;
using Stallions.Shared.DTOs.Subscriptions;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class StaffSubscriptionsTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };
    private readonly Mock<SubscriptionApiService> _subs = new(MockBehavior.Loose, Http());
    private readonly Mock<IntroOfferApiService> _offers = new(MockBehavior.Loose, Http());
    private readonly StudFarmSummaryDto _farm = new() { Id = Guid.NewGuid(), Name = "Arrowfield" };
    private readonly SeasonDto _season = new() { Id = Guid.NewGuid(), Name = "2026 Season", IsOpen = true };

    private SubscriptionDto Pending() => new()
    {
        Id = Guid.NewGuid(), StallionName = "Dundeel", StudFarmName = "Arrowfield", SeasonName = "2026 Season",
        FeeIncGst = 990m, FeeExGst = 900m, GstAmount = 90m, StandardFeeIncGst = 990m, Status = "Pending"
    };

    private IRenderedComponent<StaffSubscriptions> Render(params SubscriptionDto[] subscriptions)
    {
        this.AddTestAuthorization().SetAuthorized("staff@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        _subs.Setup(s => s.GetAllAsync(It.IsAny<Guid?>(), It.IsAny<string?>())).ReturnsAsync(subscriptions.ToList());
        Services.AddSingleton(_subs.Object);
        Services.AddSingleton(_offers.Object);

        var staffApi = new Mock<StaffApiService>(MockBehavior.Loose, Http());
        staffApi.Setup(s => s.GetSeasonsAsync()).ReturnsAsync([_season]);
        staffApi.Setup(s => s.GetStudFarmsAsync()).ReturnsAsync([_farm]);
        staffApi.Setup(s => s.GetStudFarmStallionsAsync(_farm.Id))
            .ReturnsAsync([new StallionSummaryDto { Id = Guid.NewGuid(), Name = "Dundeel", StudFarmId = _farm.Id }]);
        Services.AddSingleton(staffApi.Object);

        var settings = new Mock<PlatformSettingsApiService>(MockBehavior.Loose, Http());
        settings.Setup(s => s.GetAsync()).ReturnsAsync(new PlatformSettingsDto { StandardListingFeeIncGst = 990m });
        Services.AddSingleton(settings.Object);

        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "Staff", Email = "staff@example.com", Role = "Staff", Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));

        return RenderComponent<StaffSubscriptions>();
    }

    private static void Click(IRenderedComponent<StaffSubscriptions> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim().StartsWith(text)).Click();

    [Fact]
    public void Create_ShowsTheIntroOfferThatWillApply()
    {
        _offers.Setup(o => o.PreviewAsync(_farm.Id, _season.Id)).ReturnsAsync(new IntroOfferPreviewDto
            { StandardFeeIncGst = 990m, FeeIncGst = 495m, IntroOfferName = "Launch 2026" });
        var cut = Render();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No subscriptions match"));
        Click(cut, "+ New subscription");

        cut.Find("#new-farm").Change(_farm.Id.ToString());

        cut.WaitForAssertion(() => cut.Find(".intro-offer-preview").TextContent
            .Should().Contain("Intro offer Launch 2026 applies: $990.00 → $495.00"));
    }

    [Fact]
    public void Create_WhenTheOfferWaivesTheFee_SaysSo()
    {
        _offers.Setup(o => o.PreviewAsync(_farm.Id, _season.Id)).ReturnsAsync(new IntroOfferPreviewDto
            { StandardFeeIncGst = 990m, FeeIncGst = 0m, IntroOfferName = "Founding studs" });
        var cut = Render();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No subscriptions match"));
        Click(cut, "+ New subscription");

        cut.Find("#new-farm").Change(_farm.Id.ToString());

        cut.WaitForAssertion(() => cut.Find(".intro-offer-preview").TextContent.Should().Contain("the fee will be waived"));
    }

    [Fact]
    public void IssueTaxInvoice_ConfirmsFirst_ThenIssuesAndShowsTheNumber()
    {
        var sub = Pending();
        _subs.Setup(s => s.IssueInvoiceAsync(sub.Id)).ReturnsAsync(new SubscriptionDto
            { Id = sub.Id, Status = "Pending", TaxInvoiceNumber = "SA-000001", TaxInvoiceStatus = "Unpaid" });
        var cut = Render(sub);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Dundeel"));

        Click(cut, "Issue tax invoice");

        cut.Markup.Should().Contain("Emails Arrowfield an unpaid tax invoice for $990.00");
        _subs.Verify(s => s.IssueInvoiceAsync(It.IsAny<Guid>()), Times.Never);

        Click(cut, "Issue and email");

        _subs.Verify(s => s.IssueInvoiceAsync(sub.Id), Times.Once);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Tax invoice SA-000001 issued and emailed to Arrowfield."));
    }

    [Fact]
    public void AnInvoicedSubscription_LinksItsTaxInvoice_AndCannotBeInvoicedAgain()
    {
        var sub = Pending();
        sub.TaxInvoiceId = Guid.NewGuid();
        sub.TaxInvoiceNumber = "SA-000001";
        sub.TaxInvoiceStatus = "Unpaid";
        sub.IntroOfferName = "Launch 2026";

        var cut = Render(sub);

        cut.WaitForAssertion(() => cut.Find($"a[href='/tax-documents/{sub.TaxInvoiceId}']").TextContent.Should().Be("SA-000001"));
        cut.Markup.Should().Contain("Intro offer: Launch 2026");
        cut.FindAll("button").Should().NotContain(b => b.TextContent.Contains("Issue tax invoice"));
    }
}
```

- [ ] **Step 2: Run red** — `dotnet test tests/Client.Tests --filter "FullyQualifiedName~StaffIntroOffersTests|FullyQualifiedName~StaffSubscriptionsTests"` → compile errors.

- [ ] **Step 3: API services**

```csharp
// src/Client/Services/IntroOfferApiService.cs
using System.Net.Http.Json;
using Stallions.Shared.DTOs.IntroOffers;

namespace Stallions.Client.Services;

/// <summary>Staff-only: intro offers on the listing fee, and the fee preview for a new subscription.</summary>
public class IntroOfferApiService
{
    private readonly HttpClient _http;
    public IntroOfferApiService(HttpClient http) => _http = http;

    public virtual async Task<List<IntroOfferDto>> GetAllAsync()
    {
        var r = await _http.GetAsync("api/intro-offers");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<List<IntroOfferDto>>() ?? [];
    }

    public virtual async Task<IntroOfferDto> CreateAsync(SaveIntroOfferRequest request) =>
        await ReadOffer(await _http.PostAsJsonAsync("api/intro-offers", request));

    public virtual async Task<IntroOfferDto> UpdateAsync(Guid id, SaveIntroOfferRequest request) =>
        await ReadOffer(await _http.PutAsJsonAsync($"api/intro-offers/{id}", request));

    public virtual async Task<IntroOfferDto> RetireAsync(Guid id) =>
        await ReadOffer(await _http.PostAsync($"api/intro-offers/{id}/retire", null));

    public virtual async Task<IntroOfferPreviewDto> PreviewAsync(Guid studFarmId, Guid seasonId)
    {
        var r = await _http.GetAsync($"api/intro-offers/preview?studFarmId={studFarmId}&seasonId={seasonId}");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<IntroOfferPreviewDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    private static async Task<IntroOfferDto> ReadOffer(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<IntroOfferDto>()
               ?? throw new ApiException(500, "Empty response.");
    }
}
```

`src/Client/Services/SubscriptionApiService.cs` — add after `WaiveAsync`:

```csharp
    /// <summary>Staff: email the stud an unpaid tax invoice for a Pending subscription.</summary>
    public virtual async Task<SubscriptionDto> IssueInvoiceAsync(Guid id)
    {
        var r = await _http.PostAsync($"api/subscriptions/{id}/issue-invoice", null);
        return await ReadSubscription(r);
    }
```

`src/Client/Program.cs` — after the `TaxDocumentApiService` registration add:

```csharp
builder.Services.AddHttpClient<IntroOfferApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
```

`src/Client/_Imports.razor` — add `@using Stallions.Shared.DTOs.IntroOffers`.

`src/Client/Layout/StaffLayout.razor` — after line 34 (the `</NavLink>` closing "Listing Fees") add:

```razor
            <NavLink href="/staff/intro-offers" class="admin-nav-link" Match="NavLinkMatch.Prefix">
                <span class="admin-nav-icon">🏷️</span> Intro Offers
            </NavLink>
```

- [ ] **Step 4: Intro offers page**

```razor
@* src/Client/Pages/Staff/StaffIntroOffers.razor *@
@page "/staff/intro-offers"
@layout StaffLayout
@attribute [Authorize]
@inject IntroOfferApiService IntroOfferApi
@inject StaffApiService StaffApi
@inject UserStateService UserState
@inject NavigationManager Nav

<PageTitle>Intro Offers — Staff Admin</PageTitle>

<div class="admin-page-header">
    <h1 class="admin-page-title">Intro Offers</h1>
    <button class="btn btn-gold" @onclick="OpenCreate">+ New intro offer</button>
</div>

<p class="text-secondary text-sm">
    An intro offer waives the listing fee or takes dollars off it. It applies automatically to new listing fee
    subscriptions created up to the end of its last day (Sydney time); when several match, the lowest fee wins.
    A fee typed by hand on a new subscription ignores offers. Once an offer has been applied, only its name and
    last day can change.
</p>

@if (_loading)
{
    <p>Loading…</p>
}
else if (_error is not null)
{
    <div class="alert alert-danger">@_error</div>
}
else if (_offers.Count == 0)
{
    <div class="admin-empty-state"><p>No intro offers yet.</p></div>
}
else
{
    <table class="admin-table">
        <thead>
            <tr>
                <th>Name</th>
                <th>Offer</th>
                <th>Applies to</th>
                <th>Last day</th>
                <th>State</th>
                <th>Applied</th>
                <th>Actions</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var o in _offers)
            {
                <tr>
                    <td>@o.Name</td>
                    <td>@OfferText(o)</td>
                    <td>@(o.StudFarmName ?? "All studs") · @(o.SeasonName ?? "Any season")</td>
                    <td>@o.EndsOn.ToString("d MMM yyyy", AuCulture)</td>
                    <td><span class="badge @StateBadge(o.State)">@o.State</span></td>
                    <td>@o.TimesApplied</td>
                    <td>
                        @if (!o.IsRetired)
                        {
                            <button class="btn btn-sm btn-outline" @onclick="() => OpenEdit(o)">Edit</button>
                            <button class="btn btn-sm btn-outline" @onclick="() => OpenRetire(o)">Retire</button>
                        }
                    </td>
                </tr>
            }
        </tbody>
    </table>
}

<Modal IsOpen="@_formOpen" Title="@(_editing is null ? "New intro offer" : $"Edit intro offer — {_editing.Name}")" OnClose="CloseModals">
    @if (Locked)
    {
        <div class="alert alert-warning">
            Applied to @_editing!.TimesApplied subscription(s), so only the name and end date can change.
        </div>
    }
    <div class="form-group">
        <label class="form-label" for="offer-name">Name <span class="required">*</span></label>
        <input id="offer-name" class="form-input" maxlength="100" @bind="_name" placeholder="e.g. Launch 2026" />
        <p class="form-hint">Shown on the tax invoice as "Intro offer: name".</p>
    </div>
    <div class="form-group">
        <label class="form-label" for="offer-type">Offer</label>
        <select id="offer-type" class="form-select" @bind="_type" disabled="@Locked">
            <option value="AmountOff">Dollars off the listing fee</option>
            <option value="Waiver">Waive the listing fee</option>
        </select>
    </div>
    @if (_type == "AmountOff")
    {
        <div class="form-group">
            <label class="form-label" for="offer-amount">Amount off (inc. GST)</label>
            <div class="input-prefix">
                <span>$</span>
                <input id="offer-amount" type="number" class="form-input" min="0.01" step="0.01" @bind="_amount" disabled="@Locked" />
            </div>
            <p class="form-hint">At or above the standard listing fee, the fee is waived.</p>
        </div>
    }
    <div class="form-group">
        <label class="form-label" for="offer-farm">Stud farm</label>
        <select id="offer-farm" class="form-select" @bind="_farmId" disabled="@Locked">
            <option value="">All studs</option>
            @foreach (var f in _farms)
            {
                <option value="@f.Id">@f.Name</option>
            }
        </select>
    </div>
    <div class="form-group">
        <label class="form-label" for="offer-season">Season</label>
        <select id="offer-season" class="form-select" @bind="_seasonId" disabled="@Locked">
            <option value="">Any season</option>
            @foreach (var s in _seasons)
            {
                <option value="@s.Id">@s.Name</option>
            }
        </select>
    </div>
    <div class="form-group">
        <label class="form-label" for="offer-ends">Last day (Sydney time)</label>
        <input id="offer-ends" type="date" class="form-input" @bind="_endsOn" />
        <p class="form-hint">Applies to subscriptions created up to 11:59 pm on this day.</p>
    </div>
    @if (_modalError is not null) { <div class="alert alert-danger">@_modalError</div> }
    <div class="form-actions">
        <button class="btn btn-gold" @onclick="SaveAsync" disabled="@_busy">@(_busy ? "Saving…" : "Save intro offer")</button>
        <button class="btn btn-outline" @onclick="CloseModals" disabled="@_busy">Cancel</button>
    </div>
</Modal>

<Modal IsOpen="@(_retireFor is not null)" Title="@($"Retire {_retireFor?.Name}?")" OnClose="CloseModals">
    <p>It stops applying to new subscriptions straight away. Subscriptions it was already applied to keep it.</p>
    @if (_modalError is not null) { <div class="alert alert-danger">@_modalError</div> }
    <div class="form-actions">
        <button class="btn btn-gold" @onclick="RetireAsync" disabled="@_busy">@(_busy ? "Retiring…" : "Retire offer")</button>
        <button class="btn btn-outline" @onclick="CloseModals" disabled="@_busy">Cancel</button>
    </div>
</Modal>

@code {
    private List<IntroOfferDto> _offers = [];
    private List<StudFarmSummaryDto> _farms = [];
    private List<SeasonDto> _seasons = [];
    private bool _loading = true;
    private bool _busy;
    private bool _formOpen;
    private string? _error;
    private string? _modalError;

    private IntroOfferDto? _editing;
    private IntroOfferDto? _retireFor;
    private string _name = string.Empty;
    private string _type = "AmountOff";
    private decimal? _amount;
    private string _farmId = string.Empty;
    private string _seasonId = string.Empty;
    private DateOnly _endsOn;

    private static readonly System.Globalization.CultureInfo AuCulture =
        System.Globalization.CultureInfo.GetCultureInfo("en-AU");

    // Recorded subscriptions name the offer: its terms stay as they were when applied.
    private bool Locked => _editing is { TimesApplied: > 0 };

    protected override async Task OnInitializedAsync()
    {
        await UserState.LoadAsync();
        if (!UserState.IsStaff) { Nav.NavigateTo("/", replace: true); return; }
        try
        {
            _farms = await StaffApi.GetStudFarmsAsync();
            _seasons = await StaffApi.GetSeasonsAsync();
        }
        catch (Exception ex) { _error = ex is ApiException ae ? ae.Message : "Failed to load page data."; }
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try { _offers = await IntroOfferApi.GetAllAsync(); }
        catch (Exception ex) { _error = ex is ApiException ae ? ae.Message : "Failed to load intro offers."; }
        finally { _loading = false; }
    }

    private static string OfferText(IntroOfferDto o) =>
        o.Type == "Waiver" ? "Fee waived" : $"{(o.AmountOffIncGst ?? 0m).ToString("C2", AuCulture)} off";

    private static string StateBadge(string state) => state switch
    {
        "Active" => "badge-sub-paid",
        "Ended" => "badge-sub-none",
        _ => "badge-sub-waived"
    };

    private void OpenCreate()
    {
        CloseModals();
        _name = string.Empty;
        _type = "AmountOff";
        _amount = null;
        _farmId = string.Empty;
        _seasonId = string.Empty;
        _endsOn = DateOnly.FromDateTime(DateTime.Today.AddMonths(1));
        _formOpen = true;
    }

    private void OpenEdit(IntroOfferDto offer)
    {
        CloseModals();
        _editing = offer;
        _name = offer.Name;
        _type = offer.Type;
        _amount = offer.AmountOffIncGst;
        _farmId = offer.StudFarmId?.ToString() ?? string.Empty;
        _seasonId = offer.SeasonId?.ToString() ?? string.Empty;
        _endsOn = offer.EndsOn;
        _formOpen = true;
    }

    private void OpenRetire(IntroOfferDto offer)
    {
        CloseModals();
        _retireFor = offer;
    }

    // Mirrors the server's checks (which remain the authority).
    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(_name)) return "A name is required.";
        if (_type == "AmountOff" && _amount is not > 0m) return "Enter the dollars off the listing fee (more than $0).";
        return null;
    }

    private async Task SaveAsync()
    {
        _modalError = Validate();
        if (_modalError is not null) return;

        var request = new SaveIntroOfferRequest
        {
            Name = _name.Trim(),
            Type = _type,
            AmountOffIncGst = _type == "AmountOff" ? _amount : null,
            StudFarmId = Guid.TryParse(_farmId, out var farmId) ? farmId : null,
            SeasonId = Guid.TryParse(_seasonId, out var seasonId) ? seasonId : null,
            EndsOn = _endsOn
        };
        _busy = true;
        try
        {
            if (_editing is null) await IntroOfferApi.CreateAsync(request);
            else await IntroOfferApi.UpdateAsync(_editing.Id, request);
            CloseModals();
            await LoadAsync();
        }
        catch (Exception ex) { _modalError = ex is ApiException ae ? ae.Message : "Failed to save the intro offer."; }
        finally { _busy = false; }
    }

    private async Task RetireAsync()
    {
        if (_retireFor is null) return;
        _busy = true;
        try
        {
            await IntroOfferApi.RetireAsync(_retireFor.Id);
            CloseModals();
            await LoadAsync();
        }
        catch (Exception ex) { _modalError = ex is ApiException ae ? ae.Message : "Failed to retire the intro offer."; }
        finally { _busy = false; }
    }

    private void CloseModals()
    {
        _formOpen = false;
        _editing = null;
        _retireFor = null;
        _modalError = null;
    }
}
```

- [ ] **Step 5: Listing Fees page**

`src/Client/Pages/Staff/StaffSubscriptions.razor`:

After line 6 (`@inject PlatformSettingsApiService SettingsApi`) add `@inject IntroOfferApiService IntroOfferApi`.

After line 20 (the closing `</p>` of the intro paragraph) add:

```razor

@if (_notice is not null) { <div class="alert alert-success">@_notice</div> }
```

Table header (lines 56–64): after `<th>Payment</th>` add `<th>Tax invoice</th>`.

Fee cell (lines 73–76) becomes:

```razor
                    <td>
                        @Money(s.FeeIncGst)
                        <div class="text-muted text-xs">ex GST @Money(s.FeeExGst) · GST @Money(s.GstAmount)</div>
                        @if (s.IntroOfferName is not null)
                        {
                            <div class="text-muted text-xs">Intro offer: @s.IntroOfferName (standard @Money(s.StandardFeeIncGst))</div>
                        }
                    </td>
```

After the Payment cell (line 92, its closing `</td>`) add:

```razor
                    <td class="text-sm">
                        @if (s.TaxInvoiceId is { } invoiceId)
                        {
                            <a href="/tax-documents/@invoiceId">@s.TaxInvoiceNumber</a>
                            <span class="text-muted"> · @s.TaxInvoiceStatus</span>
                            @if (s.AdjustmentNoteNumber is not null)
                            {
                                <div class="text-muted text-xs">Reversed by @s.AdjustmentNoteNumber</div>
                            }
                        }
                        else
                        {
                            <text>—</text>
                        }
                    </td>
```

Actions cell (lines 93–99) becomes:

```razor
                    <td>
                        @if (s.Status == "Pending")
                        {
                            <button class="btn btn-sm btn-gold" @onclick="() => OpenMarkPaid(s)">Mark paid</button>
                            <button class="btn btn-sm btn-outline" @onclick="() => OpenWaive(s)">Waive</button>
                            @if (s.TaxInvoiceId is null && s.FeeIncGst > 0)
                            {
                                <button class="btn btn-sm btn-outline" @onclick="() => OpenIssueInvoice(s)">Issue tax invoice</button>
                            }
                        }
                    </td>
```

Season select (line 130) becomes `<select id="new-season" class="form-select" value="@_newSeasonId" @onchange="OnNewSeasonChanged">`.

The fee group (lines 138–148) becomes:

```razor
    <div class="form-group">
        <label class="form-label" for="new-fee">Listing fee (inc. GST)</label>
        <div class="input-prefix">
            <span>$</span>
            <input id="new-fee" type="number" class="form-input" min="0.01" step="0.01" @bind="_newFee" />
        </div>
        @if (_preview is { IntroOfferName: not null } preview && _newFee == _standardFee)
        {
            <p class="form-hint intro-offer-preview">
                Intro offer <strong>@preview.IntroOfferName</strong> applies: @Money(preview.StandardFeeIncGst) → @Money(preview.FeeIncGst)@(preview.FeeIncGst == 0m ? " (the fee will be waived)" : "")
            </p>
        }
        <p class="form-hint">
            Leave the standard fee (@Money(_standardFee)) and the best intro offer applies automatically. Type a different
            fee to set it by hand — no intro offer is then recorded. Use Waive to remove the fee entirely.
        </p>
    </div>
```

In the Mark paid modal, after the `<p>Fee: …</p>` line (line 163) add:

```razor
    <p class="form-hint">
        @if (_markPaidFor?.TaxInvoiceNumber is { } invoice)
        {
            <text>Tax invoice @invoice is marked paid and a receipt is emailed to the stud.</text>
        }
        else
        {
            <text>A paid tax invoice is emailed to the stud.</text>
        }
    </p>
```

In the Waive modal, after its first `<p>` (line 185) add:

```razor
    @if (_waiveFor?.TaxInvoiceNumber is { } waivedInvoice)
    {
        <p class="form-hint">Tax invoice @waivedInvoice is reversed by an adjustment note emailed to the stud.</p>
    }
```

After the Waive modal (line 196) add:

```razor

@* ── Issue tax invoice ────────────────────────────────────────────────── *@
<Modal IsOpen="@(_issueFor is not null)" Title="@($"Issue tax invoice — {_issueFor?.StallionName}")" OnClose="CloseModals">
    <p>
        Emails @_issueFor?.StudFarmName an unpaid tax invoice for @Money(_issueFor?.FeeIncGst ?? 0) (@_issueFor?.SeasonName),
        due within the payment terms set in Settings, with the bank details to pay by transfer.
    </p>
    @if (_modalError is not null) { <div class="alert alert-danger">@_modalError</div> }
    <div class="form-actions">
        <button class="btn btn-gold" @onclick="IssueInvoiceAsync" disabled="@_busy">@(_busy ? "Issuing…" : "Issue and email")</button>
        <button class="btn btn-outline" @onclick="CloseModals" disabled="@_busy">Cancel</button>
    </div>
</Modal>
```

In `@code` add fields:

```csharp
    private IntroOfferPreviewDto? _preview;
    private SubscriptionDto? _issueFor;
    private string? _notice;
```

In `OpenCreate()` add `_preview = null;`. Replace `OnNewFarmChanged` (lines 280–288) with:

```csharp
    private async Task OnNewFarmChanged(ChangeEventArgs e)
    {
        _newFarmId = e.Value?.ToString() ?? string.Empty;
        _newStallionId = Guid.Empty;
        _farmStallions = [];
        _preview = null;
        if (!Guid.TryParse(_newFarmId, out var farmId)) return;
        try { _farmStallions = await StaffApi.GetStudFarmStallionsAsync(farmId); }
        catch (Exception ex) { _modalError = ex is ApiException ae ? ae.Message : "Failed to load stallions."; }
        await LoadPreviewAsync();
    }

    private async Task OnNewSeasonChanged(ChangeEventArgs e)
    {
        _newSeasonId = Guid.TryParse(e.Value?.ToString(), out var id) ? id : Guid.Empty;
        await LoadPreviewAsync();
    }

    // A hint only: the server applies the best offer when the subscription is created.
    private async Task LoadPreviewAsync()
    {
        _preview = null;
        if (!Guid.TryParse(_newFarmId, out var farmId) || _newSeasonId == Guid.Empty) return;
        try { _preview = await IntroOfferApi.PreviewAsync(farmId, _newSeasonId); }
        catch (ApiException) { _preview = null; }
    }

    private void OpenIssueInvoice(SubscriptionDto s)
    {
        CloseModals();
        _issueFor = s;
    }

    private async Task IssueInvoiceAsync()
    {
        if (_issueFor is null) return;
        _busy = true; _modalError = null; _notice = null;
        try
        {
            var issued = await SubscriptionApi.IssueInvoiceAsync(_issueFor.Id);
            _notice = $"Tax invoice {issued.TaxInvoiceNumber} issued and emailed to {_issueFor.StudFarmName}.";
            CloseModals();
            await LoadAsync();
        }
        catch (Exception ex) { _modalError = ex is ApiException ae ? ae.Message : "Failed to issue the tax invoice."; }
        finally { _busy = false; }
    }
```

In `CloseModals()` add `_issueFor = null;`.

- [ ] **Step 6: Run green** — the two new test classes; full build (0 warnings) and full test run.

- [ ] **Step 7: Commit**

```bash
git add src/Client/Services/IntroOfferApiService.cs src/Client/Services/SubscriptionApiService.cs src/Client/Program.cs \
  src/Client/_Imports.razor src/Client/Layout/StaffLayout.razor src/Client/Pages/Staff/StaffIntroOffers.razor \
  src/Client/Pages/Staff/StaffSubscriptions.razor tests/Client.Tests/Pages/StaffIntroOffersTests.cs \
  tests/Client.Tests/Pages/StaffSubscriptionsTests.cs
git status --short
git commit -m "feat: client Staff — intro offers page; listing fees show the offer and issue tax invoices

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Client Staff — Invoices register with totals and CSV; refund buyer fee; remove v1 invoices

**Files:**
- Modify (rewrite): `src/Client/Pages/Staff/StaffInvoices.razor`, `src/Client/Pages/Staff/StaffInvoices.razor.css`
- Create: `src/Client/wwwroot/js/download.js`; Modify: `src/Client/wwwroot/index.html:29`
- Modify: `src/Client/Pages/Staff/StaffTransactions.razor`, `src/Client/Services/StaffApiService.cs` (remove the Invoices region lines 204–212; add `RefundPurchaseAsync`)
- Delete: `src/Shared/DTOs/Admin/InvoiceDto.cs`, `src/Shared/DTOs/Admin/InvoiceLineDto.cs`
- Modify: `src/Server/Services/AdminService.cs` (delete `GetInvoicesAsync`), `src/Server/Services/IAdminService.cs:10`,
  `src/Server/Controllers/AdminController.cs:30–35`, `tests/Server.Tests/Services/AdminServiceTests.cs` (delete `GetInvoices_ShowsBuyerFeesAndBalancePayableToStud`)
- Test: `tests/Client.Tests/Pages/StaffInvoicesTests.cs`, `tests/Client.Tests/Pages/StaffTransactionsTests.cs`

**Interfaces:**
- Consumes: Task 9 `TaxDocumentApiService` (`GetRegisterAsync`, `ExportCsvAsync`, `IssueMissingAsync`), `TaxDocumentList.KindText / SignedTotal / StatusText`;
  Task 7 `POST api/purchases/{id}/refund`, `RefundBuyerFeeRequest`, `RefundResultDto`, `TransactionDto.TaxInvoice* / AdjustmentNoteNumber`.
- Produces: `StaffApiService.RefundPurchaseAsync(Guid purchaseId, RefundBuyerFeeRequest request)` → `RefundResultDto`;
  JS `window.stallionsDownload.text(fileName, contentType, text)`.
- Removes: `InvoiceDto`, `InvoiceLineDto`, `IAdminService.GetInvoicesAsync`, `GET api/admin/invoices`, `StaffApiService.GetInvoicesAsync`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Client.Tests/Pages/StaffInvoicesTests.cs
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Staff;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.TaxDocuments;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class StaffInvoicesTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };
    private readonly Mock<TaxDocumentApiService> _api = new(MockBehavior.Loose, Http());

    private static TaxDocumentRegisterDto Register(int pending = 0) => new()
    {
        PendingCount = pending,
        Totals = new TaxDocumentTotalsDto
        {
            InvoiceCount = 4, InvoicesIncGst = 2625m, InvoicesExGst = 2386.36m, InvoicesGst = 238.64m,
            AdjustmentCount = 2, AdjustmentsIncGst = 645m, AdjustmentsExGst = 586.36m, AdjustmentsGst = 58.64m,
            NetIncGst = 1980m, NetExGst = 1800m, NetGst = 180m, UnpaidCount = 1, UnpaidIncGst = 990m
        },
        Documents =
        [
            new() { Id = Guid.NewGuid(), Number = "SA-000006", Kind = "AdjustmentNote", Status = "Paid", IssuedOn = new DateOnly(2026, 10, 12),
                RecipientName = "Jane Buyer", RecipientEmail = "jane@example.com", Description = "Adjustment to SA-000005",
                AmountIncGst = 150m, AmountExGst = 136.36m, GstAmount = 13.64m, OriginalNumber = "SA-000005" },
            new() { Id = Guid.NewGuid(), Number = "SA-000002", Kind = "ListingFeeInvoice", Status = "Unpaid", IssuedOn = new DateOnly(2026, 10, 11),
                DueOn = new DateOnly(2026, 10, 25), RecipientName = "Arrowfield", RecipientEmail = "sales@arrowfield.example",
                Description = "Listing fee — Dundeel, 2026 Season", AmountIncGst = 990m, AmountExGst = 900m, GstAmount = 90m }
        ]
    };

    private IRenderedComponent<StaffInvoices> Render(TaxDocumentRegisterDto register)
    {
        this.AddTestAuthorization().SetAuthorized("staff@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.Setup(a => a.GetRegisterAsync(It.IsAny<TaxDocumentFilter>())).ReturnsAsync(register);
        Services.AddSingleton(_api.Object);
        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "Staff", Email = "staff@example.com", Role = "Staff", Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));
        return RenderComponent<StaffInvoices>();
    }

    private static void Click(IRenderedComponent<StaffInvoices> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == text).Click();

    [Fact]
    public void ShowsTheTotals_WithAdjustmentsSubtracted_AndUnpaidSeparately()
    {
        var cut = Render(Register());

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("SA-000002"));
        var totals = cut.Find(".invoice-totals-grid").TextContent;
        totals.Should().Contain("Invoices (4)").And.Contain("$2,625.00").And.Contain("Adjustment notes (2)")
            .And.Contain("−$645.00").And.Contain("Net").And.Contain("$1,980.00").And.Contain("GST $180.00")
            .And.Contain("Unpaid (1)").And.Contain("$990.00");
        cut.Markup.Should().Contain("−$150.00").And.Contain("Unpaid — due 25 Oct 2026").And.Contain("Arrowfield");
    }

    [Fact]
    public void ApplyingFilters_SendsThem()
    {
        var cut = Render(Register());
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("SA-000002"));

        cut.Find("#filter-kind").Change("AdjustmentNote");
        cut.Find("#filter-status").Change("Paid");
        cut.Find("#filter-from").Change("2026-10-01");
        Click(cut, "Apply");

        _api.Verify(a => a.GetRegisterAsync(It.Is<TaxDocumentFilter>(f => f.Kind == "AdjustmentNote" && f.Status == "Paid"
            && f.From == new DateOnly(2026, 10, 1) && f.To == null)), Times.Once);
    }

    [Fact]
    public void FeesWithoutAnInvoice_ShowABanner_AndCanBeIssued()
    {
        _api.Setup(a => a.IssueMissingAsync()).ReturnsAsync(new IssueMissingResultDto { Issued = 2, Remaining = 0 });
        var cut = Render(Register(pending: 2));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("2 fees have no tax invoice because business details are incomplete"));
        Click(cut, "Issue missing tax invoices");

        _api.Verify(a => a.IssueMissingAsync(), Times.Once);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("2 tax document(s) issued."));
    }

    [Fact]
    public void ExportCsv_DownloadsTheFilteredDocuments()
    {
        _api.Setup(a => a.ExportCsvAsync(It.IsAny<TaxDocumentFilter>())).ReturnsAsync("Number\r\n");
        var cut = Render(Register());
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("SA-000002"));

        Click(cut, "Export CSV");

        var call = JSInterop.VerifyInvoke("stallionsDownload.text");
        call.Arguments[0]!.ToString().Should().StartWith("tax-documents-").And.EndWith(".csv");
        call.Arguments[1].Should().Be("text/csv");
        call.Arguments[2].Should().Be("Number\r\n");
    }
}
```

```csharp
// tests/Client.Tests/Pages/StaffTransactionsTests.cs
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Staff;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Admin;
using Stallions.Shared.DTOs.Checkout;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class StaffTransactionsTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };
    private readonly Mock<StaffApiService> _api = new(MockBehavior.Loose, Http());

    private IRenderedComponent<StaffTransactions> Render(params TransactionDto[] transactions)
    {
        this.AddTestAuthorization().SetAuthorized("staff@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.Setup(a => a.GetTransactionsAsync()).ReturnsAsync(transactions.ToList());
        Services.AddSingleton(_api.Object);
        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "Staff", Email = "staff@example.com", Role = "Staff", Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));
        return RenderComponent<StaffTransactions>();
    }

    private static TransactionDto Completed() => new()
    {
        PurchaseId = Guid.NewGuid(), StallionName = "Dundeel", StudFarmName = "Arrowfield", BuyerDisplayName = "Jane Buyer",
        TotalPriceIncGst = 20000m, BuyerFeeIncGst = 150m, BuyerFeeExGst = 136.36m, BuyerFeeGst = 13.64m,
        BalancePayableToStudIncGst = 19850m, Status = "Completed", PaidAt = new DateTime(2026, 10, 11),
        TaxInvoiceId = Guid.NewGuid(), TaxInvoiceNumber = "SA-000124"
    };

    [Fact]
    public void Refund_WarnsToRefundInStripeFirst_ThenRecordsItWithTheNote()
    {
        var sale = Completed();
        _api.Setup(a => a.RefundPurchaseAsync(sale.PurchaseId, It.IsAny<RefundBuyerFeeRequest>())).ReturnsAsync(new RefundResultDto
            { AdjustmentNoteNumber = "SA-000130", Message = "Refund recorded. Adjustment note SA-000130 has been emailed to the buyer." });
        var cut = Render(sale);
        cut.WaitForAssertion(() => cut.Find($"a[href='/tax-documents/{sale.TaxInvoiceId}']").TextContent.Should().Be("SA-000124"));

        cut.FindAll("button").Single(b => b.TextContent.Contains("Refund buyer fee")).Click();

        cut.Markup.Should().Contain("Refund $150.00 in Stripe first. This records the refund and emails the buyer an adjustment note.");
        cut.Find("#refund-note").Change("Duplicate charge");
        cut.FindAll("button").Single(b => b.TextContent.Contains("Record refund")).Click();

        _api.Verify(a => a.RefundPurchaseAsync(sale.PurchaseId,
            It.Is<RefundBuyerFeeRequest>(r => r.Note == "Duplicate charge")), Times.Once);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Adjustment note SA-000130 has been emailed to the buyer."));
    }

    [Fact]
    public void ARefundedSale_ShowsItsAdjustmentNote_AndCannotBeRefundedAgain()
    {
        var sale = Completed();
        sale.Status = "Refunded";
        sale.AdjustmentNoteNumber = "SA-000130";

        var cut = Render(sale);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Adjusted by SA-000130"));
        cut.FindAll("button").Should().NotContain(b => b.TextContent.Contains("Refund buyer fee"));
    }
}
```

- [ ] **Step 2: Run red** — `dotnet test tests/Client.Tests --filter "FullyQualifiedName~StaffInvoicesTests|FullyQualifiedName~StaffTransactionsTests"` → compile errors.

- [ ] **Step 3: Remove the v1 "Sales by Stud Farm" invoices**

```bash
git rm src/Shared/DTOs/Admin/InvoiceDto.cs src/Shared/DTOs/Admin/InvoiceLineDto.cs
```

- `src/Server/Services/IAdminService.cs:10` — delete `Task<ServiceResult<IReadOnlyList<InvoiceDto>>> GetInvoicesAsync();`.
- `src/Server/Services/AdminService.cs` — delete the whole `GetInvoicesAsync` method (and the blank line after it).
- `src/Server/Controllers/AdminController.cs:30–35` — delete the `[HttpGet("invoices")]` `GetInvoices` action.
- `tests/Server.Tests/Services/AdminServiceTests.cs` — delete the test `GetInvoices_ShowsBuyerFeesAndBalancePayableToStud`.
- `src/Client/Services/StaffApiService.cs` — replace the `// ── Invoices ──` region (lines 204–212) with:

```csharp
    // ── Refunds ────────────────────────────────────────────────────────────

    /// <summary>Records a buyer fee refund already made in Stripe; the buyer is emailed an adjustment note.</summary>
    public virtual async Task<RefundResultDto> RefundPurchaseAsync(Guid purchaseId, RefundBuyerFeeRequest request)
    {
        var r = await _http.PostAsJsonAsync($"api/purchases/{purchaseId}/refund", request);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<RefundResultDto>()
               ?? throw new ApiException(500, "Empty response.");
    }
```

  and add `using Stallions.Shared.DTOs.Checkout;` at the top.

`grep -rn "InvoiceDto\|InvoiceLineDto\|GetInvoicesAsync\|admin/invoices" src tests --include=*.cs --include=*.razor` → no matches.

- [ ] **Step 4: Download helper**

```js
// src/Client/wwwroot/js/download.js
// Saves text the app fetched with the signed-in user's token (a plain link can't send the token).
window.stallionsDownload = {
    text: function (fileName, contentType, text) {
        // A byte-order mark so Excel reads the UTF-8 text (em dashes, accents) correctly.
        const blob = new Blob(['﻿', text], { type: contentType + ';charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(url);
    }
};
```

`src/Client/wwwroot/index.html` — after line 29 (`<script src="js/modal-focus.js"></script>`) add `    <script src="js/download.js"></script>`.

- [ ] **Step 5: Invoices register page**

```razor
@* src/Client/Pages/Staff/StaffInvoices.razor *@
@page "/staff/invoices"
@layout StaffLayout
@attribute [Authorize]
@inject TaxDocumentApiService TaxDocumentApi
@inject UserStateService UserState
@inject NavigationManager Nav
@inject IJSRuntime JS

<PageTitle>Invoices — Staff Admin</PageTitle>

<div class="admin-page-header">
    <h1 class="admin-page-title">Invoices</h1>
    <button class="btn btn-outline" @onclick="ExportAsync" disabled="@(_busy || _register is null)">Export CSV</button>
</div>

<p class="text-secondary text-sm">
    Every tax invoice and adjustment note issued. Totals cover the documents shown: adjustment notes are
    subtracted, and unpaid invoices are shown separately. Dates are Sydney dates.
</p>

@if (_register is { PendingCount: > 0 } pending)
{
    <div class="alert alert-warning invoice-pending">
        @* Kept on one line: the sentence is matched as one piece of text. *@
        @pending.PendingCount @(pending.PendingCount == 1 ? "fee has" : "fees have") no tax invoice because business details are incomplete — complete them in <a href="/staff/settings">Settings</a>, then click
        <button class="btn btn-sm btn-gold" @onclick="IssueMissingAsync" disabled="@_busy">Issue missing tax invoices</button>
    </div>
}
@if (_notice is not null) { <div class="alert alert-success">@_notice</div> }

<div class="invoice-filters">
    <label class="form-label" for="filter-kind">Type</label>
    <select id="filter-kind" class="form-select" @bind="_kind">
        <option value="">All</option>
        <option value="ListingFeeInvoice">Listing fee invoices</option>
        <option value="BuyerFeeInvoice">Buyer fee invoices</option>
        <option value="AdjustmentNote">Adjustment notes</option>
    </select>
    <label class="form-label" for="filter-status">Status</label>
    <select id="filter-status" class="form-select" @bind="_status">
        <option value="">All</option>
        <option value="Unpaid">Unpaid</option>
        <option value="Paid">Paid</option>
    </select>
    <label class="form-label" for="filter-from">From</label>
    <input id="filter-from" type="date" class="form-input" @bind="_from" />
    <label class="form-label" for="filter-to">To</label>
    <input id="filter-to" type="date" class="form-input" @bind="_to" />
    <button class="btn btn-sm btn-gold" @onclick="LoadAsync" disabled="@_busy">Apply</button>
</div>

@if (_loading)
{
    <p>Loading…</p>
}
else if (_error is not null)
{
    <div class="alert alert-danger">@_error</div>
}
else if (_register is not null)
{
    var t = _register.Totals;
    <div class="invoice-totals-grid">
        <div class="invoice-total">
            <span class="invoice-total-label">Invoices (@t.InvoiceCount)</span>
            <strong>@Money(t.InvoicesIncGst)</strong>
            <small>ex GST @Money(t.InvoicesExGst) · GST @Money(t.InvoicesGst)</small>
        </div>
        <div class="invoice-total">
            <span class="invoice-total-label">Adjustment notes (@t.AdjustmentCount)</span>
            <strong>−@Money(t.AdjustmentsIncGst)</strong>
            <small>ex GST −@Money(t.AdjustmentsExGst) · GST −@Money(t.AdjustmentsGst)</small>
        </div>
        <div class="invoice-total invoice-total-net">
            <span class="invoice-total-label">Net</span>
            <strong>@Money(t.NetIncGst)</strong>
            <small>ex GST @Money(t.NetExGst) · GST @Money(t.NetGst)</small>
        </div>
        <div class="invoice-total invoice-total-unpaid">
            <span class="invoice-total-label">Unpaid (@t.UnpaidCount)</span>
            <strong>@Money(t.UnpaidIncGst)</strong>
            <small>issued, not yet received</small>
        </div>
    </div>

    @if (_register.Documents.Count == 0)
    {
        <div class="admin-empty-state"><p>No tax documents match these filters.</p></div>
    }
    else
    {
        <table class="admin-table">
            <thead>
                <tr>
                    <th>Number</th>
                    <th>Issued</th>
                    <th>Type</th>
                    <th>Recipient</th>
                    <th>Description</th>
                    <th class="num">Total (incl. GST)</th>
                    <th class="num">GST</th>
                    <th>Status</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var d in _register.Documents)
                {
                    <tr>
                        <td><a href="/tax-documents/@d.Id">@d.Number</a></td>
                        <td>@d.IssuedOn.ToString("d MMM yyyy", AuCulture)</td>
                        <td>@TaxDocumentList.KindText(d.Kind)</td>
                        <td>@d.RecipientName<div class="text-muted text-xs">@d.RecipientEmail</div></td>
                        <td>@d.Description</td>
                        <td class="num">@TaxDocumentList.SignedTotal(d)</td>
                        <td class="num">@(d.Kind == "AdjustmentNote" ? "−" : "")@Money(d.GstAmount)</td>
                        <td>@TaxDocumentList.StatusText(d)</td>
                    </tr>
                }
            </tbody>
        </table>
    }
}

@code {
    private TaxDocumentRegisterDto? _register;
    private bool _loading = true;
    private bool _busy;
    private string? _error;
    private string? _notice;

    private string _kind = string.Empty;
    private string _status = string.Empty;
    private DateOnly? _from;
    private DateOnly? _to;

    private static readonly System.Globalization.CultureInfo AuCulture =
        System.Globalization.CultureInfo.GetCultureInfo("en-AU");
    private static string Money(decimal amount) => amount.ToString("C2", AuCulture);

    protected override async Task OnInitializedAsync()
    {
        await UserState.LoadAsync();
        if (!UserState.IsStaff) { Nav.NavigateTo("/", replace: true); return; }
        await LoadAsync();
    }

    private TaxDocumentFilter Filter() => new()
    {
        Kind = string.IsNullOrEmpty(_kind) ? null : _kind,
        Status = string.IsNullOrEmpty(_status) ? null : _status,
        From = _from,
        To = _to
    };

    private async Task LoadAsync()
    {
        _loading = true; _error = null;
        try { _register = await TaxDocumentApi.GetRegisterAsync(Filter()); }
        catch (Exception ex) { _error = ex is ApiException ae ? ae.Message : "Failed to load the tax documents."; }
        finally { _loading = false; }
    }

    private async Task IssueMissingAsync()
    {
        _busy = true; _error = null; _notice = null;
        try
        {
            var result = await TaxDocumentApi.IssueMissingAsync();
            _notice = $"{result.Issued} tax document(s) issued." + (result.Remaining > 0 ? $" {result.Remaining} still waiting." : "");
            await LoadAsync();
        }
        catch (Exception ex) { _error = ex is ApiException ae ? ae.Message : "Failed to issue the missing tax invoices."; }
        finally { _busy = false; }
    }

    private async Task ExportAsync()
    {
        _busy = true; _error = null;
        try
        {
            var csv = await TaxDocumentApi.ExportCsvAsync(Filter());
            await JS.InvokeVoidAsync("stallionsDownload.text", $"tax-documents-{DateTime.Now:yyyy-MM-dd}.csv", "text/csv", csv);
        }
        catch (Exception ex) { _error = ex is ApiException ae ? ae.Message : "Failed to export the tax documents."; }
        finally { _busy = false; }
    }
}
```

```css
/* src/Client/Pages/Staff/StaffInvoices.razor.css */
.invoice-filters {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--space-3);
    margin: var(--space-4) 0;
}

.invoice-filters .form-label { margin: 0; }
.invoice-filters .form-select, .invoice-filters .form-input { width: auto; min-width: 150px; }

.invoice-totals-grid {
    display: grid;
    grid-template-columns: 1fr;
    gap: var(--space-4);
    margin-bottom: var(--space-6);
}

.invoice-total {
    display: flex;
    flex-direction: column;
    gap: var(--space-1);
    background: var(--white);
    border: 1px solid var(--border);
    border-radius: 8px;
    padding: var(--space-4) var(--space-5);
}

.invoice-total-label { color: var(--text-secondary); font-size: var(--font-size-sm); }
.invoice-total strong { color: var(--navy); font-size: var(--font-size-xl, 1.25rem); }
.invoice-total small { color: var(--text-muted); }
.invoice-total-net { border-color: var(--navy); }
.invoice-total-unpaid strong { color: var(--warning); }

.num { text-align: right; white-space: nowrap; }

@media (min-width: 900px) {
    .invoice-totals-grid { grid-template-columns: repeat(4, 1fr); }
}
```

- [ ] **Step 6: Transactions — tax invoice column and refund**

`src/Client/Pages/Staff/StaffTransactions.razor` (`Stallions.Shared.DTOs.Checkout` is already in `_Imports.razor` — don't add it again, a duplicate `@using` is a build warning):

After line 12 (`</div>` closing the page header) add:

```razor

@if (_notice is not null) { <div class="alert alert-success">@_notice</div> }
```

Header row (lines 32–43): after `<th>Status</th>` add `<th>Tax invoice</th>` and `<th>Actions</th>`.
In the row, after the Status cell (line 58) add:

```razor
                    <td class="text-sm">
                        @if (t.TaxInvoiceId is { } invoiceId)
                        {
                            <a href="/tax-documents/@invoiceId">@t.TaxInvoiceNumber</a>
                            @if (t.AdjustmentNoteNumber is not null)
                            {
                                <div class="text-muted text-xs">Adjusted by @t.AdjustmentNoteNumber</div>
                            }
                        }
                        else
                        {
                            <text>—</text>
                        }
                    </td>
                    <td>
                        @if (t.Status == "Completed")
                        {
                            <button class="btn btn-sm btn-outline" @onclick="() => OpenRefund(t)">Refund buyer fee</button>
                        }
                    </td>
```

After the `}` closing the table branch (line 63) add:

```razor

<Modal IsOpen="@(_refundFor is not null)" Title="@($"Refund buyer fee — {_refundFor?.StallionName}")" OnClose="CloseRefund">
    <p>Refund @Money(_refundFor?.BuyerFeeIncGst ?? 0) in Stripe first. This records the refund and emails the buyer an adjustment note.</p>
    <div class="form-group">
        <label class="form-label" for="refund-note">Note (optional, shown on the adjustment note)</label>
        <textarea id="refund-note" class="form-control" rows="2" maxlength="500" @bind="_refundNote"
                  placeholder="e.g. Duplicate charge"></textarea>
    </div>
    @if (_refundError is not null) { <div class="alert alert-danger">@_refundError</div> }
    <div class="form-actions">
        <button class="btn btn-gold" @onclick="RefundAsync" disabled="@_busy">@(_busy ? "Recording…" : "Record refund")</button>
        <button class="btn btn-outline" @onclick="CloseRefund" disabled="@_busy">Cancel</button>
    </div>
</Modal>
```

In `@code` add:

```csharp
    private TransactionDto? _refundFor;
    private string? _refundNote;
    private string? _refundError;
    private string? _notice;
    private bool _busy;

    private static readonly System.Globalization.CultureInfo AuCulture =
        System.Globalization.CultureInfo.GetCultureInfo("en-AU");
    private static string Money(decimal amount) => amount.ToString("C2", AuCulture);

    private void OpenRefund(TransactionDto t)
    {
        _refundFor = t;
        _refundNote = null;
        _refundError = null;
    }

    private void CloseRefund()
    {
        _refundFor = null;
        _refundError = null;
    }

    private async Task RefundAsync()
    {
        if (_refundFor is null) return;
        _busy = true; _refundError = null; _notice = null;
        try
        {
            var result = await StaffApi.RefundPurchaseAsync(_refundFor.PurchaseId, new RefundBuyerFeeRequest
            {
                Note = string.IsNullOrWhiteSpace(_refundNote) ? null : _refundNote.Trim()
            });
            _notice = result.Message;
            CloseRefund();
            await Load();
        }
        catch (Exception ex) { _refundError = ex is ApiException ae ? ae.Message : "Failed to record the refund."; }
        finally { _busy = false; }
    }
```

- [ ] **Step 7: Run green** — the two new test classes; full build (0 warnings) and full test run (server tests included — `AdminServiceTests` lost one test).

- [ ] **Step 8: Commit**

```bash
git add src/Client/Pages/Staff/StaffInvoices.razor src/Client/Pages/Staff/StaffInvoices.razor.css src/Client/wwwroot/js/download.js \
  src/Client/wwwroot/index.html src/Client/Pages/Staff/StaffTransactions.razor src/Client/Services/StaffApiService.cs \
  src/Server/Services/AdminService.cs src/Server/Services/IAdminService.cs src/Server/Controllers/AdminController.cs \
  tests/Server.Tests/Services/AdminServiceTests.cs tests/Client.Tests/Pages/StaffInvoicesTests.cs \
  tests/Client.Tests/Pages/StaffTransactionsTests.cs
git status --short
git commit -m "feat: Staff Invoices register (BAS totals, filters, CSV, issue missing); refund buyer fee; remove v1 invoices

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: Final check, push, dev rollout (David deploys)

- [ ] **Step 1: Whole-branch check** — `git status` clean; `dotnet build stallions-nominations-marketplace.slnx` (0 warnings);
  `dotnet test stallions-nominations-marketplace.slnx` twice (green); `dotnet ef migrations has-pending-model-changes --project src/Server --startup-project src/Server` → no changes;
  `grep -rn "NominationBinding\|BindingStatus\|InvoiceLineDto" src tests --include=*.cs --include=*.razor` → only `Data/Migrations`.
- [ ] **Step 2: Local smoke of the SQL pieces the in-memory tests can't reach** — run the API against localdb (fake payment provider, log email sender);
  as Staff complete Business details, then Mark paid one Pending subscription: the document is `SA-000001`; Mark paid a second: `SA-000002`
  (`SqlTaxDocumentNumberSource` and the sequence work). In SQL: `SELECT Number, Kind, Status FROM TaxDocuments` shows both; issuing again from
  the same subscription is impossible (filtered unique index). Then `ALTER SEQUENCE dbo.TaxDocumentNumbers RESTART WITH 1` and delete the two rows
  only if David wants a clean local numbering (dev data, no backfill).
- [ ] **Step 3: Docs** — spec **Status** → "Built on `feature/v2-phase5a-intro-offers-tax-invoices` (date); not merged"; add a
  "Changes made during the build" block to this plan if reviews changed anything; commit `docs: phase 5a spec status` (message ends with the
  blank line and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`).
- [ ] **Step 4: Push** — `git push -u origin feature/v2-phase5a-intro-offers-tax-invoices`. Don't merge or open a PR unless David asks.
- [ ] **Step 5: Dev rollout (David runs `azd deploy api --environment dev`; no infra change, no provision needed; the API applies the migration
  on start)** — then check from outside: site and `api/listings` 200; `GET api/tax-documents/mine` 401 signed out;
  `GET api/intro-offers` 401 signed out; `GET api/settings/business-details` 401 signed out; `GET api/tax-documents/export.csv` 401 signed out.
- [ ] **Step 6: Dev click-through (fake provider; test users StallNomsManager01 / StallNomBuyer01 / PaddockSide; Staff account):**
  1. As Staff, Settings → **Business details**: enter legal name, ABN typed `12 345 678 901`, address, accounts email, bank account name,
     BSB `062-000`, account number, terms 14 → "Business details saved"; reload shows the ABN grouped and the BSB as `062-000`.
  2. Staff → **Intro Offers** → New: "Launch 2026", $495 off, all studs, 2026 Season, last day 31 Dec 2026 → listed Active, applied 0.
     Staff → Listing Fees → New subscription for a StallNomsManager01 stallion: the form shows "Intro offer Launch 2026 applies: $990.00 → $495.00";
     create → $495 with "Intro offer: Launch 2026"; Intro Offers now shows applied 1 and Edit locks the type, amount and scope.
  3. As StallNomsManager01, My Stallions → **Activate** another stallion → the fake payment page charges $495 → back on My Stallions it turns
     Paid; the stud's **Tax invoices** section lists a Paid listing fee invoice (standard $990, intro offer −$495, total $495, GST $45); the
     "Tax invoice SA-…" email arrives in the log. (Optional: create a Waiver offer for this stud → Activate a third stallion → "… is active for
     2026 Season — intro offer applied", no payment page, no tax invoice.)
  4. As Staff, Listing Fees → **Issue tax invoice** on a Pending subscription → "Tax invoice SA-… issued and emailed to …"; the stud's email
     shows the amount due, due date (+14 days), bank details and "Please use SA-… as your payment reference"; the invoice column shows Unpaid.
     **Mark paid** (Bank transfer) → the invoice turns Paid and a "Receipt" email is sent. Issue another invoice and **Waive** that subscription →
     an adjustment note email "Adjustment note SA-… for tax invoice SA-…"; the column shows "Reversed by SA-…".
  5. As StallNomBuyer01 win an auction or have an offer accepted (Phase 3/4 flows) → the win / offer-accepted email has "Your tax invoice SA-…
     is available here", and a separate "Tax invoice SA-…: buyer fee" email; **Tax invoices** in the nav lists it; the stud cannot open its id (404 page "isn't available").
  6. As Staff, Transactions → that sale shows the invoice number → **Refund buyer fee** → modal "Refund $150.00 in Stripe first…" → note
     "Dev test" → "Refund recorded. Adjustment note SA-… has been emailed to the buyer."; the buyer's Tax invoices list shows the note and
     "Reversed by SA-…" on the invoice.
  7. Staff → **Invoices**: totals show invoices, adjustment notes (negative), net and unpaid separately; filter Type = Adjustment notes and a
     From/To date → only those rows; **Export CSV** downloads `tax-documents-<date>.csv` that opens in Excel with negative note amounts.
  8. Clear the ABN in Business details, have a card listing-fee payment or a buyer-fee charge happen → the payment still succeeds; Invoices shows
     "1 fee has no tax invoice because business details are incomplete…"; restore the ABN → **Issue missing tax invoices** → "1 tax document(s) issued."
  9. Open any document → **Print / Save as PDF** → the print preview shows only the document (no nav bar, footer or button).
