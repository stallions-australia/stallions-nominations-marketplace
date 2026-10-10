# v2 Phase 3 — Auction Close and Automatic Buyer Fee — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A background job in the API closes ended auctions (hidden reserve, highest bid wins), charges the winner's saved card the listing's buyer fee automatically with a grace period on failure, records the sale on `Purchase`, and emails buyers and the stud through an outbox and Azure Communication Services. The interim checkout page and its shared-secret endpoint are removed.

**Architecture:** `AuctionCloser` (scoped, unit-tested) holds all close and charge logic; `AuctionCloseService` (a `BackgroundService`) calls it every minute. Every state change runs in `ITransactionRunner` transactions; the provider charge runs between two transactions with an idempotency key per attempt. `Listing`, `Purchase` and `OutboundEmail` carry a Guid `ConcurrencyStamp` that `AppDbContext` renews on every update, so two instances can't process the same row. Emails are added to an `OutboundEmails` outbox in the same transaction as the change; `EmailDispatchService` sends them through `IEmailSender` (ACS, or a log sender locally).

**Tech Stack:** .NET 9, ASP.NET Core, EF Core (SQL Server), Blazor WASM, Stripe.net 53.0.0, Azure.Communication.Email 1.1.0, Azure.Identity, Bicep/azd, xUnit + Moq + FluentAssertions, bUnit.

**Spec:** `docs/superpowers/specs/2026-10-10-v2-phase3-auction-close-design.md`. Read it and CLAUDE.md first.
**Branch:** `feature/v2-phase3-auction-close` (already created from master; the spec is committed).

> **Plan decisions that refine the spec (agreed in planning, 2026-10-10):**
> - **Concurrency token:** a Guid `ConcurrencyStamp` (EF concurrency token) renewed by
>   `AppDbContext` on every update, instead of SQL `rowversion`. The EF in-memory test provider
>   doesn't generate `rowversion` values, so a `rowversion` conflict could never be tested; the
>   Guid stamp is detected by both providers (verified with a probe test). Same protection: any
>   update to the row changes the stamp.
> - **Emails** are composed by one class, `AuctionEmails`, with one method per email, instead of
>   one class per email.
> - **Legacy pending purchases:** the migration voids any `Pending` purchases left by the
>   interim checkout (dev data only), so the new charge job never charges them. The unique
>   `BidId` index ignores voided rows (the interim checkout could create several per bid).
> - **ACS role:** the App Service identity gets the built-in **Communication and Email Service
>   Owner** role, scoped to the Communication Services resource only.
> - The first line of each code block (`// src/...`, `@* src/... *@`) only names the file —
>   **don't copy it into the file**.

---

## Ground rules

- **Test first** for every service change: write the failing test, run it red, implement, run it green.
- After each task: `dotnet build stallions-nominations-marketplace.slnx` (0 warnings) and
  `dotnet test stallions-nominations-marketplace.slnx` must be green. `tests/Server.Tests` builds the
  client too (Server references Client), so keep the client compiling at every task.
- **One commit per task**, conventional message, ending with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Line endings:** the repo stores LF, the working copy is CRLF. Stage only the files you changed.
- **No secrets anywhere in the repo.** Email uses managed identity — there is no key.
- **No floating package versions** for new packages (pin exact versions).
- **Database:** migrations go to the local dev DB (`(localdb)\mssqllocaldb`, `StallionsNomsDev`);
  Azure dev migrates itself on deploy. `azd provision` / `azd deploy` only with David's go-ahead, dev only.
- **Time:** new code reads the time from an injected `TimeProvider` (registered as
  `TimeProvider.System`); tests use `TestClock` (Task 4). Existing code is left on `DateTime.UtcNow`.
- Existing patterns to follow: `ServiceResult` / `ServiceResult<T>`; repositories in
  `Server/Data/Repositories` (each method saves); `IAuditLogRepository.LogAsync(entityType, entityId, action, userId, details)`;
  `ITransactionRunner.RunAsync` (clears the change tracker each attempt); `GstBreakdown.FromIncGst`;
  `PaymentAmounts.ToCents`; tests use `DbContextFactory.Create(name)` (in-memory) and `InlineTransactionRunner`.

## File map

| File | Responsibility |
|---|---|
| `src/Shared/Enums/ListingStatus.cs`, `BidStatus.cs`, `ListingCloseReason.cs` | New statuses and the close reason |
| `src/Server/Data/Entities/IHasConcurrencyStamp.cs` | Marker for rows protected by `ConcurrencyStamp` |
| `src/Server/Data/Entities/OutboundEmail.cs` | Outbox row |
| `src/Server/Data/AppDbContext.cs` | New columns, indexes, stamp renewal on save |
| `src/Server/Services/PurchaseService.cs` (+ `IPurchaseService`) | Sale records: list, get, Staff refund, buyer's auction result |
| `src/Server/Options/DisclosureOptions.cs` | Buyer-fee wording (was `CheckoutOptions`) |
| `src/Server/Payments/Charges.cs` | `ChargeRequest`, `ChargeResult` |
| `src/Server/Email/*` | Options + validator, senders, outbox, dispatcher, hosted service, DI |
| `src/Server/Email/AuctionEmails.cs` (+ `IAuctionEmails`) | Builds and queues every Phase 3 email |
| `src/Server/Auctions/*` | `AuctionCloseOptions`, `IAuctionCloser`, `AuctionCloser`, `AuctionCloseService` |
| `src/Server/Data/Repositories/OutboundEmailRepository.cs` (+ interface) | Outbox persistence |
| `infra/modules/communication.bicep` | ACS + Email service + Azure-managed domain |
| `src/Client/Services/PurchaseApiService.cs` | Client for sale records and the buyer's auction result |
| `tests/Server.Tests/Helpers/TestClock.cs` | Settable `TimeProvider` for tests |

---

### Task 1: Domain — statuses, close reason, charge fields, outbox table, concurrency stamp, migration

**Files:**
- Modify: `src/Shared/Enums/ListingStatus.cs`, `src/Shared/Enums/BidStatus.cs`
- Create: `src/Shared/Enums/ListingCloseReason.cs`
- Create: `src/Server/Data/Entities/IHasConcurrencyStamp.cs`, `src/Server/Data/Entities/OutboundEmail.cs`
- Modify: `src/Server/Data/Entities/Listing.cs`, `src/Server/Data/Entities/Purchase.cs`, `src/Server/Data/AppDbContext.cs`
- Create: migration `V2Phase3AuctionClose` (generated)
- Test: `tests/Server.Tests/Data/ConcurrencyStampTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Server.Tests/Data/ConcurrencyStampTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data;

public class ConcurrencyStampTests
{
    private static DbContextOptions<AppDbContext> Options(string name) =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options;

    [Fact]
    public async Task UpdatingAListing_RenewsItsStamp()
    {
        var name = Guid.NewGuid().ToString();
        var listing = new AuctionListing { Status = ListingStatus.Active, EndDateTime = DateTime.UtcNow };
        await using (var db = new AppDbContext(Options(name))) { db.AuctionListings.Add(listing); await db.SaveChangesAsync(); }
        var before = listing.ConcurrencyStamp;

        await using (var db = new AppDbContext(Options(name)))
        {
            var loaded = await db.AuctionListings.SingleAsync();
            loaded.Status = ListingStatus.AwaitingPayment;
            await db.SaveChangesAsync();
            loaded.ConcurrencyStamp.Should().NotBe(before);
        }
    }

    [Fact]
    public async Task TwoContextsUpdatingTheSameListing_SecondOneConflicts()
    {
        var name = Guid.NewGuid().ToString();
        await using (var db = new AppDbContext(Options(name)))
        {
            db.AuctionListings.Add(new AuctionListing { Status = ListingStatus.Active, EndDateTime = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        await using var first = new AppDbContext(Options(name));
        await using var second = new AppDbContext(Options(name));
        var a = await first.AuctionListings.SingleAsync();
        var b = await second.AuctionListings.SingleAsync();

        a.Status = ListingStatus.AwaitingPayment;
        await first.SaveChangesAsync();
        b.Status = ListingStatus.Unsold;

        await second.Invoking(db => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task TwoContextsUpdatingTheSamePurchase_SecondOneConflicts()
    {
        var name = Guid.NewGuid().ToString();
        await using (var db = new AppDbContext(Options(name)))
        {
            db.Purchases.Add(new Purchase { Status = PurchaseStatus.Pending });
            await db.SaveChangesAsync();
        }
        await using var first = new AppDbContext(Options(name));
        await using var second = new AppDbContext(Options(name));
        var a = await first.Purchases.SingleAsync();
        var b = await second.Purchases.SingleAsync();

        a.ChargeAttempts = 1;
        await first.SaveChangesAsync();
        b.ChargeAttempts = 1;

        await second.Invoking(db => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
```

