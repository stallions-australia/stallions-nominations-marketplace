# v2 Phase 5b — Editable Wording, Stud Listing Terms and History — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Staff edit the buyer disclosure wording and the subject and message of every system email without a deploy (versioned, previewed, restorable, audited); Staff publish versioned stud listing terms that a stud must accept before paying a listing fee or publishing a listing; Staff see the buyer / stud T&C version history and a change log of settings, business details, intro offers, wording and terms; a failed unit of work leaves the EF change tracker clean.

**Architecture:** One new table, `ContentItems` (one row per version, unique `(Key, Version)`, version 1 of every item seeded by `HasData` from a code `ContentCatalog`). A pure `ContentTemplate` validates and renders plain text with `{Placeholder}` substitution (HTML-encoded, blank line = paragraph). A scoped `ContentService` reads the current version through a singleton 60-second `ContentCache`, renders emails and disclosures, and serves the Staff editor (list, history, save / restore / reset as new versions, preview, audit). `AuctionEmails`, `TaxDocumentEmails`, `DisclosuresController` and `TaxDocumentQueryService` switch from `DisclosureOptions` (removed with the `Checkout` config) to `ContentService`; the generated blocks (amounts table, disclosure block, contact line, invoice tables, links) stay in code exactly where they are today, so the seeded defaults reproduce today's emails byte for byte. `TermsDocuments` gain an `Audience` (Buyer / Stud, versions per audience); `StudFarms` record the accepted stud terms version; a `StudTermsGate` blocks stud self-serve Activate, Publish and Convert to Make an Offer until the current stud terms are accepted. A read-only `ChangeLogService` turns audit rows into a filtered, paged, masked change log.

**Tech Stack:** .NET 9, ASP.NET Core, EF Core (SQL Server; in-memory provider in tests), Blazor WASM, Markdig, xUnit + Moq + FluentAssertions, bUnit.

**Spec:** `docs/superpowers/specs/2026-10-12-v2-phase5b-editable-content-design.md`. Read it and CLAUDE.md first.
**Branch:** `feature/v2-phase5b-editable-content` (already created; the spec is committed).

> **Plan decisions that refine the spec (2026-10-12):**
> - **25 catalog keys.** The spec's list plus three variants today's code words differently for auctions and accepted
>   offers: `Email.PaymentFailedOffer`, `Email.PaymentAbandonedOffer`, `Email.StudSaleConfirmationOffer`. Outbox
>   template names (`OutgoingEmail.Template`) are unchanged — e.g. both no-sale variants still queue as `StudNoSale`.
> - **What Staff edit in an email:** the subject, and the message paragraphs that come *before* the first generated
>   block in today's email. Sentences that today sit *after* a generated block stay fixed with that block:
>   "The buyer fee of $X has been charged to your saved card." (the first line of the mandatory disclosure block, followed
>   by the two disclosure items), "Stud contact: …", "Please arrange the balance with the buyer under your own terms."
>   (with the stud's amounts table), "Status: Paid", "Paid: {date}", "Please use SA-… as your payment reference." (with
>   the bank details). This is what makes the default output byte-identical and keeps the disclosure and amounts uneditable.
> - **Extra placeholders** beyond the spec's list (all computed by today's code): `{BuyerEmail}`, `{BuyerFee}`,
>   `{Description}` (tax document line), `{OriginalDate}` (date of the invoice an adjustment note reverses). `{Reason}`
>   means the card failure (payment failed), the no-sale reason (stud no sale) or Staff's reason (adjustment note);
>   `{Amount}` is the price / offer / invoice total as described per item. Each item's catalog entry lists exactly the
>   values its email code supplies (a test proves it both ways).
> - **Required placeholders must appear in the message body** (the subject doesn't count): Outbid `{StallionName}` `{HighBid}`;
>   auction lost / ended / payment abandoned / stud no sale (offer) `{StallionName}`; won / offer accepted `{StallionName}` `{Amount}`;
>   payment failed `{BuyerFee}` `{Deadline}`; stud sale confirmation `{BuyerName}` `{BuyerEmail}`; stud no sale (auction) `{Reason}`;
>   offer received `{BuyerName}` `{BuyerEmail}` `{Amount}` `{Deadline}`; offer declined / expired `{Amount}`;
>   invoice due `{Amount}` `{DueDate}`; receipt `{Amount}`; adjustment note `{OriginalNumber}` `{Reason}`.
> - **Disclosure items are a single paragraph** (line breaks rejected): the client shows them inline in six places and
>   the emails / tax document page in one `<p>`.
> - **The subject must be one line** (email header safety); subjects are substituted but not HTML-encoded (they are
>   a header, as today).
> - **Seed by `HasData`** from `ContentCatalog`, ids = first 16 bytes of SHA-256("ContentItem:" + key), `CreatedAt`
>   2026-10-12 UTC, no author. Consequence: never edit a shipped catalog default — that would generate a migration that
>   rewrites version 1; Staff save new versions instead.
> - **Save / restore / reset of text identical to the current version → 400**; two saves racing for the same next
>   version → 409 (unique `(Key, Version)`). Restore re-validates the old text against today's catalog.
> - **Validation errors** travel as one 400 message, one error per line; the client splits on line breaks.
> - **Preview** returns `{Errors}` or `{Subject, Paragraphs}` as plain text; the client renders it with Blazor's own
>   encoding (no `MarkupString`). The preview shows the editable text only, with a note that the system adds the
>   amounts / buttons / disclosure / invoice details.
> - **Wording URLs use the query string** (`/staff/wording?key=Email.Outbid&version=3`): a dotted last path segment
>   (`/staff/wording/Email.Outbid`) is treated as a file by the server's `index.html` fallback on reload. API routes keep
>   `{key}` in the path (controllers match before the fallback).
> - **`GET api/terms/stud/current` returns `StudTermsStatusDto`** (current stud terms or null, the caller farm's accepted
>   version / date, `NeedsAcceptance`) — 200 even when none is published, so the client needs one call to decide.
> - **The stud terms gate also covers `ConvertToMakeAnOfferAsync`** (it creates a live listing — a publish path).
>   Creating / editing drafts, unpublish and close are not gated. Staff paths (subscriptions, force status) are never gated.
> - **Terms markdown is rendered with raw HTML disabled.** The spec says "keep the existing markdown renderer and its
>   sanitisation", but today's `Markdig.Markdown.ToHtml` passes raw HTML through; a shared `TermsMarkdown`
>   (`DisableHtml()`) is used by the buyer modal, Staff Terms and the new stud terms modal.
> - **`TermsService` re-checks Staff** on publish and history (previously controller-only), audits `PublishTerms`, and
>   returns 409 when two publishes race for the same version. `TermsServiceTests` move from a mocked repository to the
>   in-memory database (the repository gains audience overloads the old setups wouldn't hit); every existing test keeps
>   its name and assertion.
> - **`ITermsRepository.GetCurrentAsync()` (no argument) stays and means Buyer**, so `BuyerEligibility`, `UserService` and
>   their Moq setups are untouched; `GetCurrentAsync(TermsAudience)` and `GetHistoryAsync(TermsAudience)` are added
>   (the old parameterless `GetHistoryAsync` had one caller, `TermsService`).
> - **Change log:** areas come from the audit action names (`UpdatePlatformSettings`, `UpdateBusinessDetails`,
>   `Create/Update/RetireIntroOffer`, `UpdateWording`, `PublishTerms`, `AcceptStudTerms`); the acceptance audit row
>   carries the farm name. Bank account numbers show as `•••678` (stored row unchanged). Wording message and terms
>   text show "changed" + "View version" link; a wording subject change shows old → new. Dates: `OccurredAt` is UTC;
>   From/To are Sydney dates, `[From 00:00, To+1 00:00)`. `page` is 1-based, 50 per page.
> - **Content reads are `AsNoTracking`:** emails render inside other units of work (`BuyerFeeCharger`,
>   `TaxDocumentService`) whose callers must have no stray tracked entities.
> - **`Checkout` config sections are removed** from `appsettings.json` and `appsettings.Development.json` (the latter held
>   only an unused `WebhookSecret`). No infra / app setting references them.
>
> **Changes made during the build (after reviews, 2026-10-13):**
> - Content saves store the trimmed, normalised text that validation checked. Email subjects have line breaks replaced
>   by spaces (one shared helper used by both email classes); the outbox caps every subject at 300 characters
>   (`…`), so a long Staff subject plus placeholder values can never fail a charge's transaction.
> - `TermsService` publishes and stud acceptances commit with their audit row in one transaction. Audience names
>   must match exactly. The migration's Down deletes Stud terms before restoring the old index.
> - `TermsMarkdown` also neutralises links and images whose scheme isn't http, https, mailto or relative.
> - The stud-terms accept flow is one shared component (`StudTermsGate`); Activate / Publish / Convert set their busy
>   flag before the status check, so a double click can't start two actions.
> - The change log compares numbers by value (no "25.00 → 25"), shows intro-offer stud / season as names, and guards
>   huge page numbers and extreme dates. The Wording editor ignores stale previews and shows network errors.
> - Task 13's localdb smoke was skipped (LocalDB unavailable); Task 2 had verified the seed on localdb.

## Global Constraints

- Plain text only: Staff text and placeholder values are HTML-encoded in emails; pages show text through Blazor's
  encoding. No Staff-authored HTML anywhere; terms markdown has raw HTML disabled.
- The mandatory buyer disclosure (fee charged sentence + buyer fee explanation + balance arrangement), amounts tables,
  invoice details, contact lines and links are generated by code and can't be removed by editing wording.
- With the seeded defaults every email is byte-identical to today's (`DefaultEmailGolden`).
- Unknown content keys are rejected everywhere (`ArgumentException` in code paths, 404 in the Staff API).
- Every endpoint authenticated and role-checked; wording, preview, terms publish / history and change log are
  `StaffOnly` **and** re-checked in the service. Stud terms status: `StudFarmOrStaff`; accept: `StudFarmAdminOnly`.
- Audit: every wording save / restore / reset (`ContentItem` / `UpdateWording`, old and new text), every terms publish
  (`TermsDocument` / `PublishTerms`), every stud terms acceptance (`StudFarm` / `AcceptStudTerms`).
- Phase 2–5a behaviour and tests stay unchanged; test **fixtures** (constructors, `CreateSut`, setup helpers) may
  change. Existing test bodies change only where a signature changes: `DisclosuresController.GetBuyerFee` becomes async
  (Task 5) and `TermsServiceTests` moves to the in-memory database (Task 8) — each listed in its task.

## Review Focus

- **Default output drifting from today's text** → Task 6 `AuctionEmailsWordingTests.DefaultWording_MatchesTodaysEmailsExactly`
  and Task 7 `TaxDocumentEmailsWordingTests.DefaultWording_MatchesTodaysEmailsExactly` (all 22 emails, subject + text + HTML,
  against `DefaultEmailGolden`, captured from the pre-5b code), plus Task 2 `DisclosureDefaults_AreTheOldCheckoutWording`.
- **Edited wording dropping or garbling the mandatory disclosure or the amounts** → Task 6
  `WonAndCharged_WithEditedWording_KeepsTheAmountsTableDisclosureAndLinks`, Task 7
  `BuyerFeeTaxInvoice_WithEditedWording_KeepsTheInvoiceTableAndFeeExplanation`, Task 4 `Save_WithoutARequiredPlaceholder_IsRejected`.
- **HTML / script in Staff text or values** → Task 3 `RenderBody_EncodesStaffTextAndValues`, Task 6
  `EditedWording_WithMarkup_IsEncoded`, Task 3 `Validate_SubjectOnTwoLines_IsRejected`, Task 11 `TermsMarkdown_RendersRawHtmlAsText`.
- **Placeholder typos / unbalanced braces reaching an email** → Task 3 `Validate_UnknownPlaceholder_NamesIt`,
  `Validate_UnbalancedBraces`, Task 4 `Save_WithATypo_IsRejected_AndNothingIsSaved`, Task 6
  `EveryEmail_SuppliesExactlyTheCatalogsPlaceholders`.
- **Stud terms gate bypassed by another path, or Staff wrongly gated** → Task 8
  `Convert_WithoutAcceptingTheStudTerms_IsRejected`, `PublishListing_WithoutAcceptingTheStudTerms_IsRejected`,
  `Activate_WithoutAcceptingTheStudTerms_IsRejected_AndCreatesNothing`, `Activate_WhenNewerStudTermsArePublished_NeedsThemAccepted`,
  `StaffCreate_ForAFarmThatHasNotAcceptedTheStudTerms_IsNotGated`.

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
- In test code `Options.Create` clashes with the `Stallions.Server.Options` namespace (until Task 7 deletes it) — use
  `Microsoft.Extensions.Options.Options.Create`.
- Existing patterns: `ServiceResult`/`ServiceResult<T>` (`Ok`, `Created`, `BadRequest`, `Forbidden`,
  `NotFound`, `Conflict`); repositories save on each call; `ITransactionRunner.RunAsync` (tests: `InlineTransactionRunner`);
  `IAuditLogRepository.LogAsync(type, id, action, userId, details)`; `TimeProvider` for new code (tests: `TestClock`);
  `SydneyTime`; in-memory DB via `DbContextFactory.Create(name)` + `Database.EnsureCreated()` (seeds `PlatformSettings`
  **and, from Task 2, `ContentItems`**); `SqlExceptionFactory.WithNumber(2601)`; `DbErrors.IsUniqueViolation`.
  Client: services have `virtual` methods for Moq; pages check `UserState.IsStaff` / `IsStudFarmAdmin`.
- The first line of each code block (`// src/...`, `@* src/... *@`) only names the file — don't copy it.
- `file:line` references are to the code as it is at the start of this branch. When an earlier task has already
  edited that file, find the method or block by the name given.

## File map

| File | Responsibility |
|---|---|
| `src/Server/Data/ITransactionRunner.cs` | `EfTransactionRunner` clears the tracker when work throws |
| `src/Shared/Enums/TermsAudience.cs` | `Buyer` / `Stud` (stored as a string) |
| `src/Server/Data/Entities/ContentItem.cs`, `TermsDocument.cs`, `StudFarm.cs` | New table; terms audience; stud terms acceptance |
| `src/Server/Content/ContentCatalog.cs` | Keys, groups, labels, placeholders (+ samples), required, defaults, seed ids |
| `src/Server/Content/ContentTemplate.cs` | Placeholder validation; render (substitute, encode, paragraphs) |
| `src/Server/Content/ContentCache.cs`, `ContentService.cs` | 60 s cache; read / render; Staff list / history / save / restore / reset / preview |
| `src/Server/Data/Repositories/ContentRepository.cs` (+ interface) | Versions, no tracking on reads |
| `src/Server/Controllers/ContentController.cs` | `api/content` (StaffOnly) |
| `src/Server/Controllers/DisclosuresController.cs`, `TaxDocuments/TaxDocumentQueryService.cs` | Read disclosures from content |
| `src/Server/Email/AuctionEmails.cs`, `TaxDocuments/TaxDocumentEmails.cs` | Subject + message from content; generated blocks kept |
| Removed | `src/Server/Options/DisclosureOptions.cs`, `Checkout` sections of both appsettings files |
| `src/Server/Services/TermsService.cs`, `StudTermsGate.cs`, `Controllers/TermsController.cs`, `Data/Repositories/TermsRepository.cs` (+ interfaces) | Audiences, stud acceptance, publish audit, gate |
| `src/Server/Services/SubscriptionService.cs`, `ListingService.cs`, `AdminService.cs` | Gate on Activate / Publish / Convert; farm acceptance in the Staff list |
| `src/Server/Services/ChangeLogService.cs`, `Controllers/AuditController.cs`, `Data/Repositories/AuditLogRepository.cs` | Change log |
| `src/Shared/DTOs/Content/*`, `src/Shared/DTOs/ChangeLog/*`, `src/Shared/DTOs/Terms/*`, `src/Shared/DTOs/Admin/StudFarmSummaryDto.cs` | DTOs |
| `src/Client/Services/ContentApiService.cs`, `ChangeLogApiService.cs`, `TermsApiService.cs` | Client APIs |
| `src/Client/Pages/Staff/StaffWording.razor`, `StaffChangeLog.razor`, `StaffTerms.razor`, `StaffStudFarms.razor`, `Layout/StaffLayout.razor` | Staff pages and nav |
| `src/Client/Components/TermsMarkdown.cs`, `Components/Stud/StudTermsModal.razor`, `Components/Buyer/TermsAgreementModal.razor` | Safe markdown; stud terms modal |
| `src/Client/Pages/Admin/AdminStallions.razor`, `AdminListingDetail.razor` | Stud terms modal before Activate / Publish / Convert |
| `tests/Server.Tests/Helpers/CapturingLogger.cs`, `ContentTestData.cs`, `tests/Server.Tests/Email/DefaultEmailGolden.cs` | Test helpers; today's emails, captured |

---

### Task 1: `EfTransactionRunner` leaves a clean change tracker when the work fails (5a follow-up)

**Files:**
- Modify: `src/Server/Data/ITransactionRunner.cs:16-30`
- Test: `tests/Server.Tests/Data/EfTransactionRunnerTests.cs` (two tests added)

**Interfaces:**
- Consumes: existing `EfTransactionRunner(AppDbContext)`, `ITransactionRunner.RunAsync<T>(Func<Task<T>>)`.
- Produces: same signatures; behaviour: when `work` throws (synchronously or asynchronously), the change tracker is
  cleared before the exception propagates.

- [ ] **Step 1: Write the failing tests** — append inside `EfTransactionRunnerTests` (after the existing test):

```csharp
// tests/Server.Tests/Data/EfTransactionRunnerTests.cs (add inside the class)
    [Fact]
    public async Task RunAsync_WhenTheWorkThrows_ClearsTheChangeTracker_SoALaterSaveDoesNotPersistIt()
    {
        await using var db = CreateInMemoryDb();
        var runner = new EfTransactionRunner(db);

        // Like one item of "Issue missing tax invoices": it tracks a row, then fails.
        var act = () => runner.RunAsync<bool>(() =>
        {
            db.Users.Add(new User { ObjectId = "failed", Email = "failed@x", DisplayName = "Failed", Role = UserRole.Buyer });
            throw new InvalidOperationException("the item failed");
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("the item failed");
        db.ChangeTracker.Entries().Should().BeEmpty();

        // The caller carries on and saves its aggregate audit row: the failed item's row must not ride along.
        db.AuditLogs.Add(new AuditLog { EntityType = "TaxDocument", Action = "IssueMissingTaxDocuments" });
        await db.SaveChangesAsync();
        (await db.Users.CountAsync()).Should().Be(0);
        (await db.AuditLogs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_WhenTheWorkFailsAfterAnAwait_ClearsTheChangeTrackerToo()
    {
        await using var db = CreateInMemoryDb();

        var act = () => new EfTransactionRunner(db).RunAsync<int>(async () =>
        {
            db.Users.Add(new User { ObjectId = "later", Email = "later@x", DisplayName = "Later", Role = UserRole.Buyer });
            await Task.Yield();
            throw new InvalidOperationException("failed later");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }
```

- [ ] **Step 2: Run red** — `dotnet test tests/Server.Tests --filter FullyQualifiedName~EfTransactionRunnerTests` → the two new
  tests fail (`ChangeTracker.Entries()` still holds the added `User`).

- [ ] **Step 3: Implement** — replace `RunAsync` in `src/Server/Data/ITransactionRunner.cs` (lines 16–30):

```csharp
// src/Server/Data/ITransactionRunner.cs (EfTransactionRunner.RunAsync)
    public Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        // The context uses EnableRetryOnFailure, so the transaction must run inside the execution
        // strategy. A rollback does not reset the change tracker, so each attempt clears it and
        // re-reads fresh state rather than reusing stale tracked entities from a failed attempt.
        var strategy = _db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            try
            {
                await using var tx = await _db.Database.BeginTransactionAsync();
                var result = await work();
                await tx.CommitAsync();
                return result;
            }
            catch
            {
                // A failed unit of work must leave nothing tracked: otherwise the caller's next SaveChanges
                // (e.g. the aggregate audit row after a failed "issue missing" item) would save what it added.
                _db.ChangeTracker.Clear();
                throw;
            }
        });
    }
```

- [ ] **Step 4: Run green** — `EfTransactionRunnerTests` (3 tests) pass; full build (0 warnings) and full test run.

- [ ] **Step 5: Commit**

```bash
git add src/Server/Data/ITransactionRunner.cs tests/Server.Tests/Data/EfTransactionRunnerTests.cs
git status --short
git commit -m "fix: transaction runner clears the change tracker when a unit of work fails

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Domain — wording catalog and versions, terms audience, stud terms acceptance, migration

**Files:**
- Create: `src/Shared/Enums/TermsAudience.cs`
- Create: `src/Server/Data/Entities/ContentItem.cs`
- Create: `src/Server/Content/ContentCatalog.cs`
- Modify: `src/Server/Data/Entities/TermsDocument.cs`, `src/Server/Data/Entities/StudFarm.cs:13`
- Modify: `src/Server/Data/AppDbContext.cs` (usings 1–3; DbSets after line 34; StudFarm block 61–81; TermsDocument block 352–362;
  new ContentItem block after the TaxDocument block, just before `public override int SaveChanges`)
- Create: migration `V2Phase5bEditableContent` (generated, one hand edit)
- Test: `tests/Server.Tests/Data/EditableContentModelTests.cs`

**Interfaces:**
- Produces: `enum TermsAudience { Buyer, Stud }`;
  `ContentItem { Guid Id; string Key; int Version; string? Subject; string Body; DateTime CreatedAt; Guid? CreatedByUserId; User? CreatedBy }`;
  `TermsDocument.Audience (TermsAudience, default Buyer)`;
  `StudFarm.AcceptedStudTermsVersion (int?)`, `.AcceptedStudTermsAt (DateTime?)`, `.AcceptedStudTermsByUserId (Guid?)`;
  `AppDbContext.ContentItems`;
  `record ContentPlaceholder(string Name, string Description, string Sample)`;
  `record ContentCatalogEntry(string Key, string Group, string Label, string Description, bool HasSubject, bool SingleParagraph, IReadOnlyList<ContentPlaceholder> Placeholders, IReadOnlyList<string> Required, string? DefaultSubject, string DefaultBody) { bool Allows(string name) }`;
  `static class ContentGroups { Disclosures, BuyerEmails, StudEmails, TaxDocumentEmails }`;
  `static class ContentKeys` (25 `const string`s);
  `static class ContentCatalog { const int MaxKeyLength = 100, MaxSubjectLength = 200, MaxBodyLength = 4000; DateTime SeededAt; IReadOnlyList<ContentCatalogEntry> Entries; ContentCatalogEntry? Find(string?); Guid SeedId(string key) }`
  (namespace `Stallions.Server.Content`).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Server.Tests/Data/EditableContentModelTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Content;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data;

public class EditableContentModelTests
{
    private readonly string _name = Guid.NewGuid().ToString();
    private AppDbContext Db() => DbContextFactory.Create(_name);

    [Fact]
    public async Task TheSeed_HasVersion1OfEveryCatalogItem_WithTheDefaultText_AndNoAuthor()
    {
        using var db = Db();
        await db.Database.EnsureCreatedAsync();

        var rows = await db.ContentItems.ToListAsync();
        rows.Should().HaveCount(ContentCatalog.Entries.Count);
        foreach (var entry in ContentCatalog.Entries)
        {
            var row = rows.Single(r => r.Key == entry.Key);
            row.Id.Should().Be(ContentCatalog.SeedId(entry.Key));
            row.Version.Should().Be(1);
            row.Subject.Should().Be(entry.DefaultSubject);
            row.Body.Should().Be(entry.DefaultBody);
            row.CreatedByUserId.Should().BeNull();
            row.CreatedAt.Should().Be(ContentCatalog.SeededAt);
        }
    }

    [Fact]
    public void ContentItems_AreUniquePerKeyAndVersion_AndFitTheirColumns()
    {
        using var db = Db();
        var item = db.Model.FindEntityType(typeof(ContentItem))!;
        item.GetIndexes().Should().Contain(i => i.IsUnique
            && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "Key", "Version" }));
        item.FindProperty("Key")!.GetMaxLength().Should().Be(ContentCatalog.MaxKeyLength);
        item.FindProperty("Subject")!.GetMaxLength().Should().Be(ContentCatalog.MaxSubjectLength);
        item.FindProperty("Body")!.GetMaxLength().Should().Be(ContentCatalog.MaxBodyLength);
    }

    [Fact]
    public void TermsDocuments_AreNumberedPerAudience()
    {
        using var db = Db();
        var terms = db.Model.FindEntityType(typeof(TermsDocument))!;
        terms.GetIndexes().Should().Contain(i => i.IsUnique
            && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "Audience", "Version" }));
        terms.GetIndexes().Should().NotContain(i => i.IsUnique && i.Properties.Count == 1 && i.Properties[0].Name == "Version");
        terms.FindProperty("Audience")!.GetMaxLength().Should().Be(10);
        new TermsDocument().Audience.Should().Be(TermsAudience.Buyer);
    }

    [Fact]
    public async Task AStudFarm_KeepsItsStudTermsAcceptance()
    {
        var farmId = Guid.NewGuid();
        var by = Guid.NewGuid();
        var at = new DateTime(2026, 10, 12, 2, 0, 0, DateTimeKind.Utc);
        using (var db = Db())
        {
            db.StudFarms.Add(new StudFarm { Id = farmId, UserId = Guid.NewGuid(), Name = "Arrowfield",
                AcceptedStudTermsVersion = 2, AcceptedStudTermsAt = at, AcceptedStudTermsByUserId = by });
            await db.SaveChangesAsync();
        }

        using var read = Db();
        var farm = await read.StudFarms.SingleAsync(f => f.Id == farmId);
        farm.AcceptedStudTermsVersion.Should().Be(2);
        farm.AcceptedStudTermsAt.Should().Be(at);
        farm.AcceptedStudTermsByUserId.Should().Be(by);
    }

    [Fact]
    public void Catalog_KeysAndSeedIds_AreUnique()
    {
        ContentCatalog.Entries.Select(e => e.Key).Should().OnlyHaveUniqueItems();
        ContentCatalog.Entries.Select(e => ContentCatalog.SeedId(e.Key)).Should().OnlyHaveUniqueItems();
        ContentCatalog.SeedId(ContentKeys.Outbid).Should().Be(ContentCatalog.SeedId(ContentKeys.Outbid));
    }

    [Fact]
    public void DisclosureDefaults_AreTheOldCheckoutWording()
    {
        // Copied from the appsettings.json "Checkout" section this phase removes.
        ContentCatalog.Find(ContentKeys.BuyerFeeExplanation)!.DefaultBody.Should().Be(
            "The buyer fee is paid to Stallions Australia and forms part of the price — it is not added on top. You pay the balance (the price less the buyer fee) directly to the stud, under the stud's own terms. The price arrangement is entirely between you and the stud.");
        ContentCatalog.Find(ContentKeys.StudFarmBalanceArrangement)!.DefaultBody.Should().Be(
            "The stud farm will contact you separately to arrange payment of the remaining balance. The balance amount, payment terms, and any foal guarantee conditions are a matter between you and the stud farm only. Stallions Australia is not party to that arrangement.");
        ContentCatalog.Find(ContentKeys.SavedCardExplanation)!.DefaultBody.Should().Be(
            "Your saved card is charged the buyer fee automatically when you win an auction or a stud accepts your offer. The nomination price is not charged to this card — you pay the balance directly to the stud, under the stud's own terms.");
    }
}
```

- [ ] **Step 2: Run red** — `dotnet test tests/Server.Tests --filter FullyQualifiedName~EditableContentModelTests` → compile errors
  (`ContentItem`, `ContentCatalog`, `TermsAudience`, new properties).

- [ ] **Step 3: Enum and entities**

```csharp
// src/Shared/Enums/TermsAudience.cs
namespace Stallions.Shared.Enums;

/// <summary>Who a terms document is for. Versions are numbered separately per audience.</summary>
public enum TermsAudience
{
    /// <summary>Buyer Terms &amp; Conditions, accepted before bidding or making an offer.</summary>
    Buyer,
    /// <summary>Stud listing terms, accepted before paying a listing fee or publishing a listing.</summary>
    Stud
}
```

```csharp
// src/Server/Data/Entities/ContentItem.cs
namespace Stallions.Server.Data.Entities;

/// <summary>
/// One version of a Staff-editable wording item (a disclosure or an email's subject and message). Never edited:
/// each save adds the next version, and the highest version of a key is current. Keys come from ContentCatalog.
/// </summary>
public class ContentItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = string.Empty;
    public int Version { get; set; }
    /// <summary>Emails only.</summary>
    public string? Subject { get; set; }
    /// <summary>Plain text; a blank line starts a new paragraph.</summary>
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Null for the seeded version 1.</summary>
    public Guid? CreatedByUserId { get; set; }

    public User? CreatedBy { get; set; }
}
```

```csharp
// src/Server/Data/Entities/TermsDocument.cs
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

public class TermsDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Buyer T&amp;C or stud listing terms; versions are numbered per audience.</summary>
    public TermsAudience Audience { get; set; } = TermsAudience.Buyer;
    public int Version { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedByUserId { get; set; }

    public User? CreatedBy { get; set; }
}
```

`src/Server/Data/Entities/StudFarm.cs` — after line 13 (`public Guid? StudDirectoryId { get; set; }`) add:

```csharp
// src/Server/Data/Entities/StudFarm.cs (after StudDirectoryId)
    /// <summary>The stud listing terms version this farm last accepted (null = never).</summary>
    public int? AcceptedStudTermsVersion { get; set; }
    public DateTime? AcceptedStudTermsAt { get; set; }
    public Guid? AcceptedStudTermsByUserId { get; set; }
```

- [ ] **Step 4: The catalog** (today's wording, copied verbatim from `AuctionEmails.cs`, `TaxDocumentEmails.cs` and the
  `Checkout` section of `appsettings.json`; Task 3 proves every default passes its own validation, Tasks 6–7 prove the
  output is unchanged)

```csharp
// src/Server/Content/ContentCatalog.cs
using System.Security.Cryptography;
using System.Text;

namespace Stallions.Server.Content;

/// <summary>A placeholder Staff can use in one wording item, with the example value the preview shows.</summary>
public sealed record ContentPlaceholder(string Name, string Description, string Sample);

/// <summary>
/// One Staff-editable wording item. The default subject/body are the wording the code used before Phase 5b: the
/// migration seeds them as version 1 and "Reset to default" republishes them. Required placeholders must appear in
/// the message body. Never change a default after it has shipped — Staff save new versions instead.
/// </summary>
public sealed record ContentCatalogEntry(
    string Key,
    string Group,
    string Label,
    string Description,
    bool HasSubject,
    bool SingleParagraph,
    IReadOnlyList<ContentPlaceholder> Placeholders,
    IReadOnlyList<string> Required,
    string? DefaultSubject,
    string DefaultBody)
{
    public bool Allows(string name) => Placeholders.Any(p => p.Name == name);
}

public static class ContentGroups
{
    public const string Disclosures = "Disclosures";
    public const string BuyerEmails = "Buyer emails";
    public const string StudEmails = "Stud emails";
    public const string TaxDocumentEmails = "Tax document emails";
}

public static class ContentKeys
{
    public const string BuyerFeeExplanation = "Disclosure.BuyerFeeExplanation";
    public const string StudFarmBalanceArrangement = "Disclosure.StudFarmBalanceArrangement";
    public const string SavedCardExplanation = "Disclosure.SavedCardExplanation";

    public const string Outbid = "Email.Outbid";
    public const string AuctionLost = "Email.AuctionLost";
    public const string AuctionEndedWithoutSale = "Email.AuctionEndedWithoutSale";
    public const string WonAndCharged = "Email.WonAndCharged";
    public const string OfferAcceptedAndCharged = "Email.OfferAcceptedAndCharged";
    public const string PaymentFailed = "Email.PaymentFailed";
    public const string PaymentFailedOffer = "Email.PaymentFailedOffer";
    public const string PaymentAbandoned = "Email.PaymentAbandoned";
    public const string PaymentAbandonedOffer = "Email.PaymentAbandonedOffer";
    public const string OfferDeclined = "Email.OfferDeclined";
    public const string OfferExpired = "Email.OfferExpired";
    public const string OfferExpiredListingClosed = "Email.OfferExpiredListingClosed";

    public const string StudSaleConfirmation = "Email.StudSaleConfirmation";
    public const string StudSaleConfirmationOffer = "Email.StudSaleConfirmationOffer";
    public const string StudNoSaleAuction = "Email.StudNoSaleAuction";
    public const string StudNoSaleMakeAnOffer = "Email.StudNoSaleMakeAnOffer";
    public const string OfferReceived = "Email.OfferReceived";

    public const string ListingFeeTaxInvoice = "Email.ListingFeeTaxInvoice";
    public const string ListingFeeInvoiceDue = "Email.ListingFeeInvoiceDue";
    public const string ListingFeeReceipt = "Email.ListingFeeReceipt";
    public const string BuyerFeeTaxInvoice = "Email.BuyerFeeTaxInvoice";
    public const string AdjustmentNote = "Email.AdjustmentNote";
}

public static class ContentCatalog
{
    public const int MaxKeyLength = 100;
    public const int MaxSubjectLength = 200;
    public const int MaxBodyLength = 4000;

    /// <summary>CreatedAt of the seeded version 1 rows.</summary>
    public static readonly DateTime SeededAt = new(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);

    public static IReadOnlyList<ContentCatalogEntry> Entries { get; } = Build();

    private static readonly Dictionary<string, ContentCatalogEntry> ByKey =
        Entries.ToDictionary(e => e.Key, StringComparer.Ordinal);

    public static ContentCatalogEntry? Find(string? key) =>
        key != null && ByKey.TryGetValue(key, out var entry) ? entry : null;

    /// <summary>A fixed id for the seeded version 1 row of a key (the same on every build and machine).</summary>
    public static Guid SeedId(string key) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("ContentItem:" + key)).AsSpan(0, 16));

    private static ContentCatalogEntry Disclosure(string key, string label, string description, string body) =>
        new(key, ContentGroups.Disclosures, label, description, HasSubject: false, SingleParagraph: true,
            [], [], null, body);

    private static ContentCatalogEntry Email(string key, string group, string label, string description,
        ContentPlaceholder[] placeholders, string[] required, string subject, string body) =>
        new(key, group, label, description, HasSubject: true, SingleParagraph: false, placeholders, required, subject, body);

    /// <summary>Placeholders (a nested class so its fields exist before Build runs).</summary>
    private static class P
    {
        public static readonly ContentPlaceholder StallionName = new("StallionName", "The stallion's name", "Snitzel");
        public static readonly ContentPlaceholder Season = new("Season", "The season, e.g. 2026 Season", "2026 Season");
        public static readonly ContentPlaceholder StudName = new("StudName", "The stud farm's name", "Arrowfield");
        public static readonly ContentPlaceholder BuyerName = new("BuyerName", "The buyer's name", "Jane Buyer");
        public static readonly ContentPlaceholder BuyerEmail = new("BuyerEmail", "The buyer's email address", "jane@example.com");
        public static readonly ContentPlaceholder BuyerFee = new("BuyerFee", "The buyer fee (inc. GST)", "$150.00");
        public static readonly ContentPlaceholder HighBid = new("HighBid", "The new highest bid (inc. GST)", "$12,500.00");
        public static readonly ContentPlaceholder ClosesAt = new("ClosesAt", "When the auction closes (Sydney time)", "10 Oct 2026, 8:00 pm (Sydney time)");
        public static readonly ContentPlaceholder WinningBid = new("Amount", "The winning bid (inc. GST)", "$10,000.00");
        public static readonly ContentPlaceholder OfferAmount = new("Amount", "The offer amount (inc. GST)", "$9,000.00");
        public static readonly ContentPlaceholder SalePrice = new("Amount", "The sale price (inc. GST)", "$10,000.00");
        public static readonly ContentPlaceholder PaymentDeadline = new("Deadline", "The deadline to update the card (Sydney time)", "10 Oct 2026, 10:00 pm (Sydney time)");
        public static readonly ContentPlaceholder OfferDeadline = new("Deadline", "When the offer expires (Sydney time)", "18 Oct 2026, 12:00 pm (Sydney time)");
        public static readonly ContentPlaceholder CardFailure = new("Reason", "Why the card was declined, from the card issuer", "Your card was declined.");
        public static readonly ContentPlaceholder NoSaleReason = new("Reason", "Why there was no sale, e.g. “the highest bid was below your reserve.”", "the highest bid was below your reserve.");
        public static readonly ContentPlaceholder InvoiceNumber = new("InvoiceNumber", "The tax invoice number", "SA-000123");
        public static readonly ContentPlaceholder NoteNumber = new("InvoiceNumber", "The adjustment note number", "SA-000130");
        public static readonly ContentPlaceholder ListingFeeLine = new("Description", "The invoice line", "Listing fee — Dundeel, 2026 Season");
        public static readonly ContentPlaceholder BuyerFeeLine = new("Description", "The invoice line", "Buyer fee — nomination to Snitzel, 2026 Season (auction)");
        public static readonly ContentPlaceholder NoteLine = new("Description", "The line of the invoice being reversed", "Buyer fee — nomination to Snitzel, 2026 Season (auction)");
        public static readonly ContentPlaceholder InvoiceTotal = new("Amount", "The invoice total (inc. GST)", "$990.00");
        public static readonly ContentPlaceholder BuyerFeeTotal = new("Amount", "The buyer fee charged (inc. GST)", "$150.00");
        public static readonly ContentPlaceholder NoteTotal = new("Amount", "The amount reversed (inc. GST)", "$150.00");
        public static readonly ContentPlaceholder InvoiceStud = new("StudName", "The stud farm the invoice is addressed to", "Arrowfield");
        public static readonly ContentPlaceholder InvoiceBuyer = new("BuyerName", "The buyer the invoice is addressed to", "Jane Buyer");
        public static readonly ContentPlaceholder DueDate = new("DueDate", "The payment due date", "25 Oct 2026");
        public static readonly ContentPlaceholder OriginalNumber = new("OriginalNumber", "The number of the invoice being reversed", "SA-000123");
        public static readonly ContentPlaceholder OriginalDate = new("OriginalDate", "The date of the invoice being reversed", "11 Oct 2026");
        public static readonly ContentPlaceholder AdjustmentReason = new("Reason", "The reason Staff gave for the refund or waiver", "Buyer fee refunded — duplicate charge");
    }

    private static IReadOnlyList<ContentCatalogEntry> Build() =>
    [
        // ── Disclosures (single paragraph, no placeholders) ─────────────────────
        Disclosure(ContentKeys.BuyerFeeExplanation, "Buyer fee explanation",
            "Shown before bidding or making an offer, in the bid and offer confirmations, in win and offer-accepted emails, and on buyer fee tax invoices.",
            "The buyer fee is paid to Stallions Australia and forms part of the price — it is not added on top. You pay the balance (the price less the buyer fee) directly to the stud, under the stud's own terms. The price arrangement is entirely between you and the stud."),
        Disclosure(ContentKeys.StudFarmBalanceArrangement, "Paying the stud (balance arrangement)",
            "Shown before bidding or making an offer, in the offer confirmation and in win and offer-accepted emails: how the balance is paid to the stud.",
            "The stud farm will contact you separately to arrange payment of the remaining balance. The balance amount, payment terms, and any foal guarantee conditions are a matter between you and the stud farm only. Stallions Australia is not party to that arrangement."),
        Disclosure(ContentKeys.SavedCardExplanation, "Saved card explanation",
            "Shown on the buyer's saved card page: what the card is (and isn't) charged for.",
            "Your saved card is charged the buyer fee automatically when you win an auction or a stud accepts your offer. The nomination price is not charged to this card — you pay the balance directly to the stud, under the stud's own terms."),

        // ── Buyer emails ───────────────────────────────────────────────────────
        Email(ContentKeys.Outbid, ContentGroups.BuyerEmails, "Outbid",
            "To a bidder when another buyer places a higher bid. A “Bid again” link is added below your text.",
            [P.StallionName, P.Season, P.StudName, P.HighBid, P.ClosesAt], ["StallionName", "HighBid"],
            "You've been outbid on {StallionName}",
            "Another buyer has bid {HighBid} on the {StallionName} nomination, so you are no longer the highest bidder.\n\nThe auction closes {ClosesAt}."),
        Email(ContentKeys.AuctionLost, ContentGroups.BuyerEmails, "Auction lost",
            "To each losing bidder when an auction closes with a sale.",
            [P.StallionName, P.Season, P.StudName], ["StallionName"],
            "Auction closed: {StallionName}",
            "The auction for the {StallionName} nomination ({Season}) has closed and another bid won.\n\nThank you for bidding."),
        Email(ContentKeys.AuctionEndedWithoutSale, ContentGroups.BuyerEmails, "Auction ended without a sale",
            "To each bidder when an auction closes without a sale.",
            [P.StallionName, P.Season, P.StudName], ["StallionName"],
            "Auction closed: {StallionName}",
            "The auction for the {StallionName} nomination ({Season}) has closed without a sale.\n\nThank you for bidding."),
        Email(ContentKeys.WonAndCharged, ContentGroups.BuyerEmails, "Auction won — buyer fee charged",
            "To the winning bidder once the buyer fee is charged. The amounts table, the buyer fee disclosure, the stud's contact details, the tax invoice link and the sale record link are added below your text.",
            [P.StallionName, P.Season, P.StudName, P.WinningBid, P.BuyerFee], ["StallionName", "Amount"],
            "You won the {StallionName} nomination",
            "Your bid of {Amount} won the auction for the {StallionName} nomination ({Season}), offered by {StudName}."),
        Email(ContentKeys.OfferAcceptedAndCharged, ContentGroups.BuyerEmails, "Offer accepted — buyer fee charged",
            "To the buyer once the stud accepts their offer and the buyer fee is charged. The amounts table, the buyer fee disclosure, the stud's contact details, the tax invoice link and the sale record link are added below your text.",
            [P.StallionName, P.Season, P.StudName, P.OfferAmount, P.BuyerFee], ["StallionName", "Amount"],
            "Your offer for the {StallionName} nomination was accepted",
            "Your offer of {Amount} for the {StallionName} nomination ({Season}) was accepted by {StudName}."),
        Email(ContentKeys.PaymentFailed, ContentGroups.BuyerEmails, "Buyer fee payment failed (auction)",
            "To the winning bidder when the buyer fee can't be charged; they have until the deadline to update their card. An “Update your card” link is added below your text.",
            [P.StallionName, P.Season, P.StudName, P.WinningBid, P.BuyerFee, P.CardFailure, P.PaymentDeadline], ["BuyerFee", "Deadline"],
            "Action needed: payment for {StallionName} failed",
            "Your bid of {Amount} won the auction for the {StallionName} nomination, but we couldn't charge the buyer fee of {BuyerFee} to your saved card: {Reason}\n\nPlease update your card by {Deadline}. Saving a new card retries the payment straight away. If the payment can't be completed by then, the sale won't go ahead."),
        Email(ContentKeys.PaymentFailedOffer, ContentGroups.BuyerEmails, "Buyer fee payment failed (accepted offer)",
            "To the buyer when the buyer fee for their accepted offer can't be charged; they have until the deadline to update their card. An “Update your card” link is added below your text.",
            [P.StallionName, P.Season, P.StudName, P.OfferAmount, P.BuyerFee, P.CardFailure, P.PaymentDeadline], ["BuyerFee", "Deadline"],
            "Action needed: payment for {StallionName} failed",
            "Your offer of {Amount} for the {StallionName} nomination was accepted, but we couldn't charge the buyer fee of {BuyerFee} to your saved card: {Reason}\n\nPlease update your card by {Deadline}. Saving a new card retries the payment straight away. If the payment can't be completed by then, the sale won't go ahead."),
        Email(ContentKeys.PaymentAbandoned, ContentGroups.BuyerEmails, "No sale — payment not completed (auction)",
            "To the winning bidder when the deadline passes without a successful buyer fee charge.",
            [P.StallionName, P.Season, P.StudName, P.WinningBid, P.BuyerFee], ["StallionName"],
            "No sale: {StallionName}",
            "We couldn't charge the buyer fee for the {StallionName} nomination by the deadline, so the sale has not gone ahead and the nomination is no longer yours."),
        Email(ContentKeys.PaymentAbandonedOffer, ContentGroups.BuyerEmails, "No sale — payment not completed (accepted offer)",
            "To the buyer when the deadline passes without a successful buyer fee charge for their accepted offer.",
            [P.StallionName, P.Season, P.StudName, P.OfferAmount, P.BuyerFee], ["StallionName"],
            "No sale: {StallionName}",
            "We couldn't charge the buyer fee for your accepted offer on the {StallionName} nomination by the deadline, so the sale has not gone ahead."),
        Email(ContentKeys.OfferDeclined, ContentGroups.BuyerEmails, "Offer declined",
            "To the buyer when the stud declines their offer. A “View the listing” link is added below your text.",
            [P.StallionName, P.Season, P.StudName, P.OfferAmount], ["Amount"],
            "Offer declined: {StallionName}",
            "The stud has declined your offer of {Amount} for the {StallionName} nomination ({Season})."),
        Email(ContentKeys.OfferExpired, ContentGroups.BuyerEmails, "Offer expired",
            "To the buyer when their offer expires without a response. A “View the listing” link is added below your text.",
            [P.StallionName, P.Season, P.StudName, P.OfferAmount], ["Amount"],
            "Offer closed: {StallionName}",
            "Your offer of {Amount} for the {StallionName} nomination ({Season}) has expired without a response from the stud."),
        Email(ContentKeys.OfferExpiredListingClosed, ContentGroups.BuyerEmails, "Offer closed — listing closed",
            "To the buyer when the listing stops taking offers while their offer is open. A “View the listing” link is added below your text.",
            [P.StallionName, P.Season, P.StudName, P.OfferAmount], ["Amount"],
            "Offer closed: {StallionName}",
            "The {StallionName} nomination ({Season}) is no longer taking offers, so your offer of {Amount} has closed without a response."),

        // ── Stud emails ────────────────────────────────────────────────────────
        Email(ContentKeys.StudSaleConfirmation, ContentGroups.StudEmails, "Sale confirmed (auction)",
            "To the stud when an auction sells and the buyer fee is paid. The amounts table, “Please arrange the balance with the buyer under your own terms.” and the listing link are added below your text.",
            [P.StallionName, P.Season, P.StudName, P.BuyerName, P.BuyerEmail, P.SalePrice, P.BuyerFee], ["BuyerName", "BuyerEmail"],
            "Sale confirmed: {StallionName} nomination",
            "The auction for the {StallionName} nomination ({Season}) has closed with a sale, and the buyer fee has been paid.\n\nBuyer: {BuyerName}, {BuyerEmail}"),
        Email(ContentKeys.StudSaleConfirmationOffer, ContentGroups.StudEmails, "Sale confirmed (accepted offer)",
            "To the stud when an accepted offer's buyer fee is paid. The amounts table, “Please arrange the balance with the buyer under your own terms.” and the listing link are added below your text.",
            [P.StallionName, P.Season, P.StudName, P.BuyerName, P.BuyerEmail, P.SalePrice, P.BuyerFee], ["BuyerName", "BuyerEmail"],
            "Sale confirmed: {StallionName} nomination",
            "You accepted an offer for the {StallionName} nomination ({Season}), and the buyer fee has been paid.\n\nBuyer: {BuyerName}, {BuyerEmail}"),
        Email(ContentKeys.StudNoSaleAuction, ContentGroups.StudEmails, "No sale (auction)",
            "To the stud when an auction closes without a sale (no bids, reserve not met, or the winner's payment failed). An “Offer it as Make an Offer” link is added below your text.",
            [P.StallionName, P.Season, P.StudName, P.NoSaleReason], ["Reason"],
            "No sale: {StallionName} nomination",
            "The auction for the {StallionName} nomination ({Season}) has closed without a sale: {Reason}\n\nThe lot is marked Unsold in My Listings."),
        Email(ContentKeys.StudNoSaleMakeAnOffer, ContentGroups.StudEmails, "No sale (accepted offer)",
            "To the stud when the buyer fee for an offer they accepted couldn't be charged. A “View the listing” link is added below your text.",
            [P.StallionName, P.Season, P.StudName], ["StallionName"],
            "No sale: {StallionName} nomination",
            "The sale from the offer you accepted for the {StallionName} nomination ({Season}) has not gone ahead: the buyer's payment couldn't be completed.\n\nYour Make an Offer listing stays open for other offers."),
        Email(ContentKeys.OfferReceived, ContentGroups.StudEmails, "New offer",
            "To the stud when a buyer makes an offer. A “Review the offer” link is added below your text.",
            [P.StallionName, P.Season, P.StudName, P.BuyerName, P.BuyerEmail, P.OfferAmount, P.OfferDeadline], ["BuyerName", "BuyerEmail", "Amount", "Deadline"],
            "New offer: {StallionName} nomination",
            "{BuyerName} ({BuyerEmail}) has offered {Amount} for the {StallionName} nomination ({Season}).\n\nThe offer is open until {Deadline}. Accepting it charges the buyer the buyer fee straight away."),

        // ── Tax document emails ────────────────────────────────────────────────
        Email(ContentKeys.ListingFeeTaxInvoice, ContentGroups.TaxDocumentEmails, "Listing fee tax invoice (paid)",
            "To the stud when a listing fee is paid by card or marked paid. The invoice details, “Status: Paid” and the invoice link are added below your text.",
            [P.InvoiceNumber, P.ListingFeeLine, P.InvoiceTotal, P.InvoiceStud], [],
            "Tax invoice {InvoiceNumber}: {Description}",
            "Thank you — your listing fee has been paid. Your tax invoice is below."),
        Email(ContentKeys.ListingFeeInvoiceDue, ContentGroups.TaxDocumentEmails, "Listing fee tax invoice (amount due)",
            "To the stud when Staff issue an unpaid listing fee invoice. The invoice details, bank details, payment reference and invoice link are added below your text.",
            [P.InvoiceNumber, P.ListingFeeLine, P.InvoiceTotal, P.InvoiceStud, P.DueDate], ["Amount", "DueDate"],
            "Tax invoice {InvoiceNumber} — amount due {Amount} by {DueDate}",
            "Here is your tax invoice for the listing fee. Please pay {Amount} by {DueDate}."),
        Email(ContentKeys.ListingFeeReceipt, ContentGroups.TaxDocumentEmails, "Listing fee receipt",
            "To the stud when an unpaid listing fee invoice is marked paid. The invoice details, the paid date and the invoice link are added below your text.",
            [P.InvoiceNumber, P.ListingFeeLine, P.InvoiceTotal, P.InvoiceStud], ["Amount"],
            "Receipt: tax invoice {InvoiceNumber} paid",
            "We've received your payment of {Amount} for tax invoice {InvoiceNumber}. Thank you."),
        Email(ContentKeys.BuyerFeeTaxInvoice, ContentGroups.TaxDocumentEmails, "Buyer fee tax invoice",
            "To the buyer when the buyer fee is charged. The invoice details, the buyer fee explanation and the invoice link are added below your text.",
            [P.InvoiceNumber, P.BuyerFeeLine, P.BuyerFeeTotal, P.InvoiceBuyer], [],
            "Tax invoice {InvoiceNumber}: buyer fee",
            "The buyer fee has been charged to your saved card. Your tax invoice is below."),
        Email(ContentKeys.AdjustmentNote, ContentGroups.TaxDocumentEmails, "Adjustment note",
            "To the original invoice's recipient when a fee is refunded or an invoiced fee is waived. The note's details and link are added below your text.",
            [P.NoteNumber, P.OriginalNumber, P.OriginalDate, P.NoteLine, P.NoteTotal, P.AdjustmentReason], ["OriginalNumber", "Reason"],
            "Adjustment note {InvoiceNumber} for tax invoice {OriginalNumber}",
            "This adjustment note reverses tax invoice {OriginalNumber} dated {OriginalDate} in full.\n\nReason: {Reason}")
    ];
}
```

- [ ] **Step 5: `AppDbContext`**

Add `using Stallions.Server.Content;` to the usings (lines 1–3). After line 34 (`public DbSet<TaxDocument> TaxDocuments …`) add:

```csharp
// src/Server/Data/AppDbContext.cs (DbSets)
    public DbSet<ContentItem> ContentItems => Set<ContentItem>();
```

In the StudFarm block, after the `StudDirectory` relationship (line 80, before `});` on line 81) add:

```csharp
// src/Server/Data/AppDbContext.cs (inside modelBuilder.Entity<StudFarm>)
            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(f => f.AcceptedStudTermsByUserId)
                .OnDelete(DeleteBehavior.NoAction);
```

Replace the TermsDocument block (lines 352–362) with:

```csharp
// src/Server/Data/AppDbContext.cs (TermsDocument)
        // ── TermsDocument (buyer T&C and stud listing terms, versioned per audience) ──
        modelBuilder.Entity<TermsDocument>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Body).IsRequired();
            e.Property(t => t.Audience).HasConversion<string>().HasMaxLength(10).IsRequired();
            e.HasIndex(t => new { t.Audience, t.Version }).IsUnique();
            e.HasOne(t => t.CreatedBy)
                .WithMany()
                .HasForeignKey(t => t.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
```

After the TaxDocument block (the `});` just before `public override int SaveChanges(…)`, still inside `OnModelCreating`) add:

```csharp
// src/Server/Data/AppDbContext.cs (end of OnModelCreating)
        // ── Editable wording (one row per version; the highest version of a key is current) ──
        modelBuilder.Entity<ContentItem>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Key).HasMaxLength(ContentCatalog.MaxKeyLength).IsRequired();
            e.Property(c => c.Subject).HasMaxLength(ContentCatalog.MaxSubjectLength);
            e.Property(c => c.Body).HasMaxLength(ContentCatalog.MaxBodyLength).IsRequired();
            e.HasIndex(c => new { c.Key, c.Version }).IsUnique();
            e.HasOne(c => c.CreatedBy).WithMany().HasForeignKey(c => c.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);

            // Version 1 of every catalog item is the wording the code used before Phase 5b.
            e.HasData(ContentCatalog.Entries.Select(c => new ContentItem
            {
                Id = ContentCatalog.SeedId(c.Key),
                Key = c.Key,
                Version = 1,
                Subject = c.DefaultSubject,
                Body = c.DefaultBody,
                CreatedAt = ContentCatalog.SeededAt,
                CreatedByUserId = null
            }));
        });
```

- [ ] **Step 6: Run green** — the six `EditableContentModelTests` pass.

- [ ] **Step 7: Migration** — `dotnet ef migrations add V2Phase5bEditableContent --project src/Server --startup-project src/Server --output-dir Data/Migrations`.
  In the generated `Up`, find `migrationBuilder.AddColumn<string>(name: "Audience", table: "TermsDocuments", … defaultValue: "")`
  and change `defaultValue: ""` to `defaultValue: "Buyer"` (existing rows are buyer T&C). Read the whole migration: it must
  only `CreateTable` `ContentItems` (with `IX_ContentItems_Key_Version` unique and the `CreatedByUserId` FK + index),
  `InsertData` the 25 seed rows, add `TermsDocuments.Audience`, `DropIndex` `IX_TermsDocuments_Version`, `CreateIndex`
  `IX_TermsDocuments_Audience_Version` (unique), add the three `StudFarms` columns plus `IX_StudFarms_AcceptedStudTermsByUserId`
  and its FK. `Down` must reverse each (it recreates `IX_TermsDocuments_Version` as unique — fine on dev data that only has
  buyer rows). Check `dotnet ef migrations has-pending-model-changes --project src/Server --startup-project src/Server` → no changes.
  Apply locally: `dotnet ef database update --project src/Server --startup-project src/Server --connection "Server=(localdb)\mssqllocaldb;Database=StallionsNomsDev;Trusted_Connection=True;MultipleActiveResultSets=true"`.
  Then in SQL: `SELECT COUNT(*) FROM ContentItems WHERE Version = 1 AND CreatedByUserId IS NULL` → 25;
  `SELECT DISTINCT Audience FROM TermsDocuments` → `Buyer` (or no rows).

- [ ] **Step 8: Full build (0 warnings) and full test run → green.**

- [ ] **Step 9: Commit**

```bash
git add src/Shared/Enums/TermsAudience.cs src/Server/Data/Entities/ContentItem.cs src/Server/Data/Entities/TermsDocument.cs \
  src/Server/Data/Entities/StudFarm.cs src/Server/Content/ContentCatalog.cs src/Server/Data/AppDbContext.cs \
  src/Server/Data/Migrations tests/Server.Tests/Data/EditableContentModelTests.cs
git status --short
git commit -m "feat: phase 5b domain — wording catalog and versions, terms audience, stud terms acceptance

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Placeholder validation and rendering; catalog self-checks

**Files:**
- Create: `src/Server/Content/ContentTemplate.cs`
- Test: `tests/Server.Tests/Content/ContentTemplateTests.cs`, `tests/Server.Tests/Content/ContentCatalogTests.cs`

**Interfaces:**
- Consumes: Task 2 `ContentCatalogEntry`, `ContentCatalog`, `ContentKeys`, `ContentGroups`.
- Produces: `sealed record RenderedParagraph(string Html, string Text)` (Html = encoded inner HTML, line breaks as `<br>`, no `<p>`);
  `static class ContentTemplate { string Normalise(string?); IReadOnlyList<string>? PlaceholderNames(string); IReadOnlyList<string> Validate(ContentCatalogEntry, string? subject, string? body); string RenderLine(string, IReadOnlyDictionary<string,string?>, ISet<string> missing); IReadOnlyList<RenderedParagraph> RenderBody(string, IReadOnlyDictionary<string,string?>, ISet<string> missing) }`.
  Error texts (used by later tasks' tests): `"{Name} isn't available in this email."`, `"{Name} isn't available here — this wording has no placeholders."`,
  `"The message must include {Name}."`, `"The subject can't be empty."`, `"The message can't be empty."`,
  `"The subject can be at most 200 characters."`, `"The message can be at most 4,000 characters."`, `"The subject must be on one line."`,
  `"This wording is a single paragraph — remove the line breaks."`, and one starting `"Check the braces"`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Server.Tests/Content/ContentTemplateTests.cs
using FluentAssertions;
using Stallions.Server.Content;

namespace Stallions.Server.Tests.Content;

public class ContentTemplateTests
{
    private static readonly ContentCatalogEntry Email = new("Email.Test", ContentGroups.BuyerEmails, "Test", "A test email",
        HasSubject: true, SingleParagraph: false,
        [new ContentPlaceholder("StallionName", "The stallion", "Snitzel"), new ContentPlaceholder("Amount", "The amount", "$10,000.00")],
        ["Amount"], "Sold: {StallionName}", "You paid {Amount}.");

    private static readonly ContentCatalogEntry Disclosure = new("Disclosure.Test", ContentGroups.Disclosures, "Test", "A test disclosure",
        HasSubject: false, SingleParagraph: true, [], [], null, "Fee text.");

    private static readonly IReadOnlyDictionary<string, string?> Values = new Dictionary<string, string?>
    {
        ["StallionName"] = "Snitzel", ["Amount"] = "$10,000.00"
    };

    // ── Validation ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_GoodText_HasNoErrors() =>
        ContentTemplate.Validate(Email, "Sold: {StallionName}", "You won {StallionName}.\n\nYou paid {Amount}.").Should().BeEmpty();

    [Fact]
    public void Validate_UnknownPlaceholder_NamesIt() =>
        ContentTemplate.Validate(Email, "Sold", "You paid {Amount} for {Stalion}.")
            .Should().Equal("{Stalion} isn't available in this email.");

    [Fact]
    public void Validate_UnknownPlaceholderInTheSubject_IsRejectedToo() =>
        ContentTemplate.Validate(Email, "Sold: {Horse}", "You paid {Amount}.")
            .Should().Equal("{Horse} isn't available in this email.");

    [Fact]
    public void Validate_PlaceholderNamesAreCaseSensitive() =>
        ContentTemplate.Validate(Email, "Sold", "You paid {amount}.")
            .Should().BeEquivalentTo("{amount} isn't available in this email.", "The message must include {Amount}.");

    [Fact]
    public void Validate_MissingRequiredPlaceholder() =>
        ContentTemplate.Validate(Email, "Sold", "You won.").Should().Equal("The message must include {Amount}.");

    [Fact]
    public void Validate_ARequiredPlaceholderOnlyInTheSubject_DoesNotCount() =>
        ContentTemplate.Validate(Email, "You paid {Amount}", "You won.").Should().Equal("The message must include {Amount}.");

    [Theory]
    [InlineData("You paid {Amount")]
    [InlineData("You paid Amount}")]
    [InlineData("You paid {{Amount}}")]
    [InlineData("You paid {Amo{unt}")]
    public void Validate_UnbalancedBraces(string body) =>
        ContentTemplate.Validate(Email, "Sold", body).Should().ContainSingle(e => e.StartsWith("Check the braces"));

    [Fact]
    public void Validate_EmptySubjectAndBody() =>
        ContentTemplate.Validate(Email, "  ", " \r\n ").Should().BeEquivalentTo(
            "The subject can't be empty.", "The message can't be empty.", "The message must include {Amount}.");

    [Fact]
    public void Validate_TooLong() =>
        ContentTemplate.Validate(Email, new string('s', 201), "{Amount}" + new string('b', 4000)).Should().BeEquivalentTo(
            "The subject can be at most 200 characters.", "The message can be at most 4,000 characters.");

    [Fact]
    public void Validate_SubjectOnTwoLines_IsRejected() =>
        ContentTemplate.Validate(Email, "Sold\nBcc: someone@evil.example", "You paid {Amount}.")
            .Should().Equal("The subject must be on one line.");

    [Fact]
    public void Validate_DisclosureWithAPlaceholder() =>
        ContentTemplate.Validate(Disclosure, null, "The fee is {Amount}.")
            .Should().Equal("{Amount} isn't available here — this wording has no placeholders.");

    [Fact]
    public void Validate_DisclosureWithALineBreak() =>
        ContentTemplate.Validate(Disclosure, null, "One.\n\nTwo.")
            .Should().Equal("This wording is a single paragraph — remove the line breaks.");

    [Fact]
    public void Validate_DisclosureIgnoresASubject() =>
        ContentTemplate.Validate(Disclosure, "anything {Foo}\nat all", "Fee text.").Should().BeEmpty();

    // ── Rendering ───────────────────────────────────────────────────────────

    [Fact]
    public void RenderBody_SubstitutesValues_AndSplitsParagraphsOnBlankLines()
    {
        var missing = new HashSet<string>();

        var paragraphs = ContentTemplate.RenderBody("You won {StallionName}.\n\nYou paid {Amount}.", Values, missing);

        paragraphs.Should().Equal(
            new RenderedParagraph("You won Snitzel.", "You won Snitzel."),
            new RenderedParagraph("You paid $10,000.00.", "You paid $10,000.00."));
        missing.Should().BeEmpty();
    }

    [Fact]
    public void RenderBody_ALineBreakInsideAParagraph_IsABrInHtml_AndStaysInText() =>
        ContentTemplate.RenderBody("Line one\nLine two", Values, new HashSet<string>())
            .Should().Equal(new RenderedParagraph("Line one<br>Line two", "Line one\nLine two"));

    [Fact]
    public void RenderBody_NormalisesWindowsLineEndings_AndExtraBlankLines() =>
        ContentTemplate.RenderBody("One\r\n\r\n\r\n  \r\nTwo\r\n", Values, new HashSet<string>())
            .Select(p => p.Text).Should().Equal("One", "Two");

    [Fact]
    public void RenderBody_EncodesStaffTextAndValues()
    {
        var values = new Dictionary<string, string?> { ["StallionName"] = "<b>Snitzel</b>" };

        var paragraph = ContentTemplate.RenderBody("<script>alert(1)</script> {StallionName} & co", values, new HashSet<string>()).Single();

        paragraph.Html.Should().Be("&lt;script&gt;alert(1)&lt;/script&gt; &lt;b&gt;Snitzel&lt;/b&gt; &amp; co");
        paragraph.Text.Should().Be("<script>alert(1)</script> <b>Snitzel</b> & co");
    }

    [Fact]
    public void Render_APlaceholderWithNoValue_IsEmpty_AndReported()
    {
        var missing = new HashSet<string>();

        ContentTemplate.RenderLine("{BuyerFee} paid", Values, missing).Should().Be(" paid");
        missing.Should().Equal("BuyerFee");
    }

    [Fact]
    public void Render_ANullValue_IsEmpty_AndNotReported()
    {
        var missing = new HashSet<string>();

        ContentTemplate.RenderLine("Declined: {Reason}", new Dictionary<string, string?> { ["Reason"] = null }, missing)
            .Should().Be("Declined: ");
        missing.Should().BeEmpty();
    }

    [Fact]
    public void RenderLine_RendersASubject_WithoutEncoding() =>
        ContentTemplate.RenderLine("You've been outbid on {StallionName}", Values, new HashSet<string>())
            .Should().Be("You've been outbid on Snitzel");
}
```

```csharp
// tests/Server.Tests/Content/ContentCatalogTests.cs
using FluentAssertions;
using Stallions.Server.Content;

namespace Stallions.Server.Tests.Content;

public class ContentCatalogTests
{
    [Fact]
    public void TheCatalog_HasTheAgreedKeys_InOrder() =>
        ContentCatalog.Entries.Select(e => e.Key).Should().Equal(
            "Disclosure.BuyerFeeExplanation", "Disclosure.StudFarmBalanceArrangement", "Disclosure.SavedCardExplanation",
            "Email.Outbid", "Email.AuctionLost", "Email.AuctionEndedWithoutSale", "Email.WonAndCharged",
            "Email.OfferAcceptedAndCharged", "Email.PaymentFailed", "Email.PaymentFailedOffer", "Email.PaymentAbandoned",
            "Email.PaymentAbandonedOffer", "Email.OfferDeclined", "Email.OfferExpired", "Email.OfferExpiredListingClosed",
            "Email.StudSaleConfirmation", "Email.StudSaleConfirmationOffer", "Email.StudNoSaleAuction",
            "Email.StudNoSaleMakeAnOffer", "Email.OfferReceived",
            "Email.ListingFeeTaxInvoice", "Email.ListingFeeInvoiceDue", "Email.ListingFeeReceipt",
            "Email.BuyerFeeTaxInvoice", "Email.AdjustmentNote");

    [Fact]
    public void EveryDefault_PassesItsOwnValidation()
    {
        foreach (var entry in ContentCatalog.Entries)
            ContentTemplate.Validate(entry, entry.DefaultSubject, entry.DefaultBody).Should().BeEmpty(entry.Key);
    }

    [Fact]
    public void Emails_HaveASubject_AndDisclosures_HaveNoSubjectOrPlaceholders_AndAreOneParagraph()
    {
        foreach (var entry in ContentCatalog.Entries)
        {
            if (entry.Group == ContentGroups.Disclosures)
            {
                entry.HasSubject.Should().BeFalse(entry.Key);
                entry.DefaultSubject.Should().BeNull(entry.Key);
                entry.Placeholders.Should().BeEmpty(entry.Key);
                entry.SingleParagraph.Should().BeTrue(entry.Key);
            }
            else
            {
                entry.Key.Should().StartWith("Email.");
                entry.HasSubject.Should().BeTrue(entry.Key);
                entry.DefaultSubject.Should().NotBeNullOrWhiteSpace(entry.Key);
            }
        }
    }

    [Fact]
    public void RequiredPlaceholders_AreAllowed_AndNamesAreUniquePerItem()
    {
        foreach (var entry in ContentCatalog.Entries)
        {
            entry.Placeholders.Select(p => p.Name).Should().OnlyHaveUniqueItems(entry.Key);
            entry.Required.Should().OnlyContain(r => entry.Allows(r), entry.Key);
        }
    }

    [Fact]
    public void EveryItem_IsLabelled_Grouped_AndFitsItsColumns()
    {
        var groups = new[] { ContentGroups.Disclosures, ContentGroups.BuyerEmails, ContentGroups.StudEmails, ContentGroups.TaxDocumentEmails };
        foreach (var entry in ContentCatalog.Entries)
        {
            groups.Should().Contain(entry.Group, entry.Key);
            entry.Label.Should().NotBeNullOrWhiteSpace(entry.Key);
            entry.Description.Should().NotBeNullOrWhiteSpace(entry.Key);
            entry.Key.Length.Should().BeLessThanOrEqualTo(ContentCatalog.MaxKeyLength);
            entry.Placeholders.Should().OnlyContain(p => p.Description.Length > 0 && p.Sample.Length > 0, entry.Key);
        }
    }

    [Fact]
    public void Find_AnUnknownKey_IsNull()
    {
        ContentCatalog.Find("Email.NoSuchThing").Should().BeNull();
        ContentCatalog.Find(null).Should().BeNull();
        ContentCatalog.Find(ContentKeys.Outbid)!.Label.Should().Be("Outbid");
    }
}
```

- [ ] **Step 2: Run red** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~Stallions.Server.Tests.Content"` → compile errors (`ContentTemplate`, `RenderedParagraph`).

- [ ] **Step 3: Implement**

```csharp
// src/Server/Content/ContentTemplate.cs
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Stallions.Server.Content;

/// <summary>One rendered paragraph: Html is the encoded inner HTML (line breaks as &lt;br&gt;, no &lt;p&gt;); Text is plain.</summary>
public sealed record RenderedParagraph(string Html, string Text);

/// <summary>
/// Staff wording is plain text with {Placeholder} names (case-sensitive). A blank line starts a new paragraph; a single
/// line break becomes &lt;br&gt;. Substituted text — Staff's and the values — is HTML-encoded, never trusted as markup.
/// </summary>
public static partial class ContentTemplate
{
    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex BlankLines();

    public static string Normalise(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>The placeholder names in the text, in order, or null when its braces don't pair up.</summary>
    public static IReadOnlyList<string>? PlaceholderNames(string text)
    {
        var names = new List<string>();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '}') return null;
            if (text[i] != '{') continue;
            var end = text.IndexOf('}', i + 1);
            if (end < 0) return null;
            var name = text[(i + 1)..end];
            if (name.Contains('{')) return null;
            names.Add(name);
            i = end;
        }
        return names;
    }

    /// <summary>Every problem with this wording for this item; empty when it can be saved.</summary>
    public static IReadOnlyList<string> Validate(ContentCatalogEntry entry, string? subject, string? body)
    {
        var errors = new List<string>();
        var text = Normalise(body).Trim();
        var subjectText = entry.HasSubject ? (subject ?? string.Empty).Trim() : string.Empty;

        if (entry.HasSubject)
        {
            if (subjectText.Length == 0)
                errors.Add("The subject can't be empty.");
            else if (subjectText.Length > ContentCatalog.MaxSubjectLength)
                errors.Add($"The subject can be at most {ContentCatalog.MaxSubjectLength} characters.");
            if (subjectText.Contains('\n') || subjectText.Contains('\r'))
                errors.Add("The subject must be on one line.");
        }

        if (text.Length == 0)
            errors.Add("The message can't be empty.");
        else if (text.Length > ContentCatalog.MaxBodyLength)
            errors.Add($"The message can be at most {ContentCatalog.MaxBodyLength.ToString("N0", CultureInfo.InvariantCulture)} characters.");
        if (entry.SingleParagraph && text.Contains('\n'))
            errors.Add("This wording is a single paragraph — remove the line breaks.");

        var subjectNames = PlaceholderNames(subjectText);
        var bodyNames = PlaceholderNames(text);
        if (subjectNames == null || bodyNames == null)
        {
            errors.Add("Check the braces: each { must be closed by a } around a placeholder name, e.g. {StallionName}.");
            return errors;
        }

        foreach (var name in subjectNames.Concat(bodyNames).Distinct(StringComparer.Ordinal))
            if (!entry.Allows(name))
                errors.Add(entry.Placeholders.Count == 0
                    ? $"{{{name}}} isn't available here — this wording has no placeholders."
                    : $"{{{name}}} isn't available in this email.");

        foreach (var required in entry.Required)
            if (!bodyNames.Contains(required))
                errors.Add($"The message must include {{{required}}}.");

        return errors;
    }

    /// <summary>
    /// Substitutes the placeholders in one line (not encoded — used for subjects, and per line by RenderBody).
    /// A placeholder with no entry in values is rendered empty and added to missing; a null value is just empty.
    /// </summary>
    public static string RenderLine(string template, IReadOnlyDictionary<string, string?> values, ISet<string> missing)
    {
        var sb = new StringBuilder(template.Length);
        var i = 0;
        while (i < template.Length)
        {
            var open = template.IndexOf('{', i);
            var close = open < 0 ? -1 : template.IndexOf('}', open + 1);
            if (open < 0 || close < 0)
            {
                sb.Append(template, i, template.Length - i);
                break;
            }
            sb.Append(template, i, open - i);
            var name = template[(open + 1)..close];
            if (values.TryGetValue(name, out var value)) sb.Append(value);
            else missing.Add(name);
            i = close + 1;
        }
        return sb.ToString();
    }

    /// <summary>Splits on blank lines into paragraphs, substitutes each line, and encodes the HTML form.</summary>
    public static IReadOnlyList<RenderedParagraph> RenderBody(string template, IReadOnlyDictionary<string, string?> values,
        ISet<string> missing)
    {
        var paragraphs = new List<RenderedParagraph>();
        foreach (var raw in BlankLines().Split(Normalise(template)))
        {
            var paragraph = raw.Trim();
            if (paragraph.Length == 0) continue;
            var lines = paragraph.Split('\n').Select(line => RenderLine(line, values, missing)).ToList();
            paragraphs.Add(new RenderedParagraph(
                string.Join("<br>", lines.Select(WebUtility.HtmlEncode)),
                string.Join("\n", lines)));
        }
        return paragraphs;
    }
}
```

- [ ] **Step 4: Run green** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~Stallions.Server.Tests.Content"`; full build (0 warnings) and full test run.

- [ ] **Step 5: Commit**

```bash
git add src/Server/Content/ContentTemplate.cs tests/Server.Tests/Content/ContentTemplateTests.cs tests/Server.Tests/Content/ContentCatalogTests.cs
git status --short
git commit -m "feat: wording placeholders — validation and encoded paragraph rendering

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 4: `ContentService` — cached reads, rendering, Staff editing (versions, restore, reset, preview, audit) and API

**Files:**
- Create: `src/Shared/DTOs/Content/ContentItemSummaryDto.cs`, `ContentItemDetailDto.cs`, `ContentPlaceholderDto.cs`,
  `ContentVersionDto.cs`, `SaveContentRequest.cs`, `ContentPreviewDto.cs`
- Create: `src/Server/Data/Repositories/IContentRepository.cs`, `src/Server/Data/Repositories/ContentRepository.cs`
- Create: `src/Server/Content/ContentCache.cs`, `src/Server/Content/ContentService.cs`, `src/Server/Controllers/ContentController.cs`
- Modify: `src/Server/Program.cs` (one using; three registrations after line 150)
- Create (test helpers): `tests/Server.Tests/Helpers/CapturingLogger.cs`, `tests/Server.Tests/Helpers/ContentTestData.cs`
- Test: `tests/Server.Tests/Content/ContentServiceTests.cs`, `tests/Server.Tests/Data/Repositories/ContentRepositoryTests.cs`,
  `tests/Server.Tests/Controllers/ContentControllerTests.cs`

**Interfaces:**
- Consumes: Task 2 `ContentItem`, `AppDbContext.ContentItems`, `ContentCatalog`, `ContentKeys`; Task 3 `ContentTemplate`,
  `RenderedParagraph`; existing `IAuditLogRepository`, `IUserService`, `ITransactionRunner`, `TimeProvider`, `DbErrors`.
- Produces:
  `IContentRepository { Task<ContentItem?> GetCurrentAsync(string key); Task<IReadOnlyList<ContentItem>> GetCurrentForAllAsync(); Task<IReadOnlyList<ContentItem>> GetHistoryAsync(string key); Task<ContentItem?> GetVersionAsync(string key, int version); Task<bool> TryAddAsync(ContentItem item) }`
  (`ContentRepository.TryAddAsync` is `virtual`);
  `sealed class ContentCache(TimeProvider) { static TimeSpan Lifetime = 60 s; bool TryGet(string, out ContentText?); void Set(string, ContentText); void Clear() }` (singleton);
  `sealed record ContentText(string Key, int Version, string? Subject, string Body)` (Version 0 = catalog default, nothing stored);
  `sealed record RenderedEmail(string Subject, IReadOnlyList<RenderedParagraph> Paragraphs)`;
  `IContentService { Task<ContentText> GetAsync(string key); Task<IReadOnlyList<RenderedParagraph>> RenderTextAsync(string key); Task<RenderedEmail> RenderEmailAsync(string key, IReadOnlyDictionary<string,string?> values); Task<ServiceResult<IReadOnlyList<ContentItemSummaryDto>>> ListAsync(); Task<ServiceResult<ContentItemDetailDto>> GetDetailAsync(string key); Task<ServiceResult<IReadOnlyList<ContentVersionDto>>> GetHistoryAsync(string key); Task<ServiceResult<ContentItemDetailDto>> SaveAsync(string key, SaveContentRequest request); Task<ServiceResult<ContentItemDetailDto>> RestoreAsync(string key, int version); Task<ServiceResult<ContentItemDetailDto>> ResetAsync(string key); Task<ServiceResult<ContentPreviewDto>> PreviewAsync(string key, SaveContentRequest request) }`;
  `ContentService.AuditEntity = "ContentItem"`, `ContentService.AuditAction = "UpdateWording"`; audit details
  `{ Key, Version, Source ("Save" | "Restore" | "Reset"), RestoredFromVersion, Old { Version, Subject, Body } | null, New { Subject, Body } }`;
  routes (all StaffOnly): `GET api/content`, `GET api/content/{key}`, `GET api/content/{key}/versions`, `PUT api/content/{key}`,
  `POST api/content/{key}/versions/{version}/restore`, `POST api/content/{key}/reset`, `POST api/content/{key}/preview`;
  DTOs in `Stallions.Shared.DTOs.Content`; test helpers `CapturingLogger<T>` (`Warnings`), `ContentTestData.Service(...)`, `ContentTestData.SetWording(db, key, subject, body)`.

- [ ] **Step 1: Test helpers**

```csharp
// tests/Server.Tests/Helpers/CapturingLogger.cs
using Microsoft.Extensions.Logging;

namespace Stallions.Server.Tests.Helpers;

/// <summary>An ILogger that keeps what was logged, so a test can assert no warning was written.</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    public IEnumerable<string> Warnings => Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
}
```

```csharp
// tests/Server.Tests/Helpers/ContentTestData.cs
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Content;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;

namespace Stallions.Server.Tests.Helpers;

/// <summary>A real ContentService over a test database (call Database.EnsureCreated() first for the seeded wording).</summary>
public static class ContentTestData
{
    public static ContentService Service(AppDbContext db, TimeProvider? clock = null, ILogger<ContentService>? log = null,
        IUserService? users = null, ContentCache? cache = null)
    {
        var time = clock ?? TimeProvider.System;
        return new ContentService(new ContentRepository(db), cache ?? new ContentCache(time), new AuditLogRepository(db),
            users ?? new Mock<IUserService>().Object, new InlineTransactionRunner(), time,
            log ?? NullLogger<ContentService>.Instance);
    }

    /// <summary>Stores the next version of a key directly, as if Staff had saved it (no validation).</summary>
    public static void SetWording(AppDbContext db, string key, string? subject, string body)
    {
        var current = db.ContentItems.Where(c => c.Key == key).Select(c => (int?)c.Version).Max() ?? 0;
        db.ContentItems.Add(new ContentItem
        {
            Key = key, Version = current + 1, Subject = subject, Body = body, CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/Server.Tests/Data/Repositories/ContentRepositoryTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Stallions.Server.Content;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;

namespace Stallions.Server.Tests.Data.Repositories;

public class ContentRepositoryTests
{
    /// <summary>Makes every save fail the way SQL Server does (a SqlException with this number).</summary>
    private sealed class FailingSaves(int sqlNumber) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("insert failed", SqlExceptionFactory.WithNumber(sqlNumber));
    }

    [Fact]
    public async Task Current_IsTheHighestVersionOfEachKey_WithItsAuthor_AndReadsAreNotTracked()
    {
        using var db = DbContextFactory.Create(Guid.NewGuid().ToString());
        await db.Database.EnsureCreatedAsync();
        var staff = new User { ObjectId = "s", Email = "s@x", DisplayName = "Sam Staff" };
        db.Users.Add(staff);
        db.ContentItems.Add(new ContentItem { Key = ContentKeys.Outbid, Version = 2, Subject = "S", Body = "B2", CreatedByUserId = staff.Id });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var repo = new ContentRepository(db);

        var outbid = await repo.GetCurrentAsync(ContentKeys.Outbid);
        var all = await repo.GetCurrentForAllAsync();
        var history = await repo.GetHistoryAsync(ContentKeys.Outbid);

        outbid!.Version.Should().Be(2);
        outbid.CreatedBy!.DisplayName.Should().Be("Sam Staff");
        all.Should().HaveCount(ContentCatalog.Entries.Count);
        all.Single(c => c.Key == ContentKeys.Outbid).Body.Should().Be("B2");
        history.Select(h => h.Version).Should().Equal(2, 1);
        (await repo.GetVersionAsync(ContentKeys.Outbid, 1))!.Body.Should().Be(ContentCatalog.Find(ContentKeys.Outbid)!.DefaultBody);
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task TryAdd_OnAUniqueViolation_ReturnsFalse_AndStopsTracking()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(new FailingSaves(2601)).Options);
        var item = new ContentItem { Key = ContentKeys.Outbid, Version = 2, Subject = "S", Body = "B" };

        (await new ContentRepository(db).TryAddAsync(item)).Should().BeFalse();

        db.Entry(item).State.Should().Be(EntityState.Detached);
    }

    [Fact]
    public async Task TryAdd_OnAnyOtherFailure_Throws()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(new FailingSaves(547)).Options);

        var act = () => new ContentRepository(db).TryAddAsync(new ContentItem { Key = ContentKeys.Outbid, Version = 2, Body = "B" });

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
```

```csharp
// tests/Server.Tests/Content/ContentServiceTests.cs
using System.Text.Json;
using FluentAssertions;
using Moq;
using Stallions.Server.Content;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.DTOs.Content;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Content;

public class ContentServiceTests : IDisposable
{
    private const string OutbidSubject = "Outbid on {StallionName}";
    private const string OutbidBody = "{HighBid} was bid on {StallionName}.";

    private readonly AppDbContext _db = DbContextFactory.Create(Guid.NewGuid().ToString());
    private readonly Mock<IUserService> _users = new();
    private readonly TestClock _clock = new();
    private readonly ContentCache _cache;
    private readonly CapturingLogger<ContentService> _log = new();
    private readonly User _staff = new()
    {
        Id = Guid.NewGuid(), ObjectId = "staff", Email = "staff@example.com", DisplayName = "Sam Staff",
        Role = UserRole.Staff, Status = UserStatus.Active
    };

    public ContentServiceTests()
    {
        _db.Database.EnsureCreated(); // seeds version 1 of every catalog item
        _db.Users.Add(_staff);
        _db.SaveChanges();
        _cache = new ContentCache(_clock);
        SignIn(_staff);
    }

    public void Dispose() => _db.Dispose();

    private void SignIn(User user) => _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(user);

    private ContentService CreateSut(IContentRepository? repo = null) => new(repo ?? new ContentRepository(_db), _cache,
        new AuditLogRepository(_db), _users.Object, new InlineTransactionRunner(), _clock, _log);

    private static SaveContentRequest Request(string? subject, string body) => new() { Subject = subject, Body = body };

    private static Dictionary<string, string?> OutbidValues() => new()
    {
        ["StallionName"] = "Snitzel", ["Season"] = "2026 Season", ["StudName"] = "Arrowfield",
        ["HighBid"] = "$12,500.00", ["ClosesAt"] = "10 Oct 2026, 8:00 pm (Sydney time)"
    };

    private sealed class LosingRaceRepository(AppDbContext db) : ContentRepository(db)
    {
        public override Task<bool> TryAddAsync(ContentItem item) => Task.FromResult(false);
    }

    // ── Reading ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ReturnsTheSeededDefault()
    {
        var text = await CreateSut().GetAsync(ContentKeys.Outbid);

        text.Version.Should().Be(1);
        text.Subject.Should().Be("You've been outbid on {StallionName}");
        text.Body.Should().Be(ContentCatalog.Find(ContentKeys.Outbid)!.DefaultBody);
    }

    [Fact]
    public async Task Get_ReturnsTheHighestVersion()
    {
        ContentTestData.SetWording(_db, ContentKeys.Outbid, OutbidSubject, OutbidBody);

        var text = await CreateSut().GetAsync(ContentKeys.Outbid);

        text.Version.Should().Be(2);
        text.Body.Should().Be(OutbidBody);
    }

    [Fact]
    public async Task Get_AnUnknownKey_Throws()
    {
        var act = () => CreateSut().GetAsync("Email.NoSuchThing");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Get_WithNothingStored_FallsBackToTheDefault_AndLogsAWarning()
    {
        _db.ContentItems.RemoveRange(_db.ContentItems.Where(c => c.Key == ContentKeys.Outbid));
        _db.SaveChanges();

        var text = await CreateSut().GetAsync(ContentKeys.Outbid);

        text.Version.Should().Be(0);
        text.Body.Should().Be(ContentCatalog.Find(ContentKeys.Outbid)!.DefaultBody);
        _log.Warnings.Should().ContainSingle(w => w.Contains(ContentKeys.Outbid));
    }

    [Fact]
    public async Task Get_IsCachedFor60Seconds_SoAnotherInstancesSaveShowsWithinAMinute()
    {
        var sut = CreateSut();
        (await sut.GetAsync(ContentKeys.Outbid)).Version.Should().Be(1);

        ContentTestData.SetWording(_db, ContentKeys.Outbid, OutbidSubject, OutbidBody); // saved on another instance
        (await sut.GetAsync(ContentKeys.Outbid)).Version.Should().Be(1, "this instance's copy is under 60 seconds old");

        _clock.Advance(TimeSpan.FromSeconds(61));
        (await sut.GetAsync(ContentKeys.Outbid)).Body.Should().Be(OutbidBody);
    }

    [Fact]
    public async Task Save_ClearsThisInstancesCache_SoTheNextEmailUsesTheNewWording()
    {
        await CreateSut().GetAsync(ContentKeys.Outbid); // cached on this instance

        (await CreateSut().SaveAsync(ContentKeys.Outbid, Request(OutbidSubject, OutbidBody))).Succeeded.Should().BeTrue();
        var email = await CreateSut().RenderEmailAsync(ContentKeys.Outbid, OutbidValues());

        email.Subject.Should().Be("Outbid on Snitzel");
        email.Paragraphs.Should().Equal(new RenderedParagraph("$12,500.00 was bid on Snitzel.", "$12,500.00 was bid on Snitzel."));
    }

    // ── Rendering ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RenderEmail_TheDefault_SubstitutesAndSplitsIntoParagraphs()
    {
        var email = await CreateSut().RenderEmailAsync(ContentKeys.Outbid, OutbidValues());

        email.Subject.Should().Be("You've been outbid on Snitzel");
        email.Paragraphs.Select(p => p.Text).Should().Equal(
            "Another buyer has bid $12,500.00 on the Snitzel nomination, so you are no longer the highest bidder.",
            "The auction closes 10 Oct 2026, 8:00 pm (Sydney time).");
        _log.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task RenderEmail_WarnsAboutMissingAndUnexpectedValues()
    {
        var values = OutbidValues();
        values.Remove("HighBid");
        values["Typo"] = "x";

        await CreateSut().RenderEmailAsync(ContentKeys.Outbid, values);

        _log.Warnings.Should().HaveCount(2);
        _log.Warnings.Should().Contain(w => w.Contains("HighBid")).And.Contain(w => w.Contains("Typo"));
    }

    [Fact]
    public async Task RenderText_ADisclosure_IsOneEncodedParagraph()
    {
        var paragraphs = await CreateSut().RenderTextAsync(ContentKeys.BuyerFeeExplanation);

        paragraphs.Should().ContainSingle();
        paragraphs[0].Html.Should().Contain("the stud&#39;s own terms");
        paragraphs[0].Text.Should().Contain("the stud's own terms");
    }

    // ── Staff: who may ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(UserRole.Buyer)]
    [InlineData(UserRole.StudFarmAdmin)]
    public async Task EveryStaffAction_ByAnyoneElse_IsForbidden(UserRole role)
    {
        SignIn(new User { Id = Guid.NewGuid(), Role = role, Status = UserStatus.Active });
        var sut = CreateSut();

        (await sut.ListAsync()).HttpStatusCode.Should().Be(403);
        (await sut.GetDetailAsync(ContentKeys.Outbid)).HttpStatusCode.Should().Be(403);
        (await sut.GetHistoryAsync(ContentKeys.Outbid)).HttpStatusCode.Should().Be(403);
        (await sut.SaveAsync(ContentKeys.Outbid, Request(OutbidSubject, OutbidBody))).HttpStatusCode.Should().Be(403);
        (await sut.RestoreAsync(ContentKeys.Outbid, 1)).HttpStatusCode.Should().Be(403);
        (await sut.ResetAsync(ContentKeys.Outbid)).HttpStatusCode.Should().Be(403);
        (await sut.PreviewAsync(ContentKeys.Outbid, Request(OutbidSubject, OutbidBody))).HttpStatusCode.Should().Be(403);
        _db.ContentItems.Count(c => c.Key == ContentKeys.Outbid).Should().Be(1);
    }

    [Fact]
    public async Task AnUnknownKey_IsNotFound()
    {
        var sut = CreateSut();

        (await sut.GetDetailAsync("Email.Nope")).HttpStatusCode.Should().Be(404);
        (await sut.SaveAsync("Email.Nope", Request("S", "B"))).HttpStatusCode.Should().Be(404);
        (await sut.PreviewAsync("Email.Nope", Request("S", "B"))).HttpStatusCode.Should().Be(404);
    }

    // ── Staff: save ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Save_CreatesTheNextVersion_WithItsAuthor_AndAudits()
    {
        var result = await CreateSut().SaveAsync(ContentKeys.Outbid, Request("  " + OutbidSubject + " ", OutbidBody + "\r\n"));

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.Version.Should().Be(2);
        result.Value.Subject.Should().Be(OutbidSubject);
        result.Value.Body.Should().Be(OutbidBody);
        result.Value.ChangedByName.Should().Be("Sam Staff");
        result.Value.IsDefault.Should().BeFalse();
        var stored = _db.ContentItems.Single(c => c.Key == ContentKeys.Outbid && c.Version == 2);
        stored.CreatedByUserId.Should().Be(_staff.Id);
        stored.CreatedAt.Should().Be(_clock.UtcNow);

        var audit = _db.AuditLogs.Single(a => a.Action == ContentService.AuditAction);
        audit.EntityType.Should().Be(ContentService.AuditEntity);
        audit.EntityId.Should().Be(stored.Id);
        audit.UserId.Should().Be(_staff.Id);
        using var json = JsonDocument.Parse(audit.Details!);
        json.RootElement.GetProperty("Key").GetString().Should().Be(ContentKeys.Outbid);
        json.RootElement.GetProperty("Version").GetInt32().Should().Be(2);
        json.RootElement.GetProperty("Source").GetString().Should().Be("Save");
        json.RootElement.GetProperty("Old").GetProperty("Body").GetString().Should().Be(ContentCatalog.Find(ContentKeys.Outbid)!.DefaultBody);
        json.RootElement.GetProperty("New").GetProperty("Subject").GetString().Should().Be(OutbidSubject);
        json.RootElement.GetProperty("New").GetProperty("Body").GetString().Should().Be(OutbidBody);
    }

    [Fact]
    public async Task Save_WithATypo_IsRejected_AndNothingIsSaved()
    {
        var result = await CreateSut().SaveAsync(ContentKeys.Outbid, Request(OutbidSubject, "{HighBid} was bid on {StalionName}."));

        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Contain("{StalionName} isn't available in this email.");
        _db.ContentItems.Count(c => c.Key == ContentKeys.Outbid).Should().Be(1);
        _db.AuditLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_WithoutARequiredPlaceholder_IsRejected()
    {
        var result = await CreateSut().SaveAsync(ContentKeys.WonAndCharged, Request("You won", "Congratulations on {StallionName}!"));

        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Be("The message must include {Amount}.");
    }

    [Fact]
    public async Task Save_ReportsEveryError_OnePerLine()
    {
        var result = await CreateSut().SaveAsync(ContentKeys.Outbid, Request("", "{Nope}"));

        result.Error!.Split('\n').Should().BeEquivalentTo(
            "The subject can't be empty.", "{Nope} isn't available in this email.",
            "The message must include {StallionName}.", "The message must include {HighBid}.");
    }

    [Fact]
    public async Task Save_Unchanged_IsRejected()
    {
        var entry = ContentCatalog.Find(ContentKeys.Outbid)!;

        var result = await CreateSut().SaveAsync(ContentKeys.Outbid, Request(entry.DefaultSubject, entry.DefaultBody));

        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Be("Nothing has changed since the current version.");
    }

    [Fact]
    public async Task Save_LosingTheVersionRace_IsAConflict()
    {
        var result = await CreateSut(new LosingRaceRepository(_db)).SaveAsync(ContentKeys.Outbid, Request(OutbidSubject, OutbidBody));

        result.HttpStatusCode.Should().Be(409);
        _db.AuditLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_ADisclosure_StoresNoSubject()
    {
        var result = await CreateSut().SaveAsync(ContentKeys.BuyerFeeExplanation, Request("ignored", "EDITED fee wording."));

        result.Succeeded.Should().BeTrue(result.Error);
        var stored = _db.ContentItems.Single(c => c.Key == ContentKeys.BuyerFeeExplanation && c.Version == 2);
        stored.Subject.Should().BeNull();
        stored.Body.Should().Be("EDITED fee wording.");
    }

    // ── Staff: restore and reset ────────────────────────────────────────────

    [Fact]
    public async Task Restore_PublishesTheOldTextAsANewVersion_AndAudits()
    {
        var sut = CreateSut();
        await sut.SaveAsync(ContentKeys.Outbid, Request(OutbidSubject, OutbidBody));
        _clock.Advance(TimeSpan.FromMinutes(5));

        var result = await sut.RestoreAsync(ContentKeys.Outbid, 1);

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.Version.Should().Be(3);
        result.Value.Body.Should().Be(ContentCatalog.Find(ContentKeys.Outbid)!.DefaultBody);
        var audit = _db.AuditLogs.Where(a => a.Action == ContentService.AuditAction).OrderBy(a => a.Id).Last();
        using var json = JsonDocument.Parse(audit.Details!);
        json.RootElement.GetProperty("Source").GetString().Should().Be("Restore");
        json.RootElement.GetProperty("RestoredFromVersion").GetInt32().Should().Be(1);
        (await CreateSut().GetAsync(ContentKeys.Outbid)).Version.Should().Be(3);
    }

    [Fact]
    public async Task Restore_AVersionThatDoesNotExist_IsNotFound() =>
        (await CreateSut().RestoreAsync(ContentKeys.Outbid, 9)).HttpStatusCode.Should().Be(404);

    [Fact]
    public async Task Restore_TheCurrentVersion_IsRejected() =>
        (await CreateSut().RestoreAsync(ContentKeys.Outbid, 1)).Error.Should().Be("That is already the current wording.");

    [Fact]
    public async Task Reset_PublishesTheDefaultAsANewVersion()
    {
        var sut = CreateSut();
        await sut.SaveAsync(ContentKeys.Outbid, Request(OutbidSubject, OutbidBody));

        var result = await sut.ResetAsync(ContentKeys.Outbid);

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.Version.Should().Be(3);
        result.Value.IsDefault.Should().BeTrue();
        result.Value.Subject.Should().Be("You've been outbid on {StallionName}");
    }

    [Fact]
    public async Task Reset_WhenAlreadyTheDefault_IsRejected() =>
        (await CreateSut().ResetAsync(ContentKeys.Outbid)).Error.Should().Be("That is already the current wording.");

    // ── Staff: list, detail, history, preview ───────────────────────────────

    [Fact]
    public async Task List_ShowsEveryCatalogItem_InCatalogOrder_WithCurrentVersionAndAuthor()
    {
        await CreateSut().SaveAsync(ContentKeys.Outbid, Request(OutbidSubject, OutbidBody));

        var list = (await CreateSut().ListAsync()).Value!;

        list.Select(i => i.Key).Should().Equal(ContentCatalog.Entries.Select(e => e.Key));
        var outbid = list.Single(i => i.Key == ContentKeys.Outbid);
        outbid.CurrentVersion.Should().Be(2);
        outbid.ChangedByName.Should().Be("Sam Staff");
        outbid.Group.Should().Be(ContentGroups.BuyerEmails);
        var fee = list.Single(i => i.Key == ContentKeys.BuyerFeeExplanation);
        fee.CurrentVersion.Should().Be(1);
        fee.ChangedByName.Should().BeNull();
        fee.HasSubject.Should().BeFalse();
    }

    [Fact]
    public async Task Detail_ListsThePlaceholders_WithRequiredFlagsAndSamples()
    {
        var detail = (await CreateSut().GetDetailAsync(ContentKeys.Outbid)).Value!;

        detail.Version.Should().Be(1);
        detail.IsDefault.Should().BeTrue();
        detail.MaxSubjectLength.Should().Be(200);
        detail.MaxBodyLength.Should().Be(4000);
        detail.Placeholders.Select(p => p.Name).Should().Equal("StallionName", "Season", "StudName", "HighBid", "ClosesAt");
        detail.Placeholders.Where(p => p.Required).Select(p => p.Name).Should().Equal("StallionName", "HighBid");
        detail.Placeholders.Single(p => p.Name == "HighBid").Sample.Should().Be("$12,500.00");
    }

    [Fact]
    public async Task History_IsNewestFirst_AndMarksTheCurrentVersion()
    {
        await CreateSut().SaveAsync(ContentKeys.Outbid, Request(OutbidSubject, OutbidBody));

        var history = (await CreateSut().GetHistoryAsync(ContentKeys.Outbid)).Value!;

        history.Select(h => h.Version).Should().Equal(2, 1);
        history[0].IsCurrent.Should().BeTrue();
        history[0].CreatedByName.Should().Be("Sam Staff");
        history[1].IsCurrent.Should().BeFalse();
        history[1].CreatedByName.Should().BeNull();
    }

    [Fact]
    public async Task Preview_RendersTheSampleValues_WithoutSaving()
    {
        var preview = (await CreateSut().PreviewAsync(ContentKeys.Outbid, Request(OutbidSubject, "Hi.\n\n" + OutbidBody))).Value!;

        preview.Errors.Should().BeEmpty();
        preview.Subject.Should().Be("Outbid on Snitzel");
        preview.Paragraphs.Should().Equal("Hi.", "$12,500.00 was bid on Snitzel.");
        _db.ContentItems.Count(c => c.Key == ContentKeys.Outbid).Should().Be(1);
    }

    [Fact]
    public async Task Preview_ReturnsTheValidationErrors()
    {
        var preview = (await CreateSut().PreviewAsync(ContentKeys.Outbid, Request(OutbidSubject, "{HighBid} on {Foo}"))).Value!;

        preview.Errors.Should().BeEquivalentTo("{Foo} isn't available in this email.", "The message must include {StallionName}.");
        preview.Paragraphs.Should().BeEmpty();
    }
}
```

```csharp
// tests/Server.Tests/Controllers/ContentControllerTests.cs
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Stallions.Server.Content;
using Stallions.Server.Controllers;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Content;

namespace Stallions.Server.Tests.Controllers;

public class ContentControllerTests
{
    [Fact]
    public void TheWordingApi_IsStaffOnly() =>
        typeof(ContentController).GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be("StaffOnly");

    [Fact]
    public async Task Save_WhenTheServiceRejects_ReturnsItsStatusAndMessage()
    {
        var content = new Mock<IContentService>();
        content.Setup(c => c.SaveAsync("Email.Outbid", It.IsAny<SaveContentRequest>()))
            .ReturnsAsync(ServiceResult<ContentItemDetailDto>.BadRequest("The message must include {HighBid}."));

        var result = await new ContentController(content.Object).Save("Email.Outbid", new SaveContentRequest { Body = "x" }) as ObjectResult;

        result!.StatusCode.Should().Be(400);
        result.Value.Should().Be("The message must include {HighBid}.");
    }
}
```

- [ ] **Step 3: Run red** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~Content"` → compile errors.

- [ ] **Step 4: DTOs**

```csharp
// src/Shared/DTOs/Content/ContentItemSummaryDto.cs
namespace Stallions.Shared.DTOs.Content;

/// <summary>One wording item in the Staff list. CurrentVersion 0 = nothing stored (catalog default).</summary>
public class ContentItemSummaryDto
{
    public string Key { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool HasSubject { get; set; }
    public int CurrentVersion { get; set; }
    public DateTime? ChangedAt { get; set; }
    /// <summary>Null for the seeded default.</summary>
    public string? ChangedByName { get; set; }
}
```

```csharp
// src/Shared/DTOs/Content/ContentPlaceholderDto.cs
namespace Stallions.Shared.DTOs.Content;

public class ContentPlaceholderDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>The example value the preview uses.</summary>
    public string Sample { get; set; } = string.Empty;
    public bool Required { get; set; }
}
```

```csharp
// src/Shared/DTOs/Content/ContentItemDetailDto.cs
namespace Stallions.Shared.DTOs.Content;

/// <summary>The editor's view of one wording item: its current text and what may go in it.</summary>
public class ContentItemDetailDto
{
    public string Key { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool HasSubject { get; set; }
    /// <summary>Disclosures: one paragraph, no line breaks.</summary>
    public bool SingleParagraph { get; set; }
    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTime? ChangedAt { get; set; }
    public string? ChangedByName { get; set; }
    /// <summary>The current text is the catalog default (Reset would change nothing).</summary>
    public bool IsDefault { get; set; }
    public List<ContentPlaceholderDto> Placeholders { get; set; } = [];
    public int MaxSubjectLength { get; set; }
    public int MaxBodyLength { get; set; }
}
```

```csharp
// src/Shared/DTOs/Content/ContentVersionDto.cs
namespace Stallions.Shared.DTOs.Content;

public class ContentVersionDto
{
    public int Version { get; set; }
    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    /// <summary>Null for the seeded default.</summary>
    public string? CreatedByName { get; set; }
    public bool IsCurrent { get; set; }
}
```

```csharp
// src/Shared/DTOs/Content/SaveContentRequest.cs
namespace Stallions.Shared.DTOs.Content;

/// <summary>Plain text. Subject is ignored for disclosures. Limits: subject 200, body 4,000 characters.</summary>
public class SaveContentRequest
{
    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;
}
```

```csharp
// src/Shared/DTOs/Content/ContentPreviewDto.cs
namespace Stallions.Shared.DTOs.Content;

/// <summary>Either the validation errors, or the text rendered with the example values (plain text; "\n" = line break).</summary>
public class ContentPreviewDto
{
    public List<string> Errors { get; set; } = [];
    public string? Subject { get; set; }
    public List<string> Paragraphs { get; set; } = [];
}
```

- [ ] **Step 5: Repository**

```csharp
// src/Server/Data/Repositories/IContentRepository.cs
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

/// <summary>Wording versions. Reads are not tracked (emails render inside other units of work).</summary>
public interface IContentRepository
{
    /// <summary>The highest version of a key, with its author, or null if none is stored.</summary>
    Task<ContentItem?> GetCurrentAsync(string key);
    /// <summary>The highest version of every key that has one.</summary>
    Task<IReadOnlyList<ContentItem>> GetCurrentForAllAsync();
    /// <summary>Every version of a key, newest first.</summary>
    Task<IReadOnlyList<ContentItem>> GetHistoryAsync(string key);
    Task<ContentItem?> GetVersionAsync(string key, int version);
    /// <summary>Saves a new version; false when that (Key, Version) already exists because another save won.</summary>
    Task<bool> TryAddAsync(ContentItem item);
}
```

```csharp
// src/Server/Data/Repositories/ContentRepository.cs
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class ContentRepository : IContentRepository
{
    private readonly AppDbContext _db;
    public ContentRepository(AppDbContext db) => _db = db;

    private IQueryable<ContentItem> Read() => _db.ContentItems.AsNoTracking().Include(c => c.CreatedBy);

    public Task<ContentItem?> GetCurrentAsync(string key) =>
        Read().Where(c => c.Key == key).OrderByDescending(c => c.Version).FirstOrDefaultAsync();

    public async Task<IReadOnlyList<ContentItem>> GetCurrentForAllAsync() =>
        await Read()
            .Where(c => c.Version == _db.ContentItems.Where(x => x.Key == c.Key).Max(x => x.Version))
            .ToListAsync();

    public async Task<IReadOnlyList<ContentItem>> GetHistoryAsync(string key) =>
        await Read().Where(c => c.Key == key).OrderByDescending(c => c.Version).ToListAsync();

    public Task<ContentItem?> GetVersionAsync(string key, int version) =>
        Read().FirstOrDefaultAsync(c => c.Key == key && c.Version == version);

    public virtual async Task<bool> TryAddAsync(ContentItem item)
    {
        _db.ContentItems.Add(item);
        try
        {
            await _db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // Inside the caller's transaction EF rolled back to its savepoint; forget the rejected row.
            _db.Entry(item).State = EntityState.Detached;
            return false;
        }
    }
}
```

- [ ] **Step 6: Cache, service and controller**

```csharp
// src/Server/Content/ContentCache.cs
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Stallions.Server.Content;

/// <summary>
/// The current wording, per app instance, for 60 seconds. A save clears this instance's copy at once; other
/// instances pick the change up when their copy expires. Registered as a singleton.
/// </summary>
public sealed class ContentCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, (ContentText Text, DateTimeOffset ExpiresAt)> _items = new(StringComparer.Ordinal);

    public ContentCache(TimeProvider clock) => _clock = clock;

    public bool TryGet(string key, [NotNullWhen(true)] out ContentText? text)
    {
        if (_items.TryGetValue(key, out var item) && item.ExpiresAt > _clock.GetUtcNow())
        {
            text = item.Text;
            return true;
        }
        text = null;
        return false;
    }

    public void Set(string key, ContentText text) => _items[key] = (text, _clock.GetUtcNow() + Lifetime);

    public void Clear() => _items.Clear();
}
```

```csharp
// src/Server/Content/ContentService.cs
using System.Text.Json;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Content;
using Stallions.Shared.Enums;

namespace Stallions.Server.Content;

/// <summary>The current text of a wording item. Version 0 = nothing stored, so the catalog default is used.</summary>
public sealed record ContentText(string Key, int Version, string? Subject, string Body);

/// <summary>An email's rendered subject and message paragraphs; the email code adds its generated blocks.</summary>
public sealed record RenderedEmail(string Subject, IReadOnlyList<RenderedParagraph> Paragraphs);

public interface IContentService
{
    /// <summary>The current text for a catalog key (cached 60 s). Throws ArgumentException for an unknown key.</summary>
    Task<ContentText> GetAsync(string key);
    /// <summary>A disclosure's paragraphs, encoded for HTML.</summary>
    Task<IReadOnlyList<RenderedParagraph>> RenderTextAsync(string key);
    /// <summary>An email's subject and message with the values substituted (and encoded in the HTML form).</summary>
    Task<RenderedEmail> RenderEmailAsync(string key, IReadOnlyDictionary<string, string?> values);

    // Staff — StaffOnly at the controller and re-checked here.
    Task<ServiceResult<IReadOnlyList<ContentItemSummaryDto>>> ListAsync();
    Task<ServiceResult<ContentItemDetailDto>> GetDetailAsync(string key);
    Task<ServiceResult<IReadOnlyList<ContentVersionDto>>> GetHistoryAsync(string key);
    Task<ServiceResult<ContentItemDetailDto>> SaveAsync(string key, SaveContentRequest request);
    Task<ServiceResult<ContentItemDetailDto>> RestoreAsync(string key, int version);
    Task<ServiceResult<ContentItemDetailDto>> ResetAsync(string key);
    Task<ServiceResult<ContentPreviewDto>> PreviewAsync(string key, SaveContentRequest request);
}

public class ContentService : IContentService
{
    public const string AuditEntity = "ContentItem";
    public const string AuditAction = "UpdateWording";
    private const string StaffOnlyMessage = "Only Stallions Australia staff can manage wording.";
    private const string UnknownKeyMessage = "That wording item doesn't exist.";

    private static readonly IReadOnlyDictionary<string, string?> NoValues = new Dictionary<string, string?>();

    private readonly IContentRepository _repo;
    private readonly ContentCache _cache;
    private readonly IAuditLogRepository _audit;
    private readonly IUserService _users;
    private readonly ITransactionRunner _transactions;
    private readonly TimeProvider _clock;
    private readonly ILogger<ContentService> _log;

    public ContentService(IContentRepository repo, ContentCache cache, IAuditLogRepository audit, IUserService users,
        ITransactionRunner transactions, TimeProvider clock, ILogger<ContentService> log)
    {
        _repo = repo;
        _cache = cache;
        _audit = audit;
        _users = users;
        _transactions = transactions;
        _clock = clock;
        _log = log;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private static ContentCatalogEntry Entry(string key) =>
        ContentCatalog.Find(key) ?? throw new ArgumentException($"Unknown wording key '{key}'.", nameof(key));

    // ── Reading and rendering ───────────────────────────────────────────────

    public async Task<ContentText> GetAsync(string key)
    {
        var entry = Entry(key);
        if (_cache.TryGet(key, out var cached)) return cached;

        var row = await _repo.GetCurrentAsync(key);
        ContentText text;
        if (row == null)
        {
            // The migration seeds every key, so this means a missing row: never send a blank email or disclosure.
            _log.LogWarning("No wording is stored for {Key}; using the catalog default", key);
            text = new ContentText(key, 0, entry.DefaultSubject, entry.DefaultBody);
        }
        else
        {
            text = new ContentText(key, row.Version, row.Subject, row.Body);
        }
        _cache.Set(key, text);
        return text;
    }

    public async Task<IReadOnlyList<RenderedParagraph>> RenderTextAsync(string key)
    {
        var text = await GetAsync(key);
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var paragraphs = ContentTemplate.RenderBody(text.Body, NoValues, missing);
        WarnMissing(key, text.Version, missing);
        return paragraphs;
    }

    public async Task<RenderedEmail> RenderEmailAsync(string key, IReadOnlyDictionary<string, string?> values)
    {
        var entry = Entry(key);
        var text = await GetAsync(key);
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var subject = ContentTemplate.RenderLine(text.Subject ?? string.Empty, values, missing);
        var paragraphs = ContentTemplate.RenderBody(text.Body, values, missing);
        WarnMissing(key, text.Version, missing);

        var unexpected = values.Keys.Where(k => !entry.Allows(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (unexpected.Count > 0)
            _log.LogWarning("Wording {Key} was given values that aren't in its catalog entry: {Names}",
                key, string.Join(", ", unexpected));

        return new RenderedEmail(subject, paragraphs);
    }

    private void WarnMissing(string key, int version, IReadOnlyCollection<string> missing)
    {
        if (missing.Count > 0)
            _log.LogWarning("Wording {Key} version {Version} uses placeholders with no value: {Names}",
                key, version, string.Join(", ", missing));
    }

    // ── Staff ───────────────────────────────────────────────────────────────

    private async Task<User?> StaffAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        return caller is { Role: UserRole.Staff } ? caller : null;
    }

    public async Task<ServiceResult<IReadOnlyList<ContentItemSummaryDto>>> ListAsync()
    {
        if (await StaffAsync() == null)
            return ServiceResult<IReadOnlyList<ContentItemSummaryDto>>.Forbidden(StaffOnlyMessage);

        var current = (await _repo.GetCurrentForAllAsync()).ToDictionary(c => c.Key, StringComparer.Ordinal);
        var list = ContentCatalog.Entries.Select(e =>
        {
            current.TryGetValue(e.Key, out var row);
            return new ContentItemSummaryDto
            {
                Key = e.Key,
                Group = e.Group,
                Label = e.Label,
                Description = e.Description,
                HasSubject = e.HasSubject,
                CurrentVersion = row?.Version ?? 0,
                ChangedAt = row?.CreatedAt,
                ChangedByName = row?.CreatedBy?.DisplayName
            };
        }).ToList();
        return ServiceResult<IReadOnlyList<ContentItemSummaryDto>>.Ok(list);
    }

    public async Task<ServiceResult<ContentItemDetailDto>> GetDetailAsync(string key)
    {
        if (await StaffAsync() == null) return ServiceResult<ContentItemDetailDto>.Forbidden(StaffOnlyMessage);
        if (ContentCatalog.Find(key) is not { } entry) return ServiceResult<ContentItemDetailDto>.NotFound(UnknownKeyMessage);
        return ServiceResult<ContentItemDetailDto>.Ok(await DetailAsync(entry));
    }

    public async Task<ServiceResult<IReadOnlyList<ContentVersionDto>>> GetHistoryAsync(string key)
    {
        if (await StaffAsync() == null) return ServiceResult<IReadOnlyList<ContentVersionDto>>.Forbidden(StaffOnlyMessage);
        if (ContentCatalog.Find(key) is not { } entry)
            return ServiceResult<IReadOnlyList<ContentVersionDto>>.NotFound(UnknownKeyMessage);

        var rows = await _repo.GetHistoryAsync(entry.Key);
        var list = rows.Select((r, i) => new ContentVersionDto
        {
            Version = r.Version,
            Subject = r.Subject,
            Body = r.Body,
            CreatedAt = r.CreatedAt,
            CreatedByName = r.CreatedBy?.DisplayName,
            IsCurrent = i == 0
        }).ToList();
        return ServiceResult<IReadOnlyList<ContentVersionDto>>.Ok(list);
    }

    public async Task<ServiceResult<ContentItemDetailDto>> SaveAsync(string key, SaveContentRequest request)
    {
        var staff = await StaffAsync();
        if (staff == null) return ServiceResult<ContentItemDetailDto>.Forbidden(StaffOnlyMessage);
        if (ContentCatalog.Find(key) is not { } entry) return ServiceResult<ContentItemDetailDto>.NotFound(UnknownKeyMessage);

        var errors = ContentTemplate.Validate(entry, request.Subject, request.Body);
        if (errors.Count > 0) return ServiceResult<ContentItemDetailDto>.BadRequest(string.Join("\n", errors));

        var subject = entry.HasSubject ? request.Subject!.Trim() : null;
        var body = ContentTemplate.Normalise(request.Body).Trim();
        return await AddVersionAsync(entry, staff, subject, body, "Save", restoredFrom: null);
    }

    public async Task<ServiceResult<ContentItemDetailDto>> RestoreAsync(string key, int version)
    {
        var staff = await StaffAsync();
        if (staff == null) return ServiceResult<ContentItemDetailDto>.Forbidden(StaffOnlyMessage);
        if (ContentCatalog.Find(key) is not { } entry) return ServiceResult<ContentItemDetailDto>.NotFound(UnknownKeyMessage);

        var old = await _repo.GetVersionAsync(entry.Key, version);
        if (old == null) return ServiceResult<ContentItemDetailDto>.NotFound($"Version {version} doesn't exist.");

        // The catalog may have changed since that version was written (e.g. a placeholder became required).
        var errors = ContentTemplate.Validate(entry, old.Subject, old.Body);
        if (errors.Count > 0)
            return ServiceResult<ContentItemDetailDto>.BadRequest(
                $"Version {version} can't be restored as it is:\n" + string.Join("\n", errors));

        return await AddVersionAsync(entry, staff, entry.HasSubject ? old.Subject : null, old.Body, "Restore", version);
    }

    public async Task<ServiceResult<ContentItemDetailDto>> ResetAsync(string key)
    {
        var staff = await StaffAsync();
        if (staff == null) return ServiceResult<ContentItemDetailDto>.Forbidden(StaffOnlyMessage);
        if (ContentCatalog.Find(key) is not { } entry) return ServiceResult<ContentItemDetailDto>.NotFound(UnknownKeyMessage);

        return await AddVersionAsync(entry, staff, entry.DefaultSubject, entry.DefaultBody, "Reset", restoredFrom: null);
    }

    public async Task<ServiceResult<ContentPreviewDto>> PreviewAsync(string key, SaveContentRequest request)
    {
        if (await StaffAsync() == null) return ServiceResult<ContentPreviewDto>.Forbidden(StaffOnlyMessage);
        if (ContentCatalog.Find(key) is not { } entry) return ServiceResult<ContentPreviewDto>.NotFound(UnknownKeyMessage);

        var errors = ContentTemplate.Validate(entry, request.Subject, request.Body);
        if (errors.Count > 0) return ServiceResult<ContentPreviewDto>.Ok(new ContentPreviewDto { Errors = errors.ToList() });

        var samples = entry.Placeholders.ToDictionary(p => p.Name, p => (string?)p.Sample, StringComparer.Ordinal);
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        return ServiceResult<ContentPreviewDto>.Ok(new ContentPreviewDto
        {
            Subject = entry.HasSubject ? ContentTemplate.RenderLine(request.Subject!.Trim(), samples, missing) : null,
            Paragraphs = ContentTemplate.RenderBody(request.Body, samples, missing).Select(p => p.Text).ToList()
        });
    }

    /// <summary>Adds the next version and its audit row together; clears this instance's cache.</summary>
    private async Task<ServiceResult<ContentItemDetailDto>> AddVersionAsync(ContentCatalogEntry entry, User staff,
        string? subject, string body, string source, int? restoredFrom)
    {
        var unchanged = false;
        var conflict = false;
        ContentItem? added = null;
        await _transactions.RunAsync(async () =>
        {
            var current = await _repo.GetCurrentAsync(entry.Key);
            if (current != null && current.Subject == subject && current.Body == body)
            {
                unchanged = true;
                return false;
            }

            var item = new ContentItem
            {
                Key = entry.Key,
                Version = (current?.Version ?? 0) + 1,
                Subject = subject,
                Body = body,
                CreatedAt = Now,
                CreatedByUserId = staff.Id
            };
            if (!await _repo.TryAddAsync(item))
            {
                conflict = true;
                return false;
            }

            await _audit.LogAsync(AuditEntity, item.Id, AuditAction, staff.Id, JsonSerializer.Serialize(new
            {
                entry.Key,
                item.Version,
                Source = source,
                RestoredFromVersion = restoredFrom,
                Old = current == null ? null : new { current.Version, current.Subject, current.Body },
                New = new { item.Subject, item.Body }
            }));
            added = item;
            return true;
        });

        if (unchanged)
            return ServiceResult<ContentItemDetailDto>.BadRequest(source == "Save"
                ? "Nothing has changed since the current version."
                : "That is already the current wording.");
        if (conflict || added == null)
            return ServiceResult<ContentItemDetailDto>.Conflict(
                "Someone else saved this wording at the same moment. Reload it and try again.");

        _cache.Clear();
        _log.LogInformation("Staff {UserId} saved {Key} version {Version} ({Source})", staff.Id, entry.Key, added.Version, source);
        return ServiceResult<ContentItemDetailDto>.Ok(await DetailAsync(entry));
    }

    private async Task<ContentItemDetailDto> DetailAsync(ContentCatalogEntry entry)
    {
        var row = await _repo.GetCurrentAsync(entry.Key);
        var subject = row == null ? entry.DefaultSubject : row.Subject;
        var body = row?.Body ?? entry.DefaultBody;
        return new ContentItemDetailDto
        {
            Key = entry.Key,
            Group = entry.Group,
            Label = entry.Label,
            Description = entry.Description,
            HasSubject = entry.HasSubject,
            SingleParagraph = entry.SingleParagraph,
            Subject = subject,
            Body = body,
            Version = row?.Version ?? 0,
            ChangedAt = row?.CreatedAt,
            ChangedByName = row?.CreatedBy?.DisplayName,
            IsDefault = subject == entry.DefaultSubject && body == entry.DefaultBody,
            Placeholders = entry.Placeholders.Select(p => new ContentPlaceholderDto
            {
                Name = p.Name, Description = p.Description, Sample = p.Sample, Required = entry.Required.Contains(p.Name)
            }).ToList(),
            MaxSubjectLength = ContentCatalog.MaxSubjectLength,
            MaxBodyLength = ContentCatalog.MaxBodyLength
        };
    }
}
```

```csharp
// src/Server/Controllers/ContentController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Content;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Content;

namespace Stallions.Server.Controllers;

/// <summary>Staff → Wording: the disclosures and every system email's subject and message.</summary>
[ApiController]
[Route("api/content")]
[Authorize(Policy = "StaffOnly")]
public class ContentController : ControllerBase
{
    private readonly IContentService _content;
    public ContentController(IContentService content) => _content = content;

    [HttpGet]
    public async Task<IActionResult> List() => Result(await _content.ListAsync());

    [HttpGet("{key}")]
    public async Task<IActionResult> Get(string key) => Result(await _content.GetDetailAsync(key));

    [HttpGet("{key}/versions")]
    public async Task<IActionResult> Versions(string key) => Result(await _content.GetHistoryAsync(key));

    [HttpPut("{key}")]
    public async Task<IActionResult> Save(string key, [FromBody] SaveContentRequest request) =>
        Result(await _content.SaveAsync(key, request));

    [HttpPost("{key}/versions/{version:int}/restore")]
    public async Task<IActionResult> Restore(string key, int version) => Result(await _content.RestoreAsync(key, version));

    [HttpPost("{key}/reset")]
    public async Task<IActionResult> Reset(string key) => Result(await _content.ResetAsync(key));

    [HttpPost("{key}/preview")]
    public async Task<IActionResult> Preview(string key, [FromBody] SaveContentRequest request) =>
        Result(await _content.PreviewAsync(key, request));

    private IActionResult Result<T>(ServiceResult<T> r) => r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
}
```

`src/Server/Program.cs` — add `using Stallions.Server.Content;` after line 8 (`using Stallions.Server.Auth;`), and after line 150
(`builder.Services.AddScoped<ITaxDocumentQueryService, TaxDocumentQueryService>();`) add:

```csharp
// src/Server/Program.cs (after the tax document registrations)
builder.Services.AddScoped<IContentRepository, ContentRepository>();
builder.Services.AddSingleton<ContentCache>();
builder.Services.AddScoped<IContentService, ContentService>();
```

- [ ] **Step 7: Run green** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~Content"`; full build (0 warnings) and full test run.

- [ ] **Step 8: Commit**

```bash
git add src/Shared/DTOs/Content src/Server/Data/Repositories/IContentRepository.cs src/Server/Data/Repositories/ContentRepository.cs \
  src/Server/Content/ContentCache.cs src/Server/Content/ContentService.cs src/Server/Controllers/ContentController.cs \
  src/Server/Program.cs tests/Server.Tests/Helpers/CapturingLogger.cs tests/Server.Tests/Helpers/ContentTestData.cs \
  tests/Server.Tests/Content/ContentServiceTests.cs tests/Server.Tests/Data/Repositories/ContentRepositoryTests.cs \
  tests/Server.Tests/Controllers/ContentControllerTests.cs
git status --short
git commit -m "feat: ContentService — cached wording, rendering, Staff versions, restore, reset, preview and audit

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Disclosures and the tax document fee note read the stored wording

**Files:**
- Modify: `src/Server/Controllers/DisclosuresController.cs` (whole file)
- Modify: `src/Server/TaxDocuments/TaxDocumentQueryService.cs` (usings lines 2 and 7; field line 41; constructor lines 44–51;
  `GetAsync` line 64; `ToDto` lines 271 and 303)
- Test: `tests/Server.Tests/Controllers/DisclosuresControllerTests.cs` (rewritten — `GetBuyerFee` is now async),
  `tests/Server.Tests/TaxDocuments/TaxDocumentQueryServiceTests.cs` (fixture only: lines 8, 21–39, 52–57)

**Interfaces:**
- Consumes: Task 4 `IContentService.GetAsync`, `ContentTestData`; Task 2 `ContentKeys`.
- Produces: `DisclosuresController(IContentService)`, `Task<IActionResult> GetBuyerFee()` (still `[AllowAnonymous]`, same
  `BuyerFeeDisclosureDto`); `TaxDocumentQueryService(…, IContentService content, ILogger<…>)` (replaces the
  `IOptions<DisclosureOptions>` parameter in the same position); `TaxDocumentDto.FeeNote` = the current
  `Disclosure.BuyerFeeExplanation` body for buyer fee invoices.

- [ ] **Step 1: Write the failing tests** — replace `DisclosuresControllerTests.cs`:

```csharp
// tests/Server.Tests/Controllers/DisclosuresControllerTests.cs
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Content;
using Stallions.Server.Controllers;
using Stallions.Server.Data;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.DTOs.Checkout;

namespace Stallions.Server.Tests.Controllers;

public class DisclosuresControllerTests : IDisposable
{
    private readonly AppDbContext _db = DbContextFactory.Create(Guid.NewGuid().ToString());

    public DisclosuresControllerTests() => _db.Database.EnsureCreated();

    public void Dispose() => _db.Dispose();

    private async Task<BuyerFeeDisclosureDto?> GetAsync() =>
        (await new DisclosuresController(ContentTestData.Service(_db)).GetBuyerFee() as OkObjectResult)?.Value as BuyerFeeDisclosureDto;

    [Fact]
    public async Task GetBuyerFee_ReturnsTheStoredWording()
    {
        ContentTestData.SetWording(_db, ContentKeys.BuyerFeeExplanation, null, "CONFIGURED fee");
        ContentTestData.SetWording(_db, ContentKeys.StudFarmBalanceArrangement, null, "CONFIGURED balance");
        ContentTestData.SetWording(_db, ContentKeys.SavedCardExplanation, null, "CONFIGURED card");

        var dto = await GetAsync();

        dto.Should().NotBeNull();
        dto!.BuyerFeeExplanation.Should().Be("CONFIGURED fee");
        dto.BalanceArrangement.Should().Be("CONFIGURED balance");
        dto.SavedCardExplanation.Should().Be("CONFIGURED card");
    }

    [Fact]
    public async Task GetBuyerFee_BeforeAnyEdit_ReturnsTheSeededDefaults()
    {
        var dto = await GetAsync();

        dto!.BuyerFeeExplanation.Should().StartWith("The buyer fee is paid to Stallions Australia and forms part of the price");
        dto.BalanceArrangement.Should().StartWith("The stud farm will contact you separately");
        dto.SavedCardExplanation.Should().StartWith("Your saved card is charged the buyer fee automatically");
    }

    [Fact]
    public void GetBuyerFee_IsPublic() =>
        typeof(DisclosuresController).GetMethod(nameof(DisclosuresController.GetBuyerFee))!
            .GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
}
```

`TaxDocumentQueryServiceTests` fixture: replace `using Stallions.Server.Options;` (line 8) with `using Stallions.Server.Content;`;
at the end of the constructor (after `_db.SaveChanges();`, line 38) add:

```csharp
// tests/Server.Tests/TaxDocuments/TaxDocumentQueryServiceTests.cs (end of the constructor)
        ContentTestData.SetWording(_db, ContentKeys.BuyerFeeExplanation, null, "CONFIGURED fee wording.");
```

and in `CreateSut` (lines 52–57) replace the `Microsoft.Extensions.Options.Options.Create(new DisclosureOptions { … }),` argument with
`ContentTestData.Service(_db),`. No test body changes — `Get_TheBuyersOwnInvoice_IsShown_WithTheConfiguredFeeNote` now proves the
stored wording is used.

- [ ] **Step 2: Run red** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~DisclosuresControllerTests|FullyQualifiedName~TaxDocumentQueryServiceTests"`
  → compile errors (constructors still take `IOptions<DisclosureOptions>`; `GetBuyerFee` isn't awaitable).

- [ ] **Step 3: Implement**

```csharp
// src/Server/Controllers/DisclosuresController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Content;
using Stallions.Shared.DTOs.Checkout;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/disclosures")]
public class DisclosuresController : ControllerBase
{
    private readonly IContentService _content;
    public DisclosuresController(IContentService content) => _content = content;

    // Public — shown on listing pages before a buyer signs in. Wording is Staff-managed (Staff → Wording).
    [HttpGet("buyer-fee")]
    [AllowAnonymous]
    public async Task<IActionResult> GetBuyerFee() => Ok(new BuyerFeeDisclosureDto
    {
        BuyerFeeExplanation = (await _content.GetAsync(ContentKeys.BuyerFeeExplanation)).Body,
        BalanceArrangement = (await _content.GetAsync(ContentKeys.StudFarmBalanceArrangement)).Body,
        SavedCardExplanation = (await _content.GetAsync(ContentKeys.SavedCardExplanation)).Body
    });
}
```

`src/Server/TaxDocuments/TaxDocumentQueryService.cs`:
- delete line 2 (`using Microsoft.Extensions.Options;`); replace line 7 (`using Stallions.Server.Options;`) with `using Stallions.Server.Content;`
- line 41: `private readonly DisclosureOptions _disclosure;` → `private readonly IContentService _content;`
- constructor (lines 44–51) becomes:

```csharp
// src/Server/TaxDocuments/TaxDocumentQueryService.cs (constructor)
    public TaxDocumentQueryService(ITaxDocumentRepository documents, ITaxDocumentService taxDocuments,
        ISubscriptionRepository subscriptions, IPurchaseRepository purchases, IStudFarmRepository farms,
        IAuditLogRepository audit, IUserService users, ITransactionRunner transactions,
        IContentService content, ILogger<TaxDocumentQueryService> log)
    {
        _documents = documents; _taxDocuments = taxDocuments; _subscriptions = subscriptions; _purchases = purchases;
        _farms = farms; _audit = audit; _users = users; _transactions = transactions; _content = content; _log = log;
    }
```

- in `GetAsync`, replace line 64 (`return ServiceResult<TaxDocumentDto>.Ok(ToDto(document, reversedBy));`) with:

```csharp
// src/Server/TaxDocuments/TaxDocumentQueryService.cs (GetAsync)
        // The fee note is the current buyer fee explanation, read when shown (not stored on the document).
        var feeNote = document.Kind == TaxDocumentKind.BuyerFeeInvoice
            ? (await _content.GetAsync(ContentKeys.BuyerFeeExplanation)).Body
            : null;
        return ServiceResult<TaxDocumentDto>.Ok(ToDto(document, reversedBy, feeNote));
```

- line 271: `private TaxDocumentDto ToDto(TaxDocument d, TaxDocument? reversedBy) => new()` →
  `private static TaxDocumentDto ToDto(TaxDocument d, TaxDocument? reversedBy, string? feeNote) => new()`
- line 303: `FeeNote = d.Kind == TaxDocumentKind.BuyerFeeInvoice ? _disclosure.BuyerFeeExplanation : null,` → `FeeNote = feeNote,`

(`DisclosureOptions` itself stays registered until Task 7 — `AuctionEmails` and `TaxDocumentEmails` still read it.)

- [ ] **Step 4: Run green** — the two test classes; full build (0 warnings) and full test run.

- [ ] **Step 5: Commit**

```bash
git add src/Server/Controllers/DisclosuresController.cs src/Server/TaxDocuments/TaxDocumentQueryService.cs \
  tests/Server.Tests/Controllers/DisclosuresControllerTests.cs tests/Server.Tests/TaxDocuments/TaxDocumentQueryServiceTests.cs
git status --short
git commit -m "feat: buyer disclosures and the tax invoice fee note come from Staff-managed wording

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 6: Auction and Make an Offer emails use the Staff wording

**Files:**
- Modify: `src/Server/Email/AuctionEmails.cs` (whole file)
- Create (test data): `tests/Server.Tests/Email/DefaultEmailGolden.cs`
- Test: `tests/Server.Tests/Email/AuctionEmailsWordingTests.cs` (new); `tests/Server.Tests/Email/AuctionEmailsTests.cs` (fixture only:
  usings lines 1–8, class header line 12, fields 14–16, `CreateSut` lines 18–32 — no test body changes)

**Interfaces:**
- Consumes: Task 4 `IContentService.RenderEmailAsync` / `RenderTextAsync`, `RenderedEmail`, `ContentTestData`, `CapturingLogger<T>`;
  Task 2 `ContentKeys`, `ContentCatalog`; existing `IAuctionEmails` (unchanged), `IEmailOutbox`, `ITaxDocumentRepository`.
- Produces: `AuctionEmails(IEmailOutbox outbox, IUserRepository users, ITaxDocumentRepository taxDocuments, IContentService content, IOptions<EmailOptions> email, ILogger<AuctionEmails> log)`
  (replaces `IOptions<DisclosureOptions>` in the same position; DI unchanged); `DefaultEmailGolden.Emails`
  (`IReadOnlyDictionary<string, DefaultEmailGolden.Expected>`, `Expected(string Subject, string Text, string Html)`) keyed by content key,
  for all 22 emails (Task 7 uses the five tax document ones).

Values each email supplies (must equal the item's catalog placeholders — `EveryEmail_SuppliesExactlyTheCatalogsPlaceholders`):
every auction email gets `StallionName`, `Season`, `StudName` (today's `Name()` / `Season()` / `Farm()` with their fallbacks), plus —
Outbid: `HighBid`, `ClosesAt`; Won / OfferAccepted: `Amount`, `BuyerFee`; PaymentFailed(+Offer): `Amount`, `BuyerFee`, `Reason`
(= `LastChargeFailure`), `Deadline` (= `ChargeDueBy ?? now`); PaymentAbandoned(+Offer): `Amount`, `BuyerFee`; StudSaleConfirmation(+Offer):
`BuyerName`, `BuyerEmail`, `Amount`, `BuyerFee`; StudNoSaleAuction: `Reason` (= `ReasonText(reason)`); OfferReceived: `BuyerName`,
`BuyerEmail`, `Amount`, `Deadline` (= offer expiry); OfferDeclined / OfferExpired(+ListingClosed): `Amount`.

- [ ] **Step 1: Golden data** — today's 22 emails exactly as the pre-5b code produced them (captured by running
  `AuctionEmails` / `TaxDocumentEmails` at the start of this branch with the fixtures in `AuctionEmailsWordingTests` /
  `TaxDocumentEmailsWordingTests`, `PublicBaseUrl = "https://app.example/"` and the `appsettings.json` disclosure text).
  Don't regenerate it from the new code.

```csharp
// tests/Server.Tests/Email/DefaultEmailGolden.cs
using Stallions.Server.Content;

namespace Stallions.Server.Tests.Email;

/// <summary>
/// Every system email exactly as the code produced it before Phase 5b (captured from that code with the fixtures in
/// AuctionEmailsWordingTests and TaxDocumentEmailsWordingTests). With the seeded default wording the new code must
/// reproduce these byte for byte. Never regenerate this file from the current code.
/// </summary>
public static class DefaultEmailGolden
{
    public sealed record Expected(string Subject, string Text, string Html);

    public static readonly IReadOnlyDictionary<string, Expected> Emails = new Dictionary<string, Expected>
    {
        [ContentKeys.Outbid] = new(
            "You've been outbid on Snitzel",
            "Another buyer has bid $12,500.00 on the Snitzel nomination, so you are no longer the highest bidder.\n\nThe auction closes 10 Oct 2026, 8:00 pm (Sydney time).\n\nBid again: https://app.example/listings/11111111-1111-1111-1111-111111111111\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>Another buyer has bid $12,500.00 on the Snitzel nomination, so you are no longer the highest bidder.</p><p>The auction closes 10 Oct 2026, 8:00 pm (Sydney time).</p><p><a href=\"https://app.example/listings/11111111-1111-1111-1111-111111111111\">Bid again</a></p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.AuctionLost] = new(
            "Auction closed: Snitzel",
            "The auction for the Snitzel nomination (2026 Season) has closed and another bid won.\n\nThank you for bidding.\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>The auction for the Snitzel nomination (2026 Season) has closed and another bid won.</p><p>Thank you for bidding.</p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.AuctionEndedWithoutSale] = new(
            "Auction closed: Snitzel",
            "The auction for the Snitzel nomination (2026 Season) has closed without a sale.\n\nThank you for bidding.\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>The auction for the Snitzel nomination (2026 Season) has closed without a sale.</p><p>Thank you for bidding.</p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.WonAndCharged] = new(
            "You won the Snitzel nomination",
            "Your bid of $10,000.00 won the auction for the Snitzel nomination (2026 Season), offered by Arrowfield.\n\nPrice (your winning bid): $10,000.00\nBuyer fee paid to Stallions Australia: $150.00\nBalance you pay the stud directly: $9,850.00\nAll amounts include GST.\n\nThe buyer fee of $150.00 has been charged to your saved card.\n\nThe buyer fee is paid to Stallions Australia and forms part of the price — it is not added on top. You pay the balance (the price less the buyer fee) directly to the stud, under the stud's own terms. The price arrangement is entirely between you and the stud.\n\nThe stud farm will contact you separately to arrange payment of the remaining balance. The balance amount, payment terms, and any foal guarantee conditions are a matter between you and the stud farm only. Stallions Australia is not party to that arrangement.\n\nStud contact: Arrowfield, sales@arrowfield.example, 02 6545 9888\n\nYour tax invoice SA-000124 is available here: https://app.example/tax-documents/33333333-3333-3333-3333-333333333333\n\nView your sale record: https://app.example/my-purchases\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>Your bid of $10,000.00 won the auction for the Snitzel nomination (2026 Season), offered by Arrowfield.</p><table style=\"border-collapse:collapse\"><tr><td style=\"padding:4px 16px 4px 0\">Price (your winning bid)</td><td style=\"padding:4px 0;text-align:right\"><strong>$10,000.00</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Buyer fee paid to Stallions Australia</td><td style=\"padding:4px 0;text-align:right\"><strong>$150.00</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Balance you pay the stud directly</td><td style=\"padding:4px 0;text-align:right\"><strong>$9,850.00</strong></td></tr></table><p style=\"color:#6b7280;font-size:13px\">All amounts include GST.</p><p>The buyer fee of $150.00 has been charged to your saved card.</p><p>The buyer fee is paid to Stallions Australia and forms part of the price — it is not added on top. You pay the balance (the price less the buyer fee) directly to the stud, under the stud&#39;s own terms. The price arrangement is entirely between you and the stud.</p><p>The stud farm will contact you separately to arrange payment of the remaining balance. The balance amount, payment terms, and any foal guarantee conditions are a matter between you and the stud farm only. Stallions Australia is not party to that arrangement.</p><p>Stud contact: Arrowfield, sales@arrowfield.example, 02 6545 9888</p><p><a href=\"https://app.example/tax-documents/33333333-3333-3333-3333-333333333333\">Your tax invoice SA-000124 is available here</a></p><p><a href=\"https://app.example/my-purchases\">View your sale record</a></p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.OfferAcceptedAndCharged] = new(
            "Your offer for the Snitzel nomination was accepted",
            "Your offer of $9,000.00 for the Snitzel nomination (2026 Season) was accepted by Arrowfield.\n\nPrice (your accepted offer): $9,000.00\nBuyer fee paid to Stallions Australia: $150.00\nBalance you pay the stud directly: $8,850.00\nAll amounts include GST.\n\nThe buyer fee of $150.00 has been charged to your saved card.\n\nThe buyer fee is paid to Stallions Australia and forms part of the price — it is not added on top. You pay the balance (the price less the buyer fee) directly to the stud, under the stud's own terms. The price arrangement is entirely between you and the stud.\n\nThe stud farm will contact you separately to arrange payment of the remaining balance. The balance amount, payment terms, and any foal guarantee conditions are a matter between you and the stud farm only. Stallions Australia is not party to that arrangement.\n\nStud contact: Arrowfield, sales@arrowfield.example, 02 6545 9888\n\nYour tax invoice SA-000124 is available here: https://app.example/tax-documents/33333333-3333-3333-3333-333333333333\n\nView your sale record: https://app.example/my-purchases\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>Your offer of $9,000.00 for the Snitzel nomination (2026 Season) was accepted by Arrowfield.</p><table style=\"border-collapse:collapse\"><tr><td style=\"padding:4px 16px 4px 0\">Price (your accepted offer)</td><td style=\"padding:4px 0;text-align:right\"><strong>$9,000.00</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Buyer fee paid to Stallions Australia</td><td style=\"padding:4px 0;text-align:right\"><strong>$150.00</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Balance you pay the stud directly</td><td style=\"padding:4px 0;text-align:right\"><strong>$8,850.00</strong></td></tr></table><p style=\"color:#6b7280;font-size:13px\">All amounts include GST.</p><p>The buyer fee of $150.00 has been charged to your saved card.</p><p>The buyer fee is paid to Stallions Australia and forms part of the price — it is not added on top. You pay the balance (the price less the buyer fee) directly to the stud, under the stud&#39;s own terms. The price arrangement is entirely between you and the stud.</p><p>The stud farm will contact you separately to arrange payment of the remaining balance. The balance amount, payment terms, and any foal guarantee conditions are a matter between you and the stud farm only. Stallions Australia is not party to that arrangement.</p><p>Stud contact: Arrowfield, sales@arrowfield.example, 02 6545 9888</p><p><a href=\"https://app.example/tax-documents/33333333-3333-3333-3333-333333333333\">Your tax invoice SA-000124 is available here</a></p><p><a href=\"https://app.example/my-purchases\">View your sale record</a></p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.PaymentFailed] = new(
            "Action needed: payment for Snitzel failed",
            "Your bid of $10,000.00 won the auction for the Snitzel nomination, but we couldn't charge the buyer fee of $150.00 to your saved card: Your card was declined.\n\nPlease update your card by 10 Oct 2026, 10:00 pm (Sydney time). Saving a new card retries the payment straight away. If the payment can't be completed by then, the sale won't go ahead.\n\nUpdate your card: https://app.example/account/card\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>Your bid of $10,000.00 won the auction for the Snitzel nomination, but we couldn&#39;t charge the buyer fee of $150.00 to your saved card: Your card was declined.</p><p>Please update your card by 10 Oct 2026, 10:00 pm (Sydney time). Saving a new card retries the payment straight away. If the payment can&#39;t be completed by then, the sale won&#39;t go ahead.</p><p><a href=\"https://app.example/account/card\">Update your card</a></p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.PaymentFailedOffer] = new(
            "Action needed: payment for Snitzel failed",
            "Your offer of $9,000.00 for the Snitzel nomination was accepted, but we couldn't charge the buyer fee of $150.00 to your saved card: Your card was declined.\n\nPlease update your card by 10 Oct 2026, 10:00 pm (Sydney time). Saving a new card retries the payment straight away. If the payment can't be completed by then, the sale won't go ahead.\n\nUpdate your card: https://app.example/account/card\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>Your offer of $9,000.00 for the Snitzel nomination was accepted, but we couldn&#39;t charge the buyer fee of $150.00 to your saved card: Your card was declined.</p><p>Please update your card by 10 Oct 2026, 10:00 pm (Sydney time). Saving a new card retries the payment straight away. If the payment can&#39;t be completed by then, the sale won&#39;t go ahead.</p><p><a href=\"https://app.example/account/card\">Update your card</a></p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.PaymentAbandoned] = new(
            "No sale: Snitzel",
            "We couldn't charge the buyer fee for the Snitzel nomination by the deadline, so the sale has not gone ahead and the nomination is no longer yours.\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>We couldn&#39;t charge the buyer fee for the Snitzel nomination by the deadline, so the sale has not gone ahead and the nomination is no longer yours.</p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.PaymentAbandonedOffer] = new(
            "No sale: Snitzel",
            "We couldn't charge the buyer fee for your accepted offer on the Snitzel nomination by the deadline, so the sale has not gone ahead.\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>We couldn&#39;t charge the buyer fee for your accepted offer on the Snitzel nomination by the deadline, so the sale has not gone ahead.</p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.StudSaleConfirmation] = new(
            "Sale confirmed: Snitzel nomination",
            "The auction for the Snitzel nomination (2026 Season) has closed with a sale, and the buyer fee has been paid.\n\nBuyer: Jane Buyer, winner@example.com\n\nPrice: $10,000.00\nBuyer fee paid to Stallions Australia: $150.00\nBalance the buyer pays you directly: $9,850.00\nAll amounts include GST.\n\nPlease arrange the balance with the buyer under your own terms.\n\nView the listing: https://app.example/admin/listings/11111111-1111-1111-1111-111111111111\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>The auction for the Snitzel nomination (2026 Season) has closed with a sale, and the buyer fee has been paid.</p><p>Buyer: Jane Buyer, winner@example.com</p><table style=\"border-collapse:collapse\"><tr><td style=\"padding:4px 16px 4px 0\">Price</td><td style=\"padding:4px 0;text-align:right\"><strong>$10,000.00</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Buyer fee paid to Stallions Australia</td><td style=\"padding:4px 0;text-align:right\"><strong>$150.00</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Balance the buyer pays you directly</td><td style=\"padding:4px 0;text-align:right\"><strong>$9,850.00</strong></td></tr></table><p style=\"color:#6b7280;font-size:13px\">All amounts include GST.</p><p>Please arrange the balance with the buyer under your own terms.</p><p><a href=\"https://app.example/admin/listings/11111111-1111-1111-1111-111111111111\">View the listing</a></p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.StudSaleConfirmationOffer] = new(
            "Sale confirmed: Snitzel nomination",
            "You accepted an offer for the Snitzel nomination (2026 Season), and the buyer fee has been paid.\n\nBuyer: Jane Buyer, winner@example.com\n\nPrice: $9,000.00\nBuyer fee paid to Stallions Australia: $150.00\nBalance the buyer pays you directly: $8,850.00\nAll amounts include GST.\n\nPlease arrange the balance with the buyer under your own terms.\n\nView the listing: https://app.example/admin/listings/22222222-2222-2222-2222-222222222222\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>You accepted an offer for the Snitzel nomination (2026 Season), and the buyer fee has been paid.</p><p>Buyer: Jane Buyer, winner@example.com</p><table style=\"border-collapse:collapse\"><tr><td style=\"padding:4px 16px 4px 0\">Price</td><td style=\"padding:4px 0;text-align:right\"><strong>$9,000.00</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Buyer fee paid to Stallions Australia</td><td style=\"padding:4px 0;text-align:right\"><strong>$150.00</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Balance the buyer pays you directly</td><td style=\"padding:4px 0;text-align:right\"><strong>$8,850.00</strong></td></tr></table><p style=\"color:#6b7280;font-size:13px\">All amounts include GST.</p><p>Please arrange the balance with the buyer under your own terms.</p><p><a href=\"https://app.example/admin/listings/22222222-2222-2222-2222-222222222222\">View the listing</a></p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.StudNoSaleAuction] = new(
            "No sale: Snitzel nomination",
            "The auction for the Snitzel nomination (2026 Season) has closed without a sale: the highest bid was below your reserve.\n\nThe lot is marked Unsold in My Listings.\n\nOffer it as Make an Offer: https://app.example/admin/listings/11111111-1111-1111-1111-111111111111\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>The auction for the Snitzel nomination (2026 Season) has closed without a sale: the highest bid was below your reserve.</p><p>The lot is marked Unsold in My Listings.</p><p><a href=\"https://app.example/admin/listings/11111111-1111-1111-1111-111111111111\">Offer it as Make an Offer</a></p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.StudNoSaleMakeAnOffer] = new(
            "No sale: Snitzel nomination",
            "The sale from the offer you accepted for the Snitzel nomination (2026 Season) has not gone ahead: the buyer's payment couldn't be completed.\n\nYour Make an Offer listing stays open for other offers.\n\nView the listing: https://app.example/admin/listings/22222222-2222-2222-2222-222222222222\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>The sale from the offer you accepted for the Snitzel nomination (2026 Season) has not gone ahead: the buyer&#39;s payment couldn&#39;t be completed.</p><p>Your Make an Offer listing stays open for other offers.</p><p><a href=\"https://app.example/admin/listings/22222222-2222-2222-2222-222222222222\">View the listing</a></p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.OfferReceived] = new(
            "New offer: Snitzel nomination",
            "Jane Buyer (winner@example.com) has offered $9,000.00 for the Snitzel nomination (2026 Season).\n\nThe offer is open until 18 Oct 2026, 12:00 pm (Sydney time). Accepting it charges the buyer the buyer fee straight away.\n\nReview the offer: https://app.example/admin/listings/22222222-2222-2222-2222-222222222222\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>Jane Buyer (winner@example.com) has offered $9,000.00 for the Snitzel nomination (2026 Season).</p><p>The offer is open until 18 Oct 2026, 12:00 pm (Sydney time). Accepting it charges the buyer the buyer fee straight away.</p><p><a href=\"https://app.example/admin/listings/22222222-2222-2222-2222-222222222222\">Review the offer</a></p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.OfferDeclined] = new(
            "Offer declined: Snitzel",
            "The stud has declined your offer of $9,000.00 for the Snitzel nomination (2026 Season).\n\nView the listing: https://app.example/listings/22222222-2222-2222-2222-222222222222\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>The stud has declined your offer of $9,000.00 for the Snitzel nomination (2026 Season).</p><p><a href=\"https://app.example/listings/22222222-2222-2222-2222-222222222222\">View the listing</a></p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.OfferExpired] = new(
            "Offer closed: Snitzel",
            "Your offer of $9,000.00 for the Snitzel nomination (2026 Season) has expired without a response from the stud.\n\nView the listing: https://app.example/listings/22222222-2222-2222-2222-222222222222\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>Your offer of $9,000.00 for the Snitzel nomination (2026 Season) has expired without a response from the stud.</p><p><a href=\"https://app.example/listings/22222222-2222-2222-2222-222222222222\">View the listing</a></p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.OfferExpiredListingClosed] = new(
            "Offer closed: Snitzel",
            "The Snitzel nomination (2026 Season) is no longer taking offers, so your offer of $9,000.00 has closed without a response.\n\nView the listing: https://app.example/listings/22222222-2222-2222-2222-222222222222\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>The Snitzel nomination (2026 Season) is no longer taking offers, so your offer of $9,000.00 has closed without a response.</p><p><a href=\"https://app.example/listings/22222222-2222-2222-2222-222222222222\">View the listing</a></p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.ListingFeeTaxInvoice] = new(
            "Tax invoice SA-000123: Listing fee — Dundeel, 2026 Season",
            "Thank you — your listing fee has been paid. Your tax invoice is below.\n\nTax invoice: SA-000123\nDate: 11 Oct 2026\nSupplier: Stallions Australia Pty Ltd, ABN 12 345 678 901\nTo: Arrowfield\nDescription: Listing fee — Dundeel, 2026 Season\nAmount (excl. GST): $900.00\nGST: $90.00\nTotal (incl. GST): $990.00\n\nStatus: Paid\n\nView or print your tax invoice: https://app.example/tax-documents/44444444-4444-4444-4444-444444444444\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>Thank you — your listing fee has been paid. Your tax invoice is below.</p><table style=\"border-collapse:collapse\"><tr><td style=\"padding:4px 16px 4px 0\">Tax invoice</td><td style=\"padding:4px 0;text-align:right\"><strong>SA-000123</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Date</td><td style=\"padding:4px 0;text-align:right\"><strong>11 Oct 2026</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Supplier</td><td style=\"padding:4px 0;text-align:right\"><strong>Stallions Australia Pty Ltd, ABN 12 345 678 901</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">To</td><td style=\"padding:4px 0;text-align:right\"><strong>Arrowfield</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Description</td><td style=\"padding:4px 0;text-align:right\"><strong>Listing fee — Dundeel, 2026 Season</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Amount (excl. GST)</td><td style=\"padding:4px 0;text-align:right\"><strong>$900.00</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">GST</td><td style=\"padding:4px 0;text-align:right\"><strong>$90.00</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Total (incl. GST)</td><td style=\"padding:4px 0;text-align:right\"><strong>$990.00</strong></td></tr></table><p>Status: Paid</p><p><a href=\"https://app.example/tax-documents/44444444-4444-4444-4444-444444444444\">View or print your tax invoice</a></p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.ListingFeeInvoiceDue] = new(
            "Tax invoice SA-000123 — amount due $990.00 by 25 Oct 2026",
            "Here is your tax invoice for the listing fee. Please pay $990.00 by 25 Oct 2026.\n\nTax invoice: SA-000123\nDate: 11 Oct 2026\nSupplier: Stallions Australia Pty Ltd, ABN 12 345 678 901\nTo: Arrowfield\nDescription: Listing fee — Dundeel, 2026 Season\nAmount (excl. GST): $900.00\nGST: $90.00\nTotal (incl. GST): $990.00\n\nAccount name: Stallions Australia Pty Ltd\nBSB: 062-000\nAccount number: 12345678\n\nPlease use SA-000123 as your payment reference.\n\nView or print your tax invoice: https://app.example/tax-documents/44444444-4444-4444-4444-444444444444\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>Here is your tax invoice for the listing fee. Please pay $990.00 by 25 Oct 2026.</p><table style=\"border-collapse:collapse\"><tr><td style=\"padding:4px 16px 4px 0\">Tax invoice</td><td style=\"padding:4px 0;text-align:right\"><strong>SA-000123</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Date</td><td style=\"padding:4px 0;text-align:right\"><strong>11 Oct 2026</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Supplier</td><td style=\"padding:4px 0;text-align:right\"><strong>Stallions Australia Pty Ltd, ABN 12 345 678 901</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">To</td><td style=\"padding:4px 0;text-align:right\"><strong>Arrowfield</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Description</td><td style=\"padding:4px 0;text-align:right\"><strong>Listing fee — Dundeel, 2026 Season</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Amount (excl. GST)</td><td style=\"padding:4px 0;text-align:right\"><strong>$900.00</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">GST</td><td style=\"padding:4px 0;text-align:right\"><strong>$90.00</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Total (incl. GST)</td><td style=\"padding:4px 0;text-align:right\"><strong>$990.00</strong></td></tr></table><table style=\"border-collapse:collapse\"><tr><td style=\"padding:4px 16px 4px 0\">Account name</td><td style=\"padding:4px 0;text-align:right\"><strong>Stallions Australia Pty Ltd</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">BSB</td><td style=\"padding:4px 0;text-align:right\"><strong>062-000</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Account number</td><td style=\"padding:4px 0;text-align:right\"><strong>12345678</strong></td></tr></table><p>Please use SA-000123 as your payment reference.</p><p><a href=\"https://app.example/tax-documents/44444444-4444-4444-4444-444444444444\">View or print your tax invoice</a></p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.ListingFeeReceipt] = new(
            "Receipt: tax invoice SA-000123 paid",
            "We've received your payment of $990.00 for tax invoice SA-000123. Thank you.\n\nTax invoice: SA-000123\nDate: 11 Oct 2026\nSupplier: Stallions Australia Pty Ltd, ABN 12 345 678 901\nTo: Arrowfield\nDescription: Listing fee — Dundeel, 2026 Season\nAmount (excl. GST): $900.00\nGST: $90.00\nTotal (incl. GST): $990.00\n\nPaid: 11 Oct 2026\n\nView or print your tax invoice: https://app.example/tax-documents/44444444-4444-4444-4444-444444444444\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>We&#39;ve received your payment of $990.00 for tax invoice SA-000123. Thank you.</p><table style=\"border-collapse:collapse\"><tr><td style=\"padding:4px 16px 4px 0\">Tax invoice</td><td style=\"padding:4px 0;text-align:right\"><strong>SA-000123</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Date</td><td style=\"padding:4px 0;text-align:right\"><strong>11 Oct 2026</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Supplier</td><td style=\"padding:4px 0;text-align:right\"><strong>Stallions Australia Pty Ltd, ABN 12 345 678 901</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">To</td><td style=\"padding:4px 0;text-align:right\"><strong>Arrowfield</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Description</td><td style=\"padding:4px 0;text-align:right\"><strong>Listing fee — Dundeel, 2026 Season</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Amount (excl. GST)</td><td style=\"padding:4px 0;text-align:right\"><strong>$900.00</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">GST</td><td style=\"padding:4px 0;text-align:right\"><strong>$90.00</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Total (incl. GST)</td><td style=\"padding:4px 0;text-align:right\"><strong>$990.00</strong></td></tr></table><p>Paid: 11 Oct 2026</p><p><a href=\"https://app.example/tax-documents/44444444-4444-4444-4444-444444444444\">View or print your tax invoice</a></p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.BuyerFeeTaxInvoice] = new(
            "Tax invoice SA-000123: buyer fee",
            "The buyer fee has been charged to your saved card. Your tax invoice is below.\n\nTax invoice: SA-000123\nDate: 11 Oct 2026\nSupplier: Stallions Australia Pty Ltd, ABN 12 345 678 901\nTo: Jane Buyer\nDescription: Buyer fee — nomination to Snitzel, 2026 Season (auction)\nAmount (excl. GST): $136.36\nGST: $13.64\nTotal (incl. GST): $150.00\n\nThe buyer fee is paid to Stallions Australia and forms part of the price — it is not added on top. You pay the balance (the price less the buyer fee) directly to the stud, under the stud's own terms. The price arrangement is entirely between you and the stud.\n\nView or print your tax invoice: https://app.example/tax-documents/44444444-4444-4444-4444-444444444444\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>The buyer fee has been charged to your saved card. Your tax invoice is below.</p><table style=\"border-collapse:collapse\"><tr><td style=\"padding:4px 16px 4px 0\">Tax invoice</td><td style=\"padding:4px 0;text-align:right\"><strong>SA-000123</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Date</td><td style=\"padding:4px 0;text-align:right\"><strong>11 Oct 2026</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Supplier</td><td style=\"padding:4px 0;text-align:right\"><strong>Stallions Australia Pty Ltd, ABN 12 345 678 901</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">To</td><td style=\"padding:4px 0;text-align:right\"><strong>Jane Buyer</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Description</td><td style=\"padding:4px 0;text-align:right\"><strong>Buyer fee — nomination to Snitzel, 2026 Season (auction)</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Amount (excl. GST)</td><td style=\"padding:4px 0;text-align:right\"><strong>$136.36</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">GST</td><td style=\"padding:4px 0;text-align:right\"><strong>$13.64</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Total (incl. GST)</td><td style=\"padding:4px 0;text-align:right\"><strong>$150.00</strong></td></tr></table><p>The buyer fee is paid to Stallions Australia and forms part of the price — it is not added on top. You pay the balance (the price less the buyer fee) directly to the stud, under the stud&#39;s own terms. The price arrangement is entirely between you and the stud.</p><p><a href=\"https://app.example/tax-documents/44444444-4444-4444-4444-444444444444\">View or print your tax invoice</a></p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
        [ContentKeys.AdjustmentNote] = new(
            "Adjustment note SA-000130 for tax invoice SA-000123",
            "This adjustment note reverses tax invoice SA-000123 dated 11 Oct 2026 in full.\n\nReason: Buyer fee refunded — duplicate charge\n\nAdjustment note: SA-000130\nDate: 11 Oct 2026\nSupplier: Stallions Australia Pty Ltd, ABN 12 345 678 901\nTo: Arrowfield\nDescription: Listing fee — Dundeel, 2026 Season\nAmount (excl. GST): $900.00\nGST: $90.00\nTotal (incl. GST): $990.00\n\nView or print your adjustment note: https://app.example/tax-documents/88888888-8888-8888-8888-888888888888\n\nStallions Australia",
            "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\"><p>This adjustment note reverses tax invoice SA-000123 dated 11 Oct 2026 in full.</p><p>Reason: Buyer fee refunded — duplicate charge</p><table style=\"border-collapse:collapse\"><tr><td style=\"padding:4px 16px 4px 0\">Adjustment note</td><td style=\"padding:4px 0;text-align:right\"><strong>SA-000130</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Date</td><td style=\"padding:4px 0;text-align:right\"><strong>11 Oct 2026</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Supplier</td><td style=\"padding:4px 0;text-align:right\"><strong>Stallions Australia Pty Ltd, ABN 12 345 678 901</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">To</td><td style=\"padding:4px 0;text-align:right\"><strong>Arrowfield</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Description</td><td style=\"padding:4px 0;text-align:right\"><strong>Listing fee — Dundeel, 2026 Season</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Amount (excl. GST)</td><td style=\"padding:4px 0;text-align:right\"><strong>$900.00</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">GST</td><td style=\"padding:4px 0;text-align:right\"><strong>$90.00</strong></td></tr><tr><td style=\"padding:4px 16px 4px 0\">Total (incl. GST)</td><td style=\"padding:4px 0;text-align:right\"><strong>$990.00</strong></td></tr></table><p><a href=\"https://app.example/tax-documents/88888888-8888-8888-8888-888888888888\">View or print your adjustment note</a></p><p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>"),
    };
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/Server.Tests/Email/AuctionEmailsWordingTests.cs
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Content;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Email;

/// <summary>
/// The wording side of the auction emails: with the seeded defaults they are unchanged; Staff edits change only the
/// subject and message, never the generated amounts, disclosure, contact details or links.
/// The fixtures below are the ones DefaultEmailGolden was captured with — don't change them.
/// </summary>
public class AuctionEmailsWordingTests : IDisposable
{
    private static readonly User Winner = new() { Email = "winner@example.com", DisplayName = "Jane Buyer" };
    private static readonly User OutbidBidder = new() { Id = Guid.Parse("77777777-7777-7777-7777-777777777777"), Email = "outbid@example.com" };

    private readonly AppDbContext _db = DbContextFactory.Create(Guid.NewGuid().ToString());
    private readonly List<OutgoingEmail> _queued = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITaxDocumentRepository> _taxDocuments = new();
    private readonly CapturingLogger<ContentService> _contentLog = new();

    public AuctionEmailsWordingTests()
    {
        _db.Database.EnsureCreated(); // seeds version 1 (today's wording) of every item
        _users.Setup(u => u.GetByIdAsync(OutbidBidder.Id)).ReturnsAsync(OutbidBidder);
        _taxDocuments.Setup(t => t.GetBuyerFeeInvoiceAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new TaxDocument { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), Number = "SA-000124" });
    }

    public void Dispose() => _db.Dispose();

    private AuctionEmails CreateSut()
    {
        var outbox = new Mock<IEmailOutbox>();
        outbox.Setup(o => o.EnqueueAsync(It.IsAny<OutgoingEmail>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .Callback<OutgoingEmail, string?, Guid?>((e, _, _) => _queued.Add(e))
            .Returns(Task.CompletedTask);
        return new AuctionEmails(outbox.Object, _users.Object, _taxDocuments.Object,
            ContentTestData.Service(_db, log: _contentLog),
            Microsoft.Extensions.Options.Options.Create(new EmailOptions { PublicBaseUrl = "https://app.example/" }),
            NullLogger<AuctionEmails>.Instance);
    }

    private static AuctionListing Listing() => new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        EndDateTime = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc),
        Stallion = new Stallion { Name = "Snitzel" }, Season = new Season { Name = "2026 Season" },
        StudFarm = new StudFarm { Name = "Arrowfield", ContactEmail = "sales@arrowfield.example", ContactPhone = "02 6545 9888" }
    };

    private static MakeAnOfferListing OfferListing() => new()
    {
        Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Stallion = new Stallion { Name = "Snitzel" }, Season = new Season { Name = "2026 Season" },
        StudFarm = new StudFarm { Name = "Arrowfield", ContactEmail = "sales@arrowfield.example", ContactPhone = "02 6545 9888" }
    };

    private static Purchase Sale() => new()
    {
        Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
        TotalPriceIncGst = 10000m, BuyerFeeIncGst = 150m, BuyerFeeExGst = 136.36m, BuyerFeeGst = 13.64m,
        BalancePayableToStudIncGst = 9850m, ChargeDueBy = new DateTime(2026, 10, 10, 11, 0, 0, DateTimeKind.Utc),
        LastChargeFailure = "Your card was declined."
    };

    private static Purchase OfferSale()
    {
        var sale = Sale();
        sale.OfferId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        sale.TotalPriceIncGst = 9000m;
        sale.BalancePayableToStudIncGst = 8850m;
        return sale;
    }

    private static Offer TheOffer() => new() { AmountIncGst = 9000m, ExpiresAt = new DateTime(2026, 10, 18, 1, 0, 0, DateTimeKind.Utc) };

    /// <summary>Each content key and the email call that uses it.</summary>
    private static readonly IReadOnlyDictionary<string, Func<AuctionEmails, Task>> Calls = new Dictionary<string, Func<AuctionEmails, Task>>
    {
        [ContentKeys.Outbid] = s => s.OutbidAsync(Listing(), OutbidBidder.Id, 12500m),
        [ContentKeys.AuctionLost] = s => s.AuctionLostAsync(Listing(), Winner),
        [ContentKeys.AuctionEndedWithoutSale] = s => s.AuctionEndedWithoutSaleAsync(Listing(), Winner),
        [ContentKeys.WonAndCharged] = s => s.WonAndChargedAsync(Listing(), Sale(), Winner),
        [ContentKeys.OfferAcceptedAndCharged] = s => s.WonAndChargedAsync(OfferListing(), OfferSale(), Winner),
        [ContentKeys.PaymentFailed] = s => s.PaymentFailedAsync(Listing(), Sale(), Winner),
        [ContentKeys.PaymentFailedOffer] = s => s.PaymentFailedAsync(OfferListing(), OfferSale(), Winner),
        [ContentKeys.PaymentAbandoned] = s => s.PaymentAbandonedAsync(Listing(), Sale(), Winner),
        [ContentKeys.PaymentAbandonedOffer] = s => s.PaymentAbandonedAsync(OfferListing(), OfferSale(), Winner),
        [ContentKeys.StudSaleConfirmation] = s => s.StudSaleConfirmationAsync(Listing(), Sale(), Winner),
        [ContentKeys.StudSaleConfirmationOffer] = s => s.StudSaleConfirmationAsync(OfferListing(), OfferSale(), Winner),
        [ContentKeys.StudNoSaleAuction] = s => s.StudNoSaleAsync(Listing(), ListingCloseReason.ReserveNotMet),
        [ContentKeys.StudNoSaleMakeAnOffer] = s => s.StudNoSaleAsync(OfferListing(), ListingCloseReason.ChargeFailed),
        [ContentKeys.OfferReceived] = s => s.OfferReceivedAsync(OfferListing(), TheOffer(), Winner),
        [ContentKeys.OfferDeclined] = s => s.OfferDeclinedAsync(OfferListing(), TheOffer(), Winner),
        [ContentKeys.OfferExpired] = s => s.OfferExpiredAsync(OfferListing(), TheOffer(), Winner, listingClosed: false),
        [ContentKeys.OfferExpiredListingClosed] = s => s.OfferExpiredAsync(OfferListing(), TheOffer(), Winner, listingClosed: true)
    };

    public static IEnumerable<object[]> EmailKeys() => Calls.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(EmailKeys))]
    public async Task DefaultWording_MatchesTodaysEmailsExactly(string key)
    {
        await Calls[key](CreateSut());

        var email = _queued.Single();
        var expected = DefaultEmailGolden.Emails[key];
        email.Subject.Should().Be(expected.Subject);
        email.TextBody.Should().Be(expected.Text);
        email.HtmlBody.Should().Be(expected.Html);
        _contentLog.Warnings.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(EmailKeys))]
    public async Task EveryEmail_SuppliesExactlyTheCatalogsPlaceholders(string key)
    {
        var entry = ContentCatalog.Find(key)!;
        var everything = string.Join(" ", entry.Placeholders.Select(p => $"[{p.Name}={{{p.Name}}}]"));
        ContentTestData.SetWording(_db, key, "S " + everything, everything);

        await Calls[key](CreateSut());

        _contentLog.Warnings.Should().BeEmpty("the email code must supply every placeholder its catalog entry lists, and no others");
        var text = _queued.Single().TextBody;
        foreach (var p in entry.Placeholders)
            text.Should().Contain($"[{p.Name}=");
    }

    [Fact]
    public void EveryAuctionEmailKey_IsCovered() =>
        Calls.Keys.Should().BeEquivalentTo(ContentCatalog.Entries
            .Where(e => e.Group is ContentGroups.BuyerEmails or ContentGroups.StudEmails).Select(e => e.Key));

    [Fact]
    public async Task WonAndCharged_WithEditedWording_KeepsTheAmountsTableDisclosureAndLinks()
    {
        ContentTestData.SetWording(_db, ContentKeys.WonAndCharged, "Congratulations — {StallionName} is yours",
            "Well done!\n\nYou bought the {StallionName} nomination for {Amount}.");

        await Calls[ContentKeys.WonAndCharged](CreateSut());

        var email = _queued.Single();
        email.Subject.Should().Be("Congratulations — Snitzel is yours");
        email.TextBody.Should().StartWith("Well done!\n\nYou bought the Snitzel nomination for $10,000.00.\n\nPrice (your winning bid): $10,000.00\n");
        email.TextBody.Should()
            .Contain("Buyer fee paid to Stallions Australia: $150.00\nBalance you pay the stud directly: $9,850.00\nAll amounts include GST.")
            .And.Contain("The buyer fee of $150.00 has been charged to your saved card.")
            .And.Contain(ContentCatalog.Find(ContentKeys.BuyerFeeExplanation)!.DefaultBody)
            .And.Contain(ContentCatalog.Find(ContentKeys.StudFarmBalanceArrangement)!.DefaultBody)
            .And.Contain("Stud contact: Arrowfield, sales@arrowfield.example, 02 6545 9888")
            .And.Contain("Your tax invoice SA-000124 is available here: https://app.example/tax-documents/33333333-3333-3333-3333-333333333333")
            .And.EndWith("View your sale record: https://app.example/my-purchases\n\nStallions Australia");
        email.HtmlBody.Should().Contain("<p>Well done!</p><p>You bought the Snitzel nomination for $10,000.00.</p><table");
    }

    [Fact]
    public async Task EditedDisclosure_IsWhatTheWinEmailShows()
    {
        ContentTestData.SetWording(_db, ContentKeys.BuyerFeeExplanation, null, "EDITED fee wording.");
        ContentTestData.SetWording(_db, ContentKeys.StudFarmBalanceArrangement, null, "EDITED balance wording.");

        await Calls[ContentKeys.OfferAcceptedAndCharged](CreateSut());

        _queued.Single().TextBody.Should()
            .Contain("The buyer fee of $150.00 has been charged to your saved card.\n\nEDITED fee wording.\n\nEDITED balance wording.")
            .And.NotContain("it is not added on top");
    }

    [Fact]
    public async Task EditedWording_WithMarkup_IsEncoded()
    {
        ContentTestData.SetWording(_db, ContentKeys.AuctionLost, "Closed: {StallionName}",
            "<a href=\"https://evil.example\">Claim your prize</a> {StallionName}");

        await Calls[ContentKeys.AuctionLost](CreateSut());

        var email = _queued.Single();
        email.HtmlBody.Should().Contain("<p>&lt;a href=&quot;https://evil.example&quot;&gt;Claim your prize&lt;/a&gt; Snitzel</p>")
            .And.NotContain("<a href=\"https://evil.example\"");
    }

    [Fact]
    public async Task EditedWording_ALineBreak_IsABrInHtml_AndStaysInText()
    {
        ContentTestData.SetWording(_db, ContentKeys.OfferDeclined, "Declined: {StallionName}",
            "Sorry.\nYour offer of {Amount} was declined.");

        await Calls[ContentKeys.OfferDeclined](CreateSut());

        var email = _queued.Single();
        email.HtmlBody.Should().Contain("<p>Sorry.<br>Your offer of $9,000.00 was declined.</p><p><a href=");
        email.TextBody.Should().StartWith("Sorry.\nYour offer of $9,000.00 was declined.\n\nView the listing: ");
    }
}
```

`AuctionEmailsTests.cs` fixture — replace lines 1–32 with:

```csharp
// tests/Server.Tests/Email/AuctionEmailsTests.cs (lines 1–32)
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Content;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Email;

public class AuctionEmailsTests : IDisposable
{
    private readonly AppDbContext _db = DbContextFactory.Create(Guid.NewGuid().ToString());
    private readonly List<OutgoingEmail> _queued = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITaxDocumentRepository> _taxDocuments = new();

    public AuctionEmailsTests()
    {
        _db.Database.EnsureCreated();
        ContentTestData.SetWording(_db, ContentKeys.BuyerFeeExplanation, null, "CONFIGURED fee wording.");
        ContentTestData.SetWording(_db, ContentKeys.StudFarmBalanceArrangement, null, "CONFIGURED balance wording.");
    }

    public void Dispose() => _db.Dispose();

    private AuctionEmails CreateSut()
    {
        var outbox = new Mock<IEmailOutbox>();
        outbox.Setup(o => o.EnqueueAsync(It.IsAny<OutgoingEmail>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .Callback<OutgoingEmail, string?, Guid?>((e, _, _) => _queued.Add(e))
            .Returns(Task.CompletedTask);
        return new AuctionEmails(outbox.Object, _users.Object, _taxDocuments.Object, ContentTestData.Service(_db),
            Microsoft.Extensions.Options.Options.Create(new EmailOptions { PublicBaseUrl = "https://app.example/" }),
            NullLogger<AuctionEmails>.Instance);
    }
```

(The rest of the file — `Owner`, `Listing()`, `Winner`, `Sale()` and every test — is unchanged.)

- [ ] **Step 3: Run red** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~AuctionEmails"` → compile errors (`AuctionEmails` still takes `IOptions<DisclosureOptions>`).

- [ ] **Step 4: Implement** — replace `src/Server/Email/AuctionEmails.cs`:

```csharp
// src/Server/Email/AuctionEmails.cs
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;
using Stallions.Server.Content;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.Enums;

namespace Stallions.Server.Email;

/// <summary>
/// The auction and Make an Offer emails. Staff edit each email's subject and message (Staff → Wording); the amounts
/// table, the mandatory buyer-fee disclosure block, contact details and links are generated here and always added.
/// Amounts inc. GST; times in Sydney time. Bidders are never told who else bid.
/// </summary>
public class AuctionEmails : IAuctionEmails
{
    private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");
    private static readonly TimeZoneInfo Sydney = FindSydney();

    private readonly IEmailOutbox _outbox;
    private readonly IUserRepository _users;
    private readonly ITaxDocumentRepository _taxDocuments;
    private readonly IContentService _content;
    private readonly string _baseUrl;
    private readonly ILogger<AuctionEmails> _log;

    public AuctionEmails(IEmailOutbox outbox, IUserRepository users, ITaxDocumentRepository taxDocuments,
        IContentService content, IOptions<EmailOptions> email, ILogger<AuctionEmails> log)
    {
        _outbox = outbox;
        _users = users;
        _taxDocuments = taxDocuments;
        _content = content;
        _baseUrl = email.Value.PublicBaseUrl.TrimEnd('/');
        _log = log;
    }

    public async Task OutbidAsync(AuctionListing listing, Guid previousBidderUserId, decimal newHighBidIncGst)
    {
        var bidder = await _users.GetByIdAsync(previousBidderUserId);
        if (bidder == null) return;
        var email = await RenderAsync(ContentKeys.Outbid, listing,
            ("HighBid", Money(newHighBidIncGst)), ("ClosesAt", When(listing.EndDateTime)));
        await QueueAsync(bidder.Email, "Outbid", email, listing,
            Link($"/listings/{listing.Id}", "Bid again"));
    }

    public async Task AuctionLostAsync(AuctionListing listing, User bidder) =>
        await QueueAsync(bidder.Email, "AuctionLost", await RenderAsync(ContentKeys.AuctionLost, listing), listing);

    public async Task AuctionEndedWithoutSaleAsync(AuctionListing listing, User bidder) =>
        await QueueAsync(bidder.Email, "AuctionEndedWithoutSale",
            await RenderAsync(ContentKeys.AuctionEndedWithoutSale, listing), listing);

    private static bool FromOffer(Purchase p) => p.OfferId != null;

    public async Task WonAndChargedAsync(Listing listing, Purchase purchase, User winner)
    {
        // The charger issues the buyer fee tax invoice just before this email, in the same transaction.
        var invoice = await _taxDocuments.GetBuyerFeeInvoiceAsync(purchase.Id);
        var fromOffer = FromOffer(purchase);
        var email = await RenderAsync(fromOffer ? ContentKeys.OfferAcceptedAndCharged : ContentKeys.WonAndCharged, listing,
            ("Amount", Money(purchase.TotalPriceIncGst)), ("BuyerFee", Money(purchase.BuyerFeeIncGst)));

        var blocks = new List<Block>
        {
            fromOffer
                ? Amounts(purchase, "Price (your accepted offer)", "Balance you pay the stud directly")
                : Amounts(purchase, "Price (your winning bid)", "Balance you pay the stud directly"),
            // The mandatory buyer-fee disclosure block: the fee charged, then the Staff-managed disclosure wording.
            Para($"The buyer fee of {Money(purchase.BuyerFeeIncGst)} has been charged to your saved card.")
        };
        blocks.AddRange(await DisclosureAsync(ContentKeys.BuyerFeeExplanation));
        blocks.AddRange(await DisclosureAsync(ContentKeys.StudFarmBalanceArrangement));
        blocks.Add(Para(StudContact(listing)));
        if (invoice != null)
            blocks.Add(Link($"/tax-documents/{invoice.Id}", $"Your tax invoice {invoice.Number} is available here"));
        blocks.Add(Link("/my-purchases", "View your sale record"));

        await QueueAsync(winner.Email, fromOffer ? "OfferAcceptedAndCharged" : "WonAndCharged", email, listing, blocks.ToArray());
    }

    public async Task PaymentFailedAsync(Listing listing, Purchase purchase, User winner)
    {
        var email = await RenderAsync(FromOffer(purchase) ? ContentKeys.PaymentFailedOffer : ContentKeys.PaymentFailed, listing,
            ("Amount", Money(purchase.TotalPriceIncGst)), ("BuyerFee", Money(purchase.BuyerFeeIncGst)),
            ("Reason", purchase.LastChargeFailure), ("Deadline", When(purchase.ChargeDueBy ?? DateTime.UtcNow)));
        await QueueAsync(winner.Email, "PaymentFailed", email, listing,
            Link("/account/card", "Update your card"));
    }

    public async Task PaymentAbandonedAsync(Listing listing, Purchase purchase, User winner)
    {
        var email = await RenderAsync(FromOffer(purchase) ? ContentKeys.PaymentAbandonedOffer : ContentKeys.PaymentAbandoned, listing,
            ("Amount", Money(purchase.TotalPriceIncGst)), ("BuyerFee", Money(purchase.BuyerFeeIncGst)));
        await QueueAsync(winner.Email, "PaymentAbandoned", email, listing);
    }

    public async Task StudSaleConfirmationAsync(Listing listing, Purchase purchase, User winner)
    {
        var email = await RenderAsync(FromOffer(purchase) ? ContentKeys.StudSaleConfirmationOffer : ContentKeys.StudSaleConfirmation, listing,
            ("BuyerName", winner.DisplayName), ("BuyerEmail", winner.Email),
            ("Amount", Money(purchase.TotalPriceIncGst)), ("BuyerFee", Money(purchase.BuyerFeeIncGst)));
        await QueueAsync(await StudAddressAsync(listing), "StudSaleConfirmation", email, listing,
            Amounts(purchase, "Price", "Balance the buyer pays you directly"),
            Para("Please arrange the balance with the buyer under your own terms."),
            Link($"/admin/listings/{listing.Id}", "View the listing"));
    }

    public async Task StudNoSaleAsync(Listing listing, ListingCloseReason reason)
    {
        var to = await StudAddressAsync(listing);
        if (listing is MakeAnOfferListing)
            await QueueAsync(to, "StudNoSale", await RenderAsync(ContentKeys.StudNoSaleMakeAnOffer, listing), listing,
                Link($"/admin/listings/{listing.Id}", "View the listing"));
        else
            await QueueAsync(to, "StudNoSale",
                await RenderAsync(ContentKeys.StudNoSaleAuction, listing, ("Reason", ReasonText(reason))), listing,
                Link($"/admin/listings/{listing.Id}", "Offer it as Make an Offer"));
    }

    public async Task OfferReceivedAsync(MakeAnOfferListing listing, Offer offer, User buyer)
    {
        var email = await RenderAsync(ContentKeys.OfferReceived, listing,
            ("BuyerName", buyer.DisplayName), ("BuyerEmail", buyer.Email),
            ("Amount", Money(offer.AmountIncGst)), ("Deadline", When(offer.ExpiresAt)));
        await QueueAsync(await StudAddressAsync(listing), "OfferReceived", email, listing,
            Link($"/admin/listings/{listing.Id}", "Review the offer"));
    }

    public async Task OfferDeclinedAsync(MakeAnOfferListing listing, Offer offer, User buyer) =>
        await QueueAsync(buyer.Email, "OfferDeclined",
            await RenderAsync(ContentKeys.OfferDeclined, listing, ("Amount", Money(offer.AmountIncGst))), listing,
            Link($"/listings/{listing.Id}", "View the listing"));

    public async Task OfferExpiredAsync(MakeAnOfferListing listing, Offer offer, User buyer, bool listingClosed) =>
        await QueueAsync(buyer.Email, "OfferExpired",
            await RenderAsync(listingClosed ? ContentKeys.OfferExpiredListingClosed : ContentKeys.OfferExpired, listing,
                ("Amount", Money(offer.AmountIncGst))), listing,
            Link($"/listings/{listing.Id}", "View the listing"));

    // ── Building blocks ────────────────────────────────────────────────────────

    private sealed record Block(string Html, string Text);

    /// <summary>The Staff wording for this email, with the listing's names plus these values.</summary>
    private Task<RenderedEmail> RenderAsync(string key, Listing listing, params (string Name, string? Value)[] values)
    {
        var all = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["StallionName"] = Name(listing),
            ["Season"] = Season(listing),
            ["StudName"] = Farm(listing)
        };
        foreach (var (name, value) in values) all[name] = value;
        return _content.RenderEmailAsync(key, all);
    }

    private async Task<IEnumerable<Block>> DisclosureAsync(string key) =>
        (await _content.RenderTextAsync(key)).Select(ToBlock);

    private static Block ToBlock(RenderedParagraph p) => new($"<p>{p.Html}</p>", p.Text);

    /// <summary>The Staff message paragraphs first, then the generated blocks.</summary>
    private async Task QueueAsync(string? to, string template, RenderedEmail email, Listing listing, params Block[] blocks)
    {
        var all = email.Paragraphs.Select(ToBlock).Concat(blocks).ToList();
        var html = "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\">"
                   + string.Concat(all.Select(b => b.Html))
                   + "<p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>";
        var text = string.Join("\n\n", all.Select(b => b.Text)) + "\n\nStallions Australia";
        await _outbox.EnqueueAsync(new OutgoingEmail(to ?? string.Empty, email.Subject, html, text, template), "Listing", listing.Id);
    }

    private static Block Para(string text) => new($"<p>{Enc(text)}</p>", text);

    private Block Link(string path, string label)
    {
        var url = _baseUrl + path;
        return new($"<p><a href=\"{Enc(url)}\">{Enc(label)}</a></p>", $"{label}: {url}");
    }

    private static Block Amounts(Purchase p, string priceLabel, string balanceLabel)
    {
        var rows = new[]
        {
            (priceLabel, Money(p.TotalPriceIncGst)),
            ("Buyer fee paid to Stallions Australia", Money(p.BuyerFeeIncGst)),
            (balanceLabel, Money(p.BalancePayableToStudIncGst))
        };
        var html = "<table style=\"border-collapse:collapse\">"
                   + string.Concat(rows.Select(r =>
                       $"<tr><td style=\"padding:4px 16px 4px 0\">{Enc(r.Item1)}</td><td style=\"padding:4px 0;text-align:right\"><strong>{Enc(r.Item2)}</strong></td></tr>"))
                   + "</table><p style=\"color:#6b7280;font-size:13px\">All amounts include GST.</p>";
        var text = string.Join("\n", rows.Select(r => $"{r.Item1}: {r.Item2}")) + "\nAll amounts include GST.";
        return new(html, text);
    }

    private async Task<string?> StudAddressAsync(Listing listing)
    {
        var farm = listing.StudFarm;
        if (!string.IsNullOrWhiteSpace(farm?.ContactEmail)) return farm.ContactEmail;
        var owner = farm?.User ?? (farm != null ? await _users.GetByIdAsync(farm.UserId) : null);
        if (string.IsNullOrWhiteSpace(owner?.Email))
            _log.LogWarning("Stud for listing {ListingId} has no contact or owner email", listing.Id);
        return owner?.Email;
    }

    private static string StudContact(Listing listing)
    {
        var farm = listing.StudFarm;
        var parts = new[] { farm?.Name, farm?.ContactEmail, farm?.ContactPhone }.Where(p => !string.IsNullOrWhiteSpace(p));
        return $"Stud contact: {string.Join(", ", parts)}";
    }

    private static string ReasonText(ListingCloseReason reason) => reason switch
    {
        ListingCloseReason.NoBids => "there were no bids.",
        ListingCloseReason.ReserveNotMet => "the highest bid was below your reserve.",
        ListingCloseReason.ChargeFailed => "the winning buyer's payment couldn't be completed.",
        _ => "no sale was made."
    };

    private static string Name(Listing l) => l.Stallion?.Name ?? "stallion";
    private static string Season(Listing l) => l.Season?.Name ?? "this season";
    private static string Farm(Listing l) => l.StudFarm?.Name ?? "the stud";
    private static string Money(decimal amount) => amount.ToString("C2", Au);
    private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string When(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Sydney)
            .ToString("d MMM yyyy, h:mm tt", Au) + " (Sydney time)";

    private static TimeZoneInfo FindSydney()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time"); }
    }
}
```

- [ ] **Step 5: Run green** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~AuctionEmails"` (every existing
  `AuctionEmailsTests` test unchanged, and `AuctionEmailsWordingTests`); every `AuctionCloser*`, `BuyerFeeCharger*`,
  `OfferExpirer*`, `OfferServiceTests`, `BidServiceTests` test unchanged (they mock `IAuctionEmails`); full build (0 warnings) and full test run.
  If a golden test fails, fix the code, never `DefaultEmailGolden`.

- [ ] **Step 6: Commit**

```bash
git add src/Server/Email/AuctionEmails.cs tests/Server.Tests/Email/DefaultEmailGolden.cs \
  tests/Server.Tests/Email/AuctionEmailsWordingTests.cs tests/Server.Tests/Email/AuctionEmailsTests.cs
git status --short
git commit -m "feat: auction and offer emails use Staff-edited subject and message; generated blocks unchanged

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Tax document emails use the Staff wording; remove `DisclosureOptions` and the `Checkout` config

**Files:**
- Modify: `src/Server/TaxDocuments/TaxDocumentEmails.cs` (whole file)
- Delete: `src/Server/Options/DisclosureOptions.cs`
- Modify: `src/Server/Program.cs` (delete line 12 `using Stallions.Server.Options;` and lines 79–80, the `// Config options` comment and
  `builder.Services.Configure<DisclosureOptions>(…)`)
- Modify: `src/Server/appsettings.json` (delete lines 18–22, the `Checkout` section), `src/Server/appsettings.Development.json`
  (delete lines 24–26, the `Checkout` section)
- Test: `tests/Server.Tests/TaxDocuments/TaxDocumentEmailsWordingTests.cs` (new); `tests/Server.Tests/TaxDocuments/TaxDocumentEmailsTests.cs`
  (fixture only: usings lines 1–8, class header line 12, `CreateSut` lines 16–25)

**Interfaces:**
- Consumes: Task 4 `IContentService`, `ContentTestData`, `CapturingLogger<T>`; Task 6 `DefaultEmailGolden`; existing `ITaxDocumentEmails` (unchanged).
- Produces: `TaxDocumentEmails(IEmailOutbox outbox, IContentService content, IOptions<EmailOptions> email)`. Values supplied:
  every document email `InvoiceNumber` (= `d.Number`), `Description`, `Amount` (= `d.AmountIncGst`); listing fee emails `StudName`
  (= recipient), invoice due `DueDate`; buyer fee invoice `BuyerName` (= recipient); adjustment note `OriginalNumber`, `OriginalDate`, `Reason`.
  `Stallions.Server.Options` namespace no longer exists.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Server.Tests/TaxDocuments/TaxDocumentEmailsWordingTests.cs
using FluentAssertions;
using Moq;
using Stallions.Server.Content;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Email;
using Stallions.Server.TaxDocuments;
using Stallions.Server.Tests.Email;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.TaxDocuments;

/// <summary>The tax document emails' wording. The fixtures are the ones DefaultEmailGolden was captured with.</summary>
public class TaxDocumentEmailsWordingTests : IDisposable
{
    private static readonly DateTime Issued = new(2026, 10, 11, 2, 0, 0, DateTimeKind.Utc);

    private readonly AppDbContext _db = DbContextFactory.Create(Guid.NewGuid().ToString());
    private readonly List<OutgoingEmail> _queued = new();
    private readonly CapturingLogger<ContentService> _contentLog = new();

    public TaxDocumentEmailsWordingTests() => _db.Database.EnsureCreated();

    public void Dispose() => _db.Dispose();

    private TaxDocumentEmails CreateSut()
    {
        var outbox = new Mock<IEmailOutbox>();
        outbox.Setup(o => o.EnqueueAsync(It.IsAny<OutgoingEmail>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .Callback<OutgoingEmail, string?, Guid?>((e, _, _) => _queued.Add(e))
            .Returns(Task.CompletedTask);
        return new TaxDocumentEmails(outbox.Object, ContentTestData.Service(_db, log: _contentLog),
            Microsoft.Extensions.Options.Options.Create(new EmailOptions { PublicBaseUrl = "https://app.example/" }));
    }

    private static TaxDocument Doc(TaxDocumentKind kind = TaxDocumentKind.ListingFeeInvoice) => new()
    {
        Id = Guid.Parse("44444444-4444-4444-4444-444444444444"), Number = "SA-000123", Kind = kind, Status = TaxDocumentStatus.Paid,
        IssuedAt = Issued, PaidAt = Issued, Description = "Listing fee — Dundeel, 2026 Season", AmountIncGst = 990m,
        AmountExGst = 900m, GstAmount = 90m, SupplierName = "Stallions Australia Pty Ltd", SupplierAbn = "12345678901",
        SupplierAddress = "1 Racecourse Road", RecipientName = "Arrowfield", RecipientEmail = "sales@arrowfield.example"
    };

    private static TaxDocument DueInvoice()
    {
        var d = Doc();
        d.Status = TaxDocumentStatus.Unpaid;
        d.PaidAt = null;
        d.DueAt = d.IssuedAt.AddDays(14);
        d.BankAccountName = "Stallions Australia Pty Ltd";
        d.BankBsb = "062000";
        d.BankAccountNumber = "12345678";
        return d;
    }

    private static TaxDocument BuyerFeeInvoice()
    {
        var d = Doc(TaxDocumentKind.BuyerFeeInvoice);
        d.Description = "Buyer fee — nomination to Snitzel, 2026 Season (auction)";
        d.AmountIncGst = 150m;
        d.AmountExGst = 136.36m;
        d.GstAmount = 13.64m;
        d.RecipientName = "Jane Buyer";
        d.RecipientEmail = "winner@example.com";
        return d;
    }

    private static TaxDocument Note()
    {
        var n = Doc(TaxDocumentKind.AdjustmentNote);
        n.Id = Guid.Parse("88888888-8888-8888-8888-888888888888");
        n.Number = "SA-000130";
        n.Reason = "Buyer fee refunded — duplicate charge";
        return n;
    }

    private static readonly IReadOnlyDictionary<string, Func<TaxDocumentEmails, Task>> Calls = new Dictionary<string, Func<TaxDocumentEmails, Task>>
    {
        [ContentKeys.ListingFeeTaxInvoice] = s => s.ListingFeeTaxInvoiceAsync(Doc()),
        [ContentKeys.ListingFeeInvoiceDue] = s => s.ListingFeeInvoiceDueAsync(DueInvoice()),
        [ContentKeys.ListingFeeReceipt] = s => s.ListingFeeReceiptAsync(Doc()),
        [ContentKeys.BuyerFeeTaxInvoice] = s => s.BuyerFeeTaxInvoiceAsync(BuyerFeeInvoice()),
        [ContentKeys.AdjustmentNote] = s => s.AdjustmentNoteAsync(Note(), Doc(TaxDocumentKind.BuyerFeeInvoice))
    };

    public static IEnumerable<object[]> EmailKeys() => Calls.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(EmailKeys))]
    public async Task DefaultWording_MatchesTodaysEmailsExactly(string key)
    {
        await Calls[key](CreateSut());

        var email = _queued.Single();
        var expected = DefaultEmailGolden.Emails[key];
        email.Subject.Should().Be(expected.Subject);
        email.TextBody.Should().Be(expected.Text);
        email.HtmlBody.Should().Be(expected.Html);
        _contentLog.Warnings.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(EmailKeys))]
    public async Task EveryEmail_SuppliesExactlyTheCatalogsPlaceholders(string key)
    {
        var entry = ContentCatalog.Find(key)!;
        var everything = string.Join(" ", entry.Placeholders.Select(p => $"[{p.Name}={{{p.Name}}}]"));
        ContentTestData.SetWording(_db, key, "S " + everything, everything);

        await Calls[key](CreateSut());

        _contentLog.Warnings.Should().BeEmpty("the email code must supply every placeholder its catalog entry lists, and no others");
        foreach (var p in entry.Placeholders)
            _queued.Single().TextBody.Should().Contain($"[{p.Name}=");
    }

    [Fact]
    public void EveryTaxDocumentEmailKey_IsCovered() =>
        Calls.Keys.Should().BeEquivalentTo(ContentCatalog.Entries
            .Where(e => e.Group == ContentGroups.TaxDocumentEmails).Select(e => e.Key));

    [Fact]
    public async Task BuyerFeeTaxInvoice_WithEditedWording_KeepsTheInvoiceTableAndFeeExplanation()
    {
        ContentTestData.SetWording(_db, ContentKeys.BuyerFeeTaxInvoice, "Your receipt {InvoiceNumber}",
            "Hi {BuyerName}, thanks for your purchase.");

        await Calls[ContentKeys.BuyerFeeTaxInvoice](CreateSut());

        var email = _queued.Single();
        email.Subject.Should().Be("Your receipt SA-000123");
        email.TextBody.Should().StartWith("Hi Jane Buyer, thanks for your purchase.\n\nTax invoice: SA-000123\nDate: 11 Oct 2026\n")
            .And.Contain("GST: $13.64\nTotal (incl. GST): $150.00")
            .And.Contain(ContentCatalog.Find(ContentKeys.BuyerFeeExplanation)!.DefaultBody)
            .And.Contain("View or print your tax invoice: https://app.example/tax-documents/44444444-4444-4444-4444-444444444444");
    }

    [Fact]
    public async Task InvoiceDue_WithEditedWording_KeepsTheBankDetailsAndPaymentReference()
    {
        ContentTestData.SetWording(_db, ContentKeys.ListingFeeInvoiceDue, "Invoice {InvoiceNumber}",
            "{Amount} is due by {DueDate}.");

        await Calls[ContentKeys.ListingFeeInvoiceDue](CreateSut());

        _queued.Single().TextBody.Should().StartWith("$990.00 is due by 25 Oct 2026.\n\nTax invoice: SA-000123")
            .And.Contain("Account name: Stallions Australia Pty Ltd\nBSB: 062-000\nAccount number: 12345678")
            .And.Contain("Please use SA-000123 as your payment reference.");
    }

    [Fact]
    public async Task EditedFeeExplanation_IsWhatTheBuyerFeeInvoiceShows()
    {
        ContentTestData.SetWording(_db, ContentKeys.BuyerFeeExplanation, null, "EDITED fee wording.");

        await Calls[ContentKeys.BuyerFeeTaxInvoice](CreateSut());

        _queued.Single().TextBody.Should().Contain("Total (incl. GST): $150.00\n\nEDITED fee wording.\n\nView or print");
    }
}
```

`TaxDocumentEmailsTests.cs` fixture — replace lines 1–25 with:

```csharp
// tests/Server.Tests/TaxDocuments/TaxDocumentEmailsTests.cs (lines 1–25)
// tests/Server.Tests/TaxDocuments/TaxDocumentEmailsTests.cs
using FluentAssertions;
using Moq;
using Stallions.Server.Content;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Email;
using Stallions.Server.TaxDocuments;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.TaxDocuments;

public class TaxDocumentEmailsTests : IDisposable
{
    private readonly AppDbContext _db = DbContextFactory.Create(Guid.NewGuid().ToString());
    private readonly List<(OutgoingEmail Email, string? EntityType, Guid? EntityId)> _queued = new();

    public TaxDocumentEmailsTests()
    {
        _db.Database.EnsureCreated();
        ContentTestData.SetWording(_db, ContentKeys.BuyerFeeExplanation, null, "CONFIGURED fee wording.");
    }

    public void Dispose() => _db.Dispose();

    private TaxDocumentEmails CreateSut()
    {
        var outbox = new Mock<IEmailOutbox>();
        outbox.Setup(o => o.EnqueueAsync(It.IsAny<OutgoingEmail>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .Callback<OutgoingEmail, string?, Guid?>((e, t, id) => _queued.Add((e, t, id)))
            .Returns(Task.CompletedTask);
        return new TaxDocumentEmails(outbox.Object, ContentTestData.Service(_db),
            Microsoft.Extensions.Options.Options.Create(new EmailOptions { PublicBaseUrl = "https://app.example/" }));
    }
```

(`Issued`, `Invoice()` and every test are unchanged.)

- [ ] **Step 2: Run red** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~TaxDocumentEmails"` → compile errors.

- [ ] **Step 3: Implement** — replace `src/Server/TaxDocuments/TaxDocumentEmails.cs`:

```csharp
// src/Server/TaxDocuments/TaxDocumentEmails.cs
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;
using Stallions.Server.Content;
using Stallions.Server.Data.Entities;
using Stallions.Server.Email;
using Stallions.Server.Infrastructure;
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
/// Staff edit each email's subject and message (Staff → Wording); the document details, bank details, payment
/// reference, buyer fee explanation and links are generated here. Amounts inc. GST with the ex-GST and GST split;
/// dates as Sydney dates.
/// </summary>
public class TaxDocumentEmails : ITaxDocumentEmails
{
    private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");

    private readonly IEmailOutbox _outbox;
    private readonly IContentService _content;
    private readonly string _baseUrl;

    public TaxDocumentEmails(IEmailOutbox outbox, IContentService content, IOptions<EmailOptions> email)
    {
        _outbox = outbox;
        _content = content;
        _baseUrl = email.Value.PublicBaseUrl.TrimEnd('/');
    }

    public async Task ListingFeeTaxInvoiceAsync(TaxDocument d) =>
        await QueueAsync(d, "ListingFeeTaxInvoice",
            await RenderAsync(ContentKeys.ListingFeeTaxInvoice, d, ("StudName", d.RecipientName)),
            Details(d),
            Para("Status: Paid"),
            DocumentLink(d, "View or print your tax invoice"));

    public async Task ListingFeeInvoiceDueAsync(TaxDocument d) =>
        await QueueAsync(d, "ListingFeeInvoiceDue",
            await RenderAsync(ContentKeys.ListingFeeInvoiceDue, d, ("StudName", d.RecipientName), ("DueDate", Date(d.DueAt))),
            Details(d),
            Rows(("Account name", d.BankAccountName ?? string.Empty),
                 ("BSB", BusinessNumbers.FormatBsb(d.BankBsb)),
                 ("Account number", d.BankAccountNumber ?? string.Empty)),
            Para($"Please use {d.Number} as your payment reference."),
            DocumentLink(d, "View or print your tax invoice"));

    public async Task ListingFeeReceiptAsync(TaxDocument d) =>
        await QueueAsync(d, "ListingFeeReceipt",
            await RenderAsync(ContentKeys.ListingFeeReceipt, d, ("StudName", d.RecipientName)),
            Details(d),
            Para($"Paid: {Date(d.PaidAt)}"),
            DocumentLink(d, "View or print your tax invoice"));

    public async Task BuyerFeeTaxInvoiceAsync(TaxDocument d)
    {
        var email = await RenderAsync(ContentKeys.BuyerFeeTaxInvoice, d, ("BuyerName", d.RecipientName));
        var blocks = new List<Block> { Details(d) };
        blocks.AddRange((await _content.RenderTextAsync(ContentKeys.BuyerFeeExplanation)).Select(ToBlock));
        blocks.Add(DocumentLink(d, "View or print your tax invoice"));
        await QueueAsync(d, "BuyerFeeTaxInvoice", email, blocks.ToArray());
    }

    public async Task AdjustmentNoteAsync(TaxDocument note, TaxDocument original) =>
        await QueueAsync(note, "AdjustmentNote",
            await RenderAsync(ContentKeys.AdjustmentNote, note,
                ("OriginalNumber", original.Number), ("OriginalDate", Date(original.IssuedAt)), ("Reason", note.Reason)),
            Details(note),
            DocumentLink(note, "View or print your adjustment note"));

    // ── Building blocks ────────────────────────────────────────────────────────

    private sealed record Block(string Html, string Text);

    private Task<RenderedEmail> RenderAsync(string key, TaxDocument d, params (string Name, string? Value)[] values)
    {
        var all = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["InvoiceNumber"] = d.Number,
            ["Description"] = d.Description,
            ["Amount"] = Money(d.AmountIncGst)
        };
        foreach (var (name, value) in values) all[name] = value;
        return _content.RenderEmailAsync(key, all);
    }

    private static Block ToBlock(RenderedParagraph p) => new($"<p>{p.Html}</p>", p.Text);

    /// <summary>The Staff message paragraphs first, then the generated blocks.</summary>
    private async Task QueueAsync(TaxDocument d, string template, RenderedEmail email, params Block[] blocks)
    {
        var all = email.Paragraphs.Select(ToBlock).Concat(blocks).ToList();
        var html = "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\">"
                   + string.Concat(all.Select(b => b.Html))
                   + "<p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>";
        var text = string.Join("\n\n", all.Select(b => b.Text)) + "\n\nStallions Australia";
        var subject = email.Subject.Length > 300 ? email.Subject[..300] : email.Subject;
        await _outbox.EnqueueAsync(new OutgoingEmail(d.RecipientEmail, subject, html, text, template), "TaxDocument", d.Id);
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

Then remove the old configuration:

```bash
git rm src/Server/Options/DisclosureOptions.cs
```

- `src/Server/Program.cs`: delete line 12 (`using Stallions.Server.Options;`) and lines 79–80
  (`// Config options` and `builder.Services.Configure<DisclosureOptions>(builder.Configuration.GetSection(DisclosureOptions.Section));`)
  together with the blank line after them.
- `src/Server/appsettings.json`: delete the whole `"Checkout": { … },` section (lines 18–22), so `"Cors"` is followed by `"Payments"`.
- `src/Server/appsettings.Development.json`: delete the whole `"Checkout": { "WebhookSecret": … },` section (lines 24–26), so `"Cors"`
  is followed by `"Payments"` (the secret was never read).
- `grep -rn "DisclosureOptions\|Stallions.Server.Options\|\"Checkout\"" src tests --include=*.cs --include=*.json` → no matches
  (outside `bin`/`obj`).

- [ ] **Step 4: Run green** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~TaxDocument"` (every existing
  `TaxDocumentEmailsTests`, `TaxDocumentServiceTests`, `TaxDocumentQueryServiceTests` test, and the new wording tests); full build
  (0 warnings) and full test run. Run the API locally once (`dotnet run --project src/Server`) to confirm DI starts without the
  `Checkout` section, then stop it.

- [ ] **Step 5: Commit**

```bash
git add src/Server/TaxDocuments/TaxDocumentEmails.cs src/Server/Program.cs src/Server/appsettings.json \
  src/Server/appsettings.Development.json tests/Server.Tests/TaxDocuments/TaxDocumentEmailsWordingTests.cs \
  tests/Server.Tests/TaxDocuments/TaxDocumentEmailsTests.cs
git status --short
git commit -m "feat: tax document emails use Staff wording; remove DisclosureOptions and the Checkout config

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 8: Stud listing terms — audiences, acceptance, the gate on Activate / Publish / Convert, publish audit

**Files:**
- Create: `src/Shared/DTOs/Terms/StudTermsStatusDto.cs`
- Modify: `src/Shared/DTOs/Terms/TermsDocumentDto.cs`, `src/Shared/DTOs/Terms/PublishTermsRequest.cs`, `src/Shared/DTOs/Admin/StudFarmSummaryDto.cs`
- Modify: `src/Server/Data/Repositories/ITermsRepository.cs`, `src/Server/Data/Repositories/TermsRepository.cs` (whole files)
- Create: `src/Server/Services/StudTermsGate.cs`
- Modify: `src/Server/Services/ITermsService.cs`, `src/Server/Services/TermsService.cs`, `src/Server/Controllers/TermsController.cs` (whole files)
- Modify: `src/Server/Services/SubscriptionService.cs` (constructor lines 35–65; `ActivateAsync` after line 318)
- Modify: `src/Server/Services/ListingService.cs` (fields 14–22; constructor 24–44; `PublishListingAsync` after line 236;
  `ConvertToMakeAnOfferAsync` after line 414)
- Modify: `src/Server/Services/AdminService.cs` (`GetAllStudFarmsAsync` object initializer lines 101–112; `GetStudFarmByIdAsync` `new StudFarmSummaryDto` initializer)
- Test: `tests/Server.Tests/Services/TermsServiceTests.cs` (rewritten on the in-memory DB — the five existing tests keep their names and
  assertions), `tests/Server.Tests/Data/Repositories/TermsRepositoryTests.cs` (new), `tests/Server.Tests/Controllers/TermsControllerTests.cs` (new),
  `tests/Server.Tests/Services/SubscriptionServiceTests.cs` (fixture line 63–77 + new tests), `tests/Server.Tests/Services/ListingServiceTests.cs`
  (fixture lines 15–23 and 40–43 + new tests), `tests/Server.Tests/Services/ListingServiceCardTests.cs` (fixture lines 20–29),
  `tests/Server.Tests/Services/AdminServiceTests.cs` (one new test)

**Interfaces:**
- Consumes: Task 2 `TermsAudience`, `TermsDocument.Audience`, `StudFarm.AcceptedStudTerms*`; existing `IStudFarmRepository`
  (`GetByUserIdAsync`, `UpdateAsync`), `IAuditLogRepository`, `IUserService`, `DbErrors`.
- Produces:
  `ITermsRepository { Task<TermsDocument?> GetCurrentAsync() /* = Buyer */; Task<TermsDocument?> GetCurrentAsync(TermsAudience); Task<IReadOnlyList<TermsDocument>> GetHistoryAsync(TermsAudience); Task<TermsDocument> AddAsync(TermsDocument) }`;
  `sealed class StudTermsGate(ITermsRepository) { const string NotAcceptedMessage = "Accept the current stud listing terms first."; Task<bool> IsSatisfiedAsync(StudFarm) }`;
  `ITermsService { GetCurrentAsync(); PublishAsync(PublishTermsRequest); GetHistoryAsync(string? audience); GetStudStatusAsync(); AcceptStudTermsAsync(AcceptTermsRequest) }`
  (`ServiceResult<TermsDocumentDto>` / `<IReadOnlyList<TermsDocumentDto>>` / `<StudTermsStatusDto>`);
  `TermsService(ITermsRepository repo, IUserService users, IStudFarmRepository farms, IAuditLogRepository audit, TimeProvider clock)`;
  `TermsService.PublishAuditAction = "PublishTerms"` (entity `TermsDocument`, details `{ Audience, Version, PreviousVersion }`),
  `TermsService.AcceptStudAuditAction = "AcceptStudTerms"` (entity `StudFarm`, details `{ Version, StudFarmName }`);
  `SubscriptionService(…, ITransactionRunner transactions, ITermsRepository terms, TimeProvider clock, ILogger<…> log)`;
  `ListingService(…, IOfferExpirer offerExpirer, ITermsRepository terms)`;
  routes: `GET api/terms/current` (unchanged, buyer), `POST api/terms` (StaffOnly; `Audience` in the body, default Buyer),
  `GET api/terms/history?audience=Buyer|Stud` (StaffOnly), `GET api/terms/stud/current` (StudFarmOrStaff → `StudTermsStatusDto`),
  `POST api/terms/stud/accept` (StudFarmAdminOnly, body `AcceptTermsRequest { Version }` → `StudTermsStatusDto`);
  DTO additions: `TermsDocumentDto.Audience (string)`, `.CreatedByName (string?)`; `PublishTermsRequest.Audience (string?)`;
  `StudTermsStatusDto { TermsDocumentDto? Current; int? AcceptedVersion; DateTime? AcceptedAt; bool NeedsAcceptance }`;
  `StudFarmSummaryDto.AcceptedStudTermsVersion (int?)`, `.AcceptedStudTermsAt (DateTime?)`.

- [ ] **Step 1: Write the failing tests** — replace `TermsServiceTests.cs`:

```csharp
// tests/Server.Tests/Services/TermsServiceTests.cs
using System.Text.Json;
using FluentAssertions;
using Moq;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.DTOs.Terms;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class TermsServiceTests : IDisposable
{
    private readonly AppDbContext _db = DbContextFactory.Create(Guid.NewGuid().ToString());
    private readonly Mock<IUserService> _users = new();
    private readonly TestClock _clock = new();
    private readonly User _staff = NewUser(UserRole.Staff, "staff", "Sam Staff");
    private readonly User _stud = NewUser(UserRole.StudFarmAdmin, "stud", "Sally Stud");
    private readonly User _buyer = NewUser(UserRole.Buyer, "buyer", "Bob Buyer");
    private readonly StudFarm _farm;

    public TermsServiceTests()
    {
        _farm = new StudFarm { UserId = _stud.Id, Name = "Arrowfield" };
        _db.AddRange(_staff, _stud, _buyer, _farm);
        _db.SaveChanges();
        SignIn(_staff);
    }

    public void Dispose() => _db.Dispose();

    private static User NewUser(UserRole role, string id, string name) => new()
    {
        Id = Guid.NewGuid(), ObjectId = id, Email = $"{id}@example.com", DisplayName = name, Role = role, Status = UserStatus.Active
    };

    private static User Staff() => NewUser(UserRole.Staff, Guid.NewGuid().ToString(), "Another Staff");

    private void SignIn(User user) => _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(user);

    private TermsService CreateSut() =>
        new(new TermsRepository(_db), _users.Object, new StudFarmRepository(_db), new AuditLogRepository(_db), _clock);

    private TermsDocument AddTerms(TermsAudience audience, int version, string body = "Terms")
    {
        var doc = new TermsDocument
        {
            Audience = audience, Version = version, Body = body, CreatedByUserId = _staff.Id, CreatedAt = _clock.UtcNow.AddDays(version)
        };
        _db.TermsDocuments.Add(doc);
        _db.SaveChanges();
        return doc;
    }

    // ── Buyer T&C (existing behaviour) ──────────────────────────────────────

    [Fact]
    public async Task GetCurrent_WhenNonePublished_ReturnsNotFound()
    {
        var result = await CreateSut().GetCurrentAsync();
        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetCurrent_WhenPublished_ReturnsDto()
    {
        AddTerms(TermsAudience.Buyer, 3, "Hi");
        var result = await CreateSut().GetCurrentAsync();
        result.Succeeded.Should().BeTrue();
        result.Value!.Version.Should().Be(3);
        result.Value.Body.Should().Be("Hi");
        result.Value.Audience.Should().Be("Buyer");
    }

    [Fact]
    public async Task GetCurrent_IgnoresStudTerms()
    {
        AddTerms(TermsAudience.Buyer, 1, "Buyer terms");
        AddTerms(TermsAudience.Stud, 5, "Stud terms");

        (await CreateSut().GetCurrentAsync()).Value!.Body.Should().Be("Buyer terms");
    }

    [Fact]
    public async Task Publish_WhenNoExisting_CreatesVersion1()
    {
        var result = await CreateSut().PublishAsync(new PublishTermsRequest { Body = "Terms v1" });

        result.Succeeded.Should().BeTrue();
        var added = _db.TermsDocuments.Single();
        added.Version.Should().Be(1);
        added.Body.Should().Be("Terms v1");
        added.Audience.Should().Be(TermsAudience.Buyer);
    }

    [Fact]
    public async Task Publish_WhenExistingV2_CreatesVersion3()
    {
        AddTerms(TermsAudience.Buyer, 2, "old");

        var result = await CreateSut().PublishAsync(new PublishTermsRequest { Body = "Terms v3" });

        result.Succeeded.Should().BeTrue();
        result.Value!.Version.Should().Be(3);
        result.Value.CreatedByName.Should().Be("Sam Staff");
    }

    [Fact]
    public async Task Publish_WhenBodyEmpty_ReturnsBadRequest()
    {
        SignIn(Staff());
        var result = await CreateSut().PublishAsync(new PublishTermsRequest { Body = "   " });
        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    // ── Audiences, Staff re-check, audit ─────────────────────────────────────

    [Theory]
    [InlineData(UserRole.Buyer)]
    [InlineData(UserRole.StudFarmAdmin)]
    public async Task PublishAndHistory_ByAnyoneButStaff_AreForbidden(UserRole role)
    {
        SignIn(NewUser(role, "someone", "Someone"));

        (await CreateSut().PublishAsync(new PublishTermsRequest { Body = "x", Audience = "Stud" })).HttpStatusCode.Should().Be(403);
        (await CreateSut().GetHistoryAsync("Stud")).HttpStatusCode.Should().Be(403);
        _db.TermsDocuments.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_NumbersEachAudienceSeparately()
    {
        AddTerms(TermsAudience.Buyer, 2);

        var result = await CreateSut().PublishAsync(new PublishTermsRequest { Body = "Stud terms v1", Audience = "Stud" });

        result.Value!.Version.Should().Be(1);
        result.Value.Audience.Should().Be("Stud");
        _db.TermsDocuments.Single(t => t.Audience == TermsAudience.Stud).Version.Should().Be(1);
    }

    [Fact]
    public async Task Publish_AnUnknownAudience_IsBadRequest() =>
        (await CreateSut().PublishAsync(new PublishTermsRequest { Body = "x", Audience = "Vets" })).HttpStatusCode.Should().Be(400);

    [Fact]
    public async Task Publish_IsAudited()
    {
        AddTerms(TermsAudience.Stud, 1);

        var result = await CreateSut().PublishAsync(new PublishTermsRequest { Body = "Stud terms v2", Audience = "stud" });

        var audit = _db.AuditLogs.Single(a => a.Action == TermsService.PublishAuditAction);
        audit.EntityType.Should().Be("TermsDocument");
        audit.EntityId.Should().Be(result.Value!.Id);
        audit.UserId.Should().Be(_staff.Id);
        using var json = JsonDocument.Parse(audit.Details!);
        json.RootElement.GetProperty("Audience").GetString().Should().Be("Stud");
        json.RootElement.GetProperty("Version").GetInt32().Should().Be(2);
        json.RootElement.GetProperty("PreviousVersion").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task History_ForAnAudience_IsNewestFirst_WithThePublisher()
    {
        AddTerms(TermsAudience.Stud, 1, "one");
        AddTerms(TermsAudience.Stud, 2, "two");
        AddTerms(TermsAudience.Buyer, 7, "buyer");

        var history = (await CreateSut().GetHistoryAsync("Stud")).Value!;

        history.Select(h => h.Version).Should().Equal(2, 1);
        history[0].Body.Should().Be("two");
        history[0].CreatedByName.Should().Be("Sam Staff");
        (await CreateSut().GetHistoryAsync(null)).Value!.Select(h => h.Version).Should().Equal(7);
    }

    // ── Stud acceptance ─────────────────────────────────────────────────────

    [Fact]
    public async Task StudStatus_WithNothingPublished_NeedsNothing()
    {
        SignIn(_stud);

        var status = (await CreateSut().GetStudStatusAsync()).Value!;

        status.Current.Should().BeNull();
        status.NeedsAcceptance.Should().BeFalse();
    }

    [Fact]
    public async Task StudStatus_NotYetAccepted_NeedsAcceptance()
    {
        AddTerms(TermsAudience.Stud, 2, "Stud terms");
        _farm.AcceptedStudTermsVersion = 1;
        _db.SaveChanges();
        SignIn(_stud);

        var status = (await CreateSut().GetStudStatusAsync()).Value!;

        status.Current!.Version.Should().Be(2);
        status.Current.Body.Should().Be("Stud terms");
        status.AcceptedVersion.Should().Be(1);
        status.NeedsAcceptance.Should().BeTrue();
    }

    [Fact]
    public async Task StudStatus_ForStaff_ShowsTheCurrentTerms_WithoutNeedingAcceptance()
    {
        AddTerms(TermsAudience.Stud, 1);

        var status = (await CreateSut().GetStudStatusAsync()).Value!;

        status.Current!.Version.Should().Be(1);
        status.NeedsAcceptance.Should().BeFalse();
    }

    [Fact]
    public async Task StudStatus_ForABuyer_IsForbidden()
    {
        SignIn(_buyer);
        (await CreateSut().GetStudStatusAsync()).HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task AcceptStud_RecordsTheVersionOnTheFarm_AndAudits()
    {
        AddTerms(TermsAudience.Stud, 2);
        SignIn(_stud);

        var result = await CreateSut().AcceptStudTermsAsync(new AcceptTermsRequest { Version = 2 });

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.NeedsAcceptance.Should().BeFalse();
        var farm = _db.StudFarms.Single(f => f.Id == _farm.Id);
        farm.AcceptedStudTermsVersion.Should().Be(2);
        farm.AcceptedStudTermsAt.Should().Be(_clock.UtcNow);
        farm.AcceptedStudTermsByUserId.Should().Be(_stud.Id);
        var audit = _db.AuditLogs.Single(a => a.Action == TermsService.AcceptStudAuditAction);
        audit.EntityType.Should().Be("StudFarm");
        audit.EntityId.Should().Be(_farm.Id);
        using var json = JsonDocument.Parse(audit.Details!);
        json.RootElement.GetProperty("Version").GetInt32().Should().Be(2);
        json.RootElement.GetProperty("StudFarmName").GetString().Should().Be("Arrowfield");
    }

    [Fact]
    public async Task AcceptStud_AnOlderVersion_IsRejected()
    {
        AddTerms(TermsAudience.Stud, 1);
        AddTerms(TermsAudience.Stud, 2);
        SignIn(_stud);

        var result = await CreateSut().AcceptStudTermsAsync(new AcceptTermsRequest { Version = 1 });

        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Be("The stud listing terms have changed. Please review the latest version.");
        _db.StudFarms.Single(f => f.Id == _farm.Id).AcceptedStudTermsVersion.Should().BeNull();
    }

    [Fact]
    public async Task AcceptStud_WithNothingPublished_IsBadRequest()
    {
        SignIn(_stud);
        (await CreateSut().AcceptStudTermsAsync(new AcceptTermsRequest { Version = 1 })).HttpStatusCode.Should().Be(400);
    }

    [Theory]
    [InlineData(UserRole.Buyer)]
    [InlineData(UserRole.Staff)]
    public async Task AcceptStud_ByAnyoneButAStudAdmin_IsForbidden(UserRole role)
    {
        AddTerms(TermsAudience.Stud, 1);
        SignIn(NewUser(role, "other", "Other"));

        (await CreateSut().AcceptStudTermsAsync(new AcceptTermsRequest { Version = 1 })).HttpStatusCode.Should().Be(403);
    }
}
```

```csharp
// tests/Server.Tests/Data/Repositories/TermsRepositoryTests.cs
using FluentAssertions;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data.Repositories;

public class TermsRepositoryTests
{
    [Fact]
    public async Task Current_IsPerAudience_AndTheParameterlessOneIsTheBuyerTerms()
    {
        using var db = DbContextFactory.Create(Guid.NewGuid().ToString());
        db.TermsDocuments.AddRange(
            new TermsDocument { Audience = TermsAudience.Buyer, Version = 1, Body = "B1" },
            new TermsDocument { Audience = TermsAudience.Buyer, Version = 2, Body = "B2" },
            new TermsDocument { Audience = TermsAudience.Stud, Version = 3, Body = "S3" });
        await db.SaveChangesAsync();
        var repo = new TermsRepository(db);

        (await repo.GetCurrentAsync())!.Body.Should().Be("B2");
        (await repo.GetCurrentAsync(TermsAudience.Buyer))!.Body.Should().Be("B2");
        (await repo.GetCurrentAsync(TermsAudience.Stud))!.Body.Should().Be("S3");
        (await repo.GetHistoryAsync(TermsAudience.Buyer)).Select(t => t.Version).Should().Equal(2, 1);
        (await repo.GetHistoryAsync(TermsAudience.Stud)).Select(t => t.Version).Should().Equal(3);
    }
}
```

```csharp
// tests/Server.Tests/Controllers/TermsControllerTests.cs
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Stallions.Server.Controllers;

namespace Stallions.Server.Tests.Controllers;

public class TermsControllerTests
{
    private static string? Policy(string method) =>
        typeof(TermsController).GetMethod(method)!.GetCustomAttribute<AuthorizeAttribute>()?.Policy;

    [Fact]
    public void EachEndpoint_HasTheRightRole()
    {
        Policy(nameof(TermsController.Publish)).Should().Be("StaffOnly");
        Policy(nameof(TermsController.GetHistory)).Should().Be("StaffOnly");
        Policy(nameof(TermsController.GetStudStatus)).Should().Be("StudFarmOrStaff");
        Policy(nameof(TermsController.AcceptStud)).Should().Be("StudFarmAdminOnly");
        typeof(TermsController).GetMethod(nameof(TermsController.GetCurrent))!.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
    }
}
```

`SubscriptionServiceTests` — in `CreateSut` (lines 63–77) insert `new TermsRepository(_db),` after `new InlineTransactionRunner(),`.
Add after the self-serve activation tests:

```csharp
// tests/Server.Tests/Services/SubscriptionServiceTests.cs (new tests)
    // ── Stud listing terms gate ─────────────────────────────────────────────

    private void PublishStudTerms(int version)
    {
        _db.TermsDocuments.Add(new TermsDocument { Audience = TermsAudience.Stud, Version = version, Body = "Stud terms", CreatedByUserId = _staff.Id });
        _db.SaveChanges();
    }

    private void Accept(StudFarm farm, int version)
    {
        farm.AcceptedStudTermsVersion = version;
        farm.AcceptedStudTermsAt = _clock.UtcNow;
        _db.SaveChanges();
    }

    [Fact]
    public async Task Activate_WithoutAcceptingTheStudTerms_IsRejected_AndCreatesNothing()
    {
        PublishStudTerms(1);
        PaymentPageReturns("https://pay/fee");

        var result = await ActivateAs(_studUserA, _stallionA);

        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Be("Accept the current stud listing terms first.");
        _db.StallionSeasonSubscriptions.Should().BeEmpty();
        _provider.Verify(p => p.CreateListingFeeSessionAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Activate_AfterAcceptingTheCurrentStudTerms_StartsThePayment()
    {
        PublishStudTerms(1);
        Accept(_farmA, 1);
        PaymentPageReturns("https://pay/fee");

        var result = await ActivateAs(_studUserA, _stallionA);

        result.Value!.Url.Should().Be("https://pay/fee");
    }

    [Fact]
    public async Task Activate_WhenNewerStudTermsArePublished_NeedsThemAccepted()
    {
        PublishStudTerms(1);
        Accept(_farmA, 1);
        PublishStudTerms(2);
        PaymentPageReturns("https://pay/fee");

        var result = await ActivateAs(_studUserA, _stallionA);

        result.Error.Should().Be("Accept the current stud listing terms first.");
    }

    [Fact]
    public async Task StaffCreate_ForAFarmThatHasNotAcceptedTheStudTerms_IsNotGated()
    {
        PublishStudTerms(1);

        var dto = await CreatePendingAsync(_stallionA);

        dto.Status.Should().Be("Pending");
    }
```

`ListingServiceTests` — add the field `private readonly Mock<ITermsRepository> _termsMock = new();` after line 23, and pass
`_termsMock.Object` as the last `CreateSut` argument (after `_offerExpirerMock.Object`). Add (before the "Auction rules" section):

```csharp
// tests/Server.Tests/Services/ListingServiceTests.cs (new tests)
    // ── Stud listing terms gate ─────────────────────────────────────────────

    private void StudTermsPublished(int version) =>
        _termsMock.Setup(t => t.GetCurrentAsync(TermsAudience.Stud))
            .ReturnsAsync(new TermsDocument { Audience = TermsAudience.Stud, Version = version, Body = "Stud terms" });

    [Fact]
    public async Task PublishListing_WithoutAcceptingTheStudTerms_IsRejected()
    {
        var (_, farm) = SignedInFarm();
        var listing = DraftAuction(farm.Id);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        StudTermsPublished(2);
        farm.AcceptedStudTermsVersion = 1;

        var result = await CreateSut().PublishListingAsync(listing.Id);

        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Be("Accept the current stud listing terms first.");
        listing.Status.Should().Be(ListingStatus.Draft);
        _listingRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Listing>()), Times.Never);
    }

    [Fact]
    public async Task PublishListing_AfterAcceptingTheCurrentStudTerms_Publishes()
    {
        var (_, farm) = SignedInFarm();
        var listing = DraftAuction(farm.Id);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        StudTermsPublished(2);
        farm.AcceptedStudTermsVersion = 2;

        var result = await CreateSut().PublishListingAsync(listing.Id);

        result.Succeeded.Should().BeTrue(result.Error);
        listing.Status.Should().Be(ListingStatus.Active);
    }

    [Fact]
    public async Task Convert_WithoutAcceptingTheStudTerms_IsRejected()
    {
        var (_, farm) = SignedInFarm();
        var auction = ConvertibleAuction(farm.Id);
        StudTermsPublished(1);

        var result = await CreateSut().ConvertToMakeAnOfferAsync(auction.Id);

        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Be("Accept the current stud listing terms first.");
        _listingRepoMock.Verify(r => r.AddAsync(It.IsAny<Listing>()), Times.Never);
    }
```

`ListingServiceCardTests` — `CreateSut` (lines 20–29): add `new Mock<ITermsRepository>().Object` as the last argument.

`AdminServiceTests` — add:

```csharp
// tests/Server.Tests/Services/AdminServiceTests.cs (new test)
    [Fact]
    public async Task GetAllStudFarmsAsync_ShowsEachFarmsStudTermsAcceptance()
    {
        var at = new DateTime(2026, 10, 12, 2, 0, 0, DateTimeKind.Utc);
        _studFarmRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<StudFarm>
        {
            new() { Id = Guid.NewGuid(), Name = "Arrowfield", AcceptedStudTermsVersion = 2, AcceptedStudTermsAt = at },
            new() { Id = Guid.NewGuid(), Name = "Coolmore" }
        });

        var farms = (await CreateSut().GetAllStudFarmsAsync()).Value!;

        farms[0].AcceptedStudTermsVersion.Should().Be(2);
        farms[0].AcceptedStudTermsAt.Should().Be(at);
        farms[1].AcceptedStudTermsVersion.Should().BeNull();
        farms[1].AcceptedStudTermsAt.Should().BeNull();
    }
```

- [ ] **Step 2: Run red** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~Terms|FullyQualifiedName~SubscriptionServiceTests|FullyQualifiedName~ListingService|FullyQualifiedName~AdminServiceTests"` → compile errors.

- [ ] **Step 3: DTOs**

```csharp
// src/Shared/DTOs/Terms/TermsDocumentDto.cs
namespace Stallions.Shared.DTOs.Terms;

public class TermsDocumentDto
{
    public Guid Id { get; set; }
    /// <summary>"Buyer" (T&amp;C) or "Stud" (stud listing terms).</summary>
    public string Audience { get; set; } = "Buyer";
    public int Version { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? CreatedByName { get; set; }
}
```

```csharp
// src/Shared/DTOs/Terms/PublishTermsRequest.cs
namespace Stallions.Shared.DTOs.Terms;

public class PublishTermsRequest
{
    public string Body { get; set; } = string.Empty;
    /// <summary>"Buyer" (default when empty) or "Stud".</summary>
    public string? Audience { get; set; }
}
```

```csharp
// src/Shared/DTOs/Terms/StudTermsStatusDto.cs
namespace Stallions.Shared.DTOs.Terms;

/// <summary>The current stud listing terms and whether the caller's farm still has to accept them.</summary>
public class StudTermsStatusDto
{
    /// <summary>Null when no stud listing terms are published (then nothing is gated).</summary>
    public TermsDocumentDto? Current { get; set; }
    public int? AcceptedVersion { get; set; }
    public DateTime? AcceptedAt { get; set; }
    /// <summary>True for a stud admin whose farm hasn't accepted the current version; always false for Staff.</summary>
    public bool NeedsAcceptance { get; set; }
}
```

`src/Shared/DTOs/Admin/StudFarmSummaryDto.cs` — after `public DateTime CreatedAt { get; set; }` add:

```csharp
// src/Shared/DTOs/Admin/StudFarmSummaryDto.cs (new properties)
    /// <summary>The stud listing terms version this farm last accepted (null = never).</summary>
    public int? AcceptedStudTermsVersion { get; set; }
    public DateTime? AcceptedStudTermsAt { get; set; }
```

- [ ] **Step 4: Repository and gate**

```csharp
// src/Server/Data/Repositories/ITermsRepository.cs
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public interface ITermsRepository
{
    /// <summary>The current buyer T&amp;C (highest buyer version), or null if none — used by the buyer gates.</summary>
    Task<TermsDocument?> GetCurrentAsync();
    /// <summary>The current document for an audience (highest version), or null if none is published.</summary>
    Task<TermsDocument?> GetCurrentAsync(TermsAudience audience);
    /// <summary>Every version for an audience, newest first, with its publisher.</summary>
    Task<IReadOnlyList<TermsDocument>> GetHistoryAsync(TermsAudience audience);
    Task<TermsDocument> AddAsync(TermsDocument document);
}
```

```csharp
// src/Server/Data/Repositories/TermsRepository.cs
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public class TermsRepository : ITermsRepository
{
    private readonly AppDbContext _db;
    public TermsRepository(AppDbContext db) => _db = db;

    public Task<TermsDocument?> GetCurrentAsync() => GetCurrentAsync(TermsAudience.Buyer);

    public async Task<TermsDocument?> GetCurrentAsync(TermsAudience audience) =>
        await _db.TermsDocuments
            .Where(t => t.Audience == audience)
            .OrderByDescending(t => t.Version)
            .FirstOrDefaultAsync();

    public async Task<IReadOnlyList<TermsDocument>> GetHistoryAsync(TermsAudience audience) =>
        await _db.TermsDocuments
            .Include(t => t.CreatedBy)
            .Where(t => t.Audience == audience)
            .OrderByDescending(t => t.Version)
            .ToListAsync();

    public async Task<TermsDocument> AddAsync(TermsDocument document)
    {
        _db.TermsDocuments.Add(document);
        await _db.SaveChangesAsync();
        return document;
    }
}
```

```csharp
// src/Server/Services/StudTermsGate.cs
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

/// <summary>
/// A stud must accept the current stud listing terms before it pays a listing fee (Activate), publishes a listing or
/// converts an unsold lot to Make an Offer. No stud terms published = no gate. Staff actions never use this gate.
/// </summary>
public sealed class StudTermsGate(ITermsRepository terms)
{
    public const string NotAcceptedMessage = "Accept the current stud listing terms first.";

    public async Task<bool> IsSatisfiedAsync(StudFarm farm)
    {
        var current = await terms.GetCurrentAsync(TermsAudience.Stud);
        return current == null || farm.AcceptedStudTermsVersion == current.Version;
    }
}
```

- [ ] **Step 5: Service and controller**

```csharp
// src/Server/Services/ITermsService.cs
using Stallions.Shared.DTOs.Terms;

namespace Stallions.Server.Services;

public interface ITermsService
{
    /// <summary>The current buyer T&amp;C.</summary>
    Task<ServiceResult<TermsDocumentDto>> GetCurrentAsync();
    /// <summary>Staff only: publishes the next version for the request's audience (Buyer by default). Audited.</summary>
    Task<ServiceResult<TermsDocumentDto>> PublishAsync(PublishTermsRequest request);
    /// <summary>Staff only: every version for an audience ("Buyer" when null), newest first.</summary>
    Task<ServiceResult<IReadOnlyList<TermsDocumentDto>>> GetHistoryAsync(string? audience);
    /// <summary>Stud admins and Staff: the current stud listing terms and the caller farm's acceptance.</summary>
    Task<ServiceResult<StudTermsStatusDto>> GetStudStatusAsync();
    /// <summary>Stud admins: accepts the current stud listing terms for their farm. Audited.</summary>
    Task<ServiceResult<StudTermsStatusDto>> AcceptStudTermsAsync(AcceptTermsRequest request);
}
```

```csharp
// src/Server/Services/TermsService.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.Terms;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public class TermsService : ITermsService
{
    public const string PublishAuditAction = "PublishTerms";
    public const string AcceptStudAuditAction = "AcceptStudTerms";
    private const string StaffOnlyMessage = "Only Stallions Australia staff can manage terms.";

    private readonly ITermsRepository _repo;
    private readonly IUserService _users;
    private readonly IStudFarmRepository _farms;
    private readonly IAuditLogRepository _audit;
    private readonly TimeProvider _clock;

    public TermsService(ITermsRepository repo, IUserService users, IStudFarmRepository farms, IAuditLogRepository audit,
        TimeProvider clock)
    {
        _repo = repo;
        _users = users;
        _farms = farms;
        _audit = audit;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<ServiceResult<TermsDocumentDto>> GetCurrentAsync()
    {
        var current = await _repo.GetCurrentAsync(TermsAudience.Buyer);
        return current == null
            ? ServiceResult<TermsDocumentDto>.NotFound("No Terms & Conditions have been published yet.")
            : ServiceResult<TermsDocumentDto>.Ok(MapToDto(current));
    }

    public async Task<ServiceResult<TermsDocumentDto>> PublishAsync(PublishTermsRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.Staff)
            return ServiceResult<TermsDocumentDto>.Forbidden(StaffOnlyMessage);
        if (!TryAudience(request.Audience, out var audience))
            return ServiceResult<TermsDocumentDto>.BadRequest("Choose Buyer or Stud terms.");
        if (string.IsNullOrWhiteSpace(request.Body))
            return ServiceResult<TermsDocumentDto>.BadRequest("Terms body cannot be empty.");

        var current = await _repo.GetCurrentAsync(audience);
        TermsDocument created;
        try
        {
            created = await _repo.AddAsync(new TermsDocument
            {
                Audience = audience,
                Version = (current?.Version ?? 0) + 1,
                Body = request.Body.Trim(),
                CreatedByUserId = caller.Id,
                CreatedAt = Now
            });
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return ServiceResult<TermsDocumentDto>.Conflict("Another version was published at the same moment. Reload and try again.");
        }

        await _audit.LogAsync("TermsDocument", created.Id, PublishAuditAction, caller.Id, JsonSerializer.Serialize(new
        {
            Audience = audience.ToString(),
            created.Version,
            PreviousVersion = current?.Version
        }));
        created.CreatedBy = caller;
        return ServiceResult<TermsDocumentDto>.Created(MapToDto(created));
    }

    public async Task<ServiceResult<IReadOnlyList<TermsDocumentDto>>> GetHistoryAsync(string? audience)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.Staff)
            return ServiceResult<IReadOnlyList<TermsDocumentDto>>.Forbidden(StaffOnlyMessage);
        if (!TryAudience(audience, out var parsed))
            return ServiceResult<IReadOnlyList<TermsDocumentDto>>.BadRequest("Choose Buyer or Stud terms.");

        var docs = await _repo.GetHistoryAsync(parsed);
        return ServiceResult<IReadOnlyList<TermsDocumentDto>>.Ok(docs.Select(MapToDto).ToList());
    }

    public async Task<ServiceResult<StudTermsStatusDto>> GetStudStatusAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role is not (UserRole.StudFarmAdmin or UserRole.Staff))
            return ServiceResult<StudTermsStatusDto>.Forbidden("Only stud farm admins and staff can see the stud listing terms.");

        var current = await _repo.GetCurrentAsync(TermsAudience.Stud);
        if (caller.Role == UserRole.Staff)
            return ServiceResult<StudTermsStatusDto>.Ok(Status(current, farm: null));

        var farm = await _farms.GetByUserIdAsync(caller.Id);
        if (farm == null) return ServiceResult<StudTermsStatusDto>.Forbidden("No stud farm found for the current user.");
        return ServiceResult<StudTermsStatusDto>.Ok(Status(current, farm));
    }

    public async Task<ServiceResult<StudTermsStatusDto>> AcceptStudTermsAsync(AcceptTermsRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.StudFarmAdmin)
            return ServiceResult<StudTermsStatusDto>.Forbidden("Only stud farm admins can accept the stud listing terms.");
        var farm = await _farms.GetByUserIdAsync(caller.Id);
        if (farm == null) return ServiceResult<StudTermsStatusDto>.Forbidden("No stud farm found for the current user.");

        var current = await _repo.GetCurrentAsync(TermsAudience.Stud);
        if (current == null)
            return ServiceResult<StudTermsStatusDto>.BadRequest("No stud listing terms are currently published.");
        if (request.Version != current.Version)
            return ServiceResult<StudTermsStatusDto>.BadRequest("The stud listing terms have changed. Please review the latest version.");

        farm.AcceptedStudTermsVersion = current.Version;
        farm.AcceptedStudTermsAt = Now;
        farm.AcceptedStudTermsByUserId = caller.Id;
        await _farms.UpdateAsync(farm);
        await _audit.LogAsync("StudFarm", farm.Id, AcceptStudAuditAction, caller.Id,
            JsonSerializer.Serialize(new { current.Version, StudFarmName = farm.Name }));
        return ServiceResult<StudTermsStatusDto>.Ok(Status(current, farm));
    }

    private static StudTermsStatusDto Status(TermsDocument? current, StudFarm? farm) => new()
    {
        Current = current == null ? null : MapToDto(current),
        AcceptedVersion = farm?.AcceptedStudTermsVersion,
        AcceptedAt = farm?.AcceptedStudTermsAt,
        NeedsAcceptance = farm != null && current != null && farm.AcceptedStudTermsVersion != current.Version
    };

    /// <summary>Empty = Buyer; otherwise "Buyer" or "Stud", any case.</summary>
    private static bool TryAudience(string? value, out TermsAudience audience)
    {
        audience = TermsAudience.Buyer;
        if (string.IsNullOrWhiteSpace(value)) return true;
        return Enum.TryParse(value.Trim(), ignoreCase: true, out audience)
               && Enum.IsDefined(audience)
               && !char.IsDigit(value.Trim()[0]);
    }

    private static TermsDocumentDto MapToDto(TermsDocument t) => new()
    {
        Id = t.Id,
        Audience = t.Audience.ToString(),
        Version = t.Version,
        Body = t.Body,
        CreatedAt = t.CreatedAt,
        CreatedByName = t.CreatedBy?.DisplayName
    };
}
```

```csharp
// src/Server/Controllers/TermsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Terms;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/terms")]
public class TermsController : ControllerBase
{
    private readonly ITermsService _terms;
    public TermsController(ITermsService terms) => _terms = terms;

    // Any authenticated user can read the current buyer T&C (buyers need it to accept).
    [HttpGet("current")]
    [Authorize]
    public async Task<IActionResult> GetCurrent()
    {
        var r = await _terms.GetCurrentAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> Publish([FromBody] PublishTermsRequest request)
    {
        var r = await _terms.PublishAsync(request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("history")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> GetHistory([FromQuery] string? audience)
    {
        var r = await _terms.GetHistoryAsync(audience);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    // The stud's own acceptance status (Staff see the current terms only).
    [HttpGet("stud/current")]
    [Authorize(Policy = "StudFarmOrStaff")]
    public async Task<IActionResult> GetStudStatus()
    {
        var r = await _terms.GetStudStatusAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("stud/accept")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> AcceptStud([FromBody] AcceptTermsRequest request)
    {
        var r = await _terms.AcceptStudTermsAsync(request);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
}
```

`TermsService` now takes `TimeProvider` and `IStudFarmRepository` — both are already registered (`AddSingleton(TimeProvider.System)`,
`IStudFarmRepository`), so `Program.cs` needs no change.

- [ ] **Step 6: The gate in the stud paths**

`src/Server/Services/SubscriptionService.cs` — add a field `private readonly StudTermsGate _studTerms;` after `_transactions` (line 31);
in the constructor parameter list insert `ITermsRepository terms,` after `ITransactionRunner transactions,` (line 47) and in its body
add `_studTerms = new StudTermsGate(terms);` after `_transactions = transactions;`. In `ActivateAsync`, after the farm check (line 318,
`return ServiceResult<PaymentRedirectDto>.Forbidden("No stud farm found for the current user.");`) add:

```csharp
// src/Server/Services/SubscriptionService.cs (ActivateAsync, after the farm check)
        // Self-serve listing-fee payment needs the current stud listing terms accepted (none published = no gate).
        // Staff create / mark paid / waive never pass through here.
        if (!await _studTerms.IsSatisfiedAsync(farm))
            return ServiceResult<PaymentRedirectDto>.BadRequest(StudTermsGate.NotAcceptedMessage);
```

`src/Server/Services/ListingService.cs` — add the field `private readonly StudTermsGate _studTerms;` after `_offerExpirer` (line 22);
add `ITermsRepository terms` as the last constructor parameter (after `IOfferExpirer offerExpirer`) and `_studTerms = new StudTermsGate(terms);`
at the end of the constructor body. In `PublishListingAsync`, after the auction end-date check (line 236,
`return ServiceResult.BadRequest("Auction end date must be in the future.");`) add:

```csharp
// src/Server/Services/ListingService.cs (PublishListingAsync, before the listing-fee check)
        // Publishing needs the current stud listing terms accepted (none published = no gate).
        if (!await _studTerms.IsSatisfiedAsync(farm))
            return ServiceResult.BadRequest(StudTermsGate.NotAcceptedMessage);
```

In `ConvertToMakeAnOfferAsync`, after the Unsold check (line 414,
`return ServiceResult<ListingDto>.BadRequest("Only an auction lot that closed unsold can be offered as Make an Offer.");`) add:

```csharp
// src/Server/Services/ListingService.cs (ConvertToMakeAnOfferAsync, after the Unsold check)
        // Converting creates a live listing, so it is a publish: same stud terms rule.
        if (!await _studTerms.IsSatisfiedAsync(farm))
            return ServiceResult<ListingDto>.BadRequest(StudTermsGate.NotAcceptedMessage);
```

`src/Server/Services/AdminService.cs` — in `GetAllStudFarmsAsync` add to the `new StudFarmSummaryDto { … }` initializer (after
`CreatedAt = f.CreatedAt`):

```csharp
// src/Server/Services/AdminService.cs (GetAllStudFarmsAsync initializer)
            CreatedAt = f.CreatedAt,
            AcceptedStudTermsVersion = f.AcceptedStudTermsVersion,
            AcceptedStudTermsAt = f.AcceptedStudTermsAt
```

and the same two lines (`AcceptedStudTermsVersion = farm.AcceptedStudTermsVersion, AcceptedStudTermsAt = farm.AcceptedStudTermsAt`)
in the `var dto = new StudFarmSummaryDto { … }` initializer of `GetStudFarmByIdAsync`.

Check: `grep -rn "Status = ListingStatus.Active" src/Server --include=*.cs` → only `PublishListingAsync` and `ConvertToMakeAnOfferAsync`
(both gated) and Staff `AdminService.ForceListingStatus` paths (not gated by design).

- [ ] **Step 7: Run green** — the filter from Step 2 (every existing `SubscriptionServiceTests`, `ListingServiceTests`,
  `ListingServiceCardTests`, `AdminServiceTests`, `UserServiceTermsTests`, `BuyerEligibilityTests`, `BidServiceTests`,
  `OfferServiceTests` test unchanged); full build (0 warnings) and full test run.

- [ ] **Step 8: Commit**

```bash
git add src/Shared/DTOs/Terms src/Shared/DTOs/Admin/StudFarmSummaryDto.cs src/Server/Data/Repositories/ITermsRepository.cs \
  src/Server/Data/Repositories/TermsRepository.cs src/Server/Services/StudTermsGate.cs src/Server/Services/ITermsService.cs \
  src/Server/Services/TermsService.cs src/Server/Controllers/TermsController.cs src/Server/Services/SubscriptionService.cs \
  src/Server/Services/ListingService.cs src/Server/Services/AdminService.cs tests/Server.Tests/Services/TermsServiceTests.cs \
  tests/Server.Tests/Data/Repositories/TermsRepositoryTests.cs tests/Server.Tests/Controllers/TermsControllerTests.cs \
  tests/Server.Tests/Services/SubscriptionServiceTests.cs tests/Server.Tests/Services/ListingServiceTests.cs \
  tests/Server.Tests/Services/ListingServiceCardTests.cs tests/Server.Tests/Services/AdminServiceTests.cs
git status --short
git commit -m "feat: stud listing terms — per-audience versions, acceptance, gate on activate/publish/convert, publish audit

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Change log API — areas, field diff, masking, Sydney date filters, paging

**Files:**
- Create: `src/Shared/DTOs/ChangeLog/ChangeLogAreas.cs`, `ChangeLogFilter.cs`, `ChangeLogEntryDto.cs`, `ChangeLogChangeDto.cs`, `ChangeLogPageDto.cs`
- Modify: `src/Server/Data/Repositories/IAuditLogRepository.cs`, `src/Server/Data/Repositories/AuditLogRepository.cs` (one method each)
- Create: `src/Server/Services/ChangeLogService.cs`, `src/Server/Controllers/AuditController.cs`
- Modify: `src/Server/Program.cs` (one registration after the content registrations)
- Test: `tests/Server.Tests/Services/ChangeLogServiceTests.cs`, `tests/Server.Tests/Controllers/AuditControllerTests.cs`

**Interfaces:**
- Consumes: audit rows written by `PlatformSettingsService` (`PlatformSettings` / `UpdatePlatformSettings`, `UpdateBusinessDetails`,
  details `{ Old, New }`), `IntroOfferService` (`IntroOffer` / `CreateIntroOffer` | `UpdateIntroOffer` | `RetireIntroOffer`, `{ Old, New }`),
  Task 4 `ContentService.AuditAction`, Task 8 `TermsService.PublishAuditAction` / `AcceptStudAuditAction`; `SydneyTime`; `ContentCatalog.Find`.
- Produces: `IAuditLogRepository.GetPageAsync(IReadOnlyCollection<string> actions, DateTime? fromUtc, DateTime? toUtcExclusive, int skip, int take)`
  → `Task<(IReadOnlyList<AuditLog> Rows, int Total)>` (newest first, with `User`);
  `IChangeLogService { Task<ServiceResult<ChangeLogPageDto>> GetAsync(ChangeLogFilter filter) }`, `ChangeLogService.PageSize = 50`;
  route `GET api/audit/changes?area&from&to&page` (StaffOnly);
  `static class ChangeLogAreas { Settings, BusinessDetails, IntroOffers, Wording, Terms, StudTermsAccepted; IReadOnlyList<(string Value, string Label)> All; bool IsKnown(string); string Label(string) }`;
  `ChangeLogFilter { string? Area; DateOnly? From; DateOnly? To; int Page = 1 }`;
  `ChangeLogEntryDto { long Id; DateTime OccurredAt; string When; string Who; string Area; string AreaLabel; string Item; string? Note; string? Link; List<ChangeLogChangeDto> Changes }`;
  `ChangeLogChangeDto { string Field; string? OldValue; string? NewValue; bool IsLongText }`;
  `ChangeLogPageDto { List<ChangeLogEntryDto> Entries; int Page; int PageSize; int Total; int TotalPages }` (all in `Stallions.Shared.DTOs.ChangeLog`).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Server.Tests/Services/ChangeLogServiceTests.cs
using System.Text.Json;
using FluentAssertions;
using Moq;
using Stallions.Server.Content;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.DTOs.ChangeLog;
using Stallions.Shared.DTOs.Content;
using Stallions.Shared.DTOs.Settings;
using Stallions.Shared.DTOs.Terms;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class ChangeLogServiceTests : IDisposable
{
    private static readonly DateTime Noon12Oct = new(2026, 10, 12, 1, 0, 0, DateTimeKind.Utc); // 12 Oct, 12:00 pm Sydney (AEDT)

    private readonly AppDbContext _db = DbContextFactory.Create(Guid.NewGuid().ToString());
    private readonly Mock<IUserService> _users = new();
    private readonly User _staff = new()
    {
        Id = Guid.NewGuid(), ObjectId = "staff", Email = "staff@example.com", DisplayName = "Sam Staff",
        Role = UserRole.Staff, Status = UserStatus.Active
    };

    public ChangeLogServiceTests()
    {
        _db.Database.EnsureCreated();
        _db.Users.Add(_staff);
        _db.SaveChanges();
        SignIn(_staff);
    }

    public void Dispose() => _db.Dispose();

    private void SignIn(User user) => _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(user);

    private ChangeLogService CreateSut() => new(new AuditLogRepository(_db), _users.Object);

    private void Audit(string entityType, string action, object details, DateTime? at = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType, EntityId = Guid.NewGuid(), Action = action, UserId = _staff.Id,
            OccurredAt = at ?? Noon12Oct, Details = JsonSerializer.Serialize(details)
        });
        _db.SaveChanges();
    }

    private async Task<ChangeLogPageDto> GetAsync(ChangeLogFilter? filter = null)
    {
        var result = await CreateSut().GetAsync(filter ?? new ChangeLogFilter());
        result.Succeeded.Should().BeTrue(result.Error);
        return result.Value!;
    }

    [Theory]
    [InlineData(UserRole.Buyer)]
    [InlineData(UserRole.StudFarmAdmin)]
    public async Task AnyoneButStaff_IsForbidden(UserRole role)
    {
        SignIn(new User { Id = Guid.NewGuid(), Role = role, Status = UserStatus.Active });
        (await CreateSut().GetAsync(new ChangeLogFilter())).HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task ShowsOnlyTheTrackedAreas_NewestFirst_InSydneyTime_WithWhoDidIt()
    {
        Audit("PlatformSettings", "UpdatePlatformSettings",
            new { Old = new { BuyerFeeIncGst = 150m }, New = new { BuyerFeeIncGst = 175m } }, Noon12Oct);
        Audit("Listing", "ForceListingStatus", new { Status = "Cancelled" }, Noon12Oct.AddHours(1));
        Audit("IntroOffer", "CreateIntroOffer", new { Old = (object?)null, New = new { Name = "Launch 2026" } }, Noon12Oct.AddHours(2));

        var page = await GetAsync();

        page.Total.Should().Be(2);
        page.Entries.Select(e => e.Area).Should().Equal(ChangeLogAreas.IntroOffers, ChangeLogAreas.Settings);
        page.Entries[0].When.Should().Be("12 Oct 2026, 2:00 pm");
        page.Entries[0].Who.Should().Be("Sam Staff");
        page.Entries[0].AreaLabel.Should().Be("Intro offers");
    }

    [Fact]
    public async Task Settings_ShowOnlyTheFieldsThatChanged()
    {
        Audit("PlatformSettings", "UpdatePlatformSettings", new
        {
            Old = new { BuyerFeeIncGst = 150m, MinimumBidIncrement = 25m },
            New = new { BuyerFeeIncGst = 175m, MinimumBidIncrement = 25m }
        });

        var entry = (await GetAsync()).Entries.Single();

        entry.Item.Should().Be("Platform settings");
        entry.Changes.Should().ContainSingle();
        entry.Changes[0].Field.Should().Be("Buyer fee (inc. GST)");
        entry.Changes[0].OldValue.Should().Be("150");
        entry.Changes[0].NewValue.Should().Be("175");
    }

    [Fact]
    public async Task BusinessDetails_MaskTheBankAccountNumber_InTheView_ButNotInTheStoredRow()
    {
        var settings = new PlatformSettingsService(new PlatformSettingsRepository(_db), new AuditLogRepository(_db), _users.Object);
        await settings.UpdateBusinessDetailsAsync(new UpdateBusinessDetailsRequest
            { LegalName = "Stallions Australia Pty Ltd", BankAccountNumber = "12345678", InvoicePaymentTermsDays = 14 });
        await settings.UpdateBusinessDetailsAsync(new UpdateBusinessDetailsRequest
            { LegalName = "Stallions Australia Pty Ltd", BankAccountNumber = "87654321", InvoicePaymentTermsDays = 14 });

        var entries = (await GetAsync(new ChangeLogFilter { Area = ChangeLogAreas.BusinessDetails })).Entries;

        entries.Should().HaveCount(2);
        var latest = entries[0];
        latest.Item.Should().Be("Business details");
        latest.Changes.Should().ContainSingle();
        latest.Changes[0].Field.Should().Be("Bank account number");
        latest.Changes[0].OldValue.Should().Be("•••678");
        latest.Changes[0].NewValue.Should().Be("•••321");
        entries[1].Changes.Single(c => c.Field == "Bank account number").OldValue.Should().BeNull();
        _db.AuditLogs.Where(a => a.Action == "UpdateBusinessDetails").AsEnumerable()
            .Should().Contain(a => a.Details!.Contains("87654321"));
    }

    [Fact]
    public async Task IntroOffer_Created_ShowsTheNewValues_AndItsName()
    {
        Audit("IntroOffer", "CreateIntroOffer", new
        {
            Old = (object?)null,
            New = new { Name = "Launch 2026", Type = "AmountOff", AmountOffIncGst = 495m, IsRetired = false }
        });

        var entry = (await GetAsync()).Entries.Single();

        entry.Item.Should().Be("Launch 2026");
        entry.Note.Should().Be("Created");
        entry.Changes.Should().ContainEquivalentOf(new ChangeLogChangeDto { Field = "Amount off (inc. GST)", OldValue = null, NewValue = "495" });
        entry.Changes.Should().ContainEquivalentOf(new ChangeLogChangeDto { Field = "Retired", OldValue = null, NewValue = "No" });
    }

    [Fact]
    public async Task Wording_ShowsAChangedMarker_AndLinksToTheVersion()
    {
        var content = ContentTestData.Service(_db, users: _users.Object);
        (await content.SaveAsync(ContentKeys.Outbid, new SaveContentRequest
        {
            Subject = "Outbid: {StallionName}", Body = "{HighBid} was bid on {StallionName}."
        })).Succeeded.Should().BeTrue();

        var entry = (await GetAsync(new ChangeLogFilter { Area = ChangeLogAreas.Wording })).Entries.Single();

        entry.Item.Should().Be("Outbid — version 2");
        entry.Link.Should().Be("/staff/wording?key=Email.Outbid&version=2");
        entry.Changes.Should().HaveCount(2);
        entry.Changes[0].Should().BeEquivalentTo(new ChangeLogChangeDto
            { Field = "Subject", OldValue = "You've been outbid on {StallionName}", NewValue = "Outbid: {StallionName}" });
        entry.Changes[1].Should().BeEquivalentTo(new ChangeLogChangeDto { Field = "Message", IsLongText = true });
    }

    [Fact]
    public async Task Wording_ARestore_SaysWhichVersion()
    {
        var content = ContentTestData.Service(_db, users: _users.Object);
        await content.SaveAsync(ContentKeys.Outbid, new SaveContentRequest { Subject = "Outbid", Body = "{HighBid} on {StallionName}." });
        await content.RestoreAsync(ContentKeys.Outbid, 1);

        var latest = (await GetAsync()).Entries.First();

        latest.Item.Should().Be("Outbid — version 3");
        latest.Note.Should().Be("Restored version 1");
    }

    [Fact]
    public async Task Terms_ShowAChangedMarker_AndLinkToTheVersion()
    {
        var terms = new TermsService(new TermsRepository(_db), _users.Object, new StudFarmRepository(_db),
            new AuditLogRepository(_db), TimeProvider.System);
        await terms.PublishAsync(new PublishTermsRequest { Body = "Stud terms", Audience = "Stud" });

        var entry = (await GetAsync(new ChangeLogFilter { Area = ChangeLogAreas.Terms })).Entries.Single();

        entry.Item.Should().Be("Stud listing terms — version 1");
        entry.Link.Should().Be("/staff/terms?audience=Stud&version=1");
        entry.Changes.Should().ContainSingle().Which.IsLongText.Should().BeTrue();
    }

    [Fact]
    public async Task StudTermsAccepted_NamesTheFarm()
    {
        Audit("StudFarm", "AcceptStudTerms", new { Version = 2, StudFarmName = "Arrowfield" });

        var entry = (await GetAsync()).Entries.Single();

        entry.AreaLabel.Should().Be("Stud terms accepted");
        entry.Item.Should().Be("Arrowfield");
        entry.Changes.Should().ContainSingle().Which.NewValue.Should().Be("2");
    }

    [Fact]
    public async Task AreaFilter_ReturnsOnlyThatArea()
    {
        Audit("PlatformSettings", "UpdatePlatformSettings", new { Old = new { OfferExpiryDays = 7 }, New = new { OfferExpiryDays = 10 } });
        Audit("StudFarm", "AcceptStudTerms", new { Version = 1, StudFarmName = "Arrowfield" });

        var page = await GetAsync(new ChangeLogFilter { Area = ChangeLogAreas.Settings });

        page.Entries.Should().ContainSingle().Which.Area.Should().Be(ChangeLogAreas.Settings);
    }

    [Fact]
    public async Task DateFilter_UsesSydneyCalendarDates()
    {
        object details = new { Old = new { OfferExpiryDays = 7 }, New = new { OfferExpiryDays = 8 } };
        Audit("PlatformSettings", "UpdatePlatformSettings", details, new DateTime(2026, 10, 11, 12, 30, 0, DateTimeKind.Utc)); // 11 Oct 11:30 pm
        Audit("PlatformSettings", "UpdatePlatformSettings", details, new DateTime(2026, 10, 11, 13, 30, 0, DateTimeKind.Utc)); // 12 Oct 12:30 am
        Audit("PlatformSettings", "UpdatePlatformSettings", details, new DateTime(2026, 10, 12, 13, 30, 0, DateTimeKind.Utc)); // 13 Oct 12:30 am

        var page = await GetAsync(new ChangeLogFilter { From = new DateOnly(2026, 10, 12), To = new DateOnly(2026, 10, 12) });

        page.Entries.Should().ContainSingle().Which.When.Should().Be("12 Oct 2026, 12:30 am");
    }

    [Fact]
    public async Task Paging_Is50APage_NewestFirst()
    {
        for (var i = 0; i < 51; i++)
            Audit("PlatformSettings", "UpdatePlatformSettings",
                new { Old = new { OfferExpiryDays = i }, New = new { OfferExpiryDays = i + 1 } }, Noon12Oct.AddMinutes(i));

        var first = await GetAsync();
        var second = await GetAsync(new ChangeLogFilter { Page = 2 });

        first.Entries.Should().HaveCount(50);
        first.Total.Should().Be(51);
        first.TotalPages.Should().Be(2);
        first.Entries[0].Changes.Single().NewValue.Should().Be("51");
        second.Page.Should().Be(2);
        second.Entries.Should().ContainSingle().Which.Changes.Single().NewValue.Should().Be("1");
    }

    [Fact]
    public async Task AnUnknownArea_OrFromAfterTo_IsBadRequest()
    {
        (await CreateSut().GetAsync(new ChangeLogFilter { Area = "Listings" })).HttpStatusCode.Should().Be(400);
        (await CreateSut().GetAsync(new ChangeLogFilter { From = new DateOnly(2026, 10, 13), To = new DateOnly(2026, 10, 12) }))
            .HttpStatusCode.Should().Be(400);
    }
}
```

```csharp
// tests/Server.Tests/Controllers/AuditControllerTests.cs
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Stallions.Server.Controllers;

namespace Stallions.Server.Tests.Controllers;

public class AuditControllerTests
{
    [Fact]
    public void TheChangeLog_IsStaffOnly() =>
        typeof(AuditController).GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be("StaffOnly");
}
```

- [ ] **Step 2: Run red** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~ChangeLog|FullyQualifiedName~AuditControllerTests"` → compile errors.

- [ ] **Step 3: DTOs**

```csharp
// src/Shared/DTOs/ChangeLog/ChangeLogAreas.cs
namespace Stallions.Shared.DTOs.ChangeLog;

/// <summary>The areas of Staff change the change log shows.</summary>
public static class ChangeLogAreas
{
    public const string Settings = "Settings";
    public const string BusinessDetails = "BusinessDetails";
    public const string IntroOffers = "IntroOffers";
    public const string Wording = "Wording";
    public const string Terms = "Terms";
    public const string StudTermsAccepted = "StudTermsAccepted";

    public static IReadOnlyList<(string Value, string Label)> All { get; } =
    [
        (Settings, "Platform settings"),
        (BusinessDetails, "Business details"),
        (IntroOffers, "Intro offers"),
        (Wording, "Wording"),
        (Terms, "Terms published"),
        (StudTermsAccepted, "Stud terms accepted")
    ];

    public static bool IsKnown(string area) => All.Any(a => a.Value == area);

    public static string Label(string area) => All.FirstOrDefault(a => a.Value == area).Label ?? area;
}
```

```csharp
// src/Shared/DTOs/ChangeLog/ChangeLogFilter.cs
namespace Stallions.Shared.DTOs.ChangeLog;

/// <summary>From / To are Sydney calendar dates (inclusive). Page is 1-based, 50 entries a page.</summary>
public class ChangeLogFilter
{
    public string? Area { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Page { get; set; } = 1;
}
```

```csharp
// src/Shared/DTOs/ChangeLog/ChangeLogChangeDto.cs
namespace Stallions.Shared.DTOs.ChangeLog;

/// <summary>One changed field. Long text (wording, terms) has no values — the entry's Link opens the version.</summary>
public class ChangeLogChangeDto
{
    public string Field { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public bool IsLongText { get; set; }
}
```

```csharp
// src/Shared/DTOs/ChangeLog/ChangeLogEntryDto.cs
namespace Stallions.Shared.DTOs.ChangeLog;

public class ChangeLogEntryDto
{
    public long Id { get; set; }
    /// <summary>UTC.</summary>
    public DateTime OccurredAt { get; set; }
    /// <summary>Sydney time, e.g. "12 Oct 2026, 2:00 pm".</summary>
    public string When { get; set; } = string.Empty;
    public string Who { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string AreaLabel { get; set; } = string.Empty;
    public string Item { get; set; } = string.Empty;
    public string? Note { get; set; }
    /// <summary>A Staff page showing the version (wording, terms).</summary>
    public string? Link { get; set; }
    public List<ChangeLogChangeDto> Changes { get; set; } = [];
}
```

```csharp
// src/Shared/DTOs/ChangeLog/ChangeLogPageDto.cs
namespace Stallions.Shared.DTOs.ChangeLog;

public class ChangeLogPageDto
{
    public List<ChangeLogEntryDto> Entries { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages => PageSize <= 0 ? 0 : Math.Max(1, (Total + PageSize - 1) / PageSize);
}
```

- [ ] **Step 4: Repository** — add to `IAuditLogRepository` (after `GetEntityIdsWithActionAsync`):

```csharp
// src/Server/Data/Repositories/IAuditLogRepository.cs (new member)
    /// <summary>Rows with any of these actions in [fromUtc, toUtcExclusive), newest first, with the user; and the total count.</summary>
    Task<(IReadOnlyList<AuditLog> Rows, int Total)> GetPageAsync(IReadOnlyCollection<string> actions,
        DateTime? fromUtc, DateTime? toUtcExclusive, int skip, int take);
```

and to `AuditLogRepository`:

```csharp
// src/Server/Data/Repositories/AuditLogRepository.cs (new member)
    public async Task<(IReadOnlyList<AuditLog> Rows, int Total)> GetPageAsync(IReadOnlyCollection<string> actions,
        DateTime? fromUtc, DateTime? toUtcExclusive, int skip, int take)
    {
        var query = _db.AuditLogs.AsNoTracking().Where(a => actions.Contains(a.Action));
        if (fromUtc is { } start) query = query.Where(a => a.OccurredAt >= start);
        if (toUtcExclusive is { } end) query = query.Where(a => a.OccurredAt < end);

        var total = await query.CountAsync();
        var rows = await query
            .Include(a => a.User)
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Skip(skip).Take(take)
            .ToListAsync();
        return (rows, total);
    }
```

- [ ] **Step 5: Service, controller, registration**

```csharp
// src/Server/Services/ChangeLogService.cs
using System.Globalization;
using System.Text.Json;
using Stallions.Server.Content;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Infrastructure;
using Stallions.Shared.DTOs.ChangeLog;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public interface IChangeLogService
{
    /// <summary>Staff only: changes to settings, business details, intro offers, wording and terms, newest first, 50 a page.</summary>
    Task<ServiceResult<ChangeLogPageDto>> GetAsync(ChangeLogFilter filter);
}

public class ChangeLogService : IChangeLogService
{
    public const int PageSize = 50;
    private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");

    /// <summary>The audit actions shown, and their area. Action names are unique across entity types.</summary>
    private static readonly IReadOnlyDictionary<string, string> AreaByAction = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["UpdatePlatformSettings"] = ChangeLogAreas.Settings,
        ["UpdateBusinessDetails"] = ChangeLogAreas.BusinessDetails,
        ["CreateIntroOffer"] = ChangeLogAreas.IntroOffers,
        ["UpdateIntroOffer"] = ChangeLogAreas.IntroOffers,
        ["RetireIntroOffer"] = ChangeLogAreas.IntroOffers,
        [ContentService.AuditAction] = ChangeLogAreas.Wording,
        [TermsService.PublishAuditAction] = ChangeLogAreas.Terms,
        [TermsService.AcceptStudAuditAction] = ChangeLogAreas.StudTermsAccepted
    };

    private static readonly IReadOnlyDictionary<string, string> FieldLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["BuyerFeeIncGst"] = "Buyer fee (inc. GST)",
        ["StandardListingFeeIncGst"] = "Standard listing fee (inc. GST)",
        ["MinimumBidIncrement"] = "Minimum bid increment",
        ["ChargeGracePeriodHours"] = "Charge grace period (hours)",
        ["OfferExpiryDays"] = "Offer expiry (days)",
        ["BusinessLegalName"] = "Legal name",
        ["BusinessAbn"] = "ABN",
        ["BusinessAddress"] = "Address",
        ["BusinessEmail"] = "Accounts email",
        ["BankAccountName"] = "Bank account name",
        ["BankBsb"] = "BSB",
        ["BankAccountNumber"] = "Bank account number",
        ["InvoicePaymentTermsDays"] = "Payment terms (days)",
        ["Name"] = "Name",
        ["Type"] = "Type",
        ["AmountOffIncGst"] = "Amount off (inc. GST)",
        ["StudFarmId"] = "Stud farm",
        ["SeasonId"] = "Season",
        ["EndsAt"] = "Ends (UTC)",
        ["IsRetired"] = "Retired"
    };

    private readonly IAuditLogRepository _audit;
    private readonly IUserService _users;

    public ChangeLogService(IAuditLogRepository audit, IUserService users)
    {
        _audit = audit;
        _users = users;
    }

    public async Task<ServiceResult<ChangeLogPageDto>> GetAsync(ChangeLogFilter filter)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.Staff)
            return ServiceResult<ChangeLogPageDto>.Forbidden("Only Stallions Australia staff can see the change log.");
        if (filter.Area != null && !ChangeLogAreas.IsKnown(filter.Area))
            return ServiceResult<ChangeLogPageDto>.BadRequest("Choose one of the listed areas.");
        if (filter.From is { } fromDate && filter.To is { } toDate && fromDate > toDate)
            return ServiceResult<ChangeLogPageDto>.BadRequest("The From date must be on or before the To date.");

        var page = Math.Max(1, filter.Page);
        var actions = AreaByAction.Where(a => filter.Area == null || a.Value == filter.Area).Select(a => a.Key).ToList();
        DateTime? fromUtc = filter.From is { } f ? SydneyTime.StartOfDayUtc(f) : null;
        DateTime? toUtc = filter.To is { } t ? SydneyTime.StartOfDayUtc(t.AddDays(1)) : null;
        var (rows, total) = await _audit.GetPageAsync(actions, fromUtc, toUtc, (page - 1) * PageSize, PageSize);

        return ServiceResult<ChangeLogPageDto>.Ok(new ChangeLogPageDto
        {
            Entries = rows.Select(Map).ToList(),
            Page = page,
            PageSize = PageSize,
            Total = total
        });
    }

    private static ChangeLogEntryDto Map(AuditLog a)
    {
        var area = AreaByAction[a.Action];
        var utc = DateTime.SpecifyKind(a.OccurredAt, DateTimeKind.Utc);
        var entry = new ChangeLogEntryDto
        {
            Id = a.Id,
            OccurredAt = utc,
            When = TimeZoneInfo.ConvertTimeFromUtc(utc, SydneyTime.Zone).ToString("d MMM yyyy, h:mm tt", Au),
            Who = a.User?.DisplayName ?? "System",
            Area = area,
            AreaLabel = ChangeLogAreas.Label(area)
        };

        using var doc = Parse(a.Details);
        JsonElement? root = doc?.RootElement;
        switch (area)
        {
            case ChangeLogAreas.Settings:
                entry.Item = "Platform settings";
                entry.Changes = Diff(Prop(root, "Old"), Prop(root, "New"));
                break;
            case ChangeLogAreas.BusinessDetails:
                entry.Item = "Business details";
                entry.Changes = Diff(Prop(root, "Old"), Prop(root, "New"));
                break;
            case ChangeLogAreas.IntroOffers:
                entry.Item = Text(Prop(root, "New"), "Name") ?? Text(Prop(root, "Old"), "Name") ?? "Intro offer";
                entry.Note = a.Action switch { "CreateIntroOffer" => "Created", "RetireIntroOffer" => "Retired", _ => "Edited" };
                entry.Changes = Diff(Prop(root, "Old"), Prop(root, "New"));
                break;
            case ChangeLogAreas.Wording:
                MapWording(entry, root);
                break;
            case ChangeLogAreas.Terms:
                MapTerms(entry, root);
                break;
            default: // StudTermsAccepted
                entry.Item = Text(root, "StudFarmName") ?? "Stud farm";
                entry.Changes = [new ChangeLogChangeDto { Field = "Accepted version", NewValue = Text(root, "Version") }];
                break;
        }
        return entry;
    }

    private static void MapWording(ChangeLogEntryDto entry, JsonElement? root)
    {
        var key = Text(root, "Key") ?? string.Empty;
        var version = Text(root, "Version") ?? "?";
        entry.Item = $"{ContentCatalog.Find(key)?.Label ?? key} — version {version}";
        entry.Note = Text(root, "Source") switch
        {
            "Restore" => $"Restored version {Text(root, "RestoredFromVersion")}",
            "Reset" => "Reset to the default wording",
            _ => null
        };
        entry.Link = $"/staff/wording?key={Uri.EscapeDataString(key)}&version={version}";

        var old = Prop(root, "Old");
        var @new = Prop(root, "New");
        var changes = new List<ChangeLogChangeDto>();
        var oldSubject = Text(old, "Subject");
        var newSubject = Text(@new, "Subject");
        if (oldSubject != newSubject)
            changes.Add(new ChangeLogChangeDto { Field = "Subject", OldValue = oldSubject, NewValue = newSubject });
        if (Text(old, "Body") != Text(@new, "Body"))
            changes.Add(new ChangeLogChangeDto { Field = "Message", IsLongText = true });
        entry.Changes = changes;
    }

    private static void MapTerms(ChangeLogEntryDto entry, JsonElement? root)
    {
        var audience = Text(root, "Audience") == "Stud" ? "Stud" : "Buyer";
        var version = Text(root, "Version") ?? "?";
        entry.Item = $"{(audience == "Stud" ? "Stud listing terms" : "Buyer T&C")} — version {version}";
        entry.Link = $"/staff/terms?audience={audience}&version={version}";
        entry.Changes = [new ChangeLogChangeDto { Field = "Text", IsLongText = true }];
    }

    /// <summary>Old → new for every field whose value differs; unchanged fields are left out.</summary>
    private static List<ChangeLogChangeDto> Diff(JsonElement? old, JsonElement? @new)
    {
        var names = new List<string>();
        foreach (var side in new[] { @new, old })
            if (side is { ValueKind: JsonValueKind.Object } obj)
                foreach (var p in obj.EnumerateObject())
                    if (!names.Contains(p.Name)) names.Add(p.Name);

        var changes = new List<ChangeLogChangeDto>();
        foreach (var name in names)
        {
            var before = Text(old, name);
            var after = Text(@new, name);
            if (before == after) continue;
            changes.Add(new ChangeLogChangeDto
            {
                Field = FieldLabels.TryGetValue(name, out var label) ? label : name,
                OldValue = Masked(name, before),
                NewValue = Masked(name, after)
            });
        }
        return changes;
    }

    /// <summary>Bank account numbers show only their last 3 digits here; the stored audit row is unchanged.</summary>
    private static string? Masked(string field, string? value) =>
        field == "BankAccountNumber" && value != null
            ? (value.Length <= 3 ? "•••" : "•••" + value[^3..])
            : value;

    private static JsonDocument? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json); }
        catch (JsonException) { return null; }
    }

    private static JsonElement? Prop(JsonElement? obj, string name) =>
        obj is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;

    private static string? Text(JsonElement? obj, string name) => Prop(obj, name) is { } v ? Format(v) : null;

    private static string Format(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => "Yes",
        JsonValueKind.False => "No",
        _ => value.GetRawText()
    };
}
```

```csharp
// src/Server/Controllers/AuditController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.ChangeLog;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Policy = "StaffOnly")]
public class AuditController : ControllerBase
{
    private readonly IChangeLogService _changeLog;
    public AuditController(IChangeLogService changeLog) => _changeLog = changeLog;

    /// <summary>The Staff change log. from / to are Sydney dates (yyyy-MM-dd); page is 1-based.</summary>
    [HttpGet("changes")]
    public async Task<IActionResult> GetChanges([FromQuery] string? area, [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to, [FromQuery] int page = 1)
    {
        var r = await _changeLog.GetAsync(new ChangeLogFilter { Area = area, From = from, To = to, Page = page });
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
}
```

`src/Server/Program.cs` — after `builder.Services.AddScoped<IContentService, ContentService>();` add
`builder.Services.AddScoped<IChangeLogService, ChangeLogService>();`.

- [ ] **Step 6: Run green** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~ChangeLog|FullyQualifiedName~AuditControllerTests"`;
  full build (0 warnings) and full test run.

- [ ] **Step 7: Commit**

```bash
git add src/Shared/DTOs/ChangeLog src/Server/Data/Repositories/IAuditLogRepository.cs src/Server/Data/Repositories/AuditLogRepository.cs \
  src/Server/Services/ChangeLogService.cs src/Server/Controllers/AuditController.cs src/Server/Program.cs \
  tests/Server.Tests/Services/ChangeLogServiceTests.cs tests/Server.Tests/Controllers/AuditControllerTests.cs
git status --short
git commit -m "feat: Staff change log API — settings, business details, intro offers, wording and terms, masked and paged

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 10: Client Staff — Wording list and editor (preview, versions, restore, reset)

**Files:**
- Create: `src/Client/Services/ContentApiService.cs`
- Modify: `src/Client/Program.cs` (register after `IntroOfferApiService`), `src/Client/_Imports.razor` (add `@using Stallions.Shared.DTOs.Content`)
- Create: `src/Client/Pages/Staff/StaffWording.razor`, `src/Client/Pages/Staff/StaffWording.razor.css`
- Modify: `src/Client/Layout/StaffLayout.razor` (nav link after Terms &amp; Conditions, lines 44–46)
- Test: `tests/Client.Tests/Pages/StaffWordingTests.cs`

**Interfaces:**
- Consumes: Task 4 routes and `Stallions.Shared.DTOs.Content` DTOs; existing `ServiceHelpers.ExtractErrorMessageAsync`, `ApiException`,
  `UserStateService`, `Modal`.
- Produces: `ContentApiService(HttpClient)` with virtual `GetAllAsync()`, `GetAsync(string key)`, `GetHistoryAsync(string key)`,
  `SaveAsync(string key, SaveContentRequest)`, `RestoreAsync(string key, int version)`, `ResetAsync(string key)`,
  `PreviewAsync(string key, SaveContentRequest)`; page `/staff/wording` (list) and `/staff/wording?key=…[&version=n]` (editor), with
  `[Parameter] TimeSpan PreviewDelay` (default 400 ms; tests use zero).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Client.Tests/Pages/StaffWordingTests.cs
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Staff;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Content;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class StaffWordingTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };
    private readonly Mock<ContentApiService> _api = new(MockBehavior.Loose, Http());

    private static ContentItemDetailDto Outbid(int version = 1, string body = "Another buyer has bid {HighBid} on {StallionName}.") => new()
    {
        Key = "Email.Outbid", Group = "Buyer emails", Label = "Outbid",
        Description = "To a bidder when another buyer places a higher bid.",
        HasSubject = true, Subject = "You've been outbid on {StallionName}", Body = body, Version = version,
        ChangedAt = new DateTime(2026, 10, 12, 3, 0, 0, DateTimeKind.Utc), ChangedByName = version == 1 ? null : "Sam Staff",
        IsDefault = version == 1, MaxSubjectLength = 200, MaxBodyLength = 4000,
        Placeholders =
        [
            new ContentPlaceholderDto { Name = "StallionName", Description = "The stallion's name", Sample = "Snitzel", Required = true },
            new ContentPlaceholderDto { Name = "HighBid", Description = "The new highest bid (inc. GST)", Sample = "$12,500.00", Required = true }
        ]
    };

    private static List<ContentVersionDto> Versions(int current = 1) =>
        Enumerable.Range(1, current).Reverse().Select(v => new ContentVersionDto
        {
            Version = v, Subject = "You've been outbid on {StallionName}",
            Body = v == 1 ? "Default body {HighBid} {StallionName}" : $"Edited body {v} {{HighBid}} {{StallionName}}",
            CreatedAt = new DateTime(2026, 10, 12, 3, 0, 0, DateTimeKind.Utc), CreatedByName = v == 1 ? null : "Sam Staff",
            IsCurrent = v == current
        }).ToList();

    private IRenderedComponent<StaffWording> Render(string? query = null, ContentItemDetailDto? detail = null,
        List<ContentVersionDto>? versions = null, string role = "Staff")
    {
        this.AddTestAuthorization().SetAuthorized("staff@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.Setup(a => a.GetAsync("Email.Outbid")).ReturnsAsync(detail ?? Outbid());
        _api.Setup(a => a.GetHistoryAsync("Email.Outbid")).ReturnsAsync(versions ?? Versions());
        _api.Setup(a => a.PreviewAsync("Email.Outbid", It.IsAny<SaveContentRequest>())).ReturnsAsync(new ContentPreviewDto
        {
            Subject = "You've been outbid on Snitzel", Paragraphs = ["Another buyer has bid $12,500.00 on Snitzel."]
        });
        Services.AddSingleton(_api.Object);

        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "Staff", Email = "staff@example.com", Role = role, Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));

        if (query != null) Services.GetRequiredService<NavigationManager>().NavigateTo($"/staff/wording?{query}");
        return RenderComponent<StaffWording>(p => p.Add(c => c.PreviewDelay, TimeSpan.Zero));
    }

    private static void Click(IRenderedComponent<StaffWording> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == text).Click();

    private static string BodyValue(IRenderedComponent<StaffWording> cut)
    {
        var body = cut.Find("#wording-body");
        return body.GetAttribute("value") ?? body.TextContent;
    }

    [Fact]
    public void List_GroupsTheItems_WithTheirVersionAndWhoChangedThem()
    {
        _api.Setup(a => a.GetAllAsync()).ReturnsAsync(
        [
            new ContentItemSummaryDto { Key = "Disclosure.BuyerFeeExplanation", Group = "Disclosures", Label = "Buyer fee explanation",
                Description = "Shown before bidding.", CurrentVersion = 1, ChangedAt = new DateTime(2026, 10, 12, 3, 0, 0, DateTimeKind.Utc) },
            new ContentItemSummaryDto { Key = "Email.Outbid", Group = "Buyer emails", Label = "Outbid", Description = "To a bidder.",
                HasSubject = true, CurrentVersion = 3, ChangedAt = new DateTime(2026, 10, 13, 3, 0, 0, DateTimeKind.Utc), ChangedByName = "Sam Staff" }
        ]);

        var cut = Render();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Buyer fee explanation"));
        cut.FindAll("h2.wording-group").Select(h => h.TextContent).Should().Equal("Disclosures", "Buyer emails");
        cut.Markup.Should().Contain("v3").And.Contain("by Sam Staff").And.Contain("by System default");
        cut.Find("a[href='/staff/wording?key=Email.Outbid']").TextContent.Should().Be("Edit");
    }

    [Fact]
    public void Editor_ShowsTheText_ThePlaceholders_AndThePreviewFromTheServer()
    {
        var cut = Render("key=Email.Outbid");

        cut.WaitForAssertion(() => cut.Find("#wording-subject").GetAttribute("value").Should().Be("You've been outbid on {StallionName}"));
        BodyValue(cut).Should().Be("Another buyer has bid {HighBid} on {StallionName}.");
        cut.Find(".wording-placeholders").TextContent.Should().Contain("{StallionName}").And.Contain("The new highest bid (inc. GST)")
            .And.Contain("Required").And.Contain("$12,500.00");
        cut.WaitForAssertion(() => cut.Find(".wording-preview").TextContent.Should()
            .Contain("You've been outbid on Snitzel").And.Contain("Another buyer has bid $12,500.00 on Snitzel."));
    }

    [Fact]
    public void Typing_RefreshesThePreview_AndShowsValidationErrorsInline()
    {
        var cut = Render("key=Email.Outbid");
        cut.WaitForAssertion(() => cut.Find("#wording-body"));
        _api.Setup(a => a.PreviewAsync("Email.Outbid", It.Is<SaveContentRequest>(r => r.Body.Contains("{Foo}"))))
            .ReturnsAsync(new ContentPreviewDto { Errors = ["{Foo} isn't available in this email.", "The message must include {HighBid}."] });

        cut.Find("#wording-body").Input("Bid {Foo} on {StallionName}");

        cut.WaitForAssertion(() => cut.FindAll(".wording-errors li").Select(li => li.TextContent).Should()
            .Equal("{Foo} isn't available in this email.", "The message must include {HighBid}."));
        _api.Verify(a => a.PreviewAsync("Email.Outbid", It.Is<SaveContentRequest>(r =>
            r.Body == "Bid {Foo} on {StallionName}" && r.Subject == "You've been outbid on {StallionName}")), Times.Once);
    }

    [Fact]
    public void Save_SendsTheSubjectAndMessage_AndShowsTheNewVersion()
    {
        var cut = Render("key=Email.Outbid");
        cut.WaitForAssertion(() => cut.Find("#wording-body"));
        _api.Setup(a => a.SaveAsync("Email.Outbid", It.IsAny<SaveContentRequest>())).ReturnsAsync(Outbid(2, "{HighBid} on {StallionName}."));
        _api.Setup(a => a.GetHistoryAsync("Email.Outbid")).ReturnsAsync(Versions(2));

        cut.Find("#wording-subject").Input("Outbid: {StallionName}");
        cut.Find("#wording-body").Input("{HighBid} on {StallionName}.");
        Click(cut, "Save as new version");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Saved as version 2."));
        _api.Verify(a => a.SaveAsync("Email.Outbid", It.Is<SaveContentRequest>(r =>
            r.Subject == "Outbid: {StallionName}" && r.Body == "{HighBid} on {StallionName}.")), Times.Once);
        cut.FindAll(".wording-versions tbody tr").Should().HaveCount(2);
    }

    [Fact]
    public void Save_AServerRejection_ShowsEachErrorOnItsOwnLine()
    {
        var cut = Render("key=Email.Outbid");
        cut.WaitForAssertion(() => cut.Find("#wording-body"));
        _api.Setup(a => a.SaveAsync("Email.Outbid", It.IsAny<SaveContentRequest>()))
            .ThrowsAsync(new ApiException(400, "The subject can't be empty.\nThe message must include {HighBid}."));

        Click(cut, "Save as new version");

        cut.WaitForAssertion(() => cut.FindAll(".wording-errors li").Select(li => li.TextContent).Should()
            .Equal("The subject can't be empty.", "The message must include {HighBid}."));
    }

    [Fact]
    public void Reset_AsksFirst_ThenResets()
    {
        var cut = Render("key=Email.Outbid", Outbid(2, "Edited {HighBid} {StallionName}."), Versions(2));
        cut.WaitForAssertion(() => cut.Find("#wording-body"));
        _api.Setup(a => a.ResetAsync("Email.Outbid")).ReturnsAsync(Outbid(3));

        Click(cut, "Reset to default");

        cut.Markup.Should().Contain("Replace the current wording with the default?");
        _api.Verify(a => a.ResetAsync(It.IsAny<string>()), Times.Never);
        Click(cut, "Yes, reset");
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Reset to the default wording as version 3."));
        _api.Verify(a => a.ResetAsync("Email.Outbid"), Times.Once);
    }

    [Fact]
    public void Restore_AsksFirst_ThenRestoresThatVersion()
    {
        var cut = Render("key=Email.Outbid", Outbid(2, "Edited {HighBid} {StallionName}."), Versions(2));
        cut.WaitForAssertion(() => cut.FindAll(".wording-versions tbody tr").Should().HaveCount(2));
        _api.Setup(a => a.RestoreAsync("Email.Outbid", 1)).ReturnsAsync(Outbid(3));

        Click(cut, "Restore");

        cut.Markup.Should().Contain("Restore version 1?");
        Click(cut, "Yes, restore");
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Version 1 restored as version 3."));
        _api.Verify(a => a.RestoreAsync("Email.Outbid", 1), Times.Once);
    }

    [Fact]
    public void AVersionLink_OpensThatVersion()
    {
        var cut = Render("key=Email.Outbid&version=1", Outbid(2, "Edited {HighBid} {StallionName}."), Versions(2));

        cut.WaitForAssertion(() => cut.Find(".wording-version-text").TextContent.Should().Contain("Default body {HighBid} {StallionName}"));
    }

    [Fact]
    public void AnyoneButStaff_IsSentAway()
    {
        var cut = Render(role: "Buyer");

        cut.WaitForAssertion(() => _api.Verify(a => a.GetAllAsync(), Times.Never));
        cut.Markup.Should().NotContain("wording-group");
    }
}
```

- [ ] **Step 2: Run red** — `dotnet test tests/Client.Tests --filter FullyQualifiedName~StaffWordingTests` → compile errors.

- [ ] **Step 3: API service and registration**

```csharp
// src/Client/Services/ContentApiService.cs
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Content;

namespace Stallions.Client.Services;

/// <summary>Staff-only: the disclosure wording and every system email's subject and message.</summary>
public class ContentApiService
{
    private readonly HttpClient _http;
    public ContentApiService(HttpClient http) => _http = http;

    private static string Url(string key, string suffix = "") => $"api/content/{Uri.EscapeDataString(key)}{suffix}";

    public virtual async Task<List<ContentItemSummaryDto>> GetAllAsync() =>
        await Read<List<ContentItemSummaryDto>>(await _http.GetAsync("api/content"));

    public virtual async Task<ContentItemDetailDto> GetAsync(string key) =>
        await Read<ContentItemDetailDto>(await _http.GetAsync(Url(key)));

    public virtual async Task<List<ContentVersionDto>> GetHistoryAsync(string key) =>
        await Read<List<ContentVersionDto>>(await _http.GetAsync(Url(key, "/versions")));

    public virtual async Task<ContentItemDetailDto> SaveAsync(string key, SaveContentRequest request) =>
        await Read<ContentItemDetailDto>(await _http.PutAsJsonAsync(Url(key), request));

    public virtual async Task<ContentItemDetailDto> RestoreAsync(string key, int version) =>
        await Read<ContentItemDetailDto>(await _http.PostAsync(Url(key, $"/versions/{version}/restore"), null));

    public virtual async Task<ContentItemDetailDto> ResetAsync(string key) =>
        await Read<ContentItemDetailDto>(await _http.PostAsync(Url(key, "/reset"), null));

    public virtual async Task<ContentPreviewDto> PreviewAsync(string key, SaveContentRequest request) =>
        await Read<ContentPreviewDto>(await _http.PostAsJsonAsync(Url(key, "/preview"), request));

    private static async Task<T> Read<T>(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<T>() ?? throw new ApiException(500, "Empty response.");
    }
}
```

`src/Client/Program.cs` — after the `IntroOfferApiService` registration add:

```csharp
// src/Client/Program.cs (after IntroOfferApiService)
builder.Services.AddHttpClient<ContentApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
```

`src/Client/_Imports.razor` — add `@using Stallions.Shared.DTOs.Content` at the end.

`src/Client/Layout/StaffLayout.razor` — after the Terms &amp; Conditions `NavLink` (lines 44–46) add:

```razor
@* src/Client/Layout/StaffLayout.razor (after Terms & Conditions) *@
            <NavLink href="/staff/wording" class="admin-nav-link" Match="NavLinkMatch.Prefix">
                <span class="admin-nav-icon">✍️</span> Wording
            </NavLink>
```

- [ ] **Step 4: The page**

```razor
@* src/Client/Pages/Staff/StaffWording.razor *@
@page "/staff/wording"
@layout StaffLayout
@attribute [Authorize]
@implements IDisposable
@inject ContentApiService ContentApi
@inject UserStateService UserState
@inject NavigationManager Nav

<PageTitle>Wording — Staff Admin</PageTitle>

@if (Key is null)
{
    <div class="admin-page-header">
        <h1 class="admin-page-title">Wording</h1>
    </div>
    <p class="text-secondary text-sm">
        The buyer disclosures and the subject and message of every system email. Each save is a new version; older
        versions can be viewed and restored. Amounts tables, buttons and links, the buyer fee disclosure block and
        invoice details are added by the system and can't be changed here.
    </p>

    @if (_loading)
    {
        <p>Loading…</p>
    }
    else if (_error is not null)
    {
        <div class="alert alert-danger">@_error</div>
    }
    else
    {
        @foreach (var group in _items.GroupBy(i => i.Group))
        {
            <h2 class="wording-group">@group.Key</h2>
            <table class="admin-table">
                <thead>
                    <tr><th>Item</th><th>When it's used</th><th>Version</th><th>Last changed</th><th></th></tr>
                </thead>
                <tbody>
                    @foreach (var item in group)
                    {
                        <tr>
                            <td><strong>@item.Label</strong></td>
                            <td class="text-sm">@item.Description</td>
                            <td>@VersionText(item.CurrentVersion)</td>
                            <td class="text-sm">@Changed(item.ChangedAt, item.ChangedByName)</td>
                            <td class="admin-table-actions"><a class="btn btn-sm btn-outline" href="@EditorUrl(item.Key)">Edit</a></td>
                        </tr>
                    }
                </tbody>
            </table>
        }
    }
}
else
{
    <div class="admin-page-header">
        <h1 class="admin-page-title">@(_detail?.Label ?? "Wording")</h1>
        <a href="/staff/wording" class="btn btn-secondary btn-sm">← All wording</a>
    </div>

    @if (_loading)
    {
        <p>Loading…</p>
    }
    else if (_detail is null)
    {
        <div class="alert alert-danger">@(_error ?? "That wording item doesn't exist.")</div>
    }
    else
    {
        <p class="text-secondary text-sm">@_detail.Description</p>
        <p class="text-sm">Current version: <strong>@VersionText(_detail.Version)</strong> · @Changed(_detail.ChangedAt, _detail.ChangedByName)</p>

        <div class="wording-editor">
            <div class="wording-form">
                @if (_detail.HasSubject)
                {
                    <label class="form-label" for="wording-subject">Subject</label>
                    <input id="wording-subject" class="form-input" maxlength="@_detail.MaxSubjectLength"
                           value="@_subject" @oninput="OnSubjectInput" />
                }
                <label class="form-label" for="wording-body">@(_detail.HasSubject ? "Message" : "Wording")</label>
                <textarea id="wording-body" class="form-control" rows="10" maxlength="@_detail.MaxBodyLength"
                          value="@_body" @oninput="OnBodyInput"></textarea>
                <p class="text-muted text-sm">
                    @(_detail.SingleParagraph ? "One paragraph of plain text." : "Plain text. Leave a blank line between paragraphs.")
                    @_body.Length / @_detail.MaxBodyLength characters.
                </p>

                @if (_errors.Count > 0)
                {
                    <ul class="wording-errors" role="alert">
                        @foreach (var e in _errors)
                        {
                            <li>@e</li>
                        }
                    </ul>
                }
                @if (_notice is not null)
                {
                    <div class="alert alert-success" role="status">@_notice</div>
                }

                <div class="wording-actions">
                    <button class="btn btn-gold" @onclick="SaveAsync" disabled="@_busy">@(_busy ? "Saving…" : "Save as new version")</button>
                    <button class="btn btn-outline" @onclick="() => _confirmReset = true" disabled="@(_busy || _detail.IsDefault)">Reset to default</button>
                </div>

                @if (_detail.Placeholders.Count > 0)
                {
                    <h3 class="wording-subhead">Placeholders</h3>
                    <table class="admin-table wording-placeholders">
                        <thead><tr><th>Placeholder</th><th>What it shows</th><th>Example</th></tr></thead>
                        <tbody>
                            @foreach (var p in _detail.Placeholders)
                            {
                                <tr>
                                    <td>
                                        <code>{@p.Name}</code>
                                        @if (p.Required)
                                        {
                                            <span class="badge badge-sub-pending">Required</span>
                                        }
                                    </td>
                                    <td class="text-sm">@p.Description</td>
                                    <td class="text-sm">@p.Sample</td>
                                </tr>
                            }
                        </tbody>
                    </table>
                }
            </div>

            <div class="wording-preview">
                <h3 class="wording-subhead">Preview (example values)</h3>
                @if (_preview is null || _preview.Errors.Count > 0)
                {
                    <p class="text-muted text-sm">Fix the errors to see the preview.</p>
                }
                else
                {
                    @if (_preview.Subject is not null)
                    {
                        <p class="wording-preview-subject"><strong>Subject:</strong> @_preview.Subject</p>
                    }
                    <div class="wording-preview-body">
                        @foreach (var paragraph in _preview.Paragraphs)
                        {
                            var lines = paragraph.Split('\n');
                            <p>
                                @for (var i = 0; i < lines.Length; i++)
                                {
                                    <span>@lines[i]</span>
                                    if (i < lines.Length - 1)
                                    {
                                        <br />
                                    }
                                }
                            </p>
                        }
                    </div>
                    @if (_detail.HasSubject)
                    {
                        <p class="text-muted text-sm">
                            The system adds the rest of the email after this text — for example the amounts table, buttons
                            and links, the buyer fee disclosure or the invoice details.
                        </p>
                    }
                }
            </div>
        </div>

        <h3 class="wording-subhead">Previous versions</h3>
        <table class="admin-table wording-versions">
            <thead><tr><th>Version</th><th>Saved</th><th>By</th><th></th></tr></thead>
            <tbody>
                @foreach (var v in _versions)
                {
                    <tr>
                        <td>
                            v@(v.Version)
                            @if (v.IsCurrent)
                            {
                                <span class="badge badge-sub-paid">Current</span>
                            }
                        </td>
                        <td class="text-sm">@v.CreatedAt.ToLocalTime().ToString("d MMM yyyy, h:mm tt")</td>
                        <td>@(v.CreatedByName ?? "System default")</td>
                        <td class="admin-table-actions">
                            <button class="btn btn-sm btn-outline" @onclick="() => _viewing = v">View</button>
                            @if (!v.IsCurrent)
                            {
                                <button class="btn btn-sm btn-outline" @onclick="() => _restoring = v" disabled="@_busy">Restore</button>
                            }
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    }
}

<Modal IsOpen="@(_viewing is not null)" Title="@($"Version {_viewing?.Version}")" OnClose="() => _viewing = null" CloseOnBackdrop="true">
    @if (_viewing is not null)
    {
        <div class="wording-version-text">
            @if (_viewing.Subject is not null)
            {
                <p><strong>Subject:</strong> @_viewing.Subject</p>
            }
            <pre>@_viewing.Body</pre>
        </div>
    }
</Modal>

<Modal IsOpen="@(_restoring is not null)" Title="@($"Restore version {_restoring?.Version}?")" OnClose="() => _restoring = null">
    <p>The text of version @_restoring?.Version becomes the current wording, saved as a new version. Nothing is deleted.</p>
    <div class="wording-actions">
        <button class="btn btn-gold" @onclick="RestoreAsync" disabled="@_busy">Yes, restore</button>
        <button class="btn btn-outline" @onclick="() => _restoring = null" disabled="@_busy">Cancel</button>
    </div>
</Modal>

<Modal IsOpen="@_confirmReset" Title="Reset to default?" OnClose="() => _confirmReset = false">
    <p>Replace the current wording with the default? It's saved as a new version, so the current wording stays in Previous versions.</p>
    <div class="wording-actions">
        <button class="btn btn-gold" @onclick="ResetAsync" disabled="@_busy">Yes, reset</button>
        <button class="btn btn-outline" @onclick="() => _confirmReset = false" disabled="@_busy">Cancel</button>
    </div>
</Modal>

@code {
    /// <summary>The item being edited; null shows the list.</summary>
    [SupplyParameterFromQuery] public string? Key { get; set; }
    /// <summary>Opens this version's text (links from the change log).</summary>
    [SupplyParameterFromQuery] public int? Version { get; set; }
    /// <summary>How long typing must pause before the preview refreshes (tests use zero).</summary>
    [Parameter] public TimeSpan PreviewDelay { get; set; } = TimeSpan.FromMilliseconds(400);

    private List<ContentItemSummaryDto> _items = [];
    private ContentItemDetailDto? _detail;
    private List<ContentVersionDto> _versions = [];
    private ContentPreviewDto? _preview;
    private List<string> _errors = [];
    private string _subject = string.Empty;
    private string _body = string.Empty;
    private string? _error;
    private string? _notice;
    private bool _loading = true;
    private bool _busy;
    private bool _authorised;
    private bool _confirmReset;
    private ContentVersionDto? _viewing;
    private ContentVersionDto? _restoring;
    private string? _loadedKey;
    private CancellationTokenSource? _previewCts;

    protected override async Task OnInitializedAsync()
    {
        await UserState.LoadAsync();
        if (!UserState.IsStaff) { Nav.NavigateTo("/", replace: true); return; }
        _authorised = true;
    }

    // Moving between the list and an item only changes the query, so reload when it changes.
    protected override async Task OnParametersSetAsync()
    {
        if (!_authorised) return;
        var key = Key ?? string.Empty;
        if (_loadedKey == key) return;
        _loadedKey = key;
        _error = null; _notice = null; _errors = []; _detail = null; _preview = null; _versions = [];
        _viewing = null; _restoring = null; _confirmReset = false;
        if (Key is null) await LoadListAsync();
        else await LoadItemAsync(Key);
    }

    private async Task LoadListAsync()
    {
        _loading = true;
        try { _items = await ContentApi.GetAllAsync(); }
        catch (Exception ex) { _error = ex is ApiException ae ? ae.Message : "Failed to load the wording."; }
        finally { _loading = false; }
    }

    private async Task LoadItemAsync(string key)
    {
        _loading = true;
        try
        {
            _detail = await ContentApi.GetAsync(key);
            _subject = _detail.Subject ?? string.Empty;
            _body = _detail.Body;
            _versions = await ContentApi.GetHistoryAsync(key);
            if (Version is int version) _viewing = _versions.FirstOrDefault(v => v.Version == version);
        }
        catch (Exception ex)
        {
            _detail = null;
            _error = ex is ApiException ae ? ae.Message : "Failed to load this wording item.";
        }
        finally { _loading = false; }
        if (_detail is not null) await RefreshPreviewAsync();
    }

    private Task OnSubjectInput(ChangeEventArgs e)
    {
        _subject = e.Value?.ToString() ?? string.Empty;
        return SchedulePreviewAsync();
    }

    private Task OnBodyInput(ChangeEventArgs e)
    {
        _body = e.Value?.ToString() ?? string.Empty;
        return SchedulePreviewAsync();
    }

    // Debounced: only the last keystroke in a pause asks the server for a preview.
    private async Task SchedulePreviewAsync()
    {
        _notice = null;
        _previewCts?.Cancel();
        var cts = _previewCts = new CancellationTokenSource();
        if (PreviewDelay > TimeSpan.Zero)
        {
            try { await Task.Delay(PreviewDelay, cts.Token); }
            catch (TaskCanceledException) { return; }
        }
        if (cts.IsCancellationRequested) return;
        await RefreshPreviewAsync();
    }

    private async Task RefreshPreviewAsync()
    {
        if (_detail is null) return;
        try
        {
            _preview = await ContentApi.PreviewAsync(_detail.Key, Request());
            _errors = _preview.Errors;
        }
        catch (ApiException ex)
        {
            _preview = null;
            _errors = [ex.Message];
        }
    }

    private SaveContentRequest Request() => new() { Subject = _detail!.HasSubject ? _subject : null, Body = _body };

    private async Task SaveAsync()
    {
        if (_detail is null) return;
        _busy = true; _notice = null;
        try { await ApplySavedAsync(await ContentApi.SaveAsync(_detail.Key, Request()), "Saved as version"); }
        catch (ApiException ex) { _errors = SplitErrors(ex.Message); }
        finally { _busy = false; }
    }

    private async Task ResetAsync()
    {
        _confirmReset = false;
        if (_detail is null) return;
        _busy = true; _notice = null;
        try { await ApplySavedAsync(await ContentApi.ResetAsync(_detail.Key), "Reset to the default wording as version"); }
        catch (ApiException ex) { _errors = SplitErrors(ex.Message); }
        finally { _busy = false; }
    }

    private async Task RestoreAsync()
    {
        var version = _restoring;
        _restoring = null;
        if (_detail is null || version is null) return;
        _busy = true; _notice = null;
        try { await ApplySavedAsync(await ContentApi.RestoreAsync(_detail.Key, version.Version), $"Version {version.Version} restored as version"); }
        catch (ApiException ex) { _errors = SplitErrors(ex.Message); }
        finally { _busy = false; }
    }

    private async Task ApplySavedAsync(ContentItemDetailDto detail, string message)
    {
        _detail = detail;
        _subject = detail.Subject ?? string.Empty;
        _body = detail.Body;
        _errors = [];
        try { _versions = await ContentApi.GetHistoryAsync(detail.Key); }
        catch (ApiException) { /* keep the list we have; the save itself succeeded */ }
        await RefreshPreviewAsync();
        _notice = $"{message} {detail.Version}.";
    }

    private static List<string> SplitErrors(string message) =>
        message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string EditorUrl(string key) => $"/staff/wording?key={Uri.EscapeDataString(key)}";

    private static string VersionText(int version) => version == 0 ? "Default" : $"v{version}";

    private static string Changed(DateTime? at, string? by) =>
        at is null ? "Default wording" : $"{at.Value.ToLocalTime():d MMM yyyy} by {by ?? "System default"}";

    public void Dispose() => _previewCts?.Cancel();
}
```

```css
/* src/Client/Pages/Staff/StaffWording.razor.css */
.wording-group {
    font-size: var(--font-size-lg);
    margin: var(--space-6) 0 var(--space-3);
}

.wording-editor {
    display: grid;
    grid-template-columns: minmax(0, 1fr);
    gap: var(--space-6);
    margin-bottom: var(--space-6);
}

.wording-form {
    display: flex;
    flex-direction: column;
    gap: var(--space-2);
}

.wording-form textarea {
    font-family: inherit;
    line-height: 1.5;
}

.wording-errors {
    margin: 0;
    padding: var(--space-3) var(--space-3) var(--space-3) var(--space-6);
    border: 1px solid var(--danger, #b42318);
    border-radius: 8px;
    color: var(--danger, #b42318);
    font-size: var(--font-size-sm);
}

.wording-actions {
    display: flex;
    flex-wrap: wrap;
    gap: var(--space-3);
    margin: var(--space-2) 0;
}

.wording-subhead {
    font-size: var(--font-size-base, 1rem);
    margin: var(--space-6) 0 var(--space-2);
}

.wording-preview {
    background: var(--white);
    border: 1px solid var(--border);
    border-radius: 8px;
    padding: var(--space-4) var(--space-5);
    align-self: start;
}

.wording-preview .wording-subhead { margin-top: 0; }
.wording-preview-subject { padding-bottom: var(--space-2); border-bottom: 1px solid var(--border); }
.wording-preview-body p { margin: 0 0 var(--space-3); }

.wording-version-text pre {
    white-space: pre-wrap;
    font-family: inherit;
    margin: 0;
}

@media (min-width: 1000px) {
    .wording-editor { grid-template-columns: minmax(0, 3fr) minmax(0, 2fr); }
}
```

- [ ] **Step 5: Run green** — `dotnet test tests/Client.Tests --filter FullyQualifiedName~StaffWordingTests`; full build (0 warnings) and full test run.

- [ ] **Step 6: Commit**

```bash
git add src/Client/Services/ContentApiService.cs src/Client/Program.cs src/Client/_Imports.razor \
  src/Client/Pages/Staff/StaffWording.razor src/Client/Pages/Staff/StaffWording.razor.css src/Client/Layout/StaffLayout.razor \
  tests/Client.Tests/Pages/StaffWordingTests.cs
git status --short
git commit -m "feat: Staff Wording page — grouped list, editor with live preview, versions, restore and reset

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Client terms — safe markdown, Staff Terms buyer / stud switch and history, stud terms modal, Stud farms column

**Files:**
- Create: `src/Client/Components/TermsMarkdown.cs`
- Modify: `src/Client/Components/Buyer/TermsAgreementModal.razor:35`
- Modify: `src/Client/Services/TermsApiService.cs` (replace `PublishAsync` lines 23–30 and `GetHistoryAsync` lines 32–38; add two methods)
- Modify: `src/Client/Pages/Staff/StaffTerms.razor` (whole file)
- Create: `src/Client/Components/Stud/StudTermsModal.razor`, `src/Client/Components/Stud/StudTermsModal.razor.css`
- Modify: `src/Client/_Imports.razor` (add `@using Stallions.Client.Components.Stud`)
- Modify: `src/Client/Pages/Admin/AdminStallions.razor` (inject after line 9; modal after line 204; `ActivateAsync` lines 327–347)
- Modify: `src/Client/Pages/Admin/AdminListingDetail.razor` (inject after line 7; modal before `@code`; `HandlePublish` from line 314; `HandleConvert` lines 338–348)
- Modify: `src/Client/Pages/Staff/StaffStudFarms.razor` (inject; header + cell; `Load`)
- Test: `tests/Client.Tests/Components/TermsMarkdownTests.cs`, `tests/Client.Tests/Pages/StaffTermsTests.cs`, `tests/Client.Tests/Pages/StaffStudFarmsTests.cs` (new);
  `tests/Client.Tests/Pages/AdminStallionsTests.cs` (fixture: a `_terms` field registered in `RenderAt`; two new tests);
  `tests/Client.Tests/Pages/AdminListingDetailTests.cs` (fixture: `_terms` and `_admin` fields registered in `Render` and `RenderWith`; two new tests)

**Interfaces:**
- Consumes: Task 8 routes and DTOs (`TermsDocumentDto.Audience` / `CreatedByName`, `PublishTermsRequest.Audience`, `StudTermsStatusDto`,
  `StudFarmSummaryDto.AcceptedStudTerms*`), `AcceptTermsRequest`; existing `Modal`, `ErrorMessage`, `AdminApiService.PublishListingAsync` /
  `ConvertToMakeAnOfferAsync`, `SubscriptionApiService.ActivateAsync`.
- Produces: `static class TermsMarkdown { string ToHtml(string? markdown) }` (raw HTML disabled; namespace `Stallions.Client.Components`);
  `TermsApiService.PublishAsync(string body, string audience)`, `GetHistoryAsync(string audience)`, `GetStudStatusAsync()` →
  `StudTermsStatusDto`, `AcceptStudTermsAsync(int version)` → `StudTermsStatusDto` (all virtual);
  `StudTermsModal` (`[EditorRequired] TermsDocumentDto Terms`, `EventCallback OnAccepted`, `EventCallback OnCancel`; markup classes
  `.stud-terms-body`, `.stud-terms-agree`, buttons "I accept" / "Cancel"); Staff Terms query parameters `audience` and `version`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Client.Tests/Components/TermsMarkdownTests.cs
using FluentAssertions;
using Stallions.Client.Components;

namespace Stallions.Client.Tests.Components;

public class TermsMarkdownTests
{
    [Fact]
    public void TermsMarkdown_RendersRawHtmlAsText()
    {
        var html = TermsMarkdown.ToHtml("<script>alert(1)</script>\n\n**Bold** <img src=x onerror=alert(1)>");

        html.Should().NotContain("<script").And.NotContain("<img")
            .And.Contain("&lt;script&gt;").And.Contain("<strong>Bold</strong>");
    }

    [Fact]
    public void TermsMarkdown_OfNothing_IsEmpty() => TermsMarkdown.ToHtml(null).Should().BeEmpty();
}
```

```csharp
// tests/Client.Tests/Pages/StaffTermsTests.cs
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Staff;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Terms;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class StaffTermsTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };
    private readonly Mock<TermsApiService> _api = new(MockBehavior.Loose, Http());

    private static TermsDocumentDto Doc(string audience, int version, string body) => new()
    {
        Id = Guid.NewGuid(), Audience = audience, Version = version, Body = body,
        CreatedAt = new DateTime(2026, 10, 12, 3, 0, 0, DateTimeKind.Utc), CreatedByName = "Sam Staff"
    };

    private IRenderedComponent<StaffTerms> Render(string? query = null)
    {
        this.AddTestAuthorization().SetAuthorized("staff@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.Setup(a => a.GetHistoryAsync("Buyer")).ReturnsAsync([Doc("Buyer", 2, "<script>x</script> Buyer **v2**"), Doc("Buyer", 1, "Buyer v1")]);
        _api.Setup(a => a.GetHistoryAsync("Stud")).ReturnsAsync([Doc("Stud", 1, "Stud **terms** v1")]);
        Services.AddSingleton(_api.Object);

        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "Staff", Email = "staff@example.com", Role = "Staff", Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));

        if (query != null) Services.GetRequiredService<NavigationManager>().NavigateTo($"/staff/terms?{query}");
        return RenderComponent<StaffTerms>();
    }

    private static void Click(IRenderedComponent<StaffTerms> cut, string text) =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == text).Click();

    [Fact]
    public void BuyerTerms_AreShownFirst_WithTheirVersionHistory()
    {
        var cut = Render();

        cut.WaitForAssertion(() => cut.Find(".terms-current").TextContent.Should().Contain("2"));
        cut.FindAll(".terms-history tbody tr").Should().HaveCount(2);
        cut.Find(".terms-history").TextContent.Should().Contain("Sam Staff");
        cut.Find(".terms-preview-box").InnerHtml.Should().Contain("<strong>v2</strong>").And.NotContain("<script");
    }

    [Fact]
    public void TheStudSwitch_ShowsTheStudTerms_AndPublishesStudTerms()
    {
        var cut = Render();
        cut.WaitForAssertion(() => cut.FindAll(".terms-history tbody tr").Should().HaveCount(2));
        _api.Setup(a => a.PublishAsync(It.IsAny<string>(), "Stud")).ReturnsAsync(Doc("Stud", 2, "New stud terms"));

        Click(cut, "Stud listing terms");
        cut.WaitForAssertion(() => cut.FindAll(".terms-history tbody tr").Should().HaveCount(1));
        cut.Find("textarea").Input("New stud terms");
        Click(cut, "Publish new version");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("New version published."));
        _api.Verify(a => a.PublishAsync("New stud terms", "Stud"), Times.Once);
        cut.FindAll(".terms-history tbody tr").Should().HaveCount(2);
    }

    [Fact]
    public void View_ShowsAReadOnlyVersion_RenderedSafely()
    {
        var cut = Render();
        cut.WaitForAssertion(() => cut.FindAll(".terms-history tbody tr").Should().HaveCount(2));

        Click(cut, "View");

        cut.Find(".terms-version-view").InnerHtml.Should().Contain("<strong>v2</strong>").And.NotContain("<script");
    }

    [Fact]
    public void AChangeLogLink_OpensThatStudVersion()
    {
        var cut = Render("audience=Stud&version=1");

        cut.WaitForAssertion(() => cut.Find(".terms-version-view").InnerHtml.Should().Contain("<strong>terms</strong>"));
    }
}
```

```csharp
// tests/Client.Tests/Pages/StaffStudFarmsTests.cs
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Staff;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Admin;
using Stallions.Shared.DTOs.Terms;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class StaffStudFarmsTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };

    [Fact]
    public void EachFarm_ShowsWhichStudTermsVersionItAccepted()
    {
        this.AddTestAuthorization().SetAuthorized("staff@example.com");
        var at = new DateTime(2026, 10, 12, 3, 0, 0, DateTimeKind.Utc);
        var staffApi = new Mock<StaffApiService>(MockBehavior.Loose, Http());
        staffApi.Setup(s => s.GetStudFarmsAsync()).ReturnsAsync(
        [
            new StudFarmSummaryDto { Id = Guid.NewGuid(), Name = "Arrowfield", AcceptedStudTermsVersion = 2, AcceptedStudTermsAt = at },
            new StudFarmSummaryDto { Id = Guid.NewGuid(), Name = "Coolmore", AcceptedStudTermsVersion = 1, AcceptedStudTermsAt = at },
            new StudFarmSummaryDto { Id = Guid.NewGuid(), Name = "Darley" }
        ]);
        Services.AddSingleton(staffApi.Object);
        var terms = new Mock<TermsApiService>(MockBehavior.Loose, Http());
        terms.Setup(t => t.GetStudStatusAsync()).ReturnsAsync(new StudTermsStatusDto
            { Current = new TermsDocumentDto { Audience = "Stud", Version = 2, Body = "Stud terms" } });
        Services.AddSingleton(terms.Object);
        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "Staff", Email = "staff@example.com", Role = "Staff", Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));

        var cut = RenderComponent<StaffStudFarms>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Arrowfield"));
        cut.Find("thead").TextContent.Should().Contain("Stud terms");
        var rows = cut.FindAll("tbody tr");
        rows[0].TextContent.Should().Contain("v2 · 12 Oct 2026").And.NotContain("not current");
        rows[1].TextContent.Should().Contain("v1 · 12 Oct 2026 (not current)");
        rows[2].TextContent.Should().Contain("Not accepted");
    }
}
```

`AdminStallionsTests` — add the field `private readonly Mock<TermsApiService> _terms = new(MockBehavior.Loose, Http());` after `_taxDocs`
(line 24) and in `RenderAt`, after `Services.AddSingleton(_taxDocs.Object);` (line 50), add `Services.AddSingleton(_terms.Object);`. Add the
`using Stallions.Shared.DTOs.Terms;` using and these tests:

```csharp
// tests/Client.Tests/Pages/AdminStallionsTests.cs (new tests)
    private static TermsDocumentDto StudTerms() => new() { Id = Guid.NewGuid(), Audience = "Stud", Version = 2, Body = "Stud listing **terms**" };

    [Fact]
    public void Activate_WhenTheStudTermsNeedAccepting_ShowsThem_ThenGoesToPaymentAfterAccepting()
    {
        _terms.Setup(t => t.GetStudStatusAsync()).ReturnsAsync(new StudTermsStatusDto { Current = StudTerms(), NeedsAcceptance = true });
        _terms.Setup(t => t.AcceptStudTermsAsync(2)).ReturnsAsync(new StudTermsStatusDto { Current = StudTerms(), AcceptedVersion = 2 });
        _subs.Setup(s => s.ActivateAsync(_snitzel.Id)).ReturnsAsync(new PaymentRedirectDto { Url = "https://checkout.example/pay" });
        var cut = Render();

        cut.FindAll("button").Single(b => b.TextContent.Contains("Activate for")).Click();

        cut.WaitForAssertion(() => cut.Find(".stud-terms-body").InnerHtml.Should().Contain("<strong>terms</strong>"));
        _subs.Verify(s => s.ActivateAsync(It.IsAny<Guid>()), Times.Never);
        cut.Find(".stud-terms-agree input").Change(true);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "I accept").Click();

        cut.WaitForAssertion(() =>
            Services.GetRequiredService<NavigationManager>().Uri.Should().Be("https://checkout.example/pay"));
        _terms.Verify(t => t.AcceptStudTermsAsync(2), Times.Once);
    }

    [Fact]
    public void Activate_CancellingTheStudTerms_DoesNotActivate()
    {
        _terms.Setup(t => t.GetStudStatusAsync()).ReturnsAsync(new StudTermsStatusDto { Current = StudTerms(), NeedsAcceptance = true });
        var cut = Render();

        cut.FindAll("button").Single(b => b.TextContent.Contains("Activate for")).Click();
        cut.WaitForAssertion(() => cut.FindAll(".stud-terms-body").Should().ContainSingle());
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Cancel").Click();

        cut.FindAll(".stud-terms-body").Should().BeEmpty();
        _subs.Verify(s => s.ActivateAsync(It.IsAny<Guid>()), Times.Never);
        _terms.Verify(t => t.AcceptStudTermsAsync(It.IsAny<int>()), Times.Never);
    }
```

`AdminListingDetailTests` — add the fields `private readonly Mock<TermsApiService> _terms = new(MockBehavior.Loose, new HttpClient { BaseAddress = new Uri("https://localhost/") });`
and `private Mock<AdminApiService>? _admin;`; in `Render` set `_admin = adminMock;` after creating it and add `Services.AddSingleton(_terms.Object);`
after `Services.AddSingleton(adminMock.Object);`; in `RenderWith` add the same `Services.AddSingleton(_terms.Object);`. Add the
`using Stallions.Shared.DTOs.Terms;` using and these tests:

```csharp
// tests/Client.Tests/Pages/AdminListingDetailTests.cs (new tests)
    private static TermsDocumentDto StudTerms() => new() { Id = Guid.NewGuid(), Audience = "Stud", Version = 2, Body = "Stud listing **terms**" };

    [Fact]
    public void Publish_WhenTheStudTermsNeedAccepting_ShowsThem_ThenPublishesAfterAccepting()
    {
        _terms.Setup(t => t.GetStudStatusAsync()).ReturnsAsync(new StudTermsStatusDto { Current = StudTerms(), NeedsAcceptance = true });
        _terms.Setup(t => t.AcceptStudTermsAsync(2)).ReturnsAsync(new StudTermsStatusDto { Current = StudTerms(), AcceptedVersion = 2 });
        Render([Subscription("Paid")], out var cut);
        cut.WaitForAssertion(() => PublishButton(cut).HasAttribute("disabled").Should().BeFalse());

        PublishButton(cut).Click();

        cut.WaitForAssertion(() => cut.FindAll(".stud-terms-body").Should().ContainSingle());
        _admin!.Verify(a => a.PublishListingAsync(It.IsAny<Guid>()), Times.Never);
        cut.Find(".stud-terms-agree input").Change(true);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "I accept").Click();
        cut.WaitForAssertion(() => _admin.Verify(a => a.PublishListingAsync(_draft.Id), Times.Once));
        _terms.Verify(t => t.AcceptStudTermsAsync(2), Times.Once);
    }

    [Fact]
    public void Publish_WhenTheStudTermsAreAlreadyAccepted_PublishesStraightAway()
    {
        _terms.Setup(t => t.GetStudStatusAsync()).ReturnsAsync(new StudTermsStatusDto { Current = StudTerms(), AcceptedVersion = 2 });
        Render([Subscription("Paid")], out var cut);
        cut.WaitForAssertion(() => PublishButton(cut).HasAttribute("disabled").Should().BeFalse());

        PublishButton(cut).Click();

        cut.WaitForAssertion(() => _admin!.Verify(a => a.PublishListingAsync(_draft.Id), Times.Once));
        cut.FindAll(".stud-terms-body").Should().BeEmpty();
    }
```

- [ ] **Step 2: Run red** — `dotnet test tests/Client.Tests --filter "FullyQualifiedName~TermsMarkdownTests|FullyQualifiedName~StaffTermsTests|FullyQualifiedName~StaffStudFarmsTests|FullyQualifiedName~AdminStallionsTests|FullyQualifiedName~AdminListingDetailTests"` → compile errors.

- [ ] **Step 3: Safe markdown and the API service**

```csharp
// src/Client/Components/TermsMarkdown.cs
using Markdig;

namespace Stallions.Client.Components;

/// <summary>Terms markdown → HTML with raw HTML disabled, so terms text can't carry markup or script.</summary>
public static class TermsMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();

    public static string ToHtml(string? markdown) => Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
}
```

`src/Client/Components/Buyer/TermsAgreementModal.razor` line 35: `_html = Markdig.Markdown.ToHtml(Terms.Body ?? string.Empty);` →
`_html = TermsMarkdown.ToHtml(Terms.Body);`

`src/Client/Services/TermsApiService.cs` — replace `PublishAsync` and `GetHistoryAsync` (lines 23–38) with:

```csharp
// src/Client/Services/TermsApiService.cs (replaces PublishAsync and GetHistoryAsync; adds the stud methods)
    /// <summary>Staff: publishes the next version for "Buyer" or "Stud".</summary>
    public virtual async Task<TermsDocumentDto> PublishAsync(string body, string audience)
    {
        var r = await _http.PostAsJsonAsync("api/terms", new PublishTermsRequest { Body = body, Audience = audience });
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ReadError(r, "Failed to publish the terms."));
        return await r.Content.ReadFromJsonAsync<TermsDocumentDto>()
               ?? throw new ApiException(500, "Empty response.");
    }

    /// <summary>Staff: every version for "Buyer" or "Stud", newest first.</summary>
    public virtual async Task<List<TermsDocumentDto>> GetHistoryAsync(string audience)
    {
        var r = await _http.GetAsync($"api/terms/history?audience={Uri.EscapeDataString(audience)}");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, "Failed to load the terms history.");
        return await r.Content.ReadFromJsonAsync<List<TermsDocumentDto>>() ?? [];
    }

    /// <summary>Stud admins and Staff: the current stud listing terms and whether the caller's farm must accept them.</summary>
    public virtual async Task<StudTermsStatusDto> GetStudStatusAsync()
    {
        var r = await _http.GetAsync("api/terms/stud/current");
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ReadError(r, "Failed to load the stud listing terms."));
        return await r.Content.ReadFromJsonAsync<StudTermsStatusDto>() ?? new StudTermsStatusDto();
    }

    /// <summary>Stud admins: accepts this version of the stud listing terms for their farm.</summary>
    public virtual async Task<StudTermsStatusDto> AcceptStudTermsAsync(int version)
    {
        var r = await _http.PostAsJsonAsync("api/terms/stud/accept", new AcceptTermsRequest { Version = version });
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ReadError(r, "Failed to accept the stud listing terms."));
        return await r.Content.ReadFromJsonAsync<StudTermsStatusDto>() ?? new StudTermsStatusDto();
    }
```

- [ ] **Step 4: Staff Terms page** — replace `src/Client/Pages/Staff/StaffTerms.razor`:

```razor
@* src/Client/Pages/Staff/StaffTerms.razor *@
@page "/staff/terms"
@layout StaffLayout
@attribute [Authorize]
@inject TermsApiService TermsApi
@inject UserStateService UserState
@inject NavigationManager Nav

<PageTitle>Terms &amp; Conditions — Staff Admin</PageTitle>

<div class="admin-page-header">
    <h1 class="admin-page-title">Terms &amp; Conditions</h1>
</div>

<div class="terms-audience" role="tablist">
    <button type="button" role="tab" aria-selected="@(_audience == "Buyer")"
            class="btn btn-sm @(_audience == "Buyer" ? "btn-gold" : "btn-outline")" @onclick='() => SwitchAsync("Buyer")'>Buyer T&amp;C</button>
    <button type="button" role="tab" aria-selected="@(_audience == "Stud")"
            class="btn btn-sm @(_audience == "Stud" ? "btn-gold" : "btn-outline")" @onclick='() => SwitchAsync("Stud")'>Stud listing terms</button>
</div>
<p class="text-secondary text-sm">
    @(_audience == "Buyer"
        ? "Buyers accept these before they can bid or make an offer."
        : "A stud accepts these before paying a listing fee, publishing a listing or opening a lot to offers.")
</p>

@if (_loading)
{
    <p>Loading…</p>
}
else
{
    <p class="text-secondary text-sm terms-current">
        Current version: <strong>@(Current?.Version.ToString() ?? "none published")</strong>
        @if (Current is not null)
        {
            <text> · published @Current.CreatedAt.ToLocalTime().ToString("d MMM yyyy")@(Current.CreatedByName is null ? "" : $" by {Current.CreatedByName}")</text>
        }
    </p>

    <div class="terms-editor">
        <label class="form-label" for="terms-body">Body (Markdown)</label>
        <textarea id="terms-body" class="form-control" rows="16" value="@_body" @oninput="OnBodyInput"></textarea>
    </div>

    <div class="terms-preview">
        <label class="form-label">Preview</label>
        <div class="terms-preview-box">@((MarkupString)_preview)</div>
    </div>

    @if (_error is not null) { <div class="alert alert-danger">@_error</div> }
    @if (_published) { <div class="alert alert-success">New version published.</div> }

    <button class="btn btn-gold" @onclick="Publish" disabled="@_busy">
        @(_busy ? "Publishing…" : "Publish new version")
    </button>

    <h2 class="terms-history-title">Version history</h2>
    @if (_history.Count == 0)
    {
        <p class="text-muted text-sm">No versions published yet.</p>
    }
    else
    {
        <table class="admin-table terms-history">
            <thead><tr><th>Version</th><th>Published</th><th>By</th><th></th></tr></thead>
            <tbody>
                @foreach (var doc in _history)
                {
                    <tr>
                        <td>v@(doc.Version)</td>
                        <td>@doc.CreatedAt.ToLocalTime().ToString("d MMM yyyy, h:mm tt")</td>
                        <td>@(doc.CreatedByName ?? "—")</td>
                        <td class="admin-table-actions"><button class="btn btn-sm btn-outline" @onclick="() => _viewing = doc">View</button></td>
                    </tr>
                }
            </tbody>
        </table>
    }
}

<Modal IsOpen="@(_viewing is not null)" Title="@($"{AudienceLabel} — version {_viewing?.Version}")" OnClose="() => _viewing = null" CloseOnBackdrop="true">
    @if (_viewing is not null)
    {
        <div class="terms-preview-box terms-version-view">@((MarkupString)TermsMarkdown.ToHtml(_viewing.Body))</div>
    }
</Modal>

@code {
    [SupplyParameterFromQuery(Name = "audience")] public string? AudienceQuery { get; set; }
    [SupplyParameterFromQuery(Name = "version")] public int? VersionQuery { get; set; }

    private string _audience = "Buyer";
    private List<TermsDocumentDto> _history = [];
    private TermsDocumentDto? _viewing;
    private string _body = string.Empty;
    private string _preview = string.Empty;
    private bool _loading = true;
    private bool _busy;
    private bool _published;
    private string? _error;

    private TermsDocumentDto? Current => _history.FirstOrDefault();
    private string AudienceLabel => _audience == "Stud" ? "Stud listing terms" : "Buyer T&C";

    protected override async Task OnInitializedAsync()
    {
        await UserState.LoadAsync();
        if (!UserState.IsStaff) { Nav.NavigateTo("/", replace: true); return; }
        _audience = string.Equals(AudienceQuery, "Stud", StringComparison.OrdinalIgnoreCase) ? "Stud" : "Buyer";
        await Load();
        if (VersionQuery is int version) _viewing = _history.FirstOrDefault(d => d.Version == version);
    }

    private async Task Load()
    {
        _loading = true; _error = null; _published = false;
        try
        {
            _history = await TermsApi.GetHistoryAsync(_audience);
            _body = Current?.Body ?? string.Empty;
            UpdatePreview();
        }
        catch (Exception ex)
        {
            _history = [];
            _error = ex is ApiException ae ? ae.Message : "Failed to load the terms.";
        }
        finally { _loading = false; }
    }

    private async Task SwitchAsync(string audience)
    {
        if (_audience == audience) return;
        _audience = audience;
        _viewing = null;
        await Load();
    }

    private void OnBodyInput(ChangeEventArgs e)
    {
        _body = e.Value?.ToString() ?? string.Empty;
        UpdatePreview();
    }

    private void UpdatePreview() => _preview = TermsMarkdown.ToHtml(_body);

    private async Task Publish()
    {
        _busy = true; _error = null; _published = false;
        try
        {
            _history.Insert(0, await TermsApi.PublishAsync(_body, _audience));
            _published = true;
        }
        catch (Exception ex) { _error = ex is ApiException ae ? ae.Message : "Failed to publish."; }
        finally { _busy = false; }
    }
}
```

- [ ] **Step 5: Stud terms modal**

```razor
@* src/Client/Components/Stud/StudTermsModal.razor *@
<Modal IsOpen="true" Title="Stud listing terms" CloseOnBackdrop="false" OnClose="Cancel">
    <p class="text-sm">
        Stallions Australia has published stud listing terms (version @Terms.Version). Please read and accept them to continue.
    </p>
    <div class="stud-terms-body">@((MarkupString)_html)</div>
    <label class="stud-terms-agree">
        <input type="checkbox" @bind="_checked" />
        <span>I have read and accept the stud listing terms.</span>
    </label>
    <ErrorMessage Message="@_error" />
    <div class="stud-terms-actions">
        <button class="btn btn-outline" type="button" @onclick="Cancel" disabled="@_busy">Cancel</button>
        <button class="btn btn-gold" type="button" @onclick="Accept" disabled="@(!_checked || _busy)">@(_busy ? "Saving…" : "I accept")</button>
    </div>
</Modal>

@code {
    [Parameter, EditorRequired] public TermsDocumentDto Terms { get; set; } = default!;
    /// <summary>Records the acceptance, then carries on with what the stud asked to do. An ApiException is shown here.</summary>
    [Parameter] public EventCallback OnAccepted { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private string _html = string.Empty;
    private bool _checked;
    private bool _busy;
    private string? _error;

    protected override void OnParametersSet() => _html = TermsMarkdown.ToHtml(Terms.Body);

    private async Task Accept()
    {
        _busy = true; _error = null;
        try { await OnAccepted.InvokeAsync(); }
        catch (ApiException ex) { _error = ex.Message; }
        finally { _busy = false; }
    }

    private Task Cancel() => OnCancel.InvokeAsync();
}
```

```css
/* src/Client/Components/Stud/StudTermsModal.razor.css */
.stud-terms-body {
    max-height: 45vh;
    overflow-y: auto;
    border: 1px solid var(--border, #e5e7eb);
    border-radius: var(--radius-md);
    padding: var(--space-4);
    margin: var(--space-3) 0 var(--space-4);
}

.stud-terms-agree {
    display: flex;
    gap: var(--space-2);
    align-items: flex-start;
    margin-bottom: var(--space-4);
    font-size: var(--font-size-sm);
}

.stud-terms-actions {
    display: flex;
    justify-content: flex-end;
    flex-wrap: wrap;
    gap: var(--space-3);
}
```

`src/Client/_Imports.razor` — add `@using Stallions.Client.Components.Stud`.

- [ ] **Step 6: Stud pages ask first**

`src/Client/Pages/Admin/AdminStallions.razor`:
- after line 9 (`@inject UserStateService UserState`) add `@inject TermsApiService TermsApi`;
- after the last markup line (line 204, the `}` closing `else if (_authorized != null)`) add:

```razor
@* src/Client/Pages/Admin/AdminStallions.razor (after the page markup) *@
@if (_studTerms is not null)
{
    <StudTermsModal Terms="_studTerms" OnAccepted="AcceptStudTermsAsync" OnCancel="CancelStudTerms" />
}
```

- replace `ActivateAsync` (lines 327–347) with:

```csharp
// src/Client/Pages/Admin/AdminStallions.razor (@code — replaces ActivateAsync)
    private TermsDocumentDto? _studTerms;
    private Func<Task>? _afterStudTerms;

    // A stud must accept the current stud listing terms before paying a listing fee; the server enforces it too.
    private async Task ActivateAsync(Guid stallionId)
    {
        _activateError = null; _paymentNotice = null;
        StudTermsStatusDto? status;
        try { status = await TermsApi.GetStudStatusAsync(); }
        catch (ApiException ex) { _activateError = ex.Message; return; }

        if (status is { NeedsAcceptance: true, Current: not null })
        {
            _studTerms = status.Current;
            _afterStudTerms = () => StartActivationAsync(stallionId);
            return;
        }
        await StartActivationAsync(stallionId);
    }

    private async Task AcceptStudTermsAsync()
    {
        if (_studTerms is null) return;
        await TermsApi.AcceptStudTermsAsync(_studTerms.Version); // an ApiException is shown in the modal
        var next = _afterStudTerms;
        _studTerms = null;
        _afterStudTerms = null;
        if (next is not null) await next();
    }

    private void CancelStudTerms()
    {
        _studTerms = null;
        _afterStudTerms = null;
    }

    private async Task StartActivationAsync(Guid stallionId)
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

`src/Client/Pages/Admin/AdminListingDetail.razor`:
- after line 7 (`@inject UserStateService UserState`) add `@inject TermsApiService TermsApi`;
- just before `@code {` add:

```razor
@* src/Client/Pages/Admin/AdminListingDetail.razor (before @code) *@
@if (_studTerms is not null)
{
    <StudTermsModal Terms="_studTerms" OnAccepted="AcceptStudTermsAsync" OnCancel="CancelStudTerms" />
}
```

- replace `HandlePublish` (the method starting at line 314) and `HandleConvert` (lines 338–348) with:

```csharp
// src/Client/Pages/Admin/AdminListingDetail.razor (@code — replaces HandlePublish and HandleConvert)
    private TermsDocumentDto? _studTerms;
    private Func<Task>? _afterStudTerms;

    // Publishing (and opening a lot to offers) needs the current stud listing terms accepted; the server enforces it too.
    private Task HandlePublish() => WithStudTermsAsync(PublishAsync);

    private Task HandleConvert() => WithStudTermsAsync(ConvertAsync);

    private async Task WithStudTermsAsync(Func<Task> action)
    {
        _error = null;
        StudTermsStatusDto? status;
        try { status = await TermsApi.GetStudStatusAsync(); }
        catch (ApiException ex) { _error = ex.Message; return; }

        if (status is { NeedsAcceptance: true, Current: not null })
        {
            _studTerms = status.Current;
            _afterStudTerms = action;
            return;
        }
        await action();
    }

    private async Task AcceptStudTermsAsync()
    {
        if (_studTerms is null) return;
        await TermsApi.AcceptStudTermsAsync(_studTerms.Version); // an ApiException is shown in the modal
        var next = _afterStudTerms;
        _studTerms = null;
        _afterStudTerms = null;
        if (next is not null) await next();
    }

    private void CancelStudTerms()
    {
        _studTerms = null;
        _afterStudTerms = null;
    }

    private async Task PublishAsync()
    {
        _busy = true; _error = null;
        try
        {
            await AdminApi.PublishListingAsync(Id);
            await LoadAsync();
        }
        catch (ApiException ex) { _error = ex.Message; }
        finally { _busy = false; }
    }

    private async Task ConvertAsync()
    {
        _busy = true; _error = null;
        try
        {
            var created = await AdminApi.ConvertToMakeAnOfferAsync(Id);
            Nav.NavigateTo($"/admin/listings/{created.Id}");
        }
        catch (ApiException ex) { _error = ex.Message; _confirmConvert = false; }
        finally { _busy = false; }
    }
```

`src/Client/Pages/Staff/StaffStudFarms.razor`:
- after line 6 (`@inject UserStateService UserState`) add `@inject TermsApiService TermsApi`;
- after `<th>Active</th>` (line 40) add `<th>Stud terms</th>`, and after the Active `<td>…</td>` (lines 66–70) add
  `<td class="text-sm">@StudTermsText(f)</td>`;
- replace `Load` (lines 93–99) and add the helper:

```csharp
// src/Client/Pages/Staff/StaffStudFarms.razor (@code — replaces Load)
    private int? _currentStudVersion;

    private async Task Load()
    {
        _loading = true; _error = null;
        try { _farms = await StaffApi.GetStudFarmsAsync(); }
        catch (Exception ex) { _error = ex is ApiException ae ? ae.Message : "Failed to load stud farms."; }
        finally { _loading = false; }

        // Only used to flag acceptances of an older version; the list still shows without it.
        try { _currentStudVersion = (await TermsApi.GetStudStatusAsync())?.Current?.Version; }
        catch (ApiException) { _currentStudVersion = null; }
    }

    private string StudTermsText(StudFarmSummaryDto f) => f.AcceptedStudTermsVersion is int v
        ? $"v{v} · {f.AcceptedStudTermsAt?.ToLocalTime().ToString("d MMM yyyy")}{(_currentStudVersion is int c && c != v ? " (not current)" : "")}"
        : _currentStudVersion is null ? "—" : "Not accepted";
```

- [ ] **Step 7: Run green** — the filter from Step 2 (every existing `AdminStallionsTests` and `AdminListingDetailTests` test unchanged — a
  loose `TermsApiService` mock returns a null status, which means "no gate"); `AppRenderTests` and `ListingDetailTests` (they mock
  `TermsApiService` and only use `GetCurrentAsync`); full build (0 warnings) and full test run.

- [ ] **Step 8: Commit**

```bash
git add src/Client/Components/TermsMarkdown.cs src/Client/Components/Buyer/TermsAgreementModal.razor src/Client/Services/TermsApiService.cs \
  src/Client/Pages/Staff/StaffTerms.razor src/Client/Components/Stud src/Client/_Imports.razor src/Client/Pages/Admin/AdminStallions.razor \
  src/Client/Pages/Admin/AdminListingDetail.razor src/Client/Pages/Staff/StaffStudFarms.razor \
  tests/Client.Tests/Components/TermsMarkdownTests.cs tests/Client.Tests/Pages/StaffTermsTests.cs tests/Client.Tests/Pages/StaffStudFarmsTests.cs \
  tests/Client.Tests/Pages/AdminStallionsTests.cs tests/Client.Tests/Pages/AdminListingDetailTests.cs
git status --short
git commit -m "feat: stud terms in the client — accept before activate/publish, Staff terms switch and history, stud farms column

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: Client Staff — Change log page

**Files:**
- Create: `src/Client/Services/ChangeLogApiService.cs`
- Modify: `src/Client/Program.cs` (register after `ContentApiService`), `src/Client/_Imports.razor` (add `@using Stallions.Shared.DTOs.ChangeLog`)
- Create: `src/Client/Pages/Staff/StaffChangeLog.razor`, `src/Client/Pages/Staff/StaffChangeLog.razor.css`
- Modify: `src/Client/Layout/StaffLayout.razor` (nav link after Wording)
- Test: `tests/Client.Tests/Pages/StaffChangeLogTests.cs`, `tests/Client.Tests/Services/ChangeLogApiServiceTests.cs`

**Interfaces:**
- Consumes: Task 9 route `GET api/audit/changes` and `Stallions.Shared.DTOs.ChangeLog` (`ChangeLogAreas.All`).
- Produces: `ChangeLogApiService(HttpClient)` with virtual `GetAsync(ChangeLogFilter)` and `static string BuildQuery(ChangeLogFilter)`;
  page `/staff/change-log`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Client.Tests/Services/ChangeLogApiServiceTests.cs
using FluentAssertions;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.ChangeLog;

namespace Stallions.Client.Tests.Services;

public class ChangeLogApiServiceTests
{
    [Fact]
    public void BuildQuery_IncludesTheFiltersSet_AndThePage()
    {
        ChangeLogApiService.BuildQuery(new ChangeLogFilter()).Should().Be("?page=1");
        ChangeLogApiService.BuildQuery(new ChangeLogFilter
        {
            Area = "Wording", From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 31), Page = 2
        }).Should().Be("?area=Wording&from=2026-10-01&to=2026-10-31&page=2");
    }
}
```

```csharp
// tests/Client.Tests/Pages/StaffChangeLogTests.cs
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Staff;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.ChangeLog;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class StaffChangeLogTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };
    private readonly Mock<ChangeLogApiService> _api = new(MockBehavior.Loose, Http());

    private static ChangeLogEntryDto Business() => new()
    {
        Id = 1, When = "12 Oct 2026, 2:00 pm", Who = "Sam Staff", Area = ChangeLogAreas.BusinessDetails, AreaLabel = "Business details",
        Item = "Business details",
        Changes = [new ChangeLogChangeDto { Field = "Bank account number", OldValue = "•••678", NewValue = "•••321" }]
    };

    private static ChangeLogEntryDto Wording() => new()
    {
        Id = 2, When = "12 Oct 2026, 1:00 pm", Who = "Sam Staff", Area = ChangeLogAreas.Wording, AreaLabel = "Wording",
        Item = "Outbid — version 2", Link = "/staff/wording?key=Email.Outbid&version=2",
        Changes =
        [
            new ChangeLogChangeDto { Field = "Subject", OldValue = "You've been outbid", NewValue = "Outbid" },
            new ChangeLogChangeDto { Field = "Message", IsLongText = true }
        ]
    };

    private IRenderedComponent<StaffChangeLog> Render(int total = 2)
    {
        this.AddTestAuthorization().SetAuthorized("staff@example.com");
        _api.Setup(a => a.GetAsync(It.IsAny<ChangeLogFilter>())).ReturnsAsync((ChangeLogFilter f) => new ChangeLogPageDto
        {
            Entries = [Business(), Wording()], Page = f.Page, PageSize = 50, Total = total
        });
        Services.AddSingleton(_api.Object);
        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "Staff", Email = "staff@example.com", Role = "Staff", Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));
        return RenderComponent<StaffChangeLog>();
    }

    [Fact]
    public void Lists_TheChanges_WithOldAndNewValues_AndMaskedAccountNumbers()
    {
        var cut = Render();

        cut.WaitForAssertion(() => cut.FindAll(".change-log-table tbody tr").Should().HaveCount(2));
        var first = cut.FindAll(".change-log-table tbody tr")[0].TextContent;
        first.Should().Contain("12 Oct 2026, 2:00 pm").And.Contain("Sam Staff").And.Contain("Business details")
            .And.Contain("Bank account number:").And.Contain("•••678 → •••321");
    }

    [Fact]
    public void LongText_ShowsChanged_WithALinkToTheVersion()
    {
        var cut = Render();

        cut.WaitForAssertion(() => cut.Find("a[href='/staff/wording?key=Email.Outbid&version=2']").TextContent.Should().Be("View version"));
        var row = cut.FindAll(".change-log-table tbody tr")[1].TextContent;
        row.Should().Contain("Subject:").And.Contain("You've been outbid → Outbid").And.Contain("Message:").And.Contain("changed");
    }

    [Fact]
    public void Filters_AreSentToTheApi()
    {
        var cut = Render();
        cut.WaitForAssertion(() => cut.FindAll(".change-log-table tbody tr").Should().HaveCount(2));

        cut.Find("#cl-area").Change("Wording");
        cut.Find("#cl-from").Change("2026-10-01");
        cut.Find("#cl-to").Change("2026-10-31");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Apply").Click();

        cut.WaitForAssertion(() => _api.Verify(a => a.GetAsync(It.Is<ChangeLogFilter>(f =>
            f.Area == "Wording" && f.From == new DateOnly(2026, 10, 1) && f.To == new DateOnly(2026, 10, 31) && f.Page == 1)), Times.Once));
    }

    [Fact]
    public void Next_RequestsTheNextPage()
    {
        var cut = Render(total: 120);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Page 1 of 3"));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Next").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Page 2 of 3"));
        _api.Verify(a => a.GetAsync(It.Is<ChangeLogFilter>(f => f.Page == 2)), Times.Once);
    }

    [Fact]
    public void AnApiError_IsShown()
    {
        var cut = Render();
        _api.Setup(a => a.GetAsync(It.IsAny<ChangeLogFilter>())).ThrowsAsync(new ApiException(500, "Couldn't load the change log."));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Apply").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Couldn't load the change log."));
    }
}
```

- [ ] **Step 2: Run red** — `dotnet test tests/Client.Tests --filter "FullyQualifiedName~ChangeLog"` → compile errors.

- [ ] **Step 3: API service, registration, nav**

```csharp
// src/Client/Services/ChangeLogApiService.cs
using System.Globalization;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.ChangeLog;

namespace Stallions.Client.Services;

/// <summary>Staff-only: the change log of settings, business details, intro offers, wording and terms.</summary>
public class ChangeLogApiService
{
    private readonly HttpClient _http;
    public ChangeLogApiService(HttpClient http) => _http = http;

    public virtual async Task<ChangeLogPageDto> GetAsync(ChangeLogFilter filter)
    {
        var r = await _http.GetAsync("api/audit/changes" + BuildQuery(filter));
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<ChangeLogPageDto>() ?? throw new ApiException(500, "Empty response.");
    }

    public static string BuildQuery(ChangeLogFilter filter)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(filter.Area)) query.Add($"area={Uri.EscapeDataString(filter.Area)}");
        if (filter.From is { } from) query.Add($"from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        if (filter.To is { } to) query.Add($"to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        query.Add($"page={Math.Max(1, filter.Page)}");
        return "?" + string.Join("&", query);
    }
}
```

`src/Client/Program.cs` — after the `ContentApiService` registration add:

```csharp
// src/Client/Program.cs (after ContentApiService)
builder.Services.AddHttpClient<ChangeLogApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
```

`src/Client/_Imports.razor` — add `@using Stallions.Shared.DTOs.ChangeLog`.

`src/Client/Layout/StaffLayout.razor` — after the Wording `NavLink` (Task 10) add:

```razor
@* src/Client/Layout/StaffLayout.razor (after Wording) *@
            <NavLink href="/staff/change-log" class="admin-nav-link" Match="NavLinkMatch.Prefix">
                <span class="admin-nav-icon">🕘</span> Change log
            </NavLink>
```

- [ ] **Step 4: The page**

```razor
@* src/Client/Pages/Staff/StaffChangeLog.razor *@
@page "/staff/change-log"
@layout StaffLayout
@attribute [Authorize]
@inject ChangeLogApiService ChangeLogApi
@inject UserStateService UserState
@inject NavigationManager Nav

<PageTitle>Change log — Staff Admin</PageTitle>

<div class="admin-page-header">
    <h1 class="admin-page-title">Change log</h1>
</div>
<p class="text-secondary text-sm">
    Staff changes to platform settings, business details, intro offers, wording and terms, and each stud's acceptance of
    the stud listing terms. Times are Sydney time. Bank account numbers show only their last 3 digits.
</p>

<div class="change-log-filters">
    <label class="form-label" for="cl-area">Area</label>
    <select id="cl-area" class="form-select" @bind="_area">
        <option value="">All areas</option>
        @foreach (var (value, label) in ChangeLogAreas.All)
        {
            <option value="@value">@label</option>
        }
    </select>
    <label class="form-label" for="cl-from">From</label>
    <input id="cl-from" type="date" class="form-input" @bind="_from" />
    <label class="form-label" for="cl-to">To</label>
    <input id="cl-to" type="date" class="form-input" @bind="_to" />
    <button class="btn btn-gold btn-sm" @onclick="ApplyAsync" disabled="@_loading">Apply</button>
</div>

@if (_error is not null)
{
    <div class="alert alert-danger">@_error</div>
}

@if (_loading)
{
    <p>Loading…</p>
}
else if (_page is null || _page.Entries.Count == 0)
{
    <div class="admin-empty-state"><p>No changes match these filters.</p></div>
}
else
{
    <table class="admin-table change-log-table">
        <thead>
            <tr><th>When</th><th>Who</th><th>Area</th><th>Item</th><th>What changed</th></tr>
        </thead>
        <tbody>
            @foreach (var e in _page.Entries)
            {
                <tr>
                    <td class="text-sm change-log-when">@e.When</td>
                    <td>@e.Who</td>
                    <td>@e.AreaLabel</td>
                    <td>
                        @e.Item
                        @if (e.Note is not null)
                        {
                            <div class="text-muted text-sm">@e.Note</div>
                        }
                    </td>
                    <td>
                        <ul class="change-log-changes">
                            @foreach (var c in e.Changes)
                            {
                                <li>
                                    <span class="change-log-field">@c.Field:</span>
                                    @if (c.IsLongText)
                                    {
                                        <span>changed</span>
                                        @if (e.Link is not null)
                                        {
                                            <a href="@e.Link">View version</a>
                                        }
                                    }
                                    else
                                    {
                                        <span>@(c.OldValue ?? "—") → @(c.NewValue ?? "—")</span>
                                    }
                                </li>
                            }
                        </ul>
                    </td>
                </tr>
            }
        </tbody>
    </table>

    <div class="change-log-pager">
        <button class="btn btn-sm btn-outline" @onclick="() => LoadAsync(_page.Page - 1)" disabled="@(_page.Page <= 1)">Previous</button>
        <span class="text-sm">Page @_page.Page of @_page.TotalPages · @_page.Total changes</span>
        <button class="btn btn-sm btn-outline" @onclick="() => LoadAsync(_page.Page + 1)" disabled="@(_page.Page >= _page.TotalPages)">Next</button>
    </div>
}

@code {
    private string _area = string.Empty;
    private DateOnly? _from;
    private DateOnly? _to;
    private ChangeLogPageDto? _page;
    private bool _loading = true;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        await UserState.LoadAsync();
        if (!UserState.IsStaff) { Nav.NavigateTo("/", replace: true); return; }
        await LoadAsync(1);
    }

    private Task ApplyAsync() => LoadAsync(1);

    private async Task LoadAsync(int page)
    {
        _loading = true; _error = null;
        try
        {
            _page = await ChangeLogApi.GetAsync(new ChangeLogFilter
            {
                Area = string.IsNullOrEmpty(_area) ? null : _area, From = _from, To = _to, Page = page
            });
        }
        catch (Exception ex)
        {
            _page = null;
            _error = ex is ApiException ae ? ae.Message : "Failed to load the change log.";
        }
        finally { _loading = false; }
    }
}
```

```css
/* src/Client/Pages/Staff/StaffChangeLog.razor.css */
.change-log-filters {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--space-3);
    margin: var(--space-4) 0;
}

.change-log-filters .form-label { margin: 0; }
.change-log-filters .form-select, .change-log-filters .form-input { width: auto; min-width: 150px; }

.change-log-when { white-space: nowrap; }

.change-log-changes {
    list-style: none;
    margin: 0;
    padding: 0;
    font-size: var(--font-size-sm);
}

.change-log-changes li + li { margin-top: var(--space-1); }
.change-log-field { color: var(--text-secondary); margin-right: var(--space-1); }

.change-log-pager {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: var(--space-3);
    margin-top: var(--space-4);
}
```

- [ ] **Step 5: Run green** — `dotnet test tests/Client.Tests --filter "FullyQualifiedName~ChangeLog"`; full build (0 warnings) and full test run.

- [ ] **Step 6: Commit**

```bash
git add src/Client/Services/ChangeLogApiService.cs src/Client/Program.cs src/Client/_Imports.razor \
  src/Client/Pages/Staff/StaffChangeLog.razor src/Client/Pages/Staff/StaffChangeLog.razor.css src/Client/Layout/StaffLayout.razor \
  tests/Client.Tests/Pages/StaffChangeLogTests.cs tests/Client.Tests/Services/ChangeLogApiServiceTests.cs
git status --short
git commit -m "feat: Staff change log page — area and date filters, old/new values, version links, paging

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 13: Final check, push, dev rollout (David deploys)

- [ ] **Step 1: Whole-branch check** — `git status` clean; `dotnet build stallions-nominations-marketplace.slnx` (0 warnings);
  `dotnet test stallions-nominations-marketplace.slnx` twice (green); `dotnet ef migrations has-pending-model-changes --project src/Server --startup-project src/Server` → no changes;
  `grep -rn "DisclosureOptions\|Stallions.Server.Options\|\"Checkout\"\|Markdig.Markdown.ToHtml" src tests --include=*.cs --include=*.razor --include=*.json`
  → no matches outside `bin`/`obj` (the only Markdig call is inside `TermsMarkdown`).
- [ ] **Step 2: Local smoke of what the in-memory tests can't reach** — against localdb: `SELECT [Key], Version, CreatedByUserId FROM ContentItems ORDER BY [Key]`
  → 25 rows, version 1, null author; `SELECT Audience, Version FROM TermsDocuments` → existing rows are `Buyer`; inserting a second
  `Buyer` row with an existing version fails on `IX_TermsDocuments_Audience_Version` while a `Stud` row with the same number succeeds
  (delete the test rows afterwards); `SELECT name FROM sys.indexes WHERE name = 'IX_TermsDocuments_Version'` → no row.
- [ ] **Step 3: Docs** — spec **Status** → "Built on `feature/v2-phase5b-editable-content` (date); not merged"; add a
  "Changes made during the build" block to this plan if reviews changed anything; commit `docs: phase 5b spec status` (message ends with the
  blank line and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`).
- [ ] **Step 4: Push** — `git push -u origin feature/v2-phase5b-editable-content`. Don't merge or open a PR unless David asks.
- [ ] **Step 5: Dev rollout (David runs `azd deploy api --environment dev`; no infra change, no provision needed; the API applies the migration
  on start; no app setting referenced the removed `Checkout` section)** — then check from outside: site and `api/listings` 200;
  `GET api/disclosures/buyer-fee` 200 signed out with today's wording; `GET api/content` 401 signed out; `GET api/audit/changes` 401 signed out;
  `GET api/terms/stud/current` 401 signed out.
- [ ] **Step 6: Dev click-through (fake provider; test users StallNomsManager01 / StallNomBuyer01 / PaddockSide; Staff account):**
  1. As Staff, **Wording** (new nav link): the list shows Disclosures, Buyer emails, Stud emails and Tax document emails, every item "v1 ·
     … by System default". Open **Outbid**: subject and message show today's wording, the placeholders table lists `{StallionName}` and
     `{HighBid}` as Required, and the preview shows "You've been outbid on Snitzel".
  2. Type `{Stalion}` into the message → the preview is replaced by "{Stalion} isn't available in this email." (and "must include
     {StallionName}" if it was removed); fix it, change the subject to "Outbid: {StallionName}" and add a second paragraph → the preview
     updates; **Save as new version** → "Saved as version 2."; Previous versions lists v2 (Current) and v1.
  3. As StallNomBuyer01 bid on an auction, then outbid them as PaddockSide (within a minute of the save is fine on one instance) → the
     outbid email in the log has the new subject and both paragraphs, followed by the "Bid again" link.
  4. As Staff, Outbid → Previous versions → **View** v1 (read-only), **Restore** v1 → confirm → "Version 1 restored as version 3.";
     edit and save again, then **Reset to default** → confirm → a new version equal to the default; Reset is then disabled.
  5. **Wording → Buyer fee explanation**: try a line break → "This wording is a single paragraph — remove the line breaks."; change the
     wording and save. Within a minute: the bid confirmation modal on a listing shows the new wording; a buyer fee tax invoice page
     (Tax invoices → open one) shows it under the invoice; win an auction (or have an offer accepted) → the win email and the
     "Tax invoice SA-…: buyer fee" email both show it in the disclosure block, with the amounts table intact. Put the original text back.
  6. **Terms & Conditions** → **Stud listing terms** → write terms with a heading and bold text (and a `<script>` line — the preview shows
     it as text) → **Publish new version** → version history shows v1 by you; **View** opens it read-only. **Buyer T&C** still shows its
     own versions.
  7. As StallNomsManager01, My Stallions → **Activate** a stallion → the Stud listing terms modal appears; **Cancel** → nothing happens;
     Activate again → tick "I have read and accept…" → **I accept** → the fake payment page opens. Back on a Draft listing,
     **Publish** goes straight through (already accepted). As Staff publish stud terms v2 → as the stud, **Publish** another Draft (or
     "Offer as Make an Offer" on an Unsold lot) → the modal shows v2 first.
  8. Staff → **Stud Farms**: the new Stud terms column shows StallNomsManager01's farm as "v2 · <date>" (or "v1 · … (not current)" before
     step 7's re-accept) and farms that never accepted as "Not accepted". Staff → Listing Fees → create a subscription for a farm that has
     not accepted → it works (Staff aren't gated).
  9. Staff → **Change log** (new nav link): entries for the wording saves / restore / reset ("Outbid — version 2", "Message: changed · View
     version" opens that version in the editor), the terms publishes ("Stud listing terms — version 1", View version opens it), the stud's
     acceptance (farm name, accepted version), and — after changing a fee in Settings and the bank account number in Business details —
     "Buyer fee (inc. GST): 150 → 175" and "Bank account number: •••678 → •••321". Filter Area = Wording and a From/To date → only those;
     Next / Previous page through more than 50 entries if there are that many. Put the fee and bank details back.