- [ ] **Step 2: Run them red** — `dotnet test tests/Server.Tests --filter FullyQualifiedName~ConcurrencyStampTests` → compile errors (`ConcurrencyStamp`, `AwaitingPayment`, `ChargeAttempts` don't exist).

- [ ] **Step 3: Enums**

```csharp
// src/Shared/Enums/ListingStatus.cs
namespace Stallions.Shared.Enums;

public enum ListingStatus
{
    Draft,
    Active,
    Sold,
    Expired,
    Cancelled,
    /// <summary>The auction has closed with a winner; the buyer fee is being charged.</summary>
    AwaitingPayment,
    /// <summary>The auction closed without a sale — see Listing.CloseReason. Ready for Make an Offer (Phase 4).</summary>
    Unsold
}
```

```csharp
// src/Shared/Enums/ListingCloseReason.cs
namespace Stallions.Shared.Enums;

/// <summary>Why an auction closed without a sale.</summary>
public enum ListingCloseReason
{
    NoBids,
    ReserveNotMet,
    ChargeFailed
}
```

In `src/Shared/Enums/BidStatus.cs` add `Lost` as the last value, with the comment
`// Another bid won the auction (set when the auction closes).`

- [ ] **Step 4: Concurrency marker, outbox entity, entity fields**

```csharp
// src/Server/Data/Entities/IHasConcurrencyStamp.cs
namespace Stallions.Server.Data.Entities;

/// <summary>
/// Rows that background jobs and users may update at the same time. AppDbContext gives the stamp
/// a new value on every update, and EF rejects an update whose stamp changed since it was read
/// (DbUpdateConcurrencyException) — so two app instances can't both close an auction or charge
/// a purchase.
/// </summary>
public interface IHasConcurrencyStamp
{
    Guid ConcurrencyStamp { get; set; }
}
```

```csharp
// src/Server/Data/Entities/OutboundEmail.cs
namespace Stallions.Server.Data.Entities;

/// <summary>
/// An email waiting to be sent (or sent). Added in the same transaction as the change that
/// caused it, then sent by EmailDispatchService.
/// </summary>
public class OutboundEmail : IHasConcurrencyStamp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ToAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public string TextBody { get; set; } = string.Empty;
    /// <summary>Which email this is, e.g. "WonAndCharged" — for support and tests.</summary>
    public string Template { get; set; } = string.Empty;
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public int Attempts { get; set; }
    /// <summary>Not before this time. Also used as a short lease while one instance is sending.</summary>
    public DateTime? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    /// <summary>Set when the dispatcher gives up.</summary>
    public DateTime? FailedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
```

In `src/Server/Data/Entities/Listing.cs`: make the class `public class Listing : IHasConcurrencyStamp` and
add after `ClosedAt`:

```csharp
    /// <summary>Why an auction closed without a sale. Set only when Status is Unsold.</summary>
    public ListingCloseReason? CloseReason { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
```

In `src/Server/Data/Entities/Purchase.cs`: make the class `public class Purchase : IHasConcurrencyStamp`
and add after `CreatedAt`:

```csharp
    // ── Automatic buyer-fee charge (Phase 3). The purchase is the sale record. ──
    /// <summary>Charge attempts made so far; also numbers the idempotency key of each attempt.</summary>
    public int ChargeAttempts { get; set; }
    /// <summary>Set while an attempt is in flight. Older than 2 minutes = interrupted, repeated with the same key.</summary>
    public DateTime? ChargeAttemptStartedAt { get; set; }
    /// <summary>End of the grace period, set at the first failed charge.</summary>
    public DateTime? ChargeDueBy { get; set; }
    public string? LastChargeFailure { get; set; }
    /// <summary>The winner saved a new card after a failed charge; the next run retries.</summary>
    public bool RetryRequested { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
```

- [ ] **Step 5: `AppDbContext`**

Add `public DbSet<OutboundEmail> OutboundEmails => Set<OutboundEmail>();` after `ProcessedPaymentEvents`.

In the `Listing` configuration block (next to `e.Property(l => l.Status)...`):

```csharp
            e.Property(l => l.CloseReason).HasConversion<string>().HasMaxLength(20);
            e.Property(l => l.ConcurrencyStamp).IsConcurrencyToken();
```

In the `Purchase` configuration block (after `PaymentReference`):

```csharp
            e.Property(p => p.LastChargeFailure).HasMaxLength(500);
            e.Property(p => p.ConcurrencyStamp).IsConcurrencyToken();
            // One winning bid can never have two live sale records. Voided rows are excluded:
            // the removed interim checkout could leave several per bid on dev data.
            e.HasIndex(p => p.BidId).IsUnique().HasFilter("[BidId] IS NOT NULL AND [Status] <> 'Voided'");
            e.HasIndex(p => new { p.Status, p.ChargeDueBy });
```

Add a new block after the `ProcessedPaymentEvents` configuration:

```csharp
        // ── Outbound emails (outbox) ─────────────────────────────────────────
        modelBuilder.Entity<OutboundEmail>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.ToAddress).HasMaxLength(320).IsRequired();
            e.Property(m => m.Subject).HasMaxLength(300).IsRequired();
            e.Property(m => m.Template).HasMaxLength(50).IsRequired();
            e.Property(m => m.RelatedEntityType).HasMaxLength(50);
            e.Property(m => m.LastError).HasMaxLength(1000);
            e.Property(m => m.ConcurrencyStamp).IsConcurrencyToken();
            e.HasIndex(m => new { m.SentAt, m.FailedAt, m.NextAttemptAt });
        });
```

Add the stamp renewal (inside the class, after `OnModelCreating`). Overriding the `bool` overloads
covers every `SaveChanges`/`SaveChangesAsync` call:

```csharp
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RenewConcurrencyStamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RenewConcurrencyStamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // The original stamp is what EF checks against the database; the new one marks this update.
    private void RenewConcurrencyStamps()
    {
        foreach (var entry in ChangeTracker.Entries<IHasConcurrencyStamp>())
            if (entry.State == EntityState.Modified)
                entry.Entity.ConcurrencyStamp = Guid.NewGuid();
    }
```

- [ ] **Step 6: Run the tests green** — `dotnet test tests/Server.Tests --filter FullyQualifiedName~ConcurrencyStampTests` → 3 passed.

- [ ] **Step 7: Migration**

Run: `dotnet ef migrations add V2Phase3AuctionClose --project src/Server --startup-project src/Server --output-dir Data/Migrations`

Open the generated `..._V2Phase3AuctionClose.cs`. At the **start** of `Up` — before the unique
`BidId` index is created — add (dev data only; no real purchases exist). It stops the new job
charging leftovers from the interim checkout, and voids the duplicates that would otherwise break
the unique index:

```csharp
            // Pending purchases left by the removed interim checkout must never be charged.
            migrationBuilder.Sql("UPDATE Purchases SET Status = 'Voided' WHERE Status = 'Pending';");
```

The existing `ConcurrencyStamp` columns are added with an all-zero default for existing rows —
that's fine (the next update gives each row a new stamp).

Check: `dotnet ef migrations has-pending-model-changes --project src/Server --startup-project src/Server` → "No changes have been made…".

Apply locally: `dotnet ef database update --project src/Server --startup-project src/Server --connection "Server=(localdb)\mssqllocaldb;Database=StallionsNomsDev;Trusted_Connection=True;MultipleActiveResultSets=true"`

- [ ] **Step 8: Build and test everything** — `dotnet build stallions-nominations-marketplace.slnx` (0 warnings) and `dotnet test stallions-nominations-marketplace.slnx` → all green.

- [ ] **Step 9: Commit**

```bash
git add src/Shared/Enums/ListingStatus.cs src/Shared/Enums/BidStatus.cs src/Shared/Enums/ListingCloseReason.cs src/Server/Data/Entities/IHasConcurrencyStamp.cs src/Server/Data/Entities/OutboundEmail.cs src/Server/Data/Entities/Listing.cs src/Server/Data/Entities/Purchase.cs src/Server/Data/AppDbContext.cs src/Server/Data/Migrations tests/Server.Tests/Data/ConcurrencyStampTests.cs
git commit -m "feat: auction close domain — statuses, charge fields, outbox, concurrency stamp"
```

---

### Task 2: Remove the interim checkout; `PurchaseService` and `DisclosureOptions`

**Files:**
- Delete: `src/Server/Services/CheckoutService.cs`, `src/Server/Services/ICheckoutService.cs`, `src/Server/Options/CheckoutOptions.cs`,
  `src/Shared/DTOs/Checkout/CheckoutRequest.cs`, `CheckoutResponse.cs`, `CheckoutDisclosureDto.cs`,
  `src/Client/Pages/Checkout.razor`, `src/Client/Pages/Checkout.razor.css`, `src/Client/Services/CheckoutApiService.cs`,
  `tests/Server.Tests/Services/CheckoutServiceTests.cs`, `tests/Client.Tests/Pages/CheckoutTests.cs`
- Create: `src/Server/Services/IPurchaseService.cs`, `src/Server/Services/PurchaseService.cs`, `src/Server/Options/DisclosureOptions.cs`,
  `src/Client/Services/PurchaseApiService.cs`, `tests/Server.Tests/Services/PurchaseServiceTests.cs`
- Modify: `src/Server/Controllers/PurchasesController.cs`, `src/Server/Controllers/DisclosuresController.cs`, `src/Server/Program.cs`,
  `src/Server/appsettings.json`, `src/Shared/DTOs/Checkout/PurchaseDto.cs`, `src/Server/Data/Repositories/PurchaseRepository.cs`,
  `src/Server/Services/AdminService.cs`, `src/Server/Services/ListingService.cs`, `src/Client/Program.cs`, `src/Client/Pages/MyPurchases.razor`,
  `tests/Client.Tests/Pages/MyPurchasesTests.cs`, `tests/Server.Tests/Controllers/DisclosuresControllerTests.cs`, `tests/Server.Tests/Services/AdminServiceTests.cs`,
  `tests/Server.Tests/Services/ListingServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Server.Tests/Services/PurchaseServiceTests.cs
using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class PurchaseServiceTests
{
    private readonly Mock<IPurchaseRepository> _purchases = new();
    private readonly Mock<IAuditLogRepository> _audit = new();
    private readonly Mock<IUserService> _users = new();

    private PurchaseService CreateSut() => new(_purchases.Object, _audit.Object, _users.Object);

    private static User Buyer() => new() { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.Active };

    [Fact]
    public async Task Refund_RefundsTheFullBuyerFee()
    {
        var staff = new User { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active };
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(staff);
        var purchase = new Purchase
        {
            Id = Guid.NewGuid(), Status = PurchaseStatus.Completed,
            TotalPriceIncGst = 10000m, BuyerFeeIncGst = 150m, BalancePayableToStudIncGst = 9850m
        };
        _purchases.Setup(r => r.GetByIdAsync(purchase.Id)).ReturnsAsync(purchase);

        var result = await CreateSut().RefundAsync(purchase.Id);

        result.Succeeded.Should().BeTrue();
        purchase.RefundAmount.Should().Be(150m);
        purchase.Status.Should().Be(PurchaseStatus.Refunded);
    }

    [Fact]
    public async Task Refund_ByABuyer_IsForbidden()
    {
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(Buyer());

        var result = await CreateSut().RefundAsync(Guid.NewGuid());

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GetPurchaseById_ForAnotherBuyersSaleRecord_IsForbidden()
    {
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(Buyer());
        var purchase = new Purchase { Id = Guid.NewGuid(), BuyerUserId = Guid.NewGuid() };
        _purchases.Setup(r => r.GetByIdAsync(purchase.Id)).ReturnsAsync(purchase);

        var result = await CreateSut().GetPurchaseByIdAsync(purchase.Id);

        result.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GetPurchases_MapsTheChargeState()
    {
        var buyer = Buyer();
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var dueBy = new DateTime(2026, 10, 10, 4, 0, 0, DateTimeKind.Utc);
        _purchases.Setup(r => r.GetByBuyerIdAsync(buyer.Id)).ReturnsAsync(new List<Purchase>
        {
            new()
            {
                BuyerUserId = buyer.Id, Status = PurchaseStatus.Pending, ChargeDueBy = dueBy,
                LastChargeFailure = "Your card was declined.",
                Listing = new AuctionListing
                {
                    Stallion = new Stallion { Name = "Snitzel" },
                    Season = new Season { Name = "2026 Season" },
                    StudFarm = new StudFarm { Name = "Arrowfield" }
                }
            }
        });

        var result = await CreateSut().GetPurchasesAsync();

        var dto = result.Value!.Single();
        dto.StallionName.Should().Be("Snitzel");
        dto.SeasonName.Should().Be("2026 Season");
        dto.StudFarmName.Should().Be("Arrowfield");
        dto.ChargeDueBy.Should().Be(dueBy);
        dto.LastChargeFailure.Should().Be("Your card was declined.");
    }
}
```

In `tests/Server.Tests/Services/AdminServiceTests.cs` add (the file already has `CreateSut()` and `_listingRepoMock`):

```csharp
    [Theory]
    [InlineData("AwaitingPayment")]
    [InlineData("Unsold")]
    public async Task ForceListingStatus_CannotSetStatusesOwnedByTheAuctionCloser(string status)
    {
        var listing = new AuctionListing { Id = Guid.NewGuid(), Status = ListingStatus.Active };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        var result = await CreateSut().ForceListingStatusAsync(listing.Id,
            new ForceListingStatusRequest { Status = status });

        result.HttpStatusCode.Should().Be(400);
        listing.Status.Should().Be(ListingStatus.Active);
    }
```

In `tests/Server.Tests/Services/ListingServiceTests.cs` add (next to `CloseByStudFarmAsync_SetsCancelledAndClosedAt`, using the same helpers):

```csharp
    [Theory]
    [InlineData(ListingStatus.AwaitingPayment)]
    [InlineData(ListingStatus.Unsold)]
    public async Task CloseByStudFarmAsync_AfterTheAuctionClosed_IsRejected(ListingStatus status)
    {
        var caller = FarmUser(); var farm = FarmFor(caller);
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        var listing = new AuctionListing { Id = Guid.NewGuid(), StudFarmId = farm.Id, Status = status };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        var result = await CreateSut().CloseByStudFarmAsync(listing.Id);

        result.Succeeded.Should().BeFalse();
        _listingRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Listing>()), Times.Never);
    }
```

- [ ] **Step 2: Run red** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~PurchaseServiceTests|FullyQualifiedName~ForceListingStatus|FullyQualifiedName~ListingServiceTests"` → compile errors (`PurchaseService` missing).

- [ ] **Step 3: Server — options and DTOs**

```csharp
// src/Server/Options/DisclosureOptions.cs
namespace Stallions.Server.Options;

/// <summary>
/// The configured buyer-fee wording shown before bidding and in win emails. Bound from the
/// existing "Checkout" section so no deployed setting has to change. Never hardcode this text.
/// </summary>
public class DisclosureOptions
{
    public const string Section = "Checkout";
    public string StudFarmBalanceArrangement { get; set; } = string.Empty;
    public string BuyerFeeExplanation { get; set; } = string.Empty;
    public string SavedCardExplanation { get; set; } = string.Empty;
}
```

Delete `CheckoutOptions.cs`. In `DisclosuresController` replace `CheckoutOptions` with `DisclosureOptions`
(same three fields). In `tests/Server.Tests/Controllers/DisclosuresControllerTests.cs` replace
`CheckoutOptions` with `DisclosureOptions` and remove any `WebhookSecret` initialiser.

In `src/Server/appsettings.json` remove the `"WebhookSecret": "PLACEHOLDER_SET_IN_KEYVAULT",` line.
(Your local, gitignored `appsettings.Development.json` may still have a `WebhookSecret`; it's now ignored — delete it when convenient.)

Replace `src/Shared/DTOs/Checkout/PurchaseDto.cs`:

```csharp
// src/Shared/DTOs/Checkout/PurchaseDto.cs
namespace Stallions.Shared.DTOs.Checkout;

/// <summary>A sale record: the price, the buyer fee (three GST values) and the balance payable to the stud.</summary>
public class PurchaseDto
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public string StallionName { get; set; } = string.Empty;
    public string SeasonName { get; set; } = string.Empty;
    public string StudFarmName { get; set; } = string.Empty;
    public Guid BuyerUserId { get; set; }
    public decimal TotalPriceIncGst { get; set; }
    public decimal BuyerFeeIncGst { get; set; }
    public decimal BuyerFeeExGst { get; set; }
    public decimal BuyerFeeGst { get; set; }
    public decimal BalancePayableToStudIncGst { get; set; }
    public string? PaymentProvider { get; set; }
    public string? PaymentReference { get; set; }
    public DateTime? PaidAt { get; set; }
    /// <summary>Pending (charging), Completed (paid), Voided (no sale), Refunded.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Set after a failed charge: the winner must update their card by this time (UTC).</summary>
    public DateTime? ChargeDueBy { get; set; }
    public string? LastChargeFailure { get; set; }
    public decimal? RefundAmount { get; set; }
    public DateTime? RefundedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
```

Delete `CheckoutRequest.cs`, `CheckoutResponse.cs`, `CheckoutDisclosureDto.cs`.

- [ ] **Step 4: Server — repository includes**

In `PurchaseRepository`, make the buyer and by-id queries load what the sale record shows:

```csharp
    public async Task<Purchase?> GetByIdAsync(Guid id) =>
        await WithListingDetails(_db.Purchases).Include(p => p.Buyer)
            .FirstOrDefaultAsync(p => p.Id == id);

    public async Task<IReadOnlyList<Purchase>> GetByBuyerIdAsync(Guid buyerUserId) =>
        await WithListingDetails(_db.Purchases).Where(p => p.BuyerUserId == buyerUserId)
            .OrderByDescending(p => p.CreatedAt).ToListAsync();

    private static IQueryable<Purchase> WithListingDetails(IQueryable<Purchase> q) =>
        q.Include(p => p.Listing).ThenInclude(l => l.Stallion)
         .Include(p => p.Listing).ThenInclude(l => l.Season)
         .Include(p => p.Listing).ThenInclude(l => l.StudFarm);
```

(`GetAllAsync` already includes Stallion and StudFarm; add `.Include(p => p.Listing).ThenInclude(l => l.Season)` to it.)

- [ ] **Step 5: Server — `PurchaseService`**

```csharp
// src/Server/Services/IPurchaseService.cs
using Stallions.Shared.DTOs.Checkout;

namespace Stallions.Server.Services;

/// <summary>Sale records (Purchase): created by the auction closer, read by buyers and Staff.</summary>
public interface IPurchaseService
{
    Task<ServiceResult<IReadOnlyList<PurchaseDto>>> GetPurchasesAsync();
    Task<ServiceResult<PurchaseDto>> GetPurchaseByIdAsync(Guid id);
    Task<ServiceResult> RefundAsync(Guid id);
}
```

```csharp
// src/Server/Services/PurchaseService.cs
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.Checkout;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public class PurchaseService : IPurchaseService
{
    private readonly IPurchaseRepository _purchaseRepo;
    private readonly IAuditLogRepository _auditRepo;
    private readonly IUserService _users;

    public PurchaseService(IPurchaseRepository purchaseRepo, IAuditLogRepository auditRepo, IUserService users)
    {
        _purchaseRepo = purchaseRepo;
        _auditRepo = auditRepo;
        _users = users;
    }

    public async Task<ServiceResult<IReadOnlyList<PurchaseDto>>> GetPurchasesAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<IReadOnlyList<PurchaseDto>>.Forbidden();

        IReadOnlyList<Purchase> purchases = caller.Role == UserRole.Staff
            ? await _purchaseRepo.GetAllAsync()
            : await _purchaseRepo.GetByBuyerIdAsync(caller.Id);

        return ServiceResult<IReadOnlyList<PurchaseDto>>.Ok(purchases.Select(MapToDto).ToList());
    }

    public async Task<ServiceResult<PurchaseDto>> GetPurchaseByIdAsync(Guid id)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<PurchaseDto>.Forbidden();

        var purchase = await _purchaseRepo.GetByIdAsync(id);
        if (purchase == null)
            return ServiceResult<PurchaseDto>.NotFound("Sale record not found.");

        if (caller.Role != UserRole.Staff && purchase.BuyerUserId != caller.Id)
            return ServiceResult<PurchaseDto>.Forbidden();

        return ServiceResult<PurchaseDto>.Ok(MapToDto(purchase));
    }

    public async Task<ServiceResult> RefundAsync(Guid id)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult.Forbidden();
        if (caller.Role != UserRole.Staff) return ServiceResult.Forbidden("Only Staff can process refunds.");

        var purchase = await _purchaseRepo.GetByIdAsync(id);
        if (purchase == null)
            return ServiceResult.NotFound("Sale record not found.");

        if (purchase.Status != PurchaseStatus.Completed)
            return ServiceResult.BadRequest("Only paid sale records can be refunded.");

        // Manual Staff action for genuine errors: the full buyer fee is refunded.
        purchase.RefundAmount = purchase.BuyerFeeIncGst;
        purchase.RefundedAt = DateTime.UtcNow;
        purchase.Status = PurchaseStatus.Refunded;
        await _purchaseRepo.UpdateAsync(purchase);

        await _auditRepo.LogAsync("Purchase", purchase.Id, "PurchaseRefunded",
            caller.Id, $"{{\"RefundAmount\":{purchase.RefundAmount}}}");

        return ServiceResult.Ok();
    }

    internal static PurchaseDto MapToDto(Purchase p) => new()
    {
        Id = p.Id,
        ListingId = p.ListingId,
        StallionName = p.Listing?.Stallion?.Name ?? string.Empty,
        SeasonName = p.Listing?.Season?.Name ?? string.Empty,
        StudFarmName = p.Listing?.StudFarm?.Name ?? string.Empty,
        BuyerUserId = p.BuyerUserId,
        TotalPriceIncGst = p.TotalPriceIncGst,
        BuyerFeeIncGst = p.BuyerFeeIncGst,
        BuyerFeeExGst = p.BuyerFeeExGst,
        BuyerFeeGst = p.BuyerFeeGst,
        BalancePayableToStudIncGst = p.BalancePayableToStudIncGst,
        PaymentProvider = p.PaymentProvider,
        PaymentReference = p.PaymentReference,
        PaidAt = p.PaidAt,
        Status = p.Status.ToString(),
        ChargeDueBy = p.ChargeDueBy,
        LastChargeFailure = p.LastChargeFailure,
        RefundAmount = p.RefundAmount,
        RefundedAt = p.RefundedAt,
        CreatedAt = p.CreatedAt,
        CompletedAt = p.PaidAt
    };
}
```

Delete `CheckoutService.cs` and `ICheckoutService.cs`.

Replace `PurchasesController` (the checkout and shared-secret complete routes are gone):

```csharp
// src/Server/Controllers/PurchasesController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;

namespace Stallions.Server.Controllers;

/// <summary>Sale records. They are created only by the auction closer — there is no checkout endpoint.</summary>
[ApiController]
[Route("api")]
public class PurchasesController : ControllerBase
{
    private readonly IPurchaseService _purchases;

    public PurchasesController(IPurchaseService purchases) => _purchases = purchases;

    [HttpGet("purchases")]
    [Authorize]
    public async Task<IActionResult> GetAll()
    {
        var r = await _purchases.GetPurchasesAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("purchases/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetById(Guid id)
    {
        var r = await _purchases.GetPurchaseByIdAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("purchases/{id:guid}/refund")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> Refund(Guid id)
    {
        var r = await _purchases.RefundAsync(id);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }
}
```

In `Program.cs`: replace `builder.Services.Configure<CheckoutOptions>(builder.Configuration.GetSection("Checkout"));`
with `builder.Services.Configure<DisclosureOptions>(builder.Configuration.GetSection(DisclosureOptions.Section));`
and `builder.Services.AddScoped<ICheckoutService, CheckoutService>();` with
`builder.Services.AddScoped<IPurchaseService, PurchaseService>();`.

- [ ] **Step 6: Server — statuses only the closer may set**

In `AdminService.ForceListingStatusAsync`, after the `Enum.TryParse` check:

```csharp
        if (newStatus is ListingStatus.AwaitingPayment or ListingStatus.Unsold)
            return ServiceResult.BadRequest("Awaiting payment and Unsold are set only when an auction closes.");
```

In `ListingService`, the three guards `listing.Status == ListingStatus.Cancelled || listing.Status == ListingStatus.Sold`
(update, close by stud, cancel — around lines 167, 286, 303) become `IsFinished(listing.Status)`, with:

```csharp
    // Closed or being charged: the stud and Staff edit flows can't change it any more.
    private static bool IsFinished(ListingStatus status) =>
        status is ListingStatus.Cancelled or ListingStatus.Sold
            or ListingStatus.AwaitingPayment or ListingStatus.Unsold;
```

Keep each guard's existing error message.

- [ ] **Step 7: Client**

```csharp
// src/Client/Services/PurchaseApiService.cs
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Checkout;

namespace Stallions.Client.Services;

/// <summary>Sale records (the platform creates them when an auction closes).</summary>
public class PurchaseApiService
{
    private readonly HttpClient _http;
    public PurchaseApiService(HttpClient http) => _http = http;

    public virtual async Task<List<PurchaseDto>> GetMyPurchasesAsync()
    {
        var response = await _http.GetAsync("api/purchases");
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load sale records.");
        return await response.Content.ReadFromJsonAsync<List<PurchaseDto>>() ?? new List<PurchaseDto>();
    }
}
```

Delete `CheckoutApiService.cs`, `Pages/Checkout.razor`, `Pages/Checkout.razor.css` and
`tests/Client.Tests/Pages/CheckoutTests.cs`. In `src/Client/Program.cs` replace the
`CheckoutApiService` registration with `PurchaseApiService` (same registration style as the line it replaces).
In `MyPurchases.razor` change `@inject CheckoutApiService CheckoutApi` to
`@inject PurchaseApiService PurchaseApi` and `CheckoutApi.GetMyPurchasesAsync()` to
`PurchaseApi.GetMyPurchasesAsync()` (the page itself is reworked in Task 12). In `MyPurchasesTests`
replace `CheckoutApiService` with `PurchaseApiService`.

Delete `tests/Server.Tests/Services/CheckoutServiceTests.cs` (its Refund test now lives in `PurchaseServiceTests`).

- [ ] **Step 8: Run green** — the Step 2 filter passes; then the full build (0 warnings) and `dotnet test stallions-nominations-marketplace.slnx` → green. `grep -rn "CheckoutService\|CheckoutOptions\|WebhookSecret\|CheckoutApiService" src tests --include=*.cs --include=*.razor --include=*.json` → no matches.

- [ ] **Step 9: Commit**

```bash
git add -A src/Server/Services src/Server/Options src/Server/Controllers/PurchasesController.cs src/Server/Controllers/DisclosuresController.cs src/Server/Program.cs src/Server/appsettings.json src/Server/Data/Repositories/PurchaseRepository.cs src/Shared/DTOs/Checkout src/Client/Services src/Client/Pages/Checkout.razor src/Client/Pages/Checkout.razor.css src/Client/Pages/MyPurchases.razor src/Client/Program.cs tests/Server.Tests/Services tests/Server.Tests/Controllers/DisclosuresControllerTests.cs tests/Client.Tests/Pages/CheckoutTests.cs tests/Client.Tests/Pages/MyPurchasesTests.cs
git status --short   # only the files above
git commit -m "refactor: remove interim checkout; PurchaseService and DisclosureOptions"
```

---

### Task 3: Payment provider — charge a saved card (Stripe, and a declining card for the fake)

**Files:**
- Create: `src/Server/Payments/Charges.cs`
- Modify: `src/Server/Payments/IPaymentProvider.cs`, `src/Server/Payments/FakePaymentProvider.cs`, `src/Server/Controllers/FakePaymentController.cs`,
  `src/Server/Payments/Stripe/IStripeApi.cs`, `src/Server/Payments/Stripe/StripeApi.cs`, `src/Server/Payments/Stripe/StripePaymentProvider.cs`
- Test: `tests/Server.Tests/Payments/FakePaymentProviderTests.cs`, `tests/Server.Tests/Payments/StripePaymentProviderTests.cs`, `tests/Server.Tests/Controllers/FakePaymentControllerTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `FakePaymentProviderTests`:

```csharp
    private static ChargeRequest Charge(string paymentMethodId, string key = "buyer-fee-x-1") =>
        new("cus_fake_1", paymentMethodId, 150m, "Buyer fee", new Dictionary<string, string>(), key);

    [Fact]
    public async Task ApprovingWithADecliningCard_SavesACardEndingIn0002()
    {
        var sut = new FakePaymentProvider();
        var url = await sut.CreateCardSetupSessionAsync(Guid.NewGuid(), "cus_fake_1", "/ok", "/cancel");

        var approved = sut.Approve(url.Split('/').Last(), decliningCard: true);

        var card = approved!.Value.Event.Should().BeOfType<CardSavedEvent>().Subject;
        card.Last4.Should().Be(FakePaymentProvider.DecliningLast4);
    }

    [Fact]
    public async Task ChargingADecliningCard_Fails_AndAnyOtherCard_Succeeds()
    {
        var sut = new FakePaymentProvider();
        var url = await sut.CreateCardSetupSessionAsync(Guid.NewGuid(), "cus_fake_1", "/ok", "/cancel");
        var declining = (CardSavedEvent)sut.Approve(url.Split('/').Last(), decliningCard: true)!.Value.Event;

        var failed = await sut.ChargeSavedCardAsync(Charge(declining.PaymentMethodId, "k1"));
        var paid = await sut.ChargeSavedCardAsync(Charge("pm_fake_good", "k2"));

        failed.Succeeded.Should().BeFalse();
        failed.FailureMessage.Should().Be("Your card was declined.");
        paid.Succeeded.Should().BeTrue();
        paid.PaymentReference.Should().StartWith("pi_fake_");
    }

    [Fact]
    public async Task RepeatingAChargeWithTheSameKey_ReturnsTheSameResult()
    {
        var sut = new FakePaymentProvider();

        var first = await sut.ChargeSavedCardAsync(Charge("pm_fake_good", "same-key"));
        var again = await sut.ChargeSavedCardAsync(Charge("pm_fake_good", "same-key"));

        again.Should().Be(first);
    }
```

Add to `StripePaymentProviderTests` (reuse the file's existing way of building the SUT around a
`Mock<IStripeApi>`; the names below assume `_api` and `CreateSut()`):

```csharp
    private static ChargeRequest Charge() => new(
        "cus_123", "pm_123", 150m, "Buyer fee — Snitzel, 2026 Season",
        new Dictionary<string, string> { ["purchaseId"] = "p1" }, "buyer-fee-p1-1");

    [Fact]
    public async Task ChargeSavedCard_CreatesAnOffSessionConfirmedPaymentIntentInAud()
    {
        PaymentIntentCreateOptions? sent = null;
        string? key = null;
        _api.Setup(a => a.CreatePaymentIntentAsync(It.IsAny<PaymentIntentCreateOptions>(), It.IsAny<string>()))
            .Callback<PaymentIntentCreateOptions, string>((o, k) => { sent = o; key = k; })
            .ReturnsAsync(new PaymentIntent { Id = "pi_1", Status = "succeeded" });

        var result = await CreateSut().ChargeSavedCardAsync(Charge());

        result.Should().Be(ChargeResult.Success("pi_1"));
        sent!.Amount.Should().Be(15000);
        sent.Currency.Should().Be("aud");
        sent.Customer.Should().Be("cus_123");
        sent.PaymentMethod.Should().Be("pm_123");
        sent.OffSession.Should().Be(true);
        sent.Confirm.Should().Be(true);
        sent.Metadata.Should().ContainKey("purchaseId");
        key.Should().Be("buyer-fee-p1-1");
    }

    [Fact]
    public async Task ChargeSavedCard_WhenTheCardIsDeclined_ReturnsTheDeclineMessage()
    {
        _api.Setup(a => a.CreatePaymentIntentAsync(It.IsAny<PaymentIntentCreateOptions>(), It.IsAny<string>()))
            .ThrowsAsync(new StripeException(System.Net.HttpStatusCode.PaymentRequired,
                new StripeError { Type = "card_error", Code = "card_declined", DeclineCode = "insufficient_funds",
                    Message = "Your card has insufficient funds." }, "declined"));

        var result = await CreateSut().ChargeSavedCardAsync(Charge());

        result.Succeeded.Should().BeFalse();
        result.FailureCode.Should().Be("insufficient_funds");
        result.FailureMessage.Should().Be("Your card has insufficient funds.");
    }

    [Fact]
    public async Task ChargeSavedCard_WhenTheBankWantsTheCardholderToApprove_IsAFailure()
    {
        _api.Setup(a => a.CreatePaymentIntentAsync(It.IsAny<PaymentIntentCreateOptions>(), It.IsAny<string>()))
            .ThrowsAsync(new StripeException(System.Net.HttpStatusCode.PaymentRequired,
                new StripeError { Type = "card_error", Code = "authentication_required",
                    Message = "Your card requires authentication." }, "auth"));

        var result = await CreateSut().ChargeSavedCardAsync(Charge());

        result.Succeeded.Should().BeFalse();
        result.FailureCode.Should().Be("authentication_required");
    }

    [Fact]
    public async Task ChargeSavedCard_WhenStripeIsUnreachable_Throws()
    {
        _api.Setup(a => a.CreatePaymentIntentAsync(It.IsAny<PaymentIntentCreateOptions>(), It.IsAny<string>()))
            .ThrowsAsync(new StripeException(System.Net.HttpStatusCode.InternalServerError,
                new StripeError { Type = "api_error", Message = "Server error" }, "boom"));

        await CreateSut().Invoking(s => s.ChargeSavedCardAsync(Charge())).Should().ThrowAsync<StripeException>();
    }
```

Add to `FakePaymentControllerTests`:

```csharp
    private async Task<string> NewCardSession() =>
        (await _fake.CreateCardSetupSessionAsync(Guid.NewGuid(), "cus_fake_1", "https://ok", "https://no"))
            .Split('/').Last();

    [Fact]
    public async Task Show_ForACardSetup_OffersADecliningCard()
    {
        var id = await NewCardSession();

        var result = Controller().Show(id).Should().BeOfType<ContentResult>().Subject;

        result.Content.Should().Contain($"/payments/fake/{id}/approve?declining=true")
            .And.Contain("Approve with a declining card");
    }

    [Fact]
    public async Task Show_ForAListingFee_HasNoDecliningCardButton()
    {
        var id = await NewFeeSession();

        var result = Controller().Show(id).Should().BeOfType<ContentResult>().Subject;

        result.Content.Should().NotContain("declining");
    }

    [Fact]
    public async Task Approve_WithDeclining_SavesTheDecliningCard()
    {
        var id = await NewCardSession();
        PaymentEvent? processed = null;
        _processor.Setup(p => p.ProcessAsync(It.IsAny<PaymentEvent>()))
            .Callback<PaymentEvent>(e => processed = e)
            .ReturnsAsync(PaymentEventOutcome.Processed);

        await Controller().Approve(id, declining: true);

        processed.Should().BeOfType<CardSavedEvent>().Which.Last4.Should().Be(FakePaymentProvider.DecliningLast4);
    }
```

- [ ] **Step 2: Run red** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~FakePayment|FullyQualifiedName~StripePaymentProvider"` → compile errors.

- [ ] **Step 3: Charge types and the interface**

```csharp
// src/Server/Payments/Charges.cs
namespace Stallions.Server.Payments;

/// <summary>An off-session charge of a buyer's saved card. The amount always comes from the server.</summary>
public sealed record ChargeRequest(
    string CustomerId,
    string PaymentMethodId,
    decimal AmountIncGst,
    string Description,
    IReadOnlyDictionary<string, string> Metadata,
    string IdempotencyKey);

/// <summary>
/// The outcome of a charge. A decline is a result, not an exception; an unreachable provider
/// throws, so the attempt is repeated later with the same idempotency key.
/// </summary>
public sealed record ChargeResult(bool Succeeded, string? PaymentReference, string? FailureCode, string? FailureMessage)
{
    public static ChargeResult Success(string paymentReference) => new(true, paymentReference, null, null);
    public static ChargeResult Declined(string code, string message) => new(false, null, code, message);
}
```

Add to `IPaymentProvider`:

```csharp
    /// <summary>
    /// Charges a saved card without the cardholder present. The idempotency key makes a repeated
    /// attempt return the original result instead of charging again.
    /// </summary>
    Task<ChargeResult> ChargeSavedCardAsync(ChargeRequest request);
```

- [ ] **Step 4: Fake provider**

In `FakePaymentProvider` add:

```csharp
    /// <summary>The last four digits of the fake declining card (like Stripe's 4000 0000 0000 0002).</summary>
    public const string DecliningLast4 = "0002";
    private const string DecliningPrefix = "pm_fake_decline_";

    private readonly ConcurrentDictionary<string, ChargeResult> _charges = new();

    public Task<ChargeResult> ChargeSavedCardAsync(ChargeRequest request) =>
        Task.FromResult(_charges.GetOrAdd(request.IdempotencyKey, _ =>
            request.PaymentMethodId.StartsWith(DecliningPrefix, StringComparison.Ordinal)
                ? ChargeResult.Declined("card_declined", "Your card was declined.")
                : ChargeResult.Success($"pi_fake_{Guid.NewGuid():N}")));
```

Change `Approve` to take the card choice:

```csharp
    /// <summary>Completes the session successfully. Null if the session doesn't exist (or was already used).</summary>
    public (FakeSession Session, PaymentEvent Event, string SuccessUrl, string CancelUrl)? Approve(string id, bool decliningCard = false)
    {
        if (!_sessions.TryRemove(id, out var s)) return null;
        var eventId = $"evt_fake_{Guid.NewGuid():N}";
        PaymentEvent evt = s.IsCardSetup
            ? new CardSavedEvent(eventId, s.UserId!.Value, s.CustomerId!,
                $"{(decliningCard ? DecliningPrefix : "pm_fake_")}{Guid.NewGuid():N}",
                "visa", decliningCard ? DecliningLast4 : "4242", 12, DateTime.UtcNow.Year + 3)
            : new ListingFeePaidEvent(eventId, s.SubscriptionId!.Value, s.AmountCents, "aud", $"pi_fake_{Guid.NewGuid():N}");
        return (s, evt, s.SuccessUrl, s.CancelUrl);
    }
```

In `FakePaymentController.Show`, add a third form only for card setup (after the Approve form):

```csharp
        var declining = s.IsCardSetup
            ? $"""<form method="post" action="/payments/fake/{id}/approve?declining=true" style="display:inline"><button type="submit">Approve with a declining card</button></form>"""
            : string.Empty;
```

and put `{{declining}}` between the Approve and Decline forms in the HTML. Change the Approve action
signature to `public async Task<IActionResult> Approve(string id, [FromQuery] bool declining = false)`
and call `fake.Approve(id, declining)`.

- [ ] **Step 5: Stripe**

`IStripeApi`: add `Task<PaymentIntent> CreatePaymentIntentAsync(PaymentIntentCreateOptions options, string idempotencyKey);`

`StripeApi`:

```csharp
    public Task<PaymentIntent> CreatePaymentIntentAsync(PaymentIntentCreateOptions options, string idempotencyKey) =>
        new PaymentIntentService(_client).CreateAsync(options, new RequestOptions { IdempotencyKey = idempotencyKey });
```

`StripePaymentProvider`:

```csharp
    public async Task<ChargeResult> ChargeSavedCardAsync(ChargeRequest request)
    {
        var options = new PaymentIntentCreateOptions
        {
            Amount = PaymentAmounts.ToCents(request.AmountIncGst),
            Currency = "aud",
            Customer = request.CustomerId,
            PaymentMethod = request.PaymentMethodId,
            // The buyer isn't present: charge now, and fail rather than ask for 3-D Secure.
            OffSession = true,
            Confirm = true,
            Description = request.Description,
            Metadata = request.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value)
        };
        try
        {
            var intent = await _api.CreatePaymentIntentAsync(options, request.IdempotencyKey);
            if (intent.Status == "succeeded") return ChargeResult.Success(intent.Id);
            _logger.LogWarning("Buyer-fee PaymentIntent {PaymentIntentId} ended in status {Status}", intent.Id, intent.Status);
            return ChargeResult.Declined(intent.Status, "The payment could not be completed.");
        }
        catch (StripeException ex) when (ex.StripeError?.Type == "card_error")
        {
            // Includes authentication_required: the bank wants the cardholder to approve it.
            return ChargeResult.Declined(
                ex.StripeError.DeclineCode ?? ex.StripeError.Code ?? "card_error",
                ex.StripeError.Message ?? "Your card was declined.");
        }
    }
```

- [ ] **Step 6: Run green**, then the full build (0 warnings) and full test run → green.

- [ ] **Step 7: Commit**

```bash
git add src/Server/Payments src/Server/Controllers/FakePaymentController.cs tests/Server.Tests/Payments tests/Server.Tests/Controllers/FakePaymentControllerTests.cs
git commit -m "feat: charge a saved card off-session (Stripe PaymentIntent; fake declining card)"
```

---
### Task 4: Email infrastructure — options, senders, outbox, dispatcher, periodic service

**Files:**
- Create: `src/Server/Email/EmailOptions.cs`, `src/Server/Email/OutgoingEmail.cs`, `src/Server/Email/IEmailSender.cs`,
  `src/Server/Email/LogEmailSender.cs`, `src/Server/Email/AcsEmailSender.cs`, `src/Server/Email/IEmailOutbox.cs`, `src/Server/Email/EmailOutbox.cs`,
  `src/Server/Email/EmailDispatcher.cs`, `src/Server/Email/EmailDispatchService.cs`, `src/Server/Email/EmailServiceCollectionExtensions.cs`,
  `src/Server/Infrastructure/PeriodicScopedService.cs`,
  `src/Server/Data/Repositories/IOutboundEmailRepository.cs`, `src/Server/Data/Repositories/OutboundEmailRepository.cs`
- Modify: `src/Server/Stallions.Server.csproj`, `src/Server/Program.cs`, `src/Server/appsettings.json`, `src/Server/Properties/launchSettings.json`
- Create tests: `tests/Server.Tests/Helpers/TestClock.cs`, `tests/Server.Tests/Email/EmailOptionsValidatorTests.cs`,
  `tests/Server.Tests/Email/EmailServiceCollectionExtensionsTests.cs`, `tests/Server.Tests/Email/EmailDispatcherTests.cs`,
  `tests/Server.Tests/Email/EmailOutboxTests.cs`, `tests/Server.Tests/Infrastructure/PeriodicScopedServiceTests.cs`

- [ ] **Step 1: Package** — `dotnet add src/Server package Azure.Communication.Email --version 1.1.0` (check the csproj shows exactly `Version="1.1.0"`).

- [ ] **Step 2: Test helper**

```csharp
// tests/Server.Tests/Helpers/TestClock.cs
namespace Stallions.Server.Tests.Helpers;

/// <summary>A TimeProvider whose time only moves when the test says so.</summary>
public class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    public DateTime UtcNow => Now.UtcDateTime;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}
```

- [ ] **Step 3: Write the failing tests**

```csharp
// tests/Server.Tests/Email/EmailOptionsValidatorTests.cs
using FluentAssertions;
using Stallions.Server.Email;

namespace Stallions.Server.Tests.Email;

public class EmailOptionsValidatorTests
{
    private static EmailOptions Acs() => new()
    {
        Provider = EmailOptions.ProviderAcs,
        AcsEndpoint = "https://acs-stallions-noms-dev.australia.communication.azure.com",
        SenderAddress = "DoNotReply@example.azurecomm.net",
        PublicBaseUrl = "https://app-stallions-noms-dev.azurewebsites.net"
    };

    [Fact]
    public void Acs_WithEndpointSenderAndBaseUrl_IsValid() =>
        EmailOptionsValidator.Validate(Acs(), "Staging").Should().BeNull();

    [Fact]
    public void Acs_WithoutEndpoint_IsRejected() =>
        EmailOptionsValidator.Validate(new EmailOptions
        {
            Provider = "Acs", SenderAddress = "a@b.net", PublicBaseUrl = "https://x"
        }, "Staging").Should().Contain("AcsEndpoint");

    [Fact]
    public void Acs_WithoutSender_IsRejected()
    {
        var o = Acs(); o.SenderAddress = "";
        EmailOptionsValidator.Validate(o, "Production").Should().Contain("SenderAddress");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://app.example.com")]
    public void PublicBaseUrl_MustBeAbsoluteHttps(string url)
    {
        var o = Acs(); o.PublicBaseUrl = url;
        EmailOptionsValidator.Validate(o, "Staging").Should().Contain("PublicBaseUrl");
    }

    [Fact]
    public void Log_IsAllowedInDevelopment() =>
        EmailOptionsValidator.Validate(new EmailOptions { Provider = "Log", PublicBaseUrl = "https://localhost:7083" },
            "Development").Should().BeNull();

    [Fact]
    public void Log_IsRefusedInProduction() =>
        EmailOptionsValidator.Validate(new EmailOptions { Provider = "Log", PublicBaseUrl = "https://x.example" },
            "Production").Should().NotBeNull();

    [Fact]
    public void UnknownProvider_IsRejected() =>
        EmailOptionsValidator.Validate(new EmailOptions { Provider = "Smtp", PublicBaseUrl = "https://x" },
            "Development").Should().Contain("Unknown");
}
```

```csharp
// tests/Server.Tests/Email/EmailServiceCollectionExtensionsTests.cs
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Stallions.Server.Email;

namespace Stallions.Server.Tests.Email;

public class EmailServiceCollectionExtensionsTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Stallions.Server";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Fact]
    public void Log_RegistersTheLogSender()
    {
        var services = new ServiceCollection();
        services.AddEmail(Config(("Email:Provider", "Log"), ("Email:PublicBaseUrl", "https://localhost:7083")), new Env("Development"));

        services.Should().Contain(d => d.ServiceType == typeof(IEmailSender) && d.ImplementationType == typeof(LogEmailSender));
    }

    [Fact]
    public void Acs_RegistersTheAcsSender()
    {
        var services = new ServiceCollection();
        services.AddEmail(Config(("Email:Provider", "Acs"), ("Email:AcsEndpoint", "https://acs.example.communication.azure.com"),
            ("Email:SenderAddress", "DoNotReply@x.azurecomm.net"), ("Email:PublicBaseUrl", "https://app.example")),
            new Env("Staging"));

        services.Should().Contain(d => d.ServiceType == typeof(IEmailSender) && d.ImplementationType == typeof(AcsEmailSender));
    }

    [Fact]
    public void InvalidConfiguration_StopsStartup()
    {
        var act = () => new ServiceCollection().AddEmail(Config(("Email:Provider", "Acs")), new Env("Staging"));

        act.Should().Throw<InvalidOperationException>().WithMessage("Email configuration:*");
    }
}
```

```csharp
// tests/Server.Tests/Email/EmailOutboxTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Tests.Helpers;

namespace Stallions.Server.Tests.Email;

public class EmailOutboxTests
{
    [Fact]
    public async Task Enqueue_SavesAnUnsentRow()
    {
        using var db = DbContextFactory.Create(Guid.NewGuid().ToString());
        var clock = new TestClock();
        var outbox = new EmailOutbox(new OutboundEmailRepository(db), clock, NullLogger<EmailOutbox>.Instance);
        var listingId = Guid.NewGuid();

        await outbox.EnqueueAsync(new OutgoingEmail("buyer@example.com", "Subject", "<p>Hi</p>", "Hi", "Outbid"),
            "Listing", listingId);

        var row = await db.OutboundEmails.SingleAsync();
        row.ToAddress.Should().Be("buyer@example.com");
        row.Template.Should().Be("Outbid");
        row.RelatedEntityId.Should().Be(listingId);
        row.CreatedAt.Should().Be(clock.UtcNow);
        row.SentAt.Should().BeNull();
    }

    [Fact]
    public async Task Enqueue_WithoutAnAddress_IsSkipped()
    {
        using var db = DbContextFactory.Create(Guid.NewGuid().ToString());
        var outbox = new EmailOutbox(new OutboundEmailRepository(db), new TestClock(), NullLogger<EmailOutbox>.Instance);

        await outbox.EnqueueAsync(new OutgoingEmail(" ", "Subject", "<p>Hi</p>", "Hi", "Outbid"));

        (await db.OutboundEmails.CountAsync()).Should().Be(0);
    }
}
```

```csharp
// tests/Server.Tests/Email/EmailDispatcherTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Tests.Helpers;

namespace Stallions.Server.Tests.Email;

public class EmailDispatcherTests
{
    private sealed class FakeSender : IEmailSender
    {
        public List<OutgoingEmail> Sent { get; } = new();
        public bool Fail { get; set; }
        public Task SendAsync(OutgoingEmail email, CancellationToken ct)
        {
            if (Fail) throw new InvalidOperationException("ACS unavailable");
            Sent.Add(email);
            return Task.CompletedTask;
        }
    }

    private readonly string _name = Guid.NewGuid().ToString();
    private readonly TestClock _clock = new();
    private readonly FakeSender _sender = new();

    private AppDbContext Db() => DbContextFactory.Create(_name);

    private EmailDispatcher Sut(AppDbContext db) =>
        new(new OutboundEmailRepository(db), _sender, _clock, NullLogger<EmailDispatcher>.Instance);

    private async Task<Guid> Queue(DateTime? nextAttemptAt = null)
    {
        await using var db = Db();
        var row = new OutboundEmail
        {
            ToAddress = "buyer@example.com", Subject = "S", HtmlBody = "<p>B</p>", TextBody = "B",
            Template = "Outbid", CreatedAt = _clock.UtcNow, NextAttemptAt = nextAttemptAt
        };
        db.OutboundEmails.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private async Task<OutboundEmail> Row(Guid id)
    {
        await using var db = Db();
        return await db.OutboundEmails.SingleAsync(e => e.Id == id);
    }

    [Fact]
    public async Task SendsDueEmails_AndMarksThemSent()
    {
        var id = await Queue();

        var sent = await Sut(Db()).SendDueAsync();

        sent.Should().Be(1);
        _sender.Sent.Single().ToAddress.Should().Be("buyer@example.com");
        (await Row(id)).SentAt.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public async Task AFailure_IsRetriedAfterABackoff()
    {
        var id = await Queue();
        _sender.Fail = true;

        await Sut(Db()).SendDueAsync();

        var row = await Row(id);
        row.SentAt.Should().BeNull();
        row.Attempts.Should().Be(1);
        row.LastError.Should().Contain("ACS unavailable");
        row.NextAttemptAt.Should().Be(_clock.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task NotYetDue_IsLeftAlone()
    {
        await Queue(nextAttemptAt: _clock.UtcNow.AddMinutes(5));

        (await Sut(Db()).SendDueAsync()).Should().Be(0);
        _sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task AfterFiveFailures_GivesUp()
    {
        var id = await Queue();
        _sender.Fail = true;
        for (var i = 0; i < 5; i++)
        {
            await Sut(Db()).SendDueAsync();
            _clock.Advance(TimeSpan.FromHours(2));
        }

        var row = await Row(id);
        row.Attempts.Should().Be(5);
        row.FailedAt.Should().NotBeNull();

        _sender.Fail = false;
        (await Sut(Db()).SendDueAsync()).Should().Be(0);
    }
}
```

```csharp
// tests/Server.Tests/Infrastructure/PeriodicScopedServiceTests.cs
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Stallions.Server.Infrastructure;

namespace Stallions.Server.Tests.Infrastructure;

public class PeriodicScopedServiceTests
{
    private sealed class CountingService(IServiceScopeFactory scopes) : PeriodicScopedService(scopes, NullLogger.Instance)
    {
        public int Runs;
        protected override TimeSpan Interval => TimeSpan.FromMilliseconds(20);
        protected override Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
        {
            Interlocked.Increment(ref Runs);
            // A failing run must not stop the loop.
            return Runs == 1 ? throw new InvalidOperationException("first run fails") : Task.CompletedTask;
        }
    }

    [Fact]
    public async Task KeepsRunning_AfterARunFails_AndStopsCleanly()
    {
        var scopes = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var sut = new CountingService(scopes);

        await sut.StartAsync(CancellationToken.None);
        for (var i = 0; i < 250 && Volatile.Read(ref sut.Runs) < 3; i++) await Task.Delay(20);
        await sut.StopAsync(CancellationToken.None);

        sut.Runs.Should().BeGreaterThanOrEqualTo(3);
    }
}
```

- [ ] **Step 4: Run red** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~Email|FullyQualifiedName~PeriodicScopedService"` → compile errors.

- [ ] **Step 5: Implement**

```csharp
// src/Server/Email/EmailOptions.cs
namespace Stallions.Server.Email;

/// <summary>The "Email" configuration section. ACS uses the app's managed identity — there is no key.</summary>
public class EmailOptions
{
    public const string Section = "Email";
    public const string ProviderAcs = "Acs";
    public const string ProviderLog = "Log";

    /// <summary>"Acs", or "Log" (writes emails to the log; Development/Staging only).</summary>
    public string Provider { get; set; } = string.Empty;
    /// <summary>The Communication Services endpoint, e.g. https://acs-….communication.azure.com.</summary>
    public string AcsEndpoint { get; set; } = string.Empty;
    /// <summary>The verified sender, e.g. DoNotReply@….azurecomm.net.</summary>
    public string SenderAddress { get; set; } = string.Empty;
    /// <summary>The site's public address, used for links in emails.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;
}

public static class EmailOptionsValidator
{
    /// <summary>Returns an error message, or null when the configuration is usable.</summary>
    public static string? Validate(EmailOptions options, string environmentName)
    {
        if (!Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var baseUrl) || baseUrl.Scheme != Uri.UriSchemeHttps)
            return "Email:PublicBaseUrl must be an absolute https URL.";

        switch (options.Provider)
        {
            case EmailOptions.ProviderLog:
                // Allow-list: emails silently going to the log must never happen in Production.
                return environmentName is "Development" or "Staging"
                    ? null
                    : "The log email sender can only run in Development or Staging.";
            case EmailOptions.ProviderAcs:
                if (!Uri.TryCreate(options.AcsEndpoint, UriKind.Absolute, out _))
                    return "Email:AcsEndpoint is missing.";
                if (string.IsNullOrWhiteSpace(options.SenderAddress) || !options.SenderAddress.Contains('@'))
                    return "Email:SenderAddress is missing.";
                return null;
            default:
                return $"Unknown email provider '{options.Provider}'. Use Acs or Log.";
        }
    }
}
```

```csharp
// src/Server/Email/OutgoingEmail.cs
namespace Stallions.Server.Email;

/// <summary>A composed email. Template names the kind of email, e.g. "WonAndCharged".</summary>
public sealed record OutgoingEmail(string ToAddress, string Subject, string HtmlBody, string TextBody, string Template);
```

```csharp
// src/Server/Email/IEmailSender.cs
namespace Stallions.Server.Email;

/// <summary>Hands one email to the email service. Throws on failure; the dispatcher retries.</summary>
public interface IEmailSender
{
    Task SendAsync(OutgoingEmail email, CancellationToken ct);
}
```

```csharp
// src/Server/Email/LogEmailSender.cs
namespace Stallions.Server.Email;

/// <summary>Local runs and tests: writes the email to the log instead of sending it.</summary>
public class LogEmailSender : IEmailSender
{
    private readonly ILogger<LogEmailSender> _log;
    public LogEmailSender(ILogger<LogEmailSender> log) => _log = log;

    public Task SendAsync(OutgoingEmail email, CancellationToken ct)
    {
        _log.LogInformation("Email ({Template}) to {To}: {Subject}\n{Body}",
            email.Template, email.ToAddress, email.Subject, email.TextBody);
        return Task.CompletedTask;
    }
}
```

```csharp
// src/Server/Email/AcsEmailSender.cs
using Azure;
using Azure.Communication.Email;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Stallions.Server.Email;

/// <summary>
/// Azure Communication Services, authenticated with the App Service's managed identity.
/// Returns once ACS has accepted the email; delivery happens asynchronously at ACS.
/// </summary>
public class AcsEmailSender : IEmailSender
{
    private readonly EmailClient _client;
    private readonly string _sender;

    public AcsEmailSender(IOptions<EmailOptions> options)
    {
        _client = new EmailClient(new Uri(options.Value.AcsEndpoint), new DefaultAzureCredential());
        _sender = options.Value.SenderAddress;
    }

    public async Task SendAsync(OutgoingEmail email, CancellationToken ct)
    {
        var content = new EmailContent(email.Subject) { Html = email.HtmlBody, PlainText = email.TextBody };
        var message = new EmailMessage(_sender, email.ToAddress, content);
        await _client.SendAsync(WaitUntil.Started, message, ct);
    }
}
```

```csharp
// src/Server/Data/Repositories/IOutboundEmailRepository.cs
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IOutboundEmailRepository
{
    Task AddAsync(OutboundEmail email);
    /// <summary>Unsent, not given up, and due (NextAttemptAt empty or passed), oldest first.</summary>
    Task<IReadOnlyList<OutboundEmail>> GetDueAsync(DateTime now, int max);
    /// <summary>Saves; false (and the row is let go) if another instance changed it first.</summary>
    Task<bool> TryUpdateAsync(OutboundEmail email);
    Task UpdateAsync(OutboundEmail email);
}
```

```csharp
// src/Server/Data/Repositories/OutboundEmailRepository.cs
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class OutboundEmailRepository : IOutboundEmailRepository
{
    private readonly AppDbContext _db;
    public OutboundEmailRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(OutboundEmail email)
    {
        _db.OutboundEmails.Add(email);
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<OutboundEmail>> GetDueAsync(DateTime now, int max) =>
        await _db.OutboundEmails
            .Where(e => e.SentAt == null && e.FailedAt == null && (e.NextAttemptAt == null || e.NextAttemptAt <= now))
            .OrderBy(e => e.CreatedAt)
            .Take(max)
            .ToListAsync();

    public async Task<bool> TryUpdateAsync(OutboundEmail email)
    {
        try
        {
            await UpdateAsync(email);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.Entry(email).State = EntityState.Detached;
            return false;
        }
    }

    public async Task UpdateAsync(OutboundEmail email)
    {
        _db.OutboundEmails.Update(email);
        await _db.SaveChangesAsync();
    }
}
```

```csharp
// src/Server/Email/IEmailOutbox.cs
namespace Stallions.Server.Email;

/// <summary>
/// Queues an email in the current database transaction, so it is saved only if the change that
/// caused it is. EmailDispatchService sends it shortly after.
/// </summary>
public interface IEmailOutbox
{
    Task EnqueueAsync(OutgoingEmail email, string? relatedEntityType = null, Guid? relatedEntityId = null);
}
```

```csharp
// src/Server/Email/EmailOutbox.cs
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;

namespace Stallions.Server.Email;

public class EmailOutbox : IEmailOutbox
{
    private readonly IOutboundEmailRepository _repo;
    private readonly TimeProvider _clock;
    private readonly ILogger<EmailOutbox> _log;

    public EmailOutbox(IOutboundEmailRepository repo, TimeProvider clock, ILogger<EmailOutbox> log)
    {
        _repo = repo;
        _clock = clock;
        _log = log;
    }

    public async Task EnqueueAsync(OutgoingEmail email, string? relatedEntityType = null, Guid? relatedEntityId = null)
    {
        if (string.IsNullOrWhiteSpace(email.ToAddress))
        {
            _log.LogWarning("Email {Template} for {EntityType} {EntityId} skipped: no recipient address",
                email.Template, relatedEntityType, relatedEntityId);
            return;
        }

        await _repo.AddAsync(new OutboundEmail
        {
            ToAddress = email.ToAddress.Trim(),
            Subject = email.Subject,
            HtmlBody = email.HtmlBody,
            TextBody = email.TextBody,
            Template = email.Template,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            CreatedAt = _clock.GetUtcNow().UtcDateTime
        });
    }
}
```

```csharp
// src/Server/Email/EmailDispatcher.cs
using Stallions.Server.Data.Repositories;

namespace Stallions.Server.Email;

public interface IEmailDispatcher
{
    /// <summary>Sends due emails; returns how many were sent.</summary>
    Task<int> SendDueAsync(CancellationToken ct = default);
}

public class EmailDispatcher : IEmailDispatcher
{
    public const int MaxAttempts = 5;
    private const int BatchSize = 50;
    // Wait after the 1st, 2nd, 3rd and 4th failure.
    private static readonly TimeSpan[] Backoff =
        { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60) };
    // While one instance sends a row, it is "not due" for the others.
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private readonly IOutboundEmailRepository _repo;
    private readonly IEmailSender _sender;
    private readonly TimeProvider _clock;
    private readonly ILogger<EmailDispatcher> _log;

    public EmailDispatcher(IOutboundEmailRepository repo, IEmailSender sender, TimeProvider clock, ILogger<EmailDispatcher> log)
    {
        _repo = repo;
        _sender = sender;
        _clock = clock;
        _log = log;
    }

    public async Task<int> SendDueAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var sent = 0;
        foreach (var email in await _repo.GetDueAsync(now, BatchSize))
        {
            ct.ThrowIfCancellationRequested();
            email.Attempts++;
            email.NextAttemptAt = now + Lease;
            if (!await _repo.TryUpdateAsync(email)) continue; // another instance has it

            try
            {
                await _sender.SendAsync(
                    new OutgoingEmail(email.ToAddress, email.Subject, email.HtmlBody, email.TextBody, email.Template), ct);
                email.SentAt = _clock.GetUtcNow().UtcDateTime;
                email.NextAttemptAt = null;
                email.LastError = null;
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                email.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                if (email.Attempts >= MaxAttempts)
                {
                    email.FailedAt = now;
                    email.NextAttemptAt = null;
                    _log.LogError(ex, "Gave up sending email {EmailId} ({Template}) after {Attempts} attempts",
                        email.Id, email.Template, email.Attempts);
                }
                else
                {
                    email.NextAttemptAt = now + Backoff[email.Attempts - 1];
                    _log.LogWarning(ex, "Sending email {EmailId} ({Template}) failed; retrying at {NextAttemptAt}",
                        email.Id, email.Template, email.NextAttemptAt);
                }
            }
            await _repo.UpdateAsync(email);
        }
        return sent;
    }
}
```

```csharp
// src/Server/Infrastructure/PeriodicScopedService.cs
namespace Stallions.Server.Infrastructure;

/// <summary>
/// Runs a unit of work in a fresh DI scope on a fixed interval, starting immediately. A failing
/// run is logged and the next run goes ahead; stopping the app ends the loop.
/// </summary>
public abstract class PeriodicScopedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger _log;

    protected PeriodicScopedService(IServiceScopeFactory scopes, ILogger log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected abstract TimeSpan Interval { get; }
    protected abstract Task RunOnceAsync(IServiceProvider services, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    await RunOnceAsync(scope.ServiceProvider, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogError(ex, "{Service} run failed", GetType().Name);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}
```

```csharp
// src/Server/Email/EmailDispatchService.cs
using Stallions.Server.Infrastructure;

namespace Stallions.Server.Email;

/// <summary>Sends queued emails every 30 seconds.</summary>
public class EmailDispatchService : PeriodicScopedService
{
    public EmailDispatchService(IServiceScopeFactory scopes, ILogger<EmailDispatchService> log) : base(scopes, log) { }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(30);

    protected override Task RunOnceAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IEmailDispatcher>().SendDueAsync(ct);
}
```

```csharp
// src/Server/Email/EmailServiceCollectionExtensions.cs
using Stallions.Server.Data.Repositories;

namespace Stallions.Server.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>Registers the outbox, the configured sender and the dispatcher. Throws at startup on an invalid configuration.</summary>
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(EmailOptions.Section);
        services.Configure<EmailOptions>(section);
        var options = section.Get<EmailOptions>() ?? new EmailOptions();

        var error = EmailOptionsValidator.Validate(options, environment.EnvironmentName);
        if (error != null) throw new InvalidOperationException($"Email configuration: {error}");

        if (options.Provider == EmailOptions.ProviderAcs)
            services.AddSingleton<IEmailSender, AcsEmailSender>();
        else
            services.AddSingleton<IEmailSender, LogEmailSender>();

        services.AddScoped<IOutboundEmailRepository, OutboundEmailRepository>();
        services.AddScoped<IEmailOutbox, EmailOutbox>();
        services.AddScoped<IEmailDispatcher, EmailDispatcher>();
        services.AddHostedService<EmailDispatchService>();
        return services;
    }
}
```

- [ ] **Step 6: Wire up and configure**

`Program.cs` — after `builder.Services.AddPayments(...)`:

```csharp
// Clock for background jobs and new services (tests substitute their own)
builder.Services.AddSingleton(TimeProvider.System);

// Email — outbox + Azure Communication Services (or the log sender locally)
builder.Services.AddEmail(builder.Configuration, builder.Environment);
```

with `using Stallions.Server.Email;` at the top.

`appsettings.json` — add after the `Payments` section:

```json
  "Email": {
    "Provider": "Acs"
  }
```

(The endpoint, sender and base URL come from App Service settings — Task 13.)

`launchSettings.json` — in **both** profiles' `environmentVariables`, add:

```json
        "Email__Provider": "Log",
        "Email__PublicBaseUrl": "https://localhost:7083"
```

- [ ] **Step 7: Run green**, then the full build (0 warnings) and full test run → green.

- [ ] **Step 8: Commit**

```bash
git add src/Server/Email src/Server/Infrastructure src/Server/Data/Repositories/IOutboundEmailRepository.cs src/Server/Data/Repositories/OutboundEmailRepository.cs src/Server/Stallions.Server.csproj src/Server/Program.cs src/Server/appsettings.json src/Server/Properties/launchSettings.json tests/Server.Tests/Helpers/TestClock.cs tests/Server.Tests/Email tests/Server.Tests/Infrastructure
git commit -m "feat: email outbox, ACS and log senders, dispatcher service"
```

---

### Task 5: The emails — `AuctionEmails`

**Files:**
- Create: `src/Server/Email/IAuctionEmails.cs`, `src/Server/Email/AuctionEmails.cs`
- Modify: `src/Server/Program.cs`
- Test: `tests/Server.Tests/Email/AuctionEmailsTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Server.Tests/Email/AuctionEmailsTests.cs
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Options;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Email;

public class AuctionEmailsTests
{
    private readonly List<OutgoingEmail> _queued = new();
    private readonly Mock<IUserRepository> _users = new();

    private AuctionEmails CreateSut()
    {
        var outbox = new Mock<IEmailOutbox>();
        outbox.Setup(o => o.EnqueueAsync(It.IsAny<OutgoingEmail>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .Callback<OutgoingEmail, string?, Guid?>((e, _, _) => _queued.Add(e))
            .Returns(Task.CompletedTask);
        return new AuctionEmails(outbox.Object, _users.Object,
            Options.Create(new DisclosureOptions
            {
                BuyerFeeExplanation = "CONFIGURED fee wording.",
                StudFarmBalanceArrangement = "CONFIGURED balance wording."
            }),
            Options.Create(new EmailOptions { PublicBaseUrl = "https://app.example/" }),
            NullLogger<AuctionEmails>.Instance);
    }

    private static readonly User Owner = new() { Email = "owner@arrowfield.example", DisplayName = "Owner" };

    private static AuctionListing Listing(string? contactEmail = "sales@arrowfield.example") => new()
    {
        Id = Guid.NewGuid(),
        EndDateTime = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc),
        Stallion = new Stallion { Name = "Snitzel" },
        Season = new Season { Name = "2026 Season" },
        StudFarm = new StudFarm { Name = "Arrowfield", ContactEmail = contactEmail, ContactPhone = "02 6545 9888", User = Owner, UserId = Owner.Id }
    };

    private static readonly User Winner = new() { Email = "winner@example.com", DisplayName = "Jane Buyer" };

    private static Purchase Sale() => new()
    {
        TotalPriceIncGst = 10000m, BuyerFeeIncGst = 150m, BuyerFeeExGst = 136.36m, BuyerFeeGst = 13.64m,
        BalancePayableToStudIncGst = 9850m, PaymentReference = "pi_123",
        ChargeDueBy = new DateTime(2026, 10, 10, 11, 0, 0, DateTimeKind.Utc),
        LastChargeFailure = "Your card was declined."
    };

    [Fact]
    public async Task WonAndCharged_ShowsPriceFeeBalanceAndTheConfiguredWording()
    {
        await CreateSut().WonAndChargedAsync(Listing(), Sale(), Winner);

        var email = _queued.Single();
        email.ToAddress.Should().Be("winner@example.com");
        email.Template.Should().Be("WonAndCharged");
        email.Subject.Should().Contain("Snitzel");
        email.TextBody.Should().Contain("$10,000.00").And.Contain("$150.00").And.Contain("$9,850.00")
            .And.Contain("CONFIGURED fee wording.").And.Contain("CONFIGURED balance wording.")
            .And.Contain("https://app.example/my-purchases");
        email.HtmlBody.Should().Contain("$9,850.00");
    }

    [Fact]
    public async Task StudSaleConfirmation_GoesToTheFarmContact_AndNamesTheBuyer()
    {
        await CreateSut().StudSaleConfirmationAsync(Listing(), Sale(), Winner);

        var email = _queued.Single();
        email.ToAddress.Should().Be("sales@arrowfield.example");
        email.TextBody.Should().Contain("Jane Buyer").And.Contain("winner@example.com").And.Contain("$9,850.00");
    }

    [Fact]
    public async Task StudEmails_FallBackToTheOwnersEmail()
    {
        await CreateSut().StudNoSaleAsync(Listing(contactEmail: null), ListingCloseReason.ReserveNotMet);

        var email = _queued.Single();
        email.ToAddress.Should().Be("owner@arrowfield.example");
        email.TextBody.Should().Contain("below your reserve");
    }

    [Fact]
    public async Task PaymentFailed_GivesTheDeadlineInSydneyTime_AndTheCardLink()
    {
        await CreateSut().PaymentFailedAsync(Listing(), Sale(), Winner);

        var email = _queued.Single();
        email.Template.Should().Be("PaymentFailed");
        // 11:00 UTC on 10 Oct 2026 is 10:00 pm in Sydney (AEDT, UTC+11).
        email.TextBody.Should().Contain("10 Oct 2026, 10:00 pm").And.Contain("Your card was declined.")
            .And.Contain("https://app.example/account/card");
    }

    [Fact]
    public async Task Outbid_LooksUpThePreviousBidder()
    {
        var bidder = new User { Id = Guid.NewGuid(), Email = "outbid@example.com" };
        _users.Setup(u => u.GetByIdAsync(bidder.Id)).ReturnsAsync(bidder);

        await CreateSut().OutbidAsync(Listing(), bidder.Id, 12500m);

        var email = _queued.Single();
        email.ToAddress.Should().Be("outbid@example.com");
        email.TextBody.Should().Contain("$12,500.00");
    }

    [Fact]
    public async Task ValuesAreHtmlEncoded()
    {
        var listing = Listing();
        listing.Stallion.Name = "<b>Snitzel</b>";

        await CreateSut().AuctionLostAsync(listing, Winner);

        _queued.Single().HtmlBody.Should().Contain("&lt;b&gt;Snitzel&lt;/b&gt;").And.NotContain("<b>Snitzel");
    }
}
```

- [ ] **Step 2: Run red** — `dotnet test tests/Server.Tests --filter FullyQualifiedName~AuctionEmailsTests` → compile errors.

- [ ] **Step 3: Implement**

```csharp
// src/Server/Email/IAuctionEmails.cs
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Email;

/// <summary>
/// Builds and queues the auction emails (through the outbox, in the caller's transaction).
/// The listing should have Stallion, Season and StudFarm loaded.
/// </summary>
public interface IAuctionEmails
{
    Task OutbidAsync(AuctionListing listing, Guid previousBidderUserId, decimal newHighBidIncGst);
    /// <summary>To a bidder who didn't win an auction that sold.</summary>
    Task AuctionLostAsync(AuctionListing listing, User bidder);
    /// <summary>To every bidder when the auction closed without a sale.</summary>
    Task AuctionEndedWithoutSaleAsync(AuctionListing listing, User bidder);
    Task WonAndChargedAsync(AuctionListing listing, Purchase purchase, User winner);
    Task PaymentFailedAsync(AuctionListing listing, Purchase purchase, User winner);
    Task PaymentAbandonedAsync(AuctionListing listing, Purchase purchase, User winner);
    Task StudSaleConfirmationAsync(AuctionListing listing, Purchase purchase, User winner);
    Task StudNoSaleAsync(AuctionListing listing, ListingCloseReason reason);
}
```

```csharp
// src/Server/Email/AuctionEmails.cs
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Options;
using Stallions.Shared.Enums;

namespace Stallions.Server.Email;

/// <summary>
/// The auction emails. Plain, factual wording; amounts inc. GST; times in Sydney time. Buyer-fee
/// policy wording comes from DisclosureOptions (configured), never from this file. Bidders are
/// never told who else bid.
/// </summary>
public class AuctionEmails : IAuctionEmails
{
    private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");
    private static readonly TimeZoneInfo Sydney = FindSydney();

    private readonly IEmailOutbox _outbox;
    private readonly IUserRepository _users;
    private readonly DisclosureOptions _disclosure;
    private readonly string _baseUrl;
    private readonly ILogger<AuctionEmails> _log;

    public AuctionEmails(IEmailOutbox outbox, IUserRepository users, IOptions<DisclosureOptions> disclosure,
        IOptions<EmailOptions> email, ILogger<AuctionEmails> log)
    {
        _outbox = outbox;
        _users = users;
        _disclosure = disclosure.Value;
        _baseUrl = email.Value.PublicBaseUrl.TrimEnd('/');
        _log = log;
    }

    public async Task OutbidAsync(AuctionListing listing, Guid previousBidderUserId, decimal newHighBidIncGst)
    {
        var bidder = await _users.GetByIdAsync(previousBidderUserId);
        if (bidder == null) return;
        await QueueAsync(bidder.Email, "Outbid", $"You've been outbid on {Name(listing)}", listing,
            Para($"Another buyer has bid {Money(newHighBidIncGst)} on the {Name(listing)} nomination, so you are no longer the highest bidder."),
            Para($"The auction closes {When(listing.EndDateTime)}."),
            Link($"/listings/{listing.Id}", "Bid again"));
    }

    public Task AuctionLostAsync(AuctionListing listing, User bidder) =>
        QueueAsync(bidder.Email, "AuctionLost", $"Auction closed: {Name(listing)}", listing,
            Para($"The auction for the {Name(listing)} nomination ({Season(listing)}) has closed and another bid won."),
            Para("Thank you for bidding."));

    public Task AuctionEndedWithoutSaleAsync(AuctionListing listing, User bidder) =>
        QueueAsync(bidder.Email, "AuctionEndedWithoutSale", $"Auction closed: {Name(listing)}", listing,
            Para($"The auction for the {Name(listing)} nomination ({Season(listing)}) has closed without a sale."),
            Para("Thank you for bidding."));

    public Task WonAndChargedAsync(AuctionListing listing, Purchase purchase, User winner) =>
        QueueAsync(winner.Email, "WonAndCharged", $"You won the {Name(listing)} nomination", listing,
            Para($"Your bid of {Money(purchase.TotalPriceIncGst)} won the auction for the {Name(listing)} nomination ({Season(listing)}), offered by {Farm(listing)}."),
            Amounts(purchase, balanceLabel: "Balance you pay the stud directly"),
            Para($"The buyer fee of {Money(purchase.BuyerFeeIncGst)} has been charged to your saved card."),
            Para(_disclosure.BuyerFeeExplanation),
            Para(_disclosure.StudFarmBalanceArrangement),
            Para(StudContact(listing)),
            Link("/my-purchases", "View your sale record"));

    public Task PaymentFailedAsync(AuctionListing listing, Purchase purchase, User winner) =>
        QueueAsync(winner.Email, "PaymentFailed", $"Action needed: payment for {Name(listing)} failed", listing,
            Para($"Your bid of {Money(purchase.TotalPriceIncGst)} won the auction for the {Name(listing)} nomination, but we couldn't charge the buyer fee of {Money(purchase.BuyerFeeIncGst)} to your saved card: {purchase.LastChargeFailure}"),
            Para($"Please update your card by {When(purchase.ChargeDueBy ?? DateTime.UtcNow)}. Saving a new card retries the payment straight away. If the payment can't be completed by then, the sale won't go ahead."),
            Link("/account/card", "Update your card"));

    public Task PaymentAbandonedAsync(AuctionListing listing, Purchase purchase, User winner) =>
        QueueAsync(winner.Email, "PaymentAbandoned", $"No sale: {Name(listing)}", listing,
            Para($"We couldn't charge the buyer fee for the {Name(listing)} nomination by the deadline, so the sale has not gone ahead and the nomination is no longer yours."));

    public async Task StudSaleConfirmationAsync(AuctionListing listing, Purchase purchase, User winner) =>
        await QueueAsync(await StudAddressAsync(listing), "StudSaleConfirmation", $"Sale confirmed: {Name(listing)} nomination", listing,
            Para($"The auction for the {Name(listing)} nomination ({Season(listing)}) has closed with a sale, and the buyer fee has been paid."),
            Para($"Buyer: {winner.DisplayName}, {winner.Email}"),
            Amounts(purchase, balanceLabel: "Balance the buyer pays you directly"),
            Para("Please arrange the balance with the buyer under your own terms."),
            Link($"/admin/listings/{listing.Id}", "View the listing"));

    public async Task StudNoSaleAsync(AuctionListing listing, ListingCloseReason reason) =>
        await QueueAsync(await StudAddressAsync(listing), "StudNoSale", $"No sale: {Name(listing)} nomination", listing,
            Para($"The auction for the {Name(listing)} nomination ({Season(listing)}) has closed without a sale: {ReasonText(reason)}"),
            Para("The lot is marked Unsold in My Listings."),
            Link($"/admin/listings/{listing.Id}", "View the listing"));

    // ── Building blocks ────────────────────────────────────────────────────────

    private sealed record Block(string Html, string Text);

    private async Task QueueAsync(string? to, string template, string subject, AuctionListing listing, params Block[] blocks)
    {
        var html = "<div style=\"font-family:Georgia,'Times New Roman',serif;max-width:560px;color:#1f2a24\">"
                   + string.Concat(blocks.Select(b => b.Html))
                   + "<p style=\"color:#6b7280;font-size:13px\">Stallions Australia</p></div>";
        var text = string.Join("\n\n", blocks.Select(b => b.Text)) + "\n\nStallions Australia";
        await _outbox.EnqueueAsync(new OutgoingEmail(to ?? string.Empty, subject, html, text, template), "Listing", listing.Id);
    }

    private static Block Para(string text) => new($"<p>{Enc(text)}</p>", text);

    private Block Link(string path, string label)
    {
        var url = _baseUrl + path;
        return new($"<p><a href=\"{Enc(url)}\">{Enc(label)}</a></p>", $"{label}: {url}");
    }

    private static Block Amounts(Purchase p, string balanceLabel)
    {
        var rows = new[]
        {
            ("Price (your winning bid)", Money(p.TotalPriceIncGst)),
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

    private async Task<string?> StudAddressAsync(AuctionListing listing)
    {
        var farm = listing.StudFarm;
        if (!string.IsNullOrWhiteSpace(farm?.ContactEmail)) return farm.ContactEmail;
        var owner = farm?.User ?? (farm != null ? await _users.GetByIdAsync(farm.UserId) : null);
        if (string.IsNullOrWhiteSpace(owner?.Email))
            _log.LogWarning("Stud for listing {ListingId} has no contact or owner email", listing.Id);
        return owner?.Email;
    }

    private static string StudContact(AuctionListing listing)
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

    private static string Name(AuctionListing l) => l.Stallion?.Name ?? "stallion";
    private static string Season(AuctionListing l) => l.Season?.Name ?? "this season";
    private static string Farm(AuctionListing l) => l.StudFarm?.Name ?? "the stud";
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

`Program.cs` — with the other services: `builder.Services.AddScoped<IAuctionEmails, AuctionEmails>();`

> en-AU formats the AM/PM designator in lower case (`10:00 pm`). If the test's expected text differs
> only in the designator's case on your machine, fix the test to the platform's en-AU output — don't
> change the culture.

- [ ] **Step 4: Run green**, then the full build (0 warnings) and full test run → green.

- [ ] **Step 5: Commit**

```bash
git add src/Server/Email/IAuctionEmails.cs src/Server/Email/AuctionEmails.cs src/Server/Program.cs tests/Server.Tests/Email/AuctionEmailsTests.cs
git commit -m "feat: auction emails (outbid, won and charged, payment failed, no sale, stud confirmations)"
```

---

### Task 6: Repository queries for closing and charging

**Files:**
- Modify: `src/Server/Data/Repositories/IListingRepository.cs`, `ListingRepository.cs`, `IBidRepository.cs`, `BidRepository.cs`,
  `IPurchaseRepository.cs`, `PurchaseRepository.cs`
- Create: `tests/Server.Tests/Helpers/AuctionTestData.cs`, `tests/Server.Tests/Data/Repositories/AuctionCloseQueriesTests.cs`

- [ ] **Step 1: Test data helper**

```csharp
// tests/Server.Tests/Helpers/AuctionTestData.cs
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Helpers;

/// <summary>Seeds auctions, buyers, cards and bids into an in-memory AppDbContext.</summary>
public static class AuctionTestData
{
    public static User Buyer(AppDbContext db, string name = "Buyer")
    {
        var user = new User
        {
            ObjectId = Guid.NewGuid().ToString(), DisplayName = name,
            Email = $"{name.Replace(' ', '.').ToLowerInvariant()}@example.com",
            Role = UserRole.Buyer, Status = UserStatus.Active
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    public static SavedCard Card(AppDbContext db, User buyer, string paymentMethodId = "pm_good",
        string provider = "Fake", int expYear = 2030)
    {
        var card = new SavedCard
        {
            UserId = buyer.Id, Provider = provider, ProviderCustomerId = "cus_" + buyer.Id.ToString("N"),
            ProviderPaymentMethodId = paymentMethodId, Brand = "visa", Last4 = "4242", ExpMonth = 12, ExpYear = expYear
        };
        db.SavedCards.Add(card);
        db.SaveChanges();
        return card;
    }

    public static AuctionListing Auction(AppDbContext db, DateTime endsAt, decimal? reserve = null,
        decimal? buyerFee = 150m, ListingStatus status = ListingStatus.Active)
    {
        var owner = new User
        {
            ObjectId = Guid.NewGuid().ToString(), DisplayName = "Stud Owner", Email = "owner@arrowfield.example",
            Role = UserRole.StudFarmAdmin, Status = UserStatus.Active
        };
        var farm = new StudFarm { Name = "Arrowfield", UserId = owner.Id, User = owner, ContactEmail = "sales@arrowfield.example" };
        var stallion = new Stallion { Name = "Snitzel", StudFarmId = farm.Id, StudFarm = farm };
        var season = new Season { Name = "2026 Season" };
        var listing = new AuctionListing
        {
            StallionId = stallion.Id, Stallion = stallion, SeasonId = season.Id, Season = season,
            StudFarmId = farm.Id, StudFarm = farm, ListingType = ListingType.Auction, Status = status,
            EndDateTime = endsAt, ReservePrice = reserve, IsNoReserve = reserve == null,
            BuyerFeeIncGst = buyerFee, MinimumBidIncrement = 25m
        };
        db.AddRange(owner, farm, stallion, season, listing);
        db.SaveChanges();
        return listing;
    }

    public static Bid Bid(AppDbContext db, AuctionListing listing, User buyer, decimal amount,
        BidStatus status = BidStatus.Active)
    {
        var bid = new Bid { AuctionListingId = listing.Id, BuyerUserId = buyer.Id, AmountIncGst = amount, Status = status };
        db.Bids.Add(bid);
        db.SaveChanges();
        return bid;
    }
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/Server.Tests/Data/Repositories/AuctionCloseQueriesTests.cs
using FluentAssertions;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data.Repositories;

public class AuctionCloseQueriesTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
    private readonly string _name = Guid.NewGuid().ToString();
    private AppDbContext Db() => DbContextFactory.Create(_name);

    [Fact]
    public async Task AuctionsDueToClose_AreActiveAndEndedByTheCutoff_OldestFirst()
    {
        using var seed = Db();
        var later = AuctionTestData.Auction(seed, Now.AddMinutes(-5));
        var earlier = AuctionTestData.Auction(seed, Now.AddMinutes(-10));
        AuctionTestData.Auction(seed, Now.AddMinutes(5));                                   // not ended
        AuctionTestData.Auction(seed, Now.AddMinutes(-20), status: ListingStatus.Sold);     // already closed

        var ids = await new ListingRepository(Db()).GetAuctionIdsDueToCloseAsync(Now, 10);

        ids.Should().Equal(earlier.Id, later.Id);
    }

    [Fact]
    public async Task AuctionWithDetails_LoadsStallionSeasonFarmAndOwner()
    {
        using var seed = Db();
        var listing = AuctionTestData.Auction(seed, Now);

        var loaded = await new ListingRepository(Db()).GetAuctionWithDetailsAsync(listing.Id);

        loaded!.Stallion.Name.Should().Be("Snitzel");
        loaded.Season.Name.Should().Be("2026 Season");
        loaded.StudFarm.User.Email.Should().Be("owner@arrowfield.example");
    }

    [Fact]
    public async Task BidsWithBuyers_AreHighestFirst()
    {
        using var seed = Db();
        var listing = AuctionTestData.Auction(seed, Now);
        var a = AuctionTestData.Buyer(seed, "A");
        var b = AuctionTestData.Buyer(seed, "B");
        AuctionTestData.Bid(seed, listing, a, 5000m, BidStatus.Outbid);
        AuctionTestData.Bid(seed, listing, b, 6000m);

        var bids = await new BidRepository(Db()).GetByAuctionWithBuyersAsync(listing.Id);

        bids.Select(x => x.AmountIncGst).Should().Equal(6000m, 5000m);
        bids[0].Buyer.Email.Should().Be("b@example.com");
    }

    private Guid AddPurchase(Action<Purchase> configure)
    {
        using var seed = Db();
        var listing = AuctionTestData.Auction(seed, Now, status: ListingStatus.AwaitingPayment);
        var buyer = AuctionTestData.Buyer(seed);
        var purchase = new Purchase
        {
            ListingId = listing.Id, BuyerUserId = buyer.Id, BidId = Guid.NewGuid(),
            Status = PurchaseStatus.Pending, CreatedAt = Now
        };
        configure(purchase);
        seed.Purchases.Add(purchase);
        seed.SaveChanges();
        return purchase.Id;
    }

    [Fact]
    public async Task DueForCharge_CoversFirstAttemptRetryDeadlineAndInterrupted()
    {
        var first = AddPurchase(_ => { });
        var retry = AddPurchase(p => { p.ChargeAttempts = 1; p.ChargeDueBy = Now.AddHours(1); p.RetryRequested = true; });
        var deadline = AddPurchase(p => { p.ChargeAttempts = 1; p.ChargeDueBy = Now.AddMinutes(-1); });
        var interrupted = AddPurchase(p => { p.ChargeAttempts = 1; p.ChargeAttemptStartedAt = Now.AddMinutes(-3); });
        AddPurchase(p => { p.ChargeAttempts = 1; p.ChargeDueBy = Now.AddHours(1); });             // waiting
        AddPurchase(p => { p.ChargeAttempts = 1; p.ChargeAttemptStartedAt = Now.AddSeconds(-30); }); // in flight
        AddPurchase(p => p.Status = PurchaseStatus.Completed);

        var ids = await new PurchaseRepository(Db()).GetIdsDueForChargeAsync(Now, Now.AddMinutes(-2), 50);

        ids.Should().BeEquivalentTo(new[] { first, retry, deadline, interrupted });
    }

    [Fact]
    public async Task AwaitingCardRetry_IsThePendingFailedChargesOfThatBuyer()
    {
        var failed = AddPurchase(p => { p.ChargeAttempts = 1; p.ChargeDueBy = Now.AddHours(1); });
        var buyerId = Db().Purchases.Single(p => p.Id == failed).BuyerUserId;

        var found = await new PurchaseRepository(Db()).GetAwaitingCardRetryAsync(buyerId);

        found.Select(p => p.Id).Should().Equal(failed);
    }

    [Fact]
    public async Task ForCharge_LoadsTheListingDetailsAndTheBuyer()
    {
        var id = AddPurchase(_ => { });

        var p = await new PurchaseRepository(Db()).GetForChargeAsync(id);

        p!.Buyer.Should().NotBeNull();
        p.Listing.Should().BeOfType<AuctionListing>();
        p.Listing.Stallion.Name.Should().Be("Snitzel");
        p.Listing.StudFarm.User.Should().NotBeNull();
    }
}
```

- [ ] **Step 3: Run red** — compile errors.

- [ ] **Step 4: Implement**

`IListingRepository`: remove `GetExpiredAuctionsAsync` (unused) and add:

```csharp
    /// <summary>Active auctions that ended at or before the cutoff, oldest first.</summary>
    Task<IReadOnlyList<Guid>> GetAuctionIdsDueToCloseAsync(DateTime endedAtOrBefore, int max);
    /// <summary>The auction with Stallion, Season, StudFarm and the farm's owner (for emails).</summary>
    Task<AuctionListing?> GetAuctionWithDetailsAsync(Guid id);
```

`ListingRepository` (replace `GetExpiredAuctionsAsync`):

```csharp
    public async Task<IReadOnlyList<Guid>> GetAuctionIdsDueToCloseAsync(DateTime endedAtOrBefore, int max) =>
        await _db.AuctionListings
            .Where(a => a.Status == ListingStatus.Active && a.EndDateTime <= endedAtOrBefore)
            .OrderBy(a => a.EndDateTime)
            .Take(max)
            .Select(a => a.Id)
            .ToListAsync();

    public async Task<AuctionListing?> GetAuctionWithDetailsAsync(Guid id) =>
        await _db.AuctionListings
            .Include(a => a.Stallion)
            .Include(a => a.Season)
            .Include(a => a.StudFarm).ThenInclude(f => f.User)
            .FirstOrDefaultAsync(a => a.Id == id);
```

`IBidRepository` / `BidRepository`:

```csharp
    /// <summary>All bids on the auction with their buyers, highest first.</summary>
    Task<IReadOnlyList<Bid>> GetByAuctionWithBuyersAsync(Guid auctionListingId);
```

```csharp
    public async Task<IReadOnlyList<Bid>> GetByAuctionWithBuyersAsync(Guid auctionListingId) =>
        await _db.Bids
            .Include(b => b.Buyer)
            .Where(b => b.AuctionListingId == auctionListingId)
            .OrderByDescending(b => b.AmountIncGst)
            .ToListAsync();
```

`IPurchaseRepository` / `PurchaseRepository`:

```csharp
    /// <summary>
    /// Pending sale records due a charge attempt: never attempted, a retry requested after a new
    /// card, the grace period over, or an attempt interrupted (started at or before interruptedBefore).
    /// </summary>
    Task<IReadOnlyList<Guid>> GetIdsDueForChargeAsync(DateTime now, DateTime interruptedBefore, int max);
    /// <summary>With the buyer and the listing's Stallion, Season, StudFarm and owner.</summary>
    Task<Purchase?> GetForChargeAsync(Guid id);
    /// <summary>The buyer's Pending sale records whose charge has failed (in the grace period).</summary>
    Task<IReadOnlyList<Purchase>> GetAwaitingCardRetryAsync(Guid buyerUserId);
```

```csharp
    public async Task<IReadOnlyList<Guid>> GetIdsDueForChargeAsync(DateTime now, DateTime interruptedBefore, int max) =>
        await _db.Purchases
            .Where(p => p.Status == PurchaseStatus.Pending && (
                (p.ChargeAttemptStartedAt == null &&
                    (p.ChargeAttempts == 0 || p.RetryRequested || p.ChargeDueBy <= now)) ||
                (p.ChargeAttemptStartedAt != null && p.ChargeAttemptStartedAt <= interruptedBefore)))
            .OrderBy(p => p.CreatedAt)
            .Take(max)
            .Select(p => p.Id)
            .ToListAsync();

    public async Task<Purchase?> GetForChargeAsync(Guid id) =>
        await _db.Purchases
            .Include(p => p.Buyer)
            .Include(p => p.Listing).ThenInclude(l => l.Stallion)
            .Include(p => p.Listing).ThenInclude(l => l.Season)
            .Include(p => p.Listing).ThenInclude(l => l.StudFarm).ThenInclude(f => f.User)
            .FirstOrDefaultAsync(p => p.Id == id);

    public async Task<IReadOnlyList<Purchase>> GetAwaitingCardRetryAsync(Guid buyerUserId) =>
        await _db.Purchases
            .Where(p => p.BuyerUserId == buyerUserId && p.Status == PurchaseStatus.Pending && p.ChargeDueBy != null)
            .ToListAsync();
```

(Add `using Stallions.Shared.Enums;` to `PurchaseRepository`.)

- [ ] **Step 5: Run green**, full build (0 warnings) and full test run → green.

- [ ] **Step 6: Commit**

```bash
git add src/Server/Data/Repositories tests/Server.Tests/Helpers/AuctionTestData.cs tests/Server.Tests/Data/Repositories/AuctionCloseQueriesTests.cs
git commit -m "feat: repository queries for closing auctions and charging sale records"
```

---

### Task 7: `AuctionCloser` — closing ended auctions

**Files:**
- Create: `src/Server/Auctions/AuctionCloseOptions.cs`, `src/Server/Auctions/IAuctionCloser.cs`, `src/Server/Auctions/AuctionCloser.cs`
- Create tests: `tests/Server.Tests/Auctions/AuctionCloserFixture.cs`, `tests/Server.Tests/Auctions/AuctionCloserCloseTests.cs`

- [ ] **Step 1: Test fixture**

```csharp
// tests/Server.Tests/Auctions/AuctionCloserFixture.cs
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Stallions.Server.Auctions;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Payments;
using Stallions.Server.Tests.Helpers;

namespace Stallions.Server.Tests.Auctions;

/// <summary>
/// An AuctionCloser over real repositories on a shared in-memory database, with a mocked payment
/// provider, mocked emails and a test clock. Seed with Seed(); assert with Read().
/// </summary>
public class AuctionCloserFixture
{
    public string DbName { get; } = Guid.NewGuid().ToString();
    public TestClock Clock { get; } = new();
    public Mock<IPaymentProvider> Provider { get; } = new();
    public Mock<IAuctionEmails> Emails { get; } = new();
    public Mock<IAuditLogRepository> Audit { get; } = new();
    public PlatformSettings Settings { get; } = new() { ChargeGracePeriodHours = 2, BuyerFeeIncGst = 150m };
    public List<ChargeRequest> Charges { get; } = new();

    public AuctionCloserFixture()
    {
        Provider.SetupGet(p => p.Name).Returns("Fake");
        ChargesSucceed();
    }

    public void ChargesSucceed() =>
        Provider.Setup(p => p.ChargeSavedCardAsync(It.IsAny<ChargeRequest>()))
            .Callback<ChargeRequest>(Charges.Add)
            .ReturnsAsync(() => ChargeResult.Success($"pi_{Charges.Count}"));

    public void ChargesDecline() =>
        Provider.Setup(p => p.ChargeSavedCardAsync(It.IsAny<ChargeRequest>()))
            .Callback<ChargeRequest>(Charges.Add)
            .ReturnsAsync(ChargeResult.Declined("card_declined", "Your card was declined."));

    public AppDbContext Read() => DbContextFactory.Create(DbName);

    public T Seed<T>(Func<AppDbContext, T> seed)
    {
        using var db = DbContextFactory.Create(DbName);
        return seed(db);
    }

    /// <summary>A closer on its own context, as a separate run (or app instance) would have.</summary>
    public AuctionCloser CreateSut(AppDbContext? db = null, IListingRepository? listings = null)
    {
        db ??= DbContextFactory.Create(DbName);
        var settings = new Mock<IPlatformSettingsRepository>();
        settings.Setup(s => s.GetAsync()).ReturnsAsync(Settings);
        return new AuctionCloser(
            listings ?? new ListingRepository(db), new BidRepository(db), new PurchaseRepository(db), new SavedCardRepository(db),
            settings.Object, Audit.Object, Emails.Object, Provider.Object, new InlineTransactionRunner(), Clock,
            Options.Create(new AuctionCloseOptions()), NullLogger<AuctionCloser>.Instance);
    }
}
```

(Check `SavedCardRepository`'s constructor takes `AppDbContext`; it does in Phase 2.)

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/Server.Tests/Auctions/AuctionCloserCloseTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Auctions;

public class AuctionCloserCloseTests
{
    private readonly AuctionCloserFixture _f = new();
    private DateTime Ended => _f.Clock.UtcNow.AddMinutes(-1);

    [Fact]
    public async Task NoBids_ClosesUnsold_AndTellsTheStud()
    {
        var listing = _f.Seed(db => AuctionTestData.Auction(db, Ended));

        var run = await _f.CreateSut().RunAsync();

        run.AuctionsClosed.Should().Be(1);
        var closed = await _f.Read().AuctionListings.SingleAsync();
        closed.Status.Should().Be(ListingStatus.Unsold);
        closed.CloseReason.Should().Be(ListingCloseReason.NoBids);
        closed.ClosedAt.Should().Be(_f.Clock.UtcNow);
        _f.Emails.Verify(e => e.StudNoSaleAsync(It.Is<AuctionListing>(l => l.Id == listing.Id), ListingCloseReason.NoBids), Times.Once);
    }

    [Fact]
    public async Task BelowReserve_ClosesUnsold_EveryBidderLosesAndIsTold()
    {
        var (a, b) = _f.Seed(db =>
        {
            var listing = AuctionTestData.Auction(db, Ended, reserve: 20000m);
            var a = AuctionTestData.Buyer(db, "A");
            var b = AuctionTestData.Buyer(db, "B");
            AuctionTestData.Bid(db, listing, a, 9000m, BidStatus.Outbid);
            AuctionTestData.Bid(db, listing, b, 10000m);
            return (a, b);
        });

        await _f.CreateSut().RunAsync();

        using var db = _f.Read();
        (await db.AuctionListings.SingleAsync()).CloseReason.Should().Be(ListingCloseReason.ReserveNotMet);
        (await db.Bids.ToListAsync()).Should().OnlyContain(x => x.Status == BidStatus.Lost);
        (await db.Purchases.CountAsync()).Should().Be(0);
        _f.Emails.Verify(e => e.AuctionEndedWithoutSaleAsync(It.IsAny<AuctionListing>(), It.Is<User>(u => u.Id == a.Id)), Times.Once);
        _f.Emails.Verify(e => e.AuctionEndedWithoutSaleAsync(It.IsAny<AuctionListing>(), It.Is<User>(u => u.Id == b.Id)), Times.Once);
        _f.Emails.Verify(e => e.StudNoSaleAsync(It.IsAny<AuctionListing>(), ListingCloseReason.ReserveNotMet), Times.Once);
    }

    [Fact]
    public async Task ReserveMet_CreatesThePendingSaleRecord_WithTheGstSplitAndBalance()
    {
        var (winner, winningBid) = _f.Seed(db =>
        {
            var listing = AuctionTestData.Auction(db, Ended, reserve: 8000m);
            var w = AuctionTestData.Buyer(db, "Winner");
            AuctionTestData.Card(db, w);
            return (w, AuctionTestData.Bid(db, listing, w, 10000m));
        });
        _f.ChargesDecline(); // keep the sale record Pending so this test looks only at closing

        await _f.CreateSut().RunAsync();

        var purchase = await _f.Read().Purchases.SingleAsync();
        purchase.BuyerUserId.Should().Be(winner.Id);
        purchase.BidId.Should().Be(winningBid.Id);
        purchase.TotalPriceIncGst.Should().Be(10000m);
        purchase.BuyerFeeIncGst.Should().Be(150m);
        purchase.BuyerFeeGst.Should().Be(13.64m);
        purchase.BuyerFeeExGst.Should().Be(136.36m);
        purchase.BalancePayableToStudIncGst.Should().Be(9850m);
    }

    [Fact]
    public async Task ReserveMet_WinnerWins_OtherBiddersLose_AndAreTold()
    {
        var (loser, winner) = _f.Seed(db =>
        {
            var listing = AuctionTestData.Auction(db, Ended); // no reserve
            var l = AuctionTestData.Buyer(db, "Loser");
            var w = AuctionTestData.Buyer(db, "Winner");
            AuctionTestData.Card(db, w);
            AuctionTestData.Bid(db, listing, w, 5000m, BidStatus.Outbid);
            AuctionTestData.Bid(db, listing, l, 5500m, BidStatus.Outbid);
            AuctionTestData.Bid(db, listing, w, 6000m);
            return (l, w);
        });
        _f.ChargesDecline();

        await _f.CreateSut().RunAsync();

        using var db = _f.Read();
        var bids = await db.Bids.ToListAsync();
        bids.Single(x => x.AmountIncGst == 6000m).Status.Should().Be(BidStatus.Won);
        bids.Single(x => x.AmountIncGst == 5500m).Status.Should().Be(BidStatus.Lost);
        bids.Single(x => x.AmountIncGst == 5000m).Status.Should().Be(BidStatus.Outbid, "the winner's own earlier bid didn't lose");
        (await db.AuctionListings.SingleAsync()).Status.Should().Be(ListingStatus.AwaitingPayment);
        _f.Emails.Verify(e => e.AuctionLostAsync(It.IsAny<AuctionListing>(), It.Is<User>(u => u.Id == loser.Id)), Times.Once);
        _f.Emails.Verify(e => e.AuctionLostAsync(It.IsAny<AuctionListing>(), It.Is<User>(u => u.Id == winner.Id)), Times.Never);
    }

    [Fact]
    public async Task WithoutABuyerFeeSnapshot_ClosesUnsoldAsChargeFailed()
    {
        _f.Seed(db =>
        {
            var listing = AuctionTestData.Auction(db, Ended, buyerFee: null);
            AuctionTestData.Bid(db, listing, AuctionTestData.Buyer(db), 5000m);
            return listing;
        });

        await _f.CreateSut().RunAsync();

        using var db = _f.Read();
        (await db.AuctionListings.SingleAsync()).CloseReason.Should().Be(ListingCloseReason.ChargeFailed);
        (await db.Purchases.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AnAuctionEndedLessThan30SecondsAgo_IsLeftForTheNextRun()
    {
        _f.Seed(db => AuctionTestData.Auction(db, _f.Clock.UtcNow.AddSeconds(-10)));

        (await _f.CreateSut().RunAsync()).AuctionsClosed.Should().Be(0);
        (await _f.Read().AuctionListings.SingleAsync()).Status.Should().Be(ListingStatus.Active);
    }

    [Fact]
    public async Task TwoInstancesClosingTheSameAuction_OnlyOneSaleRecordIsCreated()
    {
        _f.Seed(db =>
        {
            var listing = AuctionTestData.Auction(db, Ended);
            var w = AuctionTestData.Buyer(db, "Winner");
            AuctionTestData.Card(db, w);
            AuctionTestData.Bid(db, listing, w, 6000m);
            return listing;
        });
        _f.ChargesDecline();
        // The second instance found the auction due and read it (still Active) before the first
        // one closed it: replay that by giving it the earlier due list and its stale context.
        using var staleDb = _f.Read();
        var real = new ListingRepository(staleDb);
        var dueIds = await real.GetAuctionIdsDueToCloseAsync(_f.Clock.UtcNow, 50);
        await real.GetAuctionWithDetailsAsync(dueIds.Single());
        var stale = new Mock<IListingRepository>();
        stale.Setup(r => r.GetAuctionIdsDueToCloseAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(dueIds);
        stale.Setup(r => r.GetAuctionWithDetailsAsync(It.IsAny<Guid>())).Returns<Guid>(real.GetAuctionWithDetailsAsync);
        stale.Setup(r => r.UpdateAsync(It.IsAny<Listing>())).Returns<Listing>(real.UpdateAsync);

        await _f.CreateSut().RunAsync();
        var second = await _f.CreateSut(staleDb, stale.Object).RunAsync();

        second.AuctionsClosed.Should().Be(0);
        (await _f.Read().Purchases.CountAsync()).Should().Be(1);
    }
}
```

- [ ] **Step 3: Run red** — compile errors.

- [ ] **Step 4: Implement**

```csharp
// src/Server/Auctions/AuctionCloseOptions.cs
namespace Stallions.Server.Auctions;

/// <summary>The "AuctionClose" section — operational settings, not business rules.</summary>
public class AuctionCloseOptions
{
    public const string Section = "AuctionClose";

    /// <summary>Switch the job off (maintenance, tests). On by default.</summary>
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 60;
    /// <summary>Most auctions closed, and most charges made, per run.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>A bid that passed the end-time check may still be committing just after the end.</summary>
    public static readonly TimeSpan CloseDelay = TimeSpan.FromSeconds(30);
    /// <summary>A charge attempt with no recorded result after this long was interrupted.</summary>
    public static readonly TimeSpan InterruptedAttemptAge = TimeSpan.FromMinutes(2);
}
```

```csharp
// src/Server/Auctions/IAuctionCloser.cs
namespace Stallions.Server.Auctions;

public interface IAuctionCloser
{
    /// <summary>Closes due auctions, then makes due buyer-fee charges. Safe to run on several instances at once.</summary>
    Task<AuctionCloseRun> RunAsync(CancellationToken ct = default);
}

public sealed record AuctionCloseRun(int AuctionsClosed, int ChargesAttempted);
```

```csharp
// src/Server/Auctions/AuctionCloser.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Payments;
using Stallions.Shared;
using Stallions.Shared.Enums;

namespace Stallions.Server.Auctions;

/// <summary>
/// Closes ended auctions (hidden reserve; highest bid wins) and charges each winner's saved card
/// the listing's buyer fee, with a grace period after a failed charge. Every change runs in a
/// transaction; the provider is called between two transactions, never inside one. A row changed
/// by another instance in the meantime raises a concurrency conflict and is skipped.
/// </summary>
public class AuctionCloser : IAuctionCloser
{
    private readonly IListingRepository _listings;
    private readonly IBidRepository _bids;
    private readonly IPurchaseRepository _purchases;
    private readonly ISavedCardRepository _cards;
    private readonly IPlatformSettingsRepository _settings;
    private readonly IAuditLogRepository _audit;
    private readonly IAuctionEmails _emails;
    private readonly IPaymentProvider _provider;
    private readonly ITransactionRunner _transactions;
    private readonly TimeProvider _clock;
    private readonly AuctionCloseOptions _options;
    private readonly ILogger<AuctionCloser> _log;

    public AuctionCloser(
        IListingRepository listings, IBidRepository bids, IPurchaseRepository purchases, ISavedCardRepository cards,
        IPlatformSettingsRepository settings, IAuditLogRepository audit, IAuctionEmails emails,
        IPaymentProvider provider, ITransactionRunner transactions, TimeProvider clock,
        IOptions<AuctionCloseOptions> options, ILogger<AuctionCloser> log)
    {
        _listings = listings; _bids = bids; _purchases = purchases; _cards = cards; _settings = settings;
        _audit = audit; _emails = emails; _provider = provider; _transactions = transactions; _clock = clock;
        _options = options.Value; _log = log;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<AuctionCloseRun> RunAsync(CancellationToken ct = default)
    {
        var closed = 0;
        foreach (var id in await _listings.GetAuctionIdsDueToCloseAsync(Now - AuctionCloseOptions.CloseDelay, _options.BatchSize))
        {
            ct.ThrowIfCancellationRequested();
            if (await TryAsync(() => CloseAuctionAsync(id), "close auction", id)) closed++;
        }

        var charged = 0;
        foreach (var id in await _purchases.GetIdsDueForChargeAsync(Now, Now - AuctionCloseOptions.InterruptedAttemptAge, _options.BatchSize))
        {
            ct.ThrowIfCancellationRequested();
            if (await TryAsync(() => ChargeAsync(id), "charge sale record", id)) charged++;
        }

        return new AuctionCloseRun(closed, charged);
    }

    // One auction or sale record failing never stops the run; the next run picks it up again.
    private async Task<bool> TryAsync(Func<Task<bool>> step, string what, Guid id)
    {
        try
        {
            return await step();
        }
        catch (DbUpdateConcurrencyException)
        {
            _log.LogInformation("Skipped: could not {What} {Id} — another instance changed it first", what, id);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Could not {What} {Id}; it will be retried on the next run", what, id);
            return false;
        }
    }

    // ── Closing ────────────────────────────────────────────────────────────────

    private Task<bool> CloseAuctionAsync(Guid listingId) => _transactions.RunAsync(async () =>
    {
        var now = Now;
        var listing = await _listings.GetAuctionWithDetailsAsync(listingId);
        // Re-checked inside the transaction: another run may have closed it already.
        if (listing is not { Status: ListingStatus.Active } || listing.EndDateTime > now - AuctionCloseOptions.CloseDelay)
            return false;

        var bids = await _bids.GetByAuctionWithBuyersAsync(listingId);
        var highest = bids.Where(b => b.Status == BidStatus.Active).MaxBy(b => b.AmountIncGst);
        listing.ClosedAt = now;

        if (highest == null)
            return await CloseUnsoldAsync(listing, bids, ListingCloseReason.NoBids);
        if (listing.IsReserveMetBy(highest.AmountIncGst) == false)
            return await CloseUnsoldAsync(listing, bids, ListingCloseReason.ReserveNotMet);
        if (listing.BuyerFeeIncGst is not { } fee)
        {
            _log.LogError("Auction {ListingId} has no buyer-fee snapshot, so the winner can't be charged; closing it unsold", listing.Id);
            return await CloseUnsoldAsync(listing, bids, ListingCloseReason.ChargeFailed, "MissingBuyerFeeSnapshot");
        }

        // The listing is saved first: if another instance got there first, this throws before anything else changes.
        listing.Status = ListingStatus.AwaitingPayment;
        await _listings.UpdateAsync(listing);

        foreach (var bid in bids.Where(b => b.Status is BidStatus.Active or BidStatus.Outbid))
        {
            if (bid.Id == highest.Id) bid.Status = BidStatus.Won;
            else if (bid.BuyerUserId != highest.BuyerUserId) bid.Status = BidStatus.Lost;
        }
        await _bids.UpdateRangeAsync(bids);

        var split = GstBreakdown.FromIncGst(fee);
        var purchase = new Purchase
        {
            ListingId = listing.Id,
            BuyerUserId = highest.BuyerUserId,
            BidId = highest.Id,
            TotalPriceIncGst = highest.AmountIncGst,
            BuyerFeeIncGst = split.IncGst,
            BuyerFeeExGst = split.ExGst,
            BuyerFeeGst = split.Gst,
            BalancePayableToStudIncGst = highest.AmountIncGst - split.IncGst,
            Status = PurchaseStatus.Pending,
            CreatedAt = now
        };
        await _purchases.AddAsync(purchase);

        await _audit.LogAsync("Listing", listing.Id, "AuctionClosed", null, JsonSerializer.Serialize(new
        {
            Outcome = "Won", WinningBidId = highest.Id, highest.AmountIncGst, PurchaseId = purchase.Id
        }));
        foreach (var bidder in Bidders(bids, except: highest.BuyerUserId))
            await _emails.AuctionLostAsync(listing, bidder);
        return true;
    });

    private async Task<bool> CloseUnsoldAsync(AuctionListing listing, IReadOnlyList<Bid> bids,
        ListingCloseReason reason, string? detail = null)
    {
        listing.Status = ListingStatus.Unsold;
        listing.CloseReason = reason;
        await _listings.UpdateAsync(listing);

        var open = bids.Where(b => b.Status is BidStatus.Active or BidStatus.Outbid).ToList();
        foreach (var bid in open) bid.Status = BidStatus.Lost;
        if (open.Count > 0) await _bids.UpdateRangeAsync(open);

        await _audit.LogAsync("Listing", listing.Id, "AuctionClosed", null,
            JsonSerializer.Serialize(new { Outcome = "Unsold", Reason = reason.ToString(), Detail = detail }));
        foreach (var bidder in Bidders(bids, except: null))
            await _emails.AuctionEndedWithoutSaleAsync(listing, bidder);
        await _emails.StudNoSaleAsync(listing, reason);
        return true;
    }

    // One email per bidder, however many bids they placed.
    private static IEnumerable<User> Bidders(IEnumerable<Bid> bids, Guid? except) =>
        bids.Where(b => b.BuyerUserId != except).GroupBy(b => b.BuyerUserId).Select(g => g.First().Buyer);

    // ── Charging (Task 8) ──────────────────────────────────────────────────────

    private Task<bool> ChargeAsync(Guid purchaseId) => Task.FromResult(false);
}
```

- [ ] **Step 5: Run green** — `dotnet test tests/Server.Tests --filter FullyQualifiedName~AuctionCloserCloseTests` → all pass. Full build (0 warnings) and full test run → green.

- [ ] **Step 6: Commit**

```bash
git add src/Server/Auctions tests/Server.Tests/Auctions
git commit -m "feat: AuctionCloser closes ended auctions (reserve check, winner, sale record, emails)"
```

---

### Task 8: `AuctionCloser` — charging the buyer fee, grace period, retries

**Files:**
- Modify: `src/Server/Auctions/AuctionCloser.cs`
- Test: `tests/Server.Tests/Auctions/AuctionCloserChargeTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Server.Tests/Auctions/AuctionCloserChargeTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Payments;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Auctions;

public class AuctionCloserChargeTests
{
    private readonly AuctionCloserFixture _f = new();

    /// <summary>An ended auction with one winning bid of $10,000 by a buyer with a card.</summary>
    private (AuctionListing Listing, User Winner) EndedAuctionWithWinner(bool withCard = true) => _f.Seed(db =>
    {
        var listing = AuctionTestData.Auction(db, _f.Clock.UtcNow.AddMinutes(-1));
        var winner = AuctionTestData.Buyer(db, "Winner");
        if (withCard) AuctionTestData.Card(db, winner);
        AuctionTestData.Bid(db, listing, winner, 10000m);
        return (listing, winner);
    });

    private async Task<Purchase> SaleRecord() => await _f.Read().Purchases.SingleAsync();
    private async Task<AuctionListing> Listing() => await _f.Read().AuctionListings.SingleAsync();

    [Fact]
    public async Task ASuccessfulCharge_CompletesTheSale()
    {
        EndedAuctionWithWinner();

        var run = await _f.CreateSut().RunAsync();

        run.ChargesAttempted.Should().Be(1);
        var sale = await SaleRecord();
        sale.Status.Should().Be(PurchaseStatus.Completed);
        sale.PaymentProvider.Should().Be("Fake");
        sale.PaymentReference.Should().Be("pi_1");
        sale.PaidAt.Should().Be(_f.Clock.UtcNow);
        var listing = await Listing();
        listing.Status.Should().Be(ListingStatus.Sold);
        listing.WinningBidId.Should().Be(sale.BidId);
        _f.Charges.Single().AmountIncGst.Should().Be(150m);
        _f.Charges.Single().IdempotencyKey.Should().Be($"buyer-fee-{sale.Id}-1");
        _f.Emails.Verify(e => e.WonAndChargedAsync(It.IsAny<AuctionListing>(), It.IsAny<Purchase>(), It.IsAny<User>()), Times.Once);
        _f.Emails.Verify(e => e.StudSaleConfirmationAsync(It.IsAny<AuctionListing>(), It.IsAny<Purchase>(), It.IsAny<User>()), Times.Once);
        _f.Audit.Verify(a => a.LogAsync("Purchase", sale.Id, "BuyerFeeCharged", null, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task AFirstFailure_StartsTheGracePeriodFromSettings_AndEmailsTheWinner()
    {
        EndedAuctionWithWinner();
        _f.Settings.ChargeGracePeriodHours = 5;
        _f.ChargesDecline();

        await _f.CreateSut().RunAsync();

        var sale = await SaleRecord();
        sale.Status.Should().Be(PurchaseStatus.Pending);
        sale.ChargeDueBy.Should().Be(_f.Clock.UtcNow.AddHours(5));
        sale.LastChargeFailure.Should().Be("Your card was declined.");
        sale.ChargeAttemptStartedAt.Should().BeNull();
        (await Listing()).Status.Should().Be(ListingStatus.AwaitingPayment);
        _f.Emails.Verify(e => e.PaymentFailedAsync(It.IsAny<AuctionListing>(), It.IsAny<Purchase>(), It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task WithoutAValidCard_ItFailsWithoutCallingTheProvider()
    {
        EndedAuctionWithWinner(withCard: false);

        await _f.CreateSut().RunAsync();

        _f.Charges.Should().BeEmpty();
        (await SaleRecord()).LastChargeFailure.Should().Be("No valid card on file.");
    }

    [Fact]
    public async Task DuringTheGracePeriod_NothingHappensUntilANewCardIsSaved()
    {
        EndedAuctionWithWinner();
        _f.ChargesDecline();
        await _f.CreateSut().RunAsync();

        _f.Clock.Advance(TimeSpan.FromMinutes(30));
        await _f.CreateSut().RunAsync();
        _f.Charges.Should().HaveCount(1, "no retry until a new card is saved or the deadline passes");

        _f.Seed(db =>
        {
            var p = db.Purchases.Single();
            p.RetryRequested = true;
            db.SaveChanges();
            return p;
        });
        _f.ChargesSucceed();
        await _f.CreateSut().RunAsync();

        _f.Charges.Should().HaveCount(2);
        _f.Charges[1].IdempotencyKey.Should().EndWith("-2");
        (await SaleRecord()).Status.Should().Be(PurchaseStatus.Completed);
    }

    [Fact]
    public async Task ARepeatFailureInTheGracePeriod_DoesNotEmailAgain_OrMoveTheDeadline()
    {
        EndedAuctionWithWinner();
        _f.ChargesDecline();
        await _f.CreateSut().RunAsync();
        var deadline = (await SaleRecord()).ChargeDueBy;

        _f.Seed(db => { var p = db.Purchases.Single(); p.RetryRequested = true; db.SaveChanges(); return p; });
        _f.Clock.Advance(TimeSpan.FromMinutes(10));
        await _f.CreateSut().RunAsync();

        (await SaleRecord()).ChargeDueBy.Should().Be(deadline);
        _f.Emails.Verify(e => e.PaymentFailedAsync(It.IsAny<AuctionListing>(), It.IsAny<Purchase>(), It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task AtTheDeadline_AFinalFailure_MeansNoSale()
    {
        EndedAuctionWithWinner();
        _f.ChargesDecline();
        await _f.CreateSut().RunAsync();

        _f.Clock.Advance(TimeSpan.FromHours(2));
        await _f.CreateSut().RunAsync();

        _f.Charges.Should().HaveCount(2);
        (await SaleRecord()).Status.Should().Be(PurchaseStatus.Voided);
        var listing = await Listing();
        listing.Status.Should().Be(ListingStatus.Unsold);
        listing.CloseReason.Should().Be(ListingCloseReason.ChargeFailed);
        _f.Emails.Verify(e => e.PaymentAbandonedAsync(It.IsAny<AuctionListing>(), It.IsAny<Purchase>(), It.IsAny<User>()), Times.Once);
        _f.Emails.Verify(e => e.StudNoSaleAsync(It.IsAny<AuctionListing>(), ListingCloseReason.ChargeFailed), Times.Once);
    }

    [Fact]
    public async Task AtTheDeadline_AFinalSuccess_CompletesTheSale()
    {
        EndedAuctionWithWinner();
        _f.ChargesDecline();
        await _f.CreateSut().RunAsync();

        _f.Clock.Advance(TimeSpan.FromHours(2));
        _f.ChargesSucceed();
        await _f.CreateSut().RunAsync();

        (await SaleRecord()).Status.Should().Be(PurchaseStatus.Completed);
    }

    [Fact]
    public async Task AnInterruptedAttempt_IsRepeatedWithTheSameIdempotencyKey()
    {
        EndedAuctionWithWinner();
        _f.Provider.Setup(p => p.ChargeSavedCardAsync(It.IsAny<ChargeRequest>()))
            .Callback<ChargeRequest>(_f.Charges.Add)
            .ThrowsAsync(new HttpRequestException("provider unreachable"));

        var first = await _f.CreateSut().RunAsync();
        first.ChargesAttempted.Should().Be(0);
        (await SaleRecord()).ChargeAttemptStartedAt.Should().NotBeNull();

        _f.Clock.Advance(TimeSpan.FromMinutes(1));
        await _f.CreateSut().RunAsync();
        _f.Charges.Should().HaveCount(1, "an attempt younger than 2 minutes may still be in flight");

        _f.Clock.Advance(TimeSpan.FromMinutes(2));
        _f.ChargesSucceed();
        await _f.CreateSut().RunAsync();

        _f.Charges.Should().HaveCount(2);
        _f.Charges[1].IdempotencyKey.Should().Be(_f.Charges[0].IdempotencyKey);
        (await SaleRecord()).Status.Should().Be(PurchaseStatus.Completed);
    }
}
```

- [ ] **Step 2: Run red** — `dotnet test tests/Server.Tests --filter FullyQualifiedName~AuctionCloserChargeTests` → failures (the charge step is a stub).

- [ ] **Step 3: Implement** — in `AuctionCloser.cs` replace the `// ── Charging (Task 8)` section with:

```csharp
    // ── Charging ───────────────────────────────────────────────────────────────

    private sealed record ChargeAttempt(Guid PurchaseId, int AttemptNo, Guid BuyerUserId, decimal AmountIncGst, string Description);

    private async Task<bool> ChargeAsync(Guid purchaseId)
    {
        var attempt = await _transactions.RunAsync(() => StartAttemptAsync(purchaseId));
        if (attempt == null) return false;

        // Outside any transaction. If this throws (provider unreachable) the attempt stays
        // "started" and is repeated with the same key once it counts as interrupted.
        var result = await ChargeCardAsync(attempt);

        return await _transactions.RunAsync(() => RecordResultAsync(attempt, result));
    }

    private async Task<ChargeAttempt?> StartAttemptAsync(Guid purchaseId)
    {
        var now = Now;
        var purchase = await _purchases.GetForChargeAsync(purchaseId);
        if (purchase is not { Status: PurchaseStatus.Pending } || !IsDue(purchase, now)) return null;

        // An interrupted attempt keeps its number, so the provider's idempotency key returns the
        // original result instead of charging twice.
        var attemptNo = purchase.ChargeAttemptStartedAt != null ? purchase.ChargeAttempts : purchase.ChargeAttempts + 1;
        purchase.ChargeAttempts = attemptNo;
        purchase.ChargeAttemptStartedAt = now;
        purchase.RetryRequested = false;
        await _purchases.UpdateAsync(purchase);

        return new ChargeAttempt(purchase.Id, attemptNo, purchase.BuyerUserId, purchase.BuyerFeeIncGst,
            $"Buyer fee — {purchase.Listing.Stallion?.Name}, {purchase.Listing.Season?.Name}");
    }

    private static bool IsDue(Purchase p, DateTime now) => p.ChargeAttemptStartedAt is { } started
        ? started <= now - AuctionCloseOptions.InterruptedAttemptAge
        : p.ChargeAttempts == 0 || p.RetryRequested || p.ChargeDueBy <= now;

    private async Task<ChargeResult> ChargeCardAsync(ChargeAttempt attempt)
    {
        var card = await _cards.GetByUserIdAsync(attempt.BuyerUserId);
        if (card == null || card.Provider != _provider.Name || !card.IsValidOn(DateOnly.FromDateTime(Now)))
            return ChargeResult.Declined("no_valid_card", "No valid card on file.");

        return await _provider.ChargeSavedCardAsync(new ChargeRequest(
            card.ProviderCustomerId,
            card.ProviderPaymentMethodId,
            attempt.AmountIncGst,
            attempt.Description,
            new Dictionary<string, string> { ["kind"] = "buyer-fee", ["purchaseId"] = attempt.PurchaseId.ToString() },
            $"buyer-fee-{attempt.PurchaseId}-{attempt.AttemptNo}"));
    }

    private async Task<bool> RecordResultAsync(ChargeAttempt attempt, ChargeResult result)
    {
        var now = Now;
        var purchase = await _purchases.GetForChargeAsync(attempt.PurchaseId);
        if (purchase is not { Status: PurchaseStatus.Pending } || purchase.ChargeAttempts != attempt.AttemptNo
            || purchase.ChargeAttemptStartedAt == null)
        {
            _log.LogWarning("Charge result for sale record {PurchaseId} attempt {AttemptNo} arrived after it changed; ignored",
                attempt.PurchaseId, attempt.AttemptNo);
            return false;
        }

        var listing = (AuctionListing)purchase.Listing;
        purchase.ChargeAttemptStartedAt = null;

        if (result.Succeeded)
        {
            purchase.Status = PurchaseStatus.Completed;
            purchase.PaymentProvider = _provider.Name;
            purchase.PaymentReference = result.PaymentReference;
            purchase.PaidAt = now;
            purchase.LastChargeFailure = null;
            await _purchases.UpdateAsync(purchase);

            listing.Status = ListingStatus.Sold;
            listing.WinningBidId = purchase.BidId;
            await _listings.UpdateAsync(listing);

            await _audit.LogAsync("Purchase", purchase.Id, "BuyerFeeCharged", null, JsonSerializer.Serialize(new
            {
                purchase.BuyerFeeIncGst, purchase.BuyerFeeExGst, purchase.BuyerFeeGst, purchase.PaymentReference, attempt.AttemptNo
            }));
            await _emails.WonAndChargedAsync(listing, purchase, purchase.Buyer);
            await _emails.StudSaleConfirmationAsync(listing, purchase, purchase.Buyer);
            return true;
        }

        var failure = result.FailureMessage ?? "The payment was declined.";
        purchase.LastChargeFailure = failure.Length > 500 ? failure[..500] : failure;

        if (purchase.ChargeDueBy == null)
        {
            // First failure: the grace period is read from Staff settings now.
            var settings = await _settings.GetAsync();
            purchase.ChargeDueBy = now.AddHours(settings.ChargeGracePeriodHours);
            await _purchases.UpdateAsync(purchase);
            await _audit.LogAsync("Purchase", purchase.Id, "BuyerFeeChargeFailed", null, JsonSerializer.Serialize(new
            {
                attempt.AttemptNo, result.FailureCode, purchase.ChargeDueBy
            }));
            await _emails.PaymentFailedAsync(listing, purchase, purchase.Buyer);
        }
        else if (now >= purchase.ChargeDueBy)
        {
            // Final attempt failed: no sale. Not offered to the next bidder.
            purchase.Status = PurchaseStatus.Voided;
            await _purchases.UpdateAsync(purchase);
            listing.Status = ListingStatus.Unsold;
            listing.CloseReason = ListingCloseReason.ChargeFailed;
            await _listings.UpdateAsync(listing);
            await _audit.LogAsync("Purchase", purchase.Id, "BuyerFeeChargeAbandoned", null, JsonSerializer.Serialize(new
            {
                attempt.AttemptNo, result.FailureCode
            }));
            await _emails.PaymentAbandonedAsync(listing, purchase, purchase.Buyer);
            await _emails.StudNoSaleAsync(listing, ListingCloseReason.ChargeFailed);
        }
        else
        {
            // A retry during the grace period failed too: the deadline stands; no repeat email.
            await _purchases.UpdateAsync(purchase);
            await _audit.LogAsync("Purchase", purchase.Id, "BuyerFeeChargeFailed", null, JsonSerializer.Serialize(new
            {
                attempt.AttemptNo, result.FailureCode, purchase.ChargeDueBy
            }));
        }
        return true;
    }
```

- [ ] **Step 4: Run green** — both closer test classes pass; full build (0 warnings) and full test run → green.

- [ ] **Step 5: Commit**

```bash
git add src/Server/Auctions/AuctionCloser.cs tests/Server.Tests/Auctions/AuctionCloserChargeTests.cs
git commit -m "feat: charge the winner's saved card at close, with grace period, retries and no-sale"
```

---
### Task 9: `AuctionCloseService` — run the closer every minute

**Files:**
- Create: `src/Server/Auctions/AuctionCloseService.cs`, `src/Server/Auctions/AuctionServiceCollectionExtensions.cs`
- Modify: `src/Server/Program.cs`
- Test: `tests/Server.Tests/Auctions/AuctionCloseServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Server.Tests/Auctions/AuctionCloseServiceTests.cs
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Stallions.Server.Auctions;

namespace Stallions.Server.Tests.Auctions;

public class AuctionCloseServiceTests
{
    private readonly Mock<IAuctionCloser> _closer = new();

    private AuctionCloseService CreateSut(bool enabled)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _closer.Object);
        var scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new AuctionCloseService(scopes,
            Options.Create(new AuctionCloseOptions { Enabled = enabled, IntervalSeconds = 60 }),
            NullLogger<AuctionCloseService>.Instance);
    }

    [Fact]
    public async Task WhenEnabled_RunsTheCloserStraightAway()
    {
        var ran = new TaskCompletionSource();
        _closer.Setup(c => c.RunAsync(It.IsAny<CancellationToken>()))
            .Callback(() => ran.TrySetResult())
            .ReturnsAsync(new AuctionCloseRun(0, 0));
        var sut = CreateSut(enabled: true);

        await sut.StartAsync(CancellationToken.None);
        var finished = await Task.WhenAny(ran.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        await sut.StopAsync(CancellationToken.None);

        finished.Should().Be(ran.Task);
    }

    [Fact]
    public async Task WhenDisabled_NeverRunsTheCloser()
    {
        var sut = CreateSut(enabled: false);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await sut.StopAsync(CancellationToken.None);

        _closer.Verify(c => c.RunAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run red** — compile errors.

- [ ] **Step 3: Implement**

```csharp
// src/Server/Auctions/AuctionCloseService.cs
using Microsoft.Extensions.Options;
using Stallions.Server.Infrastructure;

namespace Stallions.Server.Auctions;

/// <summary>
/// Runs IAuctionCloser every AuctionClose:IntervalSeconds (default 60). Runs inside the API (dev's
/// App Service has Always On so it keeps running when the site is idle); safe on several instances.
/// </summary>
public class AuctionCloseService : PeriodicScopedService
{
    private readonly AuctionCloseOptions _options;
    private readonly ILogger<AuctionCloseService> _log;

    public AuctionCloseService(IServiceScopeFactory scopes, IOptions<AuctionCloseOptions> options, ILogger<AuctionCloseService> log)
        : base(scopes, log)
    {
        _options = options.Value;
        _log = log;
    }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(10, _options.IntervalSeconds));

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _log.LogWarning("Auction closing is switched off (AuctionClose:Enabled = false)");
            return Task.CompletedTask;
        }
        return base.ExecuteAsync(stoppingToken);
    }

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var run = await services.GetRequiredService<IAuctionCloser>().RunAsync(ct);
        if (run.AuctionsClosed > 0 || run.ChargesAttempted > 0)
            _log.LogInformation("Auction close run: {Closed} auctions closed, {Charged} buyer-fee charges made",
                run.AuctionsClosed, run.ChargesAttempted);
    }
}
```

```csharp
// src/Server/Auctions/AuctionServiceCollectionExtensions.cs
namespace Stallions.Server.Auctions;

public static class AuctionServiceCollectionExtensions
{
    /// <summary>The auction closer and its background service. Needs AddPayments, AddEmail and TimeProvider.</summary>
    public static IServiceCollection AddAuctionClosing(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuctionCloseOptions>(configuration.GetSection(AuctionCloseOptions.Section));
        services.AddScoped<IAuctionCloser, AuctionCloser>();
        services.AddHostedService<AuctionCloseService>();
        return services;
    }
}
```

`Program.cs` — after `AddEmail(...)`: `builder.Services.AddAuctionClosing(builder.Configuration);` with
`using Stallions.Server.Auctions;`.

- [ ] **Step 4: Run green**, full build (0 warnings) and full test run → green.

- [ ] **Step 5: Local smoke check** — run the API (`dotnet run --project src/Server --launch-profile https --urls https://localhost:7090`
  if 7083 is taken) for a minute and confirm the log has no `AuctionCloseService run failed` or
  `EmailDispatchService run failed` errors. Stop it.

- [ ] **Step 6: Commit**

```bash
git add src/Server/Auctions/AuctionCloseService.cs src/Server/Auctions/AuctionServiceCollectionExtensions.cs src/Server/Program.cs tests/Server.Tests/Auctions/AuctionCloseServiceTests.cs
git commit -m "feat: run the auction closer every minute as a hosted service"
```

---

### Task 10: Saving a new card retries a failed buyer-fee charge

**Files:**
- Modify: `src/Server/Payments/PaymentEventProcessor.cs`
- Test: `tests/Server.Tests/Payments/PaymentEventProcessorTests.cs`

- [ ] **Step 1: Write the failing tests** — in `PaymentEventProcessorTests`, the three places that construct
  `PaymentEventProcessor` (`CreateSut`, the `failing` instance in
  `ProcessingFailure_ReleasesTheEventSoTheRetryIsProcessed`, and `CreateSutWith`) gain
  `new PurchaseRepository(_db),` straight after `new SubscriptionRepository(_db),`. Then add:

```csharp
    private Purchase FailedCharge(Guid buyerId, DateTime? dueBy)
    {
        var purchase = new Purchase
        {
            BuyerUserId = buyerId, Status = PurchaseStatus.Pending, ChargeAttempts = 1, ChargeDueBy = dueBy
        };
        _db.Purchases.Add(purchase);
        _db.SaveChanges();
        return purchase;
    }

    [Fact]
    public async Task CardSaved_FlagsTheBuyersFailedChargeForRetry()
    {
        var failed = FailedCharge(_buyer.Id, DateTime.UtcNow.AddHours(1));

        await CreateSut().ProcessAsync(CardSaved("evt_1"));

        (await _db.Purchases.SingleAsync(p => p.Id == failed.Id)).RetryRequested.Should().BeTrue();
    }

    [Fact]
    public async Task CardSaved_LeavesSaleRecordsWithoutAFailedCharge_Alone()
    {
        var notYetCharged = FailedCharge(_buyer.Id, dueBy: null);

        await CreateSut().ProcessAsync(CardSaved("evt_1"));

        (await _db.Purchases.SingleAsync(p => p.Id == notYetCharged.Id)).RetryRequested.Should().BeFalse();
    }
```

- [ ] **Step 2: Run red** — compile error (constructor) then the first test fails.

- [ ] **Step 3: Implement** — add `IPurchaseRepository purchases` to the constructor (after
  `ISubscriptionRepository subscriptions`, stored as `_purchases`), and at the end of `SaveCardAsync`,
  just before the audit line:

```csharp
        // A winner whose buyer-fee charge failed: the auction closer retries with the new card on its next run.
        foreach (var purchase in await _purchases.GetAwaitingCardRetryAsync(e.UserId))
        {
            purchase.RetryRequested = true;
            await _purchases.UpdateAsync(purchase);
        }
```

  Update the class comment's first sentence to: "The only code that changes data because of a
  provider callback (the auction closer handles buyer-fee charges)."

- [ ] **Step 4: Run green**, full build (0 warnings) and full test run → green (the controller tests build the
  processor through mocks or DI; if any constructs it directly, add `new PurchaseRepository(_db)` there too).

- [ ] **Step 5: Commit**

```bash
git add src/Server/Payments/PaymentEventProcessor.cs tests/Server.Tests/Payments/PaymentEventProcessorTests.cs
git commit -m "feat: saving a new card retries a failed buyer-fee charge"
```

---

### Task 11: Bidding — end-time re-check, outbid email, my-bids details, the buyer's auction result

**Files:**
- Modify: `src/Server/Services/BidService.cs`, `src/Server/Data/Repositories/BidRepository.cs`, `src/Shared/DTOs/Bids/BidDto.cs`,
  `src/Server/Services/IPurchaseService.cs`, `src/Server/Services/PurchaseService.cs`, `src/Server/Controllers/PurchasesController.cs`,
  `src/Server/Data/Repositories/IPurchaseRepository.cs`, `src/Server/Data/Repositories/PurchaseRepository.cs`, `src/Client/Services/PurchaseApiService.cs`
- Create: `src/Shared/AuctionOutcomes.cs`, `src/Shared/DTOs/Checkout/MyAuctionResultDto.cs`
- Test: `tests/Server.Tests/Services/BidServiceTests.cs`, `tests/Server.Tests/Services/PurchaseServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

`BidServiceTests`: add `private readonly Mock<IAuctionEmails> _emailsMock = new();` (with
`using Stallions.Server.Email;`) and pass `_emailsMock.Object` as the new last argument in `CreateSut`. Add:

```csharp
    [Fact]
    public async Task PlaceBid_WhenTheAuctionClosesWhileBidding_IsRejected()
    {
        var buyer = ActiveBuyer();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var open = OpenAuction();
        var closed = OpenAuction();
        closed.Id = open.Id;
        closed.Status = ListingStatus.AwaitingPayment;
        _listingRepoMock.SetupSequence(r => r.GetAuctionByIdAsync(open.Id)).ReturnsAsync(open).ReturnsAsync(closed);

        var result = await CreateSut().PlaceBidAsync(open.Id, new PlaceBidRequest { AmountIncGst = 5000m });

        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Be("This auction has ended.");
        _bidRepoMock.Verify(r => r.AddAsync(It.IsAny<Bid>()), Times.Never);
    }

    [Fact]
    public async Task PlaceBid_OutbiddingSomeone_QueuesTheirOutbidEmail()
    {
        var buyer = ActiveBuyer();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var auction = OpenAuction();
        _listingRepoMock.Setup(r => r.GetAuctionByIdAsync(auction.Id)).ReturnsAsync(auction);
        var previous = new Bid { AuctionListingId = auction.Id, BuyerUserId = Guid.NewGuid(), AmountIncGst = 5000m };
        _bidRepoMock.Setup(r => r.GetHighestBidAsync(auction.Id)).ReturnsAsync(previous);
        _bidRepoMock.Setup(r => r.AddAsync(It.IsAny<Bid>())).ReturnsAsync((Bid b) => b);

        await CreateSut().PlaceBidAsync(auction.Id, new PlaceBidRequest { AmountIncGst = 5500m });

        _emailsMock.Verify(e => e.OutbidAsync(auction, previous.BuyerUserId, 5500m), Times.Once);
    }

    [Fact]
    public async Task PlaceBid_TheFirstBid_SendsNoOutbidEmail()
    {
        var buyer = ActiveBuyer();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var auction = OpenAuction();
        _listingRepoMock.Setup(r => r.GetAuctionByIdAsync(auction.Id)).ReturnsAsync(auction);
        _bidRepoMock.Setup(r => r.GetHighestBidAsync(auction.Id)).ReturnsAsync((Bid?)null);
        _bidRepoMock.Setup(r => r.AddAsync(It.IsAny<Bid>())).ReturnsAsync((Bid b) => b);

        await CreateSut().PlaceBidAsync(auction.Id, new PlaceBidRequest { AmountIncGst = 5000m });

        _emailsMock.Verify(e => e.OutbidAsync(It.IsAny<AuctionListing>(), It.IsAny<Guid>(), It.IsAny<decimal>()), Times.Never);
    }

    [Fact]
    public async Task GetMine_SaysWhetherTheAuctionIsStillOpen()
    {
        var buyer = ActiveBuyer();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var open = OpenAuction();
        open.Stallion = new Stallion { Name = "Snitzel" };
        var ended = OpenAuction();
        ended.Status = ListingStatus.Unsold;
        _bidRepoMock.Setup(r => r.GetByBuyerIdAsync(buyer.Id)).ReturnsAsync(new List<Bid>
        {
            new() { AuctionListing = open, Status = BidStatus.Active },
            new() { AuctionListing = ended, Status = BidStatus.Lost }
        });

        var result = await CreateSut().GetMineAsync();

        result.Value![0].AuctionOpen.Should().BeTrue();
        result.Value[0].StallionName.Should().Be("Snitzel");
        result.Value[1].AuctionOpen.Should().BeFalse();
    }
```

`PurchaseServiceTests`: add `private readonly Mock<IListingRepository> _listings = new();` and
`private readonly Mock<IBidRepository> _bids = new();`, change `CreateSut()` to
`new(_purchases.Object, _audit.Object, _users.Object, _listings.Object, _bids.Object)`, add
`using Stallions.Shared;`, and add:

```csharp
    private (User Buyer, AuctionListing Listing) BuyerAndAuction(ListingStatus status, ListingCloseReason? reason = null)
    {
        var buyer = Buyer();
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var listing = new AuctionListing { Id = Guid.NewGuid(), Status = status, CloseReason = reason };
        _listings.Setup(r => r.GetAuctionByIdAsync(listing.Id)).ReturnsAsync(listing);
        return (buyer, listing);
    }

    [Theory]
    [InlineData(PurchaseStatus.Completed, false, AuctionOutcomes.Won)]
    [InlineData(PurchaseStatus.Pending, false, AuctionOutcomes.PaymentPending)]
    [InlineData(PurchaseStatus.Pending, true, AuctionOutcomes.PaymentFailed)]
    [InlineData(PurchaseStatus.Voided, true, AuctionOutcomes.NoSale)]
    public async Task MyAuctionResult_ForTheWinner_FollowsTheSaleRecord(PurchaseStatus status, bool failed, string expected)
    {
        var (buyer, listing) = BuyerAndAuction(ListingStatus.AwaitingPayment);
        var dueBy = failed ? DateTime.UtcNow.AddHours(1) : (DateTime?)null;
        var sale = new Purchase { Id = Guid.NewGuid(), Status = status, ChargeDueBy = dueBy };
        _purchases.Setup(r => r.GetByListingAndBuyerAsync(listing.Id, buyer.Id)).ReturnsAsync(sale);

        var result = (await CreateSut().GetMyAuctionResultAsync(listing.Id)).Value!;

        result.Outcome.Should().Be(expected);
        result.PurchaseId.Should().Be(sale.Id);
        result.ChargeDueBy.Should().Be(dueBy);
    }

    [Theory]
    [InlineData(ListingStatus.Sold, null, AuctionOutcomes.Lost)]
    [InlineData(ListingStatus.Unsold, ListingCloseReason.ReserveNotMet, AuctionOutcomes.EndedWithoutSale)]
    [InlineData(ListingStatus.Unsold, ListingCloseReason.ChargeFailed, AuctionOutcomes.Lost)]
    public async Task MyAuctionResult_ForAnotherBidder(ListingStatus status, ListingCloseReason? reason, string expected)
    {
        var (buyer, listing) = BuyerAndAuction(status, reason);
        _bids.Setup(r => r.GetByAuctionListingIdAsync(listing.Id))
            .ReturnsAsync(new List<Bid> { new() { BuyerUserId = buyer.Id, AmountIncGst = 5000m } });

        (await CreateSut().GetMyAuctionResultAsync(listing.Id)).Value!.Outcome.Should().Be(expected);
    }

    [Fact]
    public async Task MyAuctionResult_WhileOpen_OrWithoutBids_IsNone()
    {
        var (_, open) = BuyerAndAuction(ListingStatus.Active);
        _bids.Setup(r => r.GetByAuctionListingIdAsync(It.IsAny<Guid>())).ReturnsAsync(new List<Bid>());

        (await CreateSut().GetMyAuctionResultAsync(open.Id)).Value!.Outcome.Should().Be(AuctionOutcomes.None);
    }
```

- [ ] **Step 2: Run red** — compile errors.

- [ ] **Step 3: Shared types**

```csharp
// src/Shared/AuctionOutcomes.cs
namespace Stallions.Shared;

/// <summary>How a closed auction turned out for the signed-in buyer (MyAuctionResultDto.Outcome).</summary>
public static class AuctionOutcomes
{
    public const string None = "None";
    public const string Won = "Won";
    public const string PaymentPending = "PaymentPending";
    public const string PaymentFailed = "PaymentFailed";
    public const string NoSale = "NoSale";
    public const string Lost = "Lost";
    public const string EndedWithoutSale = "EndedWithoutSale";
}
```

```csharp
// src/Shared/DTOs/Checkout/MyAuctionResultDto.cs
namespace Stallions.Shared.DTOs.Checkout;

/// <summary>The signed-in buyer's result on one auction — see AuctionOutcomes.</summary>
public class MyAuctionResultDto
{
    public string Outcome { get; set; } = AuctionOutcomes.None;
    public Guid? PurchaseId { get; set; }
    /// <summary>After a failed charge: update the card by this time (UTC).</summary>
    public DateTime? ChargeDueBy { get; set; }
}
```

`BidDto` — add:

```csharp
    public string StallionName { get; set; } = string.Empty;
    /// <summary>The auction is still taking bids.</summary>
    public bool AuctionOpen { get; set; }
```

- [ ] **Step 4: BidService**

Constructor: add `IAuctionEmails emails` as the last parameter (field `_emails`, `using Stallions.Server.Email;`).
In `PlaceBidAsync`, inside the transaction, **first** after `_db.ChangeTracker.Clear();` and `BeginTransactionAsync`
(inside the `try`, before `GetHighestBidAsync`):

```csharp
                // Re-checked inside the transaction: once the auction has ended, no bid may land —
                // the auction closer reads the bids 30 seconds after the end.
                var current = await _listingRepo.GetAuctionByIdAsync(auctionListingId);
                if (current is not { Status: ListingStatus.Active } || current.EndDateTime <= DateTime.UtcNow)
                    return ServiceResult<BidDto>.BadRequest("This auction has ended.");
```

and just before `await tx.CommitAsync();`:

```csharp
                if (highest != null)
                    await _emails.OutbidAsync(current, highest.BuyerUserId, request.AmountIncGst);
```

Update the transaction comment: `caller` and `listing` are read-only; the listing is re-read inside
the transaction for the end-time check.

`MapToDto`:

```csharp
    private static BidDto MapToDto(Bid b) => new()
    {
        Id = b.Id,
        AuctionListingId = b.AuctionListingId,
        BuyerUserId = b.BuyerUserId,
        AmountIncGst = b.AmountIncGst,
        PlacedAt = b.PlacedAt,
        Status = b.Status.ToString(),
        StallionName = b.AuctionListing?.Stallion?.Name ?? string.Empty,
        AuctionOpen = b.AuctionListing is { Status: ListingStatus.Active } a && a.EndDateTime > DateTime.UtcNow
    };
```

`BidRepository.GetByBuyerIdAsync` — load the auction and stallion:

```csharp
    public async Task<IReadOnlyList<Bid>> GetByBuyerIdAsync(Guid buyerUserId) =>
        await _db.Bids
            .Include(b => b.AuctionListing).ThenInclude(a => a.Stallion)
            .Where(b => b.BuyerUserId == buyerUserId)
            .OrderByDescending(b => b.PlacedAt)
            .ToListAsync();
```

- [ ] **Step 5: The buyer's auction result**

`IPurchaseRepository` / `PurchaseRepository`:

```csharp
    /// <summary>The buyer's most recent sale record on the listing, if any.</summary>
    Task<Purchase?> GetByListingAndBuyerAsync(Guid listingId, Guid buyerUserId);
```

```csharp
    public async Task<Purchase?> GetByListingAndBuyerAsync(Guid listingId, Guid buyerUserId) =>
        await _db.Purchases
            .Where(p => p.ListingId == listingId && p.BuyerUserId == buyerUserId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();
```

`IPurchaseService`: add `Task<ServiceResult<MyAuctionResultDto>> GetMyAuctionResultAsync(Guid listingId);`

`PurchaseService`: constructor gains `IListingRepository listingRepo, IBidRepository bidRepo` (last two
parameters, fields `_listingRepo`, `_bidRepo`; `using Stallions.Shared;`). Add:

```csharp
    public async Task<ServiceResult<MyAuctionResultDto>> GetMyAuctionResultAsync(Guid listingId)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<MyAuctionResultDto>.Forbidden();

        var listing = await _listingRepo.GetAuctionByIdAsync(listingId);
        if (listing == null) return ServiceResult<MyAuctionResultDto>.NotFound("Auction listing not found.");

        var sale = await _purchaseRepo.GetByListingAndBuyerAsync(listingId, caller.Id);
        if (sale != null)
        {
            return ServiceResult<MyAuctionResultDto>.Ok(new MyAuctionResultDto
            {
                Outcome = sale.Status switch
                {
                    PurchaseStatus.Completed or PurchaseStatus.Refunded => AuctionOutcomes.Won,
                    PurchaseStatus.Pending => sale.ChargeDueBy != null ? AuctionOutcomes.PaymentFailed : AuctionOutcomes.PaymentPending,
                    _ => AuctionOutcomes.NoSale
                },
                PurchaseId = sale.Id,
                ChargeDueBy = sale.ChargeDueBy
            });
        }

        if (listing.Status == ListingStatus.Active)
            return ServiceResult<MyAuctionResultDto>.Ok(new MyAuctionResultDto());

        var bids = await _bidRepo.GetByAuctionListingIdAsync(listingId);
        if (!bids.Any(b => b.BuyerUserId == caller.Id))
            return ServiceResult<MyAuctionResultDto>.Ok(new MyAuctionResultDto());

        // Below reserve (or no snapshot) — nobody won. A failed winner's charge still means another buyer won.
        var endedWithoutSale = listing.Status == ListingStatus.Unsold && listing.CloseReason != ListingCloseReason.ChargeFailed;
        return ServiceResult<MyAuctionResultDto>.Ok(new MyAuctionResultDto
        {
            Outcome = endedWithoutSale ? AuctionOutcomes.EndedWithoutSale : AuctionOutcomes.Lost
        });
    }
```

`PurchasesController`:

```csharp
    [HttpGet("listings/{id:guid}/my-result")]
    [Authorize(Policy = "BuyerOnly")]
    public async Task<IActionResult> GetMyAuctionResult(Guid id)
    {
        var r = await _purchases.GetMyAuctionResultAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
```

`PurchaseApiService` (client):

```csharp
    public virtual async Task<MyAuctionResultDto?> GetMyResultAsync(Guid listingId)
    {
        var response = await _http.GetAsync($"api/listings/{listingId}/my-result");
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, "Failed to load your auction result.");
        return await response.Content.ReadFromJsonAsync<MyAuctionResultDto>();
    }
```

- [ ] **Step 6: Run green**, full build (0 warnings) and full test run → green.

- [ ] **Step 7: Commit**

```bash
git add src/Server/Services/BidService.cs src/Server/Data/Repositories src/Shared/DTOs/Bids/BidDto.cs src/Shared/AuctionOutcomes.cs src/Shared/DTOs/Checkout/MyAuctionResultDto.cs src/Server/Services/IPurchaseService.cs src/Server/Services/PurchaseService.cs src/Server/Controllers/PurchasesController.cs src/Client/Services/PurchaseApiService.cs tests/Server.Tests/Services/BidServiceTests.cs tests/Server.Tests/Services/PurchaseServiceTests.cs
git commit -m "feat: bid end-time re-check, outbid email, my-bids details, buyer's auction result"
```

---

### Task 12: Client — closed auctions, My Bids, My sale records, status badges, card retry notice

**Files:**
- Create: `src/Client/Components/ListingStatusText.cs`, `tests/Client.Tests/Components/ListingStatusTextTests.cs`, `tests/Client.Tests/Pages/MyBidsTests.cs`
- Modify: `src/Shared/DTOs/Listings/ListingDto.cs`, `src/Shared/DTOs/Admin/ListingStaffSummaryDto.cs`, `src/Server/Services/ListingService.cs`,
  `src/Server/Services/AdminService.cs`, `src/Client/Pages/ListingDetail.razor`, `src/Client/Pages/ListingDetail.razor.css`,
  `src/Client/Pages/MyBids.razor`, `src/Client/Pages/MyPurchases.razor`, `src/Client/Pages/AccountCard.razor`,
  `src/Client/Pages/Admin/AdminListings.razor`, `src/Client/Pages/Admin/AdminListingDetail.razor`, `src/Client/Pages/Staff/StaffListings.razor`,
  `src/Client/Layout/NavBar.razor` (only if it links "My Purchases" — rename the link text to "My sale records")
- Test: `tests/Client.Tests/Pages/ListingDetailTests.cs`, `tests/Client.Tests/Pages/MyPurchasesTests.cs`, `tests/Client.Tests/Pages/AccountCardTests.cs`

- [ ] **Step 1: Close reason on listing DTOs**

`ListingDto` and `ListingStaffSummaryDto` — add:

```csharp
    /// <summary>Why the auction closed without a sale (NoBids, ReserveNotMet, ChargeFailed); null otherwise.</summary>
    public string? CloseReason { get; set; }
```

Set it in `ListingService`'s DTO mapping (`CloseReason = al.CloseReason?.ToString()`, next to `ClosedAt`) and in
`AdminService`'s staff summary mapping (`CloseReason = l.CloseReason?.ToString()`).

- [ ] **Step 2: Write the failing client tests**

```csharp
// tests/Client.Tests/Components/ListingStatusTextTests.cs
using FluentAssertions;
using Stallions.Client.Components;

namespace Stallions.Client.Tests.Components;

public class ListingStatusTextTests
{
    [Theory]
    [InlineData("AwaitingPayment", null, "Awaiting payment")]
    [InlineData("Unsold", "NoBids", "Unsold — no bids")]
    [InlineData("Unsold", "ReserveNotMet", "Unsold — reserve not met")]
    [InlineData("Unsold", "ChargeFailed", "Unsold — payment failed")]
    [InlineData("Active", null, "Active")]
    public void Label(string status, string? reason, string expected) =>
        ListingStatusText.Label(status, reason).Should().Be(expected);

    [Theory]
    [InlineData("Sold", true)]
    [InlineData("Cancelled", true)]
    [InlineData("AwaitingPayment", true)]
    [InlineData("Unsold", true)]
    [InlineData("Active", false)]
    [InlineData("Draft", false)]
    public void IsFinished(string status, bool expected) =>
        ListingStatusText.IsFinished(status).Should().Be(expected);
}
```

```csharp
// tests/Client.Tests/Pages/MyBidsTests.cs
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Bids;

namespace Stallions.Client.Tests.Pages;

public class MyBidsTests : TestContext
{
    private IRenderedComponent<MyBids> Render(params BidDto[] bids)
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        var api = new Mock<BidApiService>(MockBehavior.Loose, new HttpClient { BaseAddress = new Uri("https://localhost/") });
        api.Setup(a => a.GetMyBidsAsync()).ReturnsAsync(bids.ToList());
        Services.AddSingleton(api.Object);
        return RenderComponent<MyBids>();
    }

    [Fact]
    public void ShowsLeadingWonAndLost_WithTheStallion()
    {
        var cut = Render(
            new BidDto { Id = Guid.NewGuid(), StallionName = "Snitzel", Status = "Active", AuctionOpen = true, AmountIncGst = 5000m },
            new BidDto { Id = Guid.NewGuid(), StallionName = "Zoustar", Status = "Won", AmountIncGst = 6000m },
            new BidDto { Id = Guid.NewGuid(), StallionName = "I Am Invincible", Status = "Lost", AmountIncGst = 7000m });

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Leading"));
        cut.Markup.Should().Contain("Snitzel").And.Contain("Won").And.Contain("Lost").And.Contain("Sale record");
    }

    [Fact]
    public void BidAgain_OnlyWhileTheAuctionIsOpen()
    {
        var cut = Render(
            new BidDto { Id = Guid.NewGuid(), Status = "Outbid", AuctionOpen = true },
            new BidDto { Id = Guid.NewGuid(), Status = "Outbid", AuctionOpen = false });

        cut.WaitForAssertion(() => cut.FindAll("a").Count(a => a.TextContent.Contains("Bid again")).Should().Be(1));
    }
}
```

`ListingDetailTests` — extend `RegisterServices` with a `MyAuctionResultDto? myResult = null` parameter and register:

```csharp
        var purchaseMock = new Mock<PurchaseApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        purchaseMock.Setup(s => s.GetMyResultAsync(listing.Id)).ReturnsAsync(myResult);
        Services.AddSingleton(purchaseMock.Object);
```

and add (`using Stallions.Shared;`):

```csharp
    private static AuctionListingDto ClosedAuction(string status = "AwaitingPayment") => new()
    {
        Id = Guid.NewGuid(), StallionName = "Snitzel", StudFarmName = "Arrowfield", Status = status,
        EndDateTime = DateTime.UtcNow.AddMinutes(-5), BuyerFeeIncGst = 150m, MinimumBidIncrement = 25m
    };

    [Fact]
    public void AClosedAuction_HidesTheBidForm()
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        var listing = ClosedAuction("Sold");
        RegisterServices(listing, role: "Buyer", card: new SavedCardDto { IsValid = true },
            myResult: new MyAuctionResultDto { Outcome = AuctionOutcomes.Lost });

        var cut = RenderComponent<ListingDetail>(p => p.Add(c => c.Id, listing.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Auction closed"));
        cut.Markup.Should().NotContain("Place bid");
    }

    [Fact]
    public void AnActiveAuctionPastItsEndTime_IsShownAsClosed()
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        var listing = ClosedAuction("Active");
        RegisterServices(listing, role: "Buyer", card: new SavedCardDto { IsValid = true });

        var cut = RenderComponent<ListingDetail>(p => p.Add(c => c.Id, listing.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Auction closed"));
        cut.Markup.Should().NotContain("Place bid");
    }

    [Fact]
    public void TheWinnerWithAFailedPayment_IsAskedToUpdateTheirCard()
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        var listing = ClosedAuction();
        RegisterServices(listing, role: "Buyer", card: new SavedCardDto { IsValid = true },
            myResult: new MyAuctionResultDto { Outcome = AuctionOutcomes.PaymentFailed, ChargeDueBy = DateTime.UtcNow.AddHours(2) });

        var cut = RenderComponent<ListingDetail>(p => p.Add(c => c.Id, listing.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("payment failed"));
        cut.Find("a[href='/account/card']").TextContent.Should().Contain("Update your card");
    }

    [Fact]
    public void TheWinner_IsPointedToTheirSaleRecord()
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        var listing = ClosedAuction("Sold");
        RegisterServices(listing, role: "Buyer", card: new SavedCardDto { IsValid = true },
            myResult: new MyAuctionResultDto { Outcome = AuctionOutcomes.Won });

        var cut = RenderComponent<ListingDetail>(p => p.Add(c => c.Id, listing.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("You won this auction"));
        cut.Find("a[href='/my-purchases']").Should().NotBeNull();
    }
```

`MyPurchasesTests` — add (reuse the file's registration pattern with `PurchaseApiService`):

```csharp
    [Fact]
    public void AFailedPayment_ShowsTheDeadlineAndTheCardLink_AndAVoidedSaleShowsNoSale()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("buyer@test.com");
        var mock = new Mock<PurchaseApiService>(MockBehavior.Loose, new HttpClient { BaseAddress = new Uri("https://localhost/") });
        mock.Setup(s => s.GetMyPurchasesAsync()).ReturnsAsync(new List<PurchaseDto>
        {
            new() { StallionName = "Snitzel", Status = "Pending", ChargeDueBy = DateTime.UtcNow.AddHours(2) },
            new() { StallionName = "Zoustar", Status = "Voided", ChargeDueBy = DateTime.UtcNow.AddHours(-1) }
        });
        Services.AddSingleton(mock.Object);

        var cut = RenderComponent<MyPurchases>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("My sale records"));
        cut.Markup.Should().Contain("Payment failed — update your card by").And.Contain("No sale — payment not completed");
        cut.Find("a[href='/account/card']").Should().NotBeNull();
    }
```

`AccountCardTests` — in `Render`, register a purchases mock before rendering (add a
`List<PurchaseDto>? purchases = null` parameter):

```csharp
        var purchaseApi = new Mock<PurchaseApiService>(MockBehavior.Loose, Http());
        purchaseApi.Setup(s => s.GetMyPurchasesAsync()).ReturnsAsync(purchases ?? new List<PurchaseDto>());
        Services.AddSingleton(purchaseApi.Object);
```

(with `using Stallions.Shared.DTOs.Checkout;` — already present) and add:

```csharp
    [Fact]
    public void AfterAFailedCharge_SavingACardIsSaidToRetryThePayment()
    {
        _payments.Setup(s => s.GetMyCardAsync()).ReturnsAsync(new SavedCardDto
            { Brand = "visa", Last4 = "0002", ExpMonth = 8, ExpYear = 2028, IsValid = true });

        var cut = Render(purchases: new List<PurchaseDto>
        {
            new() { StallionName = "Snitzel", Status = "Pending", ChargeDueBy = DateTime.UtcNow.AddHours(1) }
        });

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Saving a new card will retry the payment for Snitzel"));
    }
```

- [ ] **Step 3: Run red** — `dotnet test tests/Client.Tests` → compile errors / failures.

- [ ] **Step 4: Status helper**

```csharp
// src/Client/Components/ListingStatusText.cs
namespace Stallions.Client.Components;

/// <summary>Display text and badge class for listing statuses (stud and Staff pages).</summary>
public static class ListingStatusText
{
    public static string Label(string status, string? closeReason) => status switch
    {
        "AwaitingPayment" => "Awaiting payment",
        "Unsold" => closeReason switch
        {
            "NoBids" => "Unsold — no bids",
            "ReserveNotMet" => "Unsold — reserve not met",
            "ChargeFailed" => "Unsold — payment failed",
            _ => "Unsold"
        },
        _ => status
    };

    public static string BadgeClass(string status) => status switch
    {
        "Active" => "badge-active",
        "Draft" => "badge-draft",
        "Cancelled" => "badge-cancelled",
        "Expired" => "badge-expired",
        "Sold" => "badge-sold",
        "AwaitingPayment" => "badge-draft",
        "Unsold" => "badge-expired",
        _ => ""
    };

    /// <summary>Closed, sold, or being charged: no more stud edits or Close action.</summary>
    public static bool IsFinished(string status) =>
        status is "Cancelled" or "Sold" or "AwaitingPayment" or "Unsold";
}
```

- [ ] **Step 5: Stud and Staff listing pages**

- `AdminListings.razor`: the badge becomes
  `<span class="badge @ListingStatusText.BadgeClass(listing.Status)">@ListingStatusText.Label(listing.Status, listing.CloseReason)</span>`;
  delete the page's private `BadgeClass` method.
- `AdminListingDetail.razor`: same for its status badge; delete its private `BadgeClass`; every
  `_listing.Status != "Cancelled" && _listing.Status != "Sold"` condition (description section, Close
  action) becomes `!ListingStatusText.IsFinished(_listing.Status)`.
- `StaffListings.razor`: the status cell shows `ListingStatusText.Label(l.Status, l.CloseReason)` with
  `ListingStatusText.BadgeClass(l.Status)`. The override dropdown stays as it is (the server refuses
  AwaitingPayment and Unsold).
- Add `@using Stallions.Client.Components` to `src/Client/_Imports.razor` if it isn't there.

- [ ] **Step 6: Listing page** (`ListingDetail.razor`)

Add `@inject PurchaseApiService PurchaseApi` and `@using Stallions.Shared`. Wrap the existing
`<AuthorizeView>…</AuthorizeView>` bid block (the one inside `listing-auction-block`) so it only shows
while the auction is open, and show the closed notice otherwise:

```razor
                    @if (IsOpen(auction))
                    {
                        @* existing <AuthorizeView> bid block, unchanged *@
                    }
                    else
                    {
                        <div class="auction-closed" role="status">
                            <p class="auction-closed-title">Auction closed</p>
                            @switch (_myResult?.Outcome)
                            {
                                case AuctionOutcomes.Won:
                                    <p>You won this auction. <a href="/my-purchases">View your sale record</a></p>
                                    break;
                                case AuctionOutcomes.PaymentPending:
                                    <p>You won this auction. We're charging the buyer fee to your saved card.</p>
                                    break;
                                case AuctionOutcomes.PaymentFailed:
                                    <p class="text-danger">You won this auction, but the buyer fee payment failed. Update your card by @FormatLocal(_myResult.ChargeDueBy) to keep the nomination.</p>
                                    <a href="/account/card" class="btn btn-gold btn-full">Update your card</a>
                                    break;
                                case AuctionOutcomes.NoSale:
                                    <p>No sale — the buyer fee payment wasn't completed in time.</p>
                                    break;
                            }
                        </div>
                    }
```

In `@code`:

```csharp
    private MyAuctionResultDto? _myResult;

    // Closed once the server closes it, or as soon as the end time passes (the closer runs every minute).
    private static bool IsOpen(AuctionListingDto auction) =>
        auction.Status == "Active" && AsUtc(auction.EndDateTime) > DateTime.UtcNow;

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value
    };

    private static string FormatLocal(DateTime? utc) =>
        utc is { } v ? AsUtc(v).ToLocalTime().ToString("d MMM, h:mm tt") : "the deadline";

    // A signed-in buyer's own result on a closed auction. Secondary content: failures are ignored.
    private async Task LoadMyResultAsync()
    {
        _myResult = null;
        if (!UserState.IsBuyer) return;
        try { _myResult = await PurchaseApi.GetMyResultAsync(Id); }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or AccessTokenNotAvailableException) { }
    }
```

In `Load()`, after `await LoadCardAsync();`:

```csharp
                if (_listing is AuctionListingDto a && !IsOpen(a)) await LoadMyResultAsync();
```

`ListingDetail.razor.css` — add:

```css
.auction-closed {
    border: 1px solid var(--border, #e5e2da);
    border-radius: 8px;
    padding: var(--space-4);
    margin-block: var(--space-4);
}

.auction-closed-title {
    font-weight: 600;
    margin-bottom: var(--space-2);
}
```

- [ ] **Step 7: My Bids** (`MyBids.razor`) — replace the row and the two helper methods:

```razor
                <div class="bid-row">
                    <div class="bid-info">
                        <a href="/listings/@bid.AuctionListingId" class="bid-listing-link">
                            @(string.IsNullOrEmpty(bid.StallionName) ? "View listing" : bid.StallionName)
                        </a>
                        <div class="bid-date text-muted text-sm">@bid.PlacedAt.ToString("d MMM yyyy, h:mm tt")</div>
                    </div>
                    <div class="bid-amount">@bid.AmountIncGst.ToString("C0", AuCulture)</div>
                    <div class="bid-status">
                        <span class="badge @GetStatusBadge(bid.Status)">@GetStatusLabel(bid.Status)</span>
                        @if (bid.Status == "Outbid" && bid.AuctionOpen)
                        {
                            <a href="/listings/@bid.AuctionListingId" class="btn btn-sm btn-outline" style="margin-top: var(--space-2)">Bid again</a>
                        }
                        @if (bid.Status == "Won")
                        {
                            <a href="/my-purchases" class="btn btn-sm btn-outline" style="margin-top: var(--space-2)">Sale record</a>
                        }
                    </div>
                </div>
```

```csharp
    private static string GetStatusBadge(string status) => status switch
    {
        "Active" => "badge-leading",
        "Outbid" => "badge-outbid",
        "Won"    => "badge-won",
        "Lost"   => "badge-lost",
        _        => ""
    };

    // Only one bid per auction is Active: the current high bid.
    private static string GetStatusLabel(string status) => status switch
    {
        "Active" => "Leading",
        _        => status
    };
```

- [ ] **Step 8: My sale records** (`MyPurchases.razor`) — retitle to "My sale records" (PageTitle, h1,
  empty state "No sale records yet", error "Failed to load sale records. Please try again."), show the
  season and stud under the stallion, and replace the status cell and badge helper:

```razor
                    <div class="purchase-info">
                        <div class="purchase-stallion">@p.StallionName</div>
                        <div class="text-muted text-sm">@p.SeasonName · @p.StudFarmName</div>
                    </div>
```

```razor
                    <div class="purchase-status">
                        <span class="badge @GetStatusBadge(p)">@GetStatusLabel(p)</span>
                        @if (p.Status == "Pending" && p.ChargeDueBy is not null)
                        {
                            <a href="/account/card" class="btn btn-sm btn-outline" style="margin-top: var(--space-2)">Update your card</a>
                        }
                        <div class="purchase-date text-muted text-xs">@p.CreatedAt.ToString("d MMM yyyy")</div>
                    </div>
```

```csharp
    private static string GetStatusLabel(PurchaseDto p) => p.Status switch
    {
        "Completed" => "Paid",
        "Pending" when p.ChargeDueBy is { } due => $"Payment failed — update your card by {Local(due)}",
        "Pending" => "Charging buyer fee",
        "Voided" => "No sale — payment not completed",
        _ => p.Status
    };

    private static string GetStatusBadge(PurchaseDto p) => p.Status switch
    {
        "Completed" => "badge-won",
        "Pending" when p.ChargeDueBy is not null => "badge-ending",
        "Pending" => "badge-auction",
        _ => "badge-lost"
    };

    private static string Local(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("d MMM, h:mm tt");
```

(Add `@using Stallions.Shared.DTOs.Checkout` at the top and change the `_purchases` field type to `List<PurchaseDto>?`.)
If `NavBar.razor` links to `/my-purchases` with the text "My Purchases", change the text to "My sale records".

- [ ] **Step 9: Card page** (`AccountCard.razor`) — add `@inject PurchaseApiService PurchaseApi`, a field
  `private string? _retryFor;`, and in `OnInitializedAsync`, after `_card = await LoadCardAsync();` and the
  `if (token.IsCancellationRequested) return;` that follows it:

```csharp
        // A winner whose buyer-fee charge failed: saving a new card retries it.
        try
        {
            var purchases = await PurchaseApi.GetMyPurchasesAsync() ?? new List<PurchaseDto>();
            _retryFor = purchases.FirstOrDefault(p => p.Status == "Pending" && p.ChargeDueBy != null)?.StallionName;
        }
        catch (ApiException) { _retryFor = null; }
```

and in the markup, directly under the disclosure paragraph:

```razor
    @if (_retryFor is not null)
    {
        <p class="account-card-retry" role="status">Saving a new card will retry the payment for @_retryFor.</p>
    }
```

(`using Stallions.Shared.DTOs.Checkout` via `@using` if needed.)

- [ ] **Step 10: Run green** — `dotnet test tests/Client.Tests` then the full build (0 warnings) and full test run → green.

- [ ] **Step 11: Commit**

```bash
git add src/Shared/DTOs/Listings/ListingDto.cs src/Shared/DTOs/Admin/ListingStaffSummaryDto.cs src/Server/Services/ListingService.cs src/Server/Services/AdminService.cs src/Client tests/Client.Tests
git status --short   # only intended files
git commit -m "feat: client — closed auctions, auction results, My Bids, My sale records, close-reason badges"
```

---

### Task 13: Infra, configuration and docs

**Files:**
- Create: `infra/modules/communication.bicep`
- Modify: `infra/main.bicep`, `infra/modules/appservice.bicep`, `infra/modules/keyvault-rbac.bicep`, `CLAUDE.md`

- [ ] **Step 1: Communication Services**

```bicep
// infra/modules/communication.bicep
param environmentName string
param tags object

// Email Communication Service with an Azure-managed sender domain (DoNotReply@<id>.azurecomm.net),
// free and usable straight away. Replace with a verified custom domain when DNS is ready.
resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: 'ecs-stallions-noms-${environmentName}'
  location: 'global'
  tags: tags
  properties: {
    dataLocation: 'Australia'
  }
}

resource managedDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  tags: tags
  properties: {
    domainManagement: 'AzureManaged'
    userEngagementTracking: 'Disabled'
  }
}

resource communication 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: 'acs-stallions-noms-${environmentName}'
  location: 'global'
  tags: tags
  properties: {
    dataLocation: 'Australia'
    linkedDomains: [
      managedDomain.id
    ]
  }
}

output communicationServiceName string = communication.name
output endpoint string = 'https://${communication.properties.hostName}'
output senderAddress string = 'DoNotReply@${managedDomain.properties.fromSenderDomain}'
```

- [ ] **Step 2: App Service** (`appservice.bicep`)

Add params `param emailEndpoint string` and `param emailSenderAddress string`. Change `alwaysOn: isProduction`
to `alwaysOn: true` with the comment `// The auction close and email jobs run inside the app, so it must never idle.`
Append to `appSettings`:

```bicep
        {
          name: 'Email__Provider'
          value: 'Acs'
        }
        {
          name: 'Email__AcsEndpoint'
          value: emailEndpoint
        }
        {
          name: 'Email__SenderAddress'
          value: emailSenderAddress
        }
        {
          // Links in emails. Change when a custom domain is added.
          name: 'Email__PublicBaseUrl'
          value: 'https://app-stallions-noms-${environmentName}.azurewebsites.net'
        }
```

- [ ] **Step 3: Role assignment** (`keyvault-rbac.bicep`)

First confirm the built-in role's id (read-only):
`az role definition list --name "Communication and Email Service Owner" --query "[0].name" -o tsv`
→ expected `09976791-48a7-449e-bb21-39d1a415f350`. If it prints a different GUID, use that one.

Add `param communicationServiceName string` and:

```bicep
// Built-in "Communication and Email Service Owner" — lets the App Service identity send email.
var communicationEmailOwnerRoleId = '09976791-48a7-449e-bb21-39d1a415f350'

resource communicationService 'Microsoft.Communication/communicationServices@2023-04-01' existing = {
  name: communicationServiceName
}

// App Service — send email through Communication Services (scoped to that resource only)
resource appServiceEmailRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(communicationService.id, appServicePrincipalId, communicationEmailOwnerRoleId)
  scope: communicationService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', communicationEmailOwnerRoleId)
    principalId: appServicePrincipalId
    principalType: 'ServicePrincipal'
  }
}
```

- [ ] **Step 4: Wire `main.bicep`**

Add before `module appservice`:

```bicep
module communication './modules/communication.bicep' = {
  name: 'communication'
  scope: rg
  params: {
    environmentName: environmentName
    tags: tags
  }
}
```

Pass `emailEndpoint: communication.outputs.endpoint` and `emailSenderAddress: communication.outputs.senderAddress`
to `appservice`, `communicationServiceName: communication.outputs.communicationServiceName` to `rbac`, and add
`output AZURE_COMMUNICATION_SERVICE_NAME string = communication.outputs.communicationServiceName`.

- [ ] **Step 5: Validate** — `az bicep build --file infra/main.bicep --stdout > /dev/null` → no errors
  (warnings that already existed are fine). Do **not** provision here.

- [ ] **Step 6: CLAUDE.md** — in **Tech Stack** change the Serverless line to
  `- **Serverless:** Azure Functions — provisioned but not used yet; background jobs run inside the API (see Architecture Notes)`
  and add `- **Email:** Azure Communication Services (managed identity) via an outbox table`. In **Architecture Notes**
  replace the Azure Functions bullet with:
  `- Background jobs run as hosted services inside the API: AuctionCloseService (closes auctions, charges the buyer fee, grace period) and EmailDispatchService (sends the OutboundEmails outbox). Both are safe on several instances (concurrency stamps).`

- [ ] **Step 7: Full build (0 warnings) and full test run → green.**

- [ ] **Step 8: Commit**

```bash
git add infra/modules/communication.bicep infra/main.bicep infra/modules/appservice.bicep infra/modules/keyvault-rbac.bicep CLAUDE.md
git commit -m "feat(infra): Communication Services email, App Service email settings and Always On"
```

---

### Task 14: Final check, push, and the dev rollout (with David's go-ahead)

- [ ] **Step 1: Whole-branch check** — `git status` clean; `dotnet build stallions-nominations-marketplace.slnx` (0 warnings);
  `dotnet test stallions-nominations-marketplace.slnx` (all green, run twice);
  `dotnet ef migrations has-pending-model-changes --project src/Server --startup-project src/Server` → no changes.
  `grep -rn "WebhookSecret\|CheckoutService\|/checkout" src --include=*.cs --include=*.razor` → nothing.
- [ ] **Step 2: Spec status** — in the spec, set **Status** to "Built on `feature/v2-phase3-auction-close` (2026-10-10); not merged", commit `docs: phase 3 spec status`.
- [ ] **Step 3: Push** — `git push -u origin feature/v2-phase3-auction-close`. Do **not** merge or open a PR unless David asks.
- [ ] **Step 4: Dev rollout — ask David first.** It needs `azd provision --environment dev` (new
  Communication Services resources, role assignment, App Service settings, Always On) then
  `azd deploy api --environment dev` (the migration runs on startup). After deploying, check from outside:
  the site and `api/listings` return 200; `api/listings/{id}/my-result` returns 401 signed out;
  `POST api/purchases/{id}/complete` and `POST api/listings/{id}/checkout` now return 404/405.
- [ ] **Step 5: Dev click-through (David, fake provider)** — use a short auction (end time a few minutes ahead):
  1. Buyer A (card 4242) bids, buyer B outbids → A gets the outbid email.
  2. After the end, within ~1–2 minutes: listing Sold, B charged ($150), B gets "won and charged", A gets "auction closed", the stud gets the sale confirmation; My sale records shows Paid with price, fee and balance.
  3. Repeat with B saving a card via **Approve with a declining card** → payment-failed email, listing "Awaiting payment", My sale records shows the deadline; B saves a good card → paid on the next run.
  4. Repeat and leave the declining card (set the grace period to 1 hour — the minimum — in Staff Settings first) → after the hour, the final attempt fails: no sale, listing "Unsold — payment failed", both emails.
  5. An auction with a reserve above the highest bid → "Unsold — reserve not met", bidders and stud told.
