# v2 Phase 4 — Make an Offer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A stud converts an Unsold auction lot into a Make an Offer listing; verified buyers with a saved card send private offers (at least the buyer fee); the stud accepts (buyer fee charged at once, sale record created) or declines; offers and listings expire; emails at every step.

**Architecture:** A new TPT listing type `MakeAnOfferListing` (linked to its source auction) and an `Offer` entity. The Phase 3 charge code moves out of `AuctionCloser` into a shared `BuyerFeeCharger` used by the closer (auction wins) and by `OfferService.AcceptAsync` (accepted offers); only the listing-side effect differs by source. An `OfferExpirer` step joins the existing background run. Buyer eligibility (verified, current T&C, valid card) is shared between bids and offers.

**Tech Stack:** .NET 9, ASP.NET Core, EF Core (SQL Server), Blazor WASM, xUnit + Moq + FluentAssertions, bUnit.

**Spec:** `docs/superpowers/specs/2026-10-11-v2-phase4-make-an-offer-design.md`. Read it and CLAUDE.md first.
**Branch:** `feature/v2-phase4-make-an-offer` (already created; the spec is committed).

## Global Constraints

- All amounts inc. GST; fees stored as three values via `GstBreakdown.FromIncGst`; fee amounts come from the listing snapshot / Platform Settings, never the client.
- Never display an asking price, other buyers' offers, offer counts or the reserve.
- A buyer must be verified (`UserStatus.Active`), have accepted the current T&C, and have a valid saved card to offer — same rules and the same enforcement as bidding.
- Offer expiry = `PlatformSettings.OfferExpiryDays` (read when the offer is made); listing expiry = end of `Season.EndDate`, Sydney time.
- Buyer disclosure wording comes from `DisclosureOptions` (configured) — never hardcoded policy text.
- Every endpoint authenticated and role-checked (`BuyerOnly`, `StudFarmAdminOnly`, `StudFarmOrStaff`); ownership checked in services; all state changes audited.
- Phase 3 auction behaviour and its existing tests stay unchanged (test fixtures may change; test files asserting behaviour may not).

> **Plan decisions that refine the spec (2026-10-11):**
> - `Offer` has **no `PurchaseId` column**: the sale record for an accepted offer is found via
>   `Purchase.OfferId` (unique). This avoids a two-way foreign key cycle between Offers and Purchases.
> - The shared buyer check is a small class, `BuyerEligibility`, constructed inside `BidService` and
>   `OfferService` from their existing `ITermsRepository`/`ICardService`, so `BidService`'s
>   constructor (and its tests) don't change.
> - Charge outcomes are reported as `ChargeOutcome { Skipped, Paid, PaymentFailed, NoSale }`.
> - Pending offers whose listing is no longer Active are also expired by the background step (a
>   safety net if expiring them right after a stud close fails).
>
> **Changes made during the build (after task reviews, 2026-10-11):**
> - The client read path for `MakeAnOfferListingDto` is tested in Client.Tests (Server.Tests pulls
>   System.Text.Json 10, which rejects the `listingType` property/discriminator overlap that the
>   client's STJ 9 accepts).
> - `ExpirePendingOffersAsync` only expires offers that are past expiry or on a listing that is no
>   longer open; its test seeds a Cancelled listing.
> - An accepted offer keeps `Status = Accepted` if its sale is later voided; buyer views show
>   "Accepted — no sale".
> - `AdminListingDetail` reloads when its listing id changes (after Convert, or via the link to the
>   Make an Offer listing).
> - Final review: a duplicate pending offer is detected by the unique-index error only (other save
>   failures are logged and rethrown); Decline and Withdraw run in a transaction; an offer never
>   expires after its listing; a stud close/cancel that races the expirer returns 409; the offer
>   confirmation shows cents; offer and bid confirmations stay disabled until the disclosure has
>   loaded; network and sign-in errors on offer actions show a message (the stud panel reloads).
> - The first line of each code block (`// src/...`, `@* src/... *@`) only names the file — don't copy it.

## Review Focus

- **Accept double-click / two tabs:** two accepts of one offer must create one sale record and one charge → pinned in Task 5 (`Accept_TwiceConcurrently_CreatesOneSaleRecord`).
- **Offer racing listing expiry or stud close:** an offer made as the listing closes must be rejected or expired, never left Pending on a closed listing → Task 5 (in-transaction re-check) and Task 6 (`PendingOfferOnAClosedListing_IsExpired`).
- **Accepted offer whose charge later fails for good:** the listing must stay open and other offers untouched → Task 2 (`OfferSale_FinalFailure_LeavesTheListingOpen`).
- **Amount edge cases:** exactly the buyer fee (allowed), a cent below (rejected), more than two decimals or above 10,000,000 (rejected) → Task 5 (`Make_AmountRules`).
- **Converting twice / after the season:** a second conversion while one is live, or after `Season.EndDate`, must be refused → Task 4 (`Convert_Twice_IsRejected`, `Convert_AfterTheSeasonEnded_IsRejected`).

---

## Ground rules

- **Test first**: write the failing test, run it red, implement, run it green.
- After each task: `dotnet build stallions-nominations-marketplace.slnx` (0 warnings) and
  `dotnet test stallions-nominations-marketplace.slnx` green. `tests/Server.Tests` builds the client
  too, so keep the client compiling at every task.
- **One commit per task**, conventional message, ending with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Line endings:** repo LF, working copy CRLF. Stage only the files you changed.
- **Database:** migrations go to the local dev DB (`(localdb)\mssqllocaldb`, `StallionsNomsDev`);
  Azure dev migrates itself on deploy. `azd deploy` only by David.
- In test code `Options.Create` clashes with the `Stallions.Server.Options` namespace — use
  `Microsoft.Extensions.Options.Options.Create`.
- Existing patterns: `ServiceResult`/`ServiceResult<T>` (`Ok`, `Created`, `BadRequest`, `Forbidden`,
  `NotFound`, `Conflict`); repositories save on each call; `ITransactionRunner.RunAsync` clears the
  change tracker per attempt; `IAuditLogRepository.LogAsync(type, id, action, userId, details)`;
  `TimeProvider` for new code (tests: `TestClock`); `AuctionTestData` seeding helper; in-memory DB via
  `DbContextFactory.Create(name)`.

## File map

| File | Responsibility |
|---|---|
| `src/Shared/Enums/ListingType.cs`, `OfferStatus.cs` | `MakeAnOffer` type; offer statuses |
| `src/Server/Data/Entities/MakeAnOfferListing.cs`, `Offer.cs` | New entities |
| `src/Server/Sales/BuyerFeeCharger.cs` (+ `IBuyerFeeCharger`, `ChargeOutcome`) | The shared charge pipeline (moved from `AuctionCloser`) |
| `src/Server/Services/BuyerEligibility.cs` | Verified + current T&C + valid card, for bids and offers |
| `src/Server/Infrastructure/SydneyTime.cs` | Sydney "today" and end-of-day in UTC |
| `src/Server/Services/OfferService.cs` (+ `IOfferService`), `Controllers/OffersController.cs` | Make / withdraw / view / accept / decline |
| `src/Server/Data/Repositories/OfferRepository.cs` (+ interface) | Offer persistence and queries |
| `src/Server/Sales/OfferExpirer.cs` (+ `IOfferExpirer`) | Expire offers and Make an Offer listings |
| `src/Shared/DTOs/Offers/*` | `MakeOfferRequest`, `OfferDto`, `StudOfferDto`, `AcceptOfferResultDto` |
| `src/Shared/DTOs/Listings/MakeAnOfferListingDto.cs` | Listing DTO for the new type |
| `src/Client/Services/OfferApiService.cs` | Client for offers |
| `src/Client/Components/Offers/*`, `Pages/MyOffers.razor` | Offer UI |

---

### Task 1: Domain — Make an Offer listing, offers, sale-record link, migration

**Files:**
- Modify: `src/Shared/Enums/ListingType.cs`; Create: `src/Shared/Enums/OfferStatus.cs`
- Create: `src/Server/Data/Entities/MakeAnOfferListing.cs`, `src/Server/Data/Entities/Offer.cs`
- Modify: `src/Server/Data/Entities/Purchase.cs`, `src/Server/Data/AppDbContext.cs`
- Create: migration `V2Phase4MakeAnOffer` (generated)
- Test: `tests/Server.Tests/Data/MakeAnOfferModelTests.cs`

**Interfaces:**
- Produces: `ListingType.MakeAnOffer`; `OfferStatus { Pending, Accepted, Declined, Withdrawn, Expired }`;
  `MakeAnOfferListing : Listing { Guid SourceAuctionListingId; DateTime ExpiresAt; AuctionListing SourceAuctionListing; ICollection<Offer> Offers }`;
  `Offer : IHasConcurrencyStamp { Guid Id; Guid ListingId; Guid BuyerUserId; decimal AmountIncGst; OfferStatus Status; DateTime CreatedAt; DateTime ExpiresAt; DateTime? RespondedAt; Guid ConcurrencyStamp; MakeAnOfferListing Listing; User Buyer }`;
  `Purchase.OfferId (Guid?)`, `Purchase.Offer`; `AppDbContext.MakeAnOfferListings`, `AppDbContext.Offers`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Server.Tests/Data/MakeAnOfferModelTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data;

public class MakeAnOfferModelTests
{
    private readonly string _name = Guid.NewGuid().ToString();
    private AppDbContext Db() => DbContextFactory.Create(_name);

    [Fact]
    public async Task AMakeAnOfferListing_RoundTripsAsItsOwnType_WithItsSourceAuction()
    {
        Guid id;
        using (var db = Db())
        {
            var auction = AuctionTestData.Auction(db, DateTime.UtcNow.AddDays(-1), status: ListingStatus.Unsold);
            var mao = new MakeAnOfferListing
            {
                StallionId = auction.StallionId, SeasonId = auction.SeasonId, StudFarmId = auction.StudFarmId,
                ListingType = ListingType.MakeAnOffer, Status = ListingStatus.Active,
                SourceAuctionListingId = auction.Id, ExpiresAt = DateTime.UtcNow.AddDays(30), BuyerFeeIncGst = 150m
            };
            db.MakeAnOfferListings.Add(mao);
            await db.SaveChangesAsync();
            id = mao.Id;
        }

        using var read = Db();
        var loaded = await read.Listings.SingleAsync(l => l.Id == id);
        loaded.Should().BeOfType<MakeAnOfferListing>()
            .Which.SourceAuctionListingId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task TwoContextsUpdatingTheSameOffer_SecondOneConflicts()
    {
        using (var db = Db())
        {
            db.Offers.Add(new Offer { ListingId = Guid.NewGuid(), BuyerUserId = Guid.NewGuid(), AmountIncGst = 5000m,
                CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(7) });
            await db.SaveChangesAsync();
        }
        using var first = Db();
        using var second = Db();
        var a = await first.Offers.SingleAsync();
        var b = await second.Offers.SingleAsync();

        a.Status = OfferStatus.Accepted;
        await first.SaveChangesAsync();
        b.Status = OfferStatus.Declined;

        await second.Invoking(db => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public void TheModel_AllowsOnePendingOfferPerBuyerPerListing_AndOneLiveSaleRecordPerOffer()
    {
        using var db = Db();
        var offer = db.Model.FindEntityType(typeof(Offer))!;
        offer.GetIndexes().Should().Contain(i => i.IsUnique
            && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "ListingId", "BuyerUserId" })
            && i.GetFilter() == "[Status] = 'Pending'");

        var purchase = db.Model.FindEntityType(typeof(Purchase))!;
        purchase.GetIndexes().Should().Contain(i => i.IsUnique
            && i.Properties.Single().Name == "OfferId"
            && i.GetFilter() == "[OfferId] IS NOT NULL AND [Status] <> 'Voided'");
    }
}
```

- [ ] **Step 2: Run red** — `dotnet test tests/Server.Tests --filter FullyQualifiedName~MakeAnOfferModelTests` → compile errors.

- [ ] **Step 3: Enums and entities**

`src/Shared/Enums/ListingType.cs` → `public enum ListingType { Auction, MakeAnOffer }`.

```csharp
// src/Shared/Enums/OfferStatus.cs
namespace Stallions.Shared.Enums;

/// <summary>Where a buyer's offer stands. Whether an Accepted offer completed is on its sale record.</summary>
public enum OfferStatus
{
    Pending,
    Accepted,
    Declined,
    Withdrawn,
    Expired
}
```

```csharp
// src/Server/Data/Entities/MakeAnOfferListing.cs
namespace Stallions.Server.Data.Entities;

/// <summary>
/// A nomination open to private offers, created by the stud from an Unsold auction lot. Stays open
/// after an accepted offer (supply is unlimited) until the stud closes it or the season ends.
/// </summary>
public class MakeAnOfferListing : Listing
{
    public Guid SourceAuctionListingId { get; set; }
    /// <summary>End of the season's last day (Sydney time), in UTC.</summary>
    public DateTime ExpiresAt { get; set; }

    public AuctionListing SourceAuctionListing { get; set; } = null!;
    public ICollection<Offer> Offers { get; set; } = new List<Offer>();
}
```

```csharp
// src/Server/Data/Entities/Offer.cs
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Entities;

/// <summary>A buyer's private offer on a Make an Offer listing. Only the stud (and Staff) see it.</summary>
public class Offer : IHasConcurrencyStamp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public Guid BuyerUserId { get; set; }
    public decimal AmountIncGst { get; set; }
    public OfferStatus Status { get; set; } = OfferStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public MakeAnOfferListing Listing { get; set; } = null!;
    public User Buyer { get; set; } = null!;
}
```

In `Purchase.cs` add after `BidId`:

```csharp
    /// <summary>Set when the sale came from an accepted offer (exactly one of BidId / OfferId is set).</summary>
    public Guid? OfferId { get; set; }
```

and with the navigations: `public Offer? Offer { get; set; }`.

- [ ] **Step 4: `AppDbContext`**

DbSets (after `AuctionListings`): `public DbSet<MakeAnOfferListing> MakeAnOfferListings => Set<MakeAnOfferListing>();`
and (after `OutboundEmails`): `public DbSet<Offer> Offers => Set<Offer>();`

After the `AuctionListing` configuration block:

```csharp
        // ── Make an Offer listings (TPT) ─────────────────────────────────────
        modelBuilder.Entity<MakeAnOfferListing>(e =>
        {
            e.ToTable("MakeAnOfferListings");
            e.HasOne(m => m.SourceAuctionListing)
                .WithMany()
                .HasForeignKey(m => m.SourceAuctionListingId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(m => m.SourceAuctionListingId);
        });

        // ── Offers ───────────────────────────────────────────────────────────
        modelBuilder.Entity<Offer>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.AmountIncGst).HasPrecision(12, 2);
            e.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(o => o.ConcurrencyStamp).IsConcurrencyToken();
            e.HasOne(o => o.Listing).WithMany(l => l.Offers).HasForeignKey(o => o.ListingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.Buyer).WithMany().HasForeignKey(o => o.BuyerUserId).OnDelete(DeleteBehavior.Restrict);
            // One live offer per buyer per listing.
            e.HasIndex(o => new { o.ListingId, o.BuyerUserId }).IsUnique().HasFilter("[Status] = 'Pending'");
            e.HasIndex(o => new { o.Status, o.ExpiresAt });
        });
```

In the `Purchase` block, after the `Bid` relationship:

```csharp
            e.HasOne(p => p.Offer)
                .WithMany()
                .HasForeignKey(p => p.OfferId)
                .OnDelete(DeleteBehavior.NoAction);
            // One accepted offer can never have two live sale records.
            e.HasIndex(p => p.OfferId).IsUnique().HasFilter("[OfferId] IS NOT NULL AND [Status] <> 'Voided'");
```

- [ ] **Step 5: Run green** — the three tests pass.

- [ ] **Step 6: Migration** — `dotnet ef migrations add V2Phase4MakeAnOffer --project src/Server --startup-project src/Server --output-dir Data/Migrations`;
  check `dotnet ef migrations has-pending-model-changes --project src/Server --startup-project src/Server` → no changes;
  apply locally: `dotnet ef database update --project src/Server --startup-project src/Server --connection "Server=(localdb)\mssqllocaldb;Database=StallionsNomsDev;Trusted_Connection=True;MultipleActiveResultSets=true"`.
  Read the generated migration: it must only create `MakeAnOfferListings`, `Offers`, `Purchases.OfferId` and their indexes/FKs.

- [ ] **Step 7: Full build (0 warnings) and full test run → green.** (No `ListingService` code handles the new type yet; no Make an Offer listing can exist until Task 4.)

- [ ] **Step 8: Commit**

```bash
git add src/Shared/Enums/ListingType.cs src/Shared/Enums/OfferStatus.cs src/Server/Data/Entities/MakeAnOfferListing.cs src/Server/Data/Entities/Offer.cs src/Server/Data/Entities/Purchase.cs src/Server/Data/AppDbContext.cs src/Server/Data/Migrations tests/Server.Tests/Data/MakeAnOfferModelTests.cs
git commit -m "feat: Make an Offer domain — listing type, offers, sale-record link"
```

---

### Task 2: Shared charge pipeline — `BuyerFeeCharger`, listing-neutral emails

**Files:**
- Create: `src/Server/Sales/BuyerFeeCharger.cs` (with `IBuyerFeeCharger`, `ChargeOutcome`)
- Modify: `src/Server/Auctions/AuctionCloser.cs`, `src/Server/Auctions/AuctionServiceCollectionExtensions.cs`,
  `src/Server/Email/IAuctionEmails.cs`, `src/Server/Email/AuctionEmails.cs`
- Modify (fixture only): `tests/Server.Tests/Auctions/AuctionCloserFixture.cs`
- Test: `tests/Server.Tests/Sales/BuyerFeeChargerOfferTests.cs`, `tests/Server.Tests/Email/AuctionEmailsTests.cs` (add tests only)

**Interfaces:**
- Consumes: Task 1 `Purchase.OfferId`, `MakeAnOfferListing`.
- Produces: `enum ChargeOutcome { Skipped, Paid, PaymentFailed, NoSale }`;
  `interface IBuyerFeeCharger { Task<ChargeOutcome> ChargeAsync(Guid purchaseId); }` (throws when the provider is unreachable — the attempt stays interrupted);
  `IAuctionEmails` methods `WonAndChargedAsync`, `PaymentFailedAsync`, `PaymentAbandonedAsync`, `StudSaleConfirmationAsync` now take `Listing listing` (not `AuctionListing`); `StudNoSaleAsync(Listing listing, ListingCloseReason reason)`.
  `AuctionCloser` constructor: `(IListingRepository, IBidRepository, IPurchaseRepository, IAuditLogRepository, IAuctionEmails, IBuyerFeeCharger, ITransactionRunner, TimeProvider, IOptions<AuctionCloseOptions>, ILogger<AuctionCloser>)`.

This task is a **refactor plus one new branch**: every existing `AuctionCloserCloseTests`/`AuctionCloserChargeTests`/`AuctionEmailsTests` test must pass **unchanged**. Only `AuctionCloserFixture` changes (it builds the charger).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Server.Tests/Sales/BuyerFeeChargerOfferTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Sales;
using Stallions.Server.Tests.Auctions;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Sales;

public class BuyerFeeChargerOfferTests
{
    private readonly AuctionCloserFixture _f = new();

    /// <summary>An Active Make an Offer listing with an accepted $9,000 offer and its Pending sale record.</summary>
    private (Guid ListingId, Guid PurchaseId, Guid OtherOfferId) AcceptedOffer(bool withCard = true) => _f.Seed(db =>
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
        if (withCard) AuctionTestData.Card(db, buyer);
        var other = AuctionTestData.Buyer(db, "Other Buyer");
        var offer = new Offer { ListingId = listing.Id, BuyerUserId = buyer.Id, AmountIncGst = 9000m,
            Status = OfferStatus.Accepted, CreatedAt = _f.Clock.UtcNow, ExpiresAt = _f.Clock.UtcNow.AddDays(7) };
        var pending = new Offer { ListingId = listing.Id, BuyerUserId = other.Id, AmountIncGst = 8000m,
            CreatedAt = _f.Clock.UtcNow, ExpiresAt = _f.Clock.UtcNow.AddDays(7) };
        db.Offers.AddRange(offer, pending);
        var purchase = new Purchase
        {
            ListingId = listing.Id, BuyerUserId = buyer.Id, OfferId = offer.Id, TotalPriceIncGst = 9000m,
            BuyerFeeIncGst = 150m, BuyerFeeExGst = 136.36m, BuyerFeeGst = 13.64m, BalancePayableToStudIncGst = 8850m,
            Status = PurchaseStatus.Pending, CreatedAt = _f.Clock.UtcNow
        };
        db.Purchases.Add(purchase);
        db.SaveChanges();
        return (listing.Id, purchase.Id, pending.Id);
    });

    private async Task<Purchase> Sale(Guid id) => await _f.Read().Purchases.SingleAsync(p => p.Id == id);
    private async Task<Listing> Listing(Guid id) => await _f.Read().Listings.SingleAsync(l => l.Id == id);

    [Fact]
    public async Task OfferSale_Paid_CompletesTheSaleRecord_AndLeavesTheListingOpen()
    {
        var (listingId, purchaseId, otherOfferId) = AcceptedOffer();

        var outcome = await _f.CreateCharger().ChargeAsync(purchaseId);

        outcome.Should().Be(ChargeOutcome.Paid);
        (await Sale(purchaseId)).Status.Should().Be(PurchaseStatus.Completed);
        (await Listing(listingId)).Status.Should().Be(ListingStatus.Active);
        (await _f.Read().Offers.SingleAsync(o => o.Id == otherOfferId)).Status.Should().Be(OfferStatus.Pending);
        _f.Emails.Verify(e => e.WonAndChargedAsync(It.Is<Listing>(l => l.Id == listingId), It.IsAny<Purchase>(), It.IsAny<User>()), Times.Once);
        _f.Emails.Verify(e => e.StudSaleConfirmationAsync(It.Is<Listing>(l => l.Id == listingId), It.IsAny<Purchase>(), It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task OfferSale_FirstFailure_StartsTheGracePeriod()
    {
        var (_, purchaseId, _) = AcceptedOffer();
        _f.ChargesDecline();

        var outcome = await _f.CreateCharger().ChargeAsync(purchaseId);

        outcome.Should().Be(ChargeOutcome.PaymentFailed);
        (await Sale(purchaseId)).ChargeDueBy.Should().Be(_f.Clock.UtcNow.AddHours(2));
    }

    [Fact]
    public async Task OfferSale_FinalFailure_LeavesTheListingOpen()
    {
        var (listingId, purchaseId, otherOfferId) = AcceptedOffer();
        _f.ChargesDecline();
        await _f.CreateCharger().ChargeAsync(purchaseId);
        _f.Clock.Advance(TimeSpan.FromHours(2));

        var outcome = await _f.CreateCharger().ChargeAsync(purchaseId);

        outcome.Should().Be(ChargeOutcome.NoSale);
        (await Sale(purchaseId)).Status.Should().Be(PurchaseStatus.Voided);
        var listing = await Listing(listingId);
        listing.Status.Should().Be(ListingStatus.Active);
        listing.CloseReason.Should().BeNull();
        (await _f.Read().Offers.SingleAsync(o => o.Id == otherOfferId)).Status.Should().Be(OfferStatus.Pending);
        _f.Emails.Verify(e => e.StudNoSaleAsync(It.Is<Listing>(l => l.Id == listingId), ListingCloseReason.ChargeFailed), Times.Once);
    }

    [Fact]
    public async Task NotDue_IsSkipped()
    {
        var (_, purchaseId, _) = AcceptedOffer();
        await _f.CreateCharger().ChargeAsync(purchaseId);

        (await _f.CreateCharger().ChargeAsync(purchaseId)).Should().Be(ChargeOutcome.Skipped);
    }
}
```

Add to `AuctionEmailsTests` (reuse its `CreateSut()`, `_queued`, `Listing()` helpers; add `using Stallions.Shared.Enums;` if missing):

```csharp
    private static MakeAnOfferListing OfferListing() => new()
    {
        Id = Guid.NewGuid(),
        Stallion = new Stallion { Name = "Snitzel" },
        Season = new Season { Name = "2026 Season" },
        StudFarm = new StudFarm { Name = "Arrowfield", ContactEmail = "sales@arrowfield.example" }
    };

    private static Purchase OfferSale() => new()
    {
        OfferId = Guid.NewGuid(), TotalPriceIncGst = 9000m, BuyerFeeIncGst = 150m, BuyerFeeExGst = 136.36m,
        BuyerFeeGst = 13.64m, BalancePayableToStudIncGst = 8850m
    };

    [Fact]
    public async Task AnAcceptedOfferCharged_UsesOfferWording_AndShowsTheSplit()
    {
        await CreateSut().WonAndChargedAsync(OfferListing(), OfferSale(), new User { Email = "buyer@example.com" });

        var email = _queued.Single();
        email.Template.Should().Be("OfferAcceptedAndCharged");
        email.Subject.Should().Contain("offer").And.Contain("accepted");
        email.TextBody.Should().Contain("Price (your accepted offer): $9,000.00").And.Contain("$8,850.00")
            .And.Contain("CONFIGURED fee wording.").And.NotContain("winning bid");
    }

    [Fact]
    public async Task StudNoSale_ForAnOfferListing_SaysTheListingStaysOpen()
    {
        await CreateSut().StudNoSaleAsync(OfferListing(), ListingCloseReason.ChargeFailed);

        _queued.Single().TextBody.Should().Contain("stays open").And.NotContain("auction");
    }

    [Fact]
    public async Task StudNoSale_ForAnAuction_LinksToMakeAnOffer()
    {
        await CreateSut().StudNoSaleAsync(Listing(), ListingCloseReason.ReserveNotMet);

        _queued.Single().TextBody.Should().Contain("Offer it as Make an Offer");
    }
```

- [ ] **Step 2: Run red** — compile errors (`BuyerFeeCharger`, `CreateCharger`, `MakeAnOfferListing` overloads).

- [ ] **Step 3: Move the charge code into `BuyerFeeCharger`**

Create `src/Server/Sales/BuyerFeeCharger.cs`. Move — **verbatim** — from `AuctionCloser.cs` the `ChargeAttempt` record, `ChargeAsync`, `StartAttemptAsync`, `IsDue`, `ChargeCardAsync` and `RecordResultAsync`, then apply only these changes:

```csharp
// src/Server/Sales/BuyerFeeCharger.cs  (top of file; method bodies moved from AuctionCloser)
using System.Text.Json;
using Stallions.Server.Auctions;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Payments;
using Stallions.Shared.Enums;

namespace Stallions.Server.Sales;

/// <summary>What one charge run did to a sale record.</summary>
public enum ChargeOutcome
{
    /// <summary>Not due, already handled, or flagged for Staff — nothing sent.</summary>
    Skipped,
    Paid,
    /// <summary>Declined; the sale record waits in its grace period.</summary>
    PaymentFailed,
    /// <summary>The final attempt failed: the sale record is Voided.</summary>
    NoSale
}

public interface IBuyerFeeCharger
{
    /// <summary>
    /// Makes one charge attempt for a Pending sale record (auction win or accepted offer) if it is due.
    /// Throws when the provider is unreachable; the attempt is then repeated later with the same key.
    /// </summary>
    Task<ChargeOutcome> ChargeAsync(Guid purchaseId);
}

/// <summary>
/// The buyer-fee charge pipeline (moved from AuctionCloser): snapshot the request, call the provider
/// between two transactions, record the result. Only the listing-side effect depends on whether the
/// sale came from an auction win (listing Sold / Unsold) or an accepted offer (listing unchanged).
/// </summary>
public class BuyerFeeCharger : IBuyerFeeCharger
{
    private readonly IPurchaseRepository _purchases;
    private readonly IListingRepository _listings;
    private readonly IBidRepository _bids;
    private readonly ISavedCardRepository _cards;
    private readonly IPlatformSettingsRepository _settings;
    private readonly IAuditLogRepository _audit;
    private readonly IAuctionEmails _emails;
    private readonly IPaymentProvider _provider;
    private readonly ITransactionRunner _transactions;
    private readonly TimeProvider _clock;
    private readonly ILogger<BuyerFeeCharger> _log;

    public BuyerFeeCharger(
        IPurchaseRepository purchases, IListingRepository listings, IBidRepository bids, ISavedCardRepository cards,
        IPlatformSettingsRepository settings, IAuditLogRepository audit, IAuctionEmails emails,
        IPaymentProvider provider, ITransactionRunner transactions, TimeProvider clock, ILogger<BuyerFeeCharger> log)
    {
        _purchases = purchases; _listings = listings; _bids = bids; _cards = cards; _settings = settings;
        _audit = audit; _emails = emails; _provider = provider; _transactions = transactions; _clock = clock; _log = log;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
```

Changes to the moved code:

1. `ChargeAsync` becomes public and returns the outcome:

```csharp
    public async Task<ChargeOutcome> ChargeAsync(Guid purchaseId)
    {
        var attempt = await _transactions.RunAsync(() => StartAttemptAsync(purchaseId));
        if (attempt == null) return ChargeOutcome.Skipped;

        // Outside any transaction. If this throws (provider unreachable) the attempt stays
        // "started" and is repeated with the same key and the same request.
        var result = await ChargeCardAsync(attempt);

        return await _transactions.RunAsync(() => RecordResultAsync(attempt, result));
    }
```

2. `RecordResultAsync` returns `Task<ChargeOutcome>`: the "state changed" branch returns `ChargeOutcome.Skipped`;
   success returns `ChargeOutcome.Paid`; first failure and in-grace failure return `ChargeOutcome.PaymentFailed`;
   the final failure returns `ChargeOutcome.NoSale`.

3. Replace `var listing = (AuctionListing)purchase.Listing;` with

```csharp
        // An auction win changes the listing (Sold / Unsold); an accepted offer leaves its listing open.
        var listing = purchase.Listing;
        var auction = purchase.BidId != null ? listing as AuctionListing : null;
```

   success branch: replace the two `listing.Status = …; listing.WinningBidId = …; await _listings.UpdateAsync(listing);` lines with

```csharp
            if (auction != null)
            {
                auction.Status = ListingStatus.Sold;
                auction.WinningBidId = purchase.BidId;
                await _listings.UpdateAsync(auction);
            }
```

   final-failure branch: wrap the listing and winning-bid changes:

```csharp
            if (auction != null)
            {
                auction.Status = ListingStatus.Unsold;
                auction.CloseReason = ListingCloseReason.ChargeFailed;
                await _listings.UpdateAsync(auction);
                var winningBid = purchase.BidId is { } bidId ? await _bids.GetByIdAsync(bidId) : null;
                if (winningBid != null)
                {
                    winningBid.Status = BidStatus.Lost; // no sale, so the bid no longer shows as Won
                    await _bids.UpdateAsync(winningBid);
                }
            }
```

   The email calls stay as they are (they pass `listing`, now typed `Listing`).

4. `AuctionCloseOptions.InterruptedAttemptAge` / `StuckAttemptAge` are still referenced from `Stallions.Server.Auctions` — leave them there.

In `AuctionCloser.cs`: delete the moved members and the now-unused fields (`_cards`, `_settings`, `_provider`); the
constructor becomes

```csharp
    public AuctionCloser(
        IListingRepository listings, IBidRepository bids, IPurchaseRepository purchases,
        IAuditLogRepository audit, IAuctionEmails emails, IBuyerFeeCharger charger,
        ITransactionRunner transactions, TimeProvider clock,
        IOptions<AuctionCloseOptions> options, ILogger<AuctionCloser> log)
```

and the charge loop calls the charger:

```csharp
            if (await TryAsync(async () => await _charger.ChargeAsync(id) != ChargeOutcome.Skipped, "charge sale record", id)) charged++;
```

Update the class comment: it closes auctions and hands due sale records to `IBuyerFeeCharger`.

`AuctionServiceCollectionExtensions.AddAuctionClosing`: add `services.AddScoped<IBuyerFeeCharger, BuyerFeeCharger>();`
(with `using Stallions.Server.Sales;`).

- [ ] **Step 4: Listing-neutral emails with offer wording**

`IAuctionEmails`: change the parameter type of `WonAndChargedAsync`, `PaymentFailedAsync`, `PaymentAbandonedAsync`,
`StudSaleConfirmationAsync` and `StudNoSaleAsync` from `AuctionListing listing` to `Listing listing`, and update the
summary to "auction and Make an Offer emails". `OutbidAsync`, `AuctionLostAsync`, `AuctionEndedWithoutSaleAsync` keep
`AuctionListing`.

`AuctionEmails`: change `QueueAsync`, `StudAddressAsync`, `StudContact`, `Name`, `Season`, `Farm` to take `Listing`.
Change `Amounts` to take the price label: `private static Block Amounts(Purchase p, string priceLabel, string balanceLabel)`
with the first row `(priceLabel, Money(p.TotalPriceIncGst))`. Replace the five methods with:

```csharp
    private static bool FromOffer(Purchase p) => p.OfferId != null;

    public Task WonAndChargedAsync(Listing listing, Purchase purchase, User winner) => FromOffer(purchase)
        ? QueueAsync(winner.Email, "OfferAcceptedAndCharged", $"Your offer for the {Name(listing)} nomination was accepted", listing,
            Para($"Your offer of {Money(purchase.TotalPriceIncGst)} for the {Name(listing)} nomination ({Season(listing)}) was accepted by {Farm(listing)}."),
            Amounts(purchase, "Price (your accepted offer)", "Balance you pay the stud directly"),
            Para($"The buyer fee of {Money(purchase.BuyerFeeIncGst)} has been charged to your saved card."),
            Para(_disclosure.BuyerFeeExplanation),
            Para(_disclosure.StudFarmBalanceArrangement),
            Para(StudContact(listing)),
            Link("/my-purchases", "View your sale record"))
        : QueueAsync(winner.Email, "WonAndCharged", $"You won the {Name(listing)} nomination", listing,
            Para($"Your bid of {Money(purchase.TotalPriceIncGst)} won the auction for the {Name(listing)} nomination ({Season(listing)}), offered by {Farm(listing)}."),
            Amounts(purchase, "Price (your winning bid)", "Balance you pay the stud directly"),
            Para($"The buyer fee of {Money(purchase.BuyerFeeIncGst)} has been charged to your saved card."),
            Para(_disclosure.BuyerFeeExplanation),
            Para(_disclosure.StudFarmBalanceArrangement),
            Para(StudContact(listing)),
            Link("/my-purchases", "View your sale record"));

    public Task PaymentFailedAsync(Listing listing, Purchase purchase, User winner) =>
        QueueAsync(winner.Email, "PaymentFailed", $"Action needed: payment for {Name(listing)} failed", listing,
            Para(FromOffer(purchase)
                ? $"Your offer of {Money(purchase.TotalPriceIncGst)} for the {Name(listing)} nomination was accepted, but we couldn't charge the buyer fee of {Money(purchase.BuyerFeeIncGst)} to your saved card: {purchase.LastChargeFailure}"
                : $"Your bid of {Money(purchase.TotalPriceIncGst)} won the auction for the {Name(listing)} nomination, but we couldn't charge the buyer fee of {Money(purchase.BuyerFeeIncGst)} to your saved card: {purchase.LastChargeFailure}"),
            Para($"Please update your card by {When(purchase.ChargeDueBy ?? DateTime.UtcNow)}. Saving a new card retries the payment straight away. If the payment can't be completed by then, the sale won't go ahead."),
            Link("/account/card", "Update your card"));

    public Task PaymentAbandonedAsync(Listing listing, Purchase purchase, User winner) =>
        QueueAsync(winner.Email, "PaymentAbandoned", $"No sale: {Name(listing)}", listing,
            Para(FromOffer(purchase)
                ? $"We couldn't charge the buyer fee for your accepted offer on the {Name(listing)} nomination by the deadline, so the sale has not gone ahead."
                : $"We couldn't charge the buyer fee for the {Name(listing)} nomination by the deadline, so the sale has not gone ahead and the nomination is no longer yours."));

    public async Task StudSaleConfirmationAsync(Listing listing, Purchase purchase, User winner) =>
        await QueueAsync(await StudAddressAsync(listing), "StudSaleConfirmation", $"Sale confirmed: {Name(listing)} nomination", listing,
            Para(FromOffer(purchase)
                ? $"You accepted an offer for the {Name(listing)} nomination ({Season(listing)}), and the buyer fee has been paid."
                : $"The auction for the {Name(listing)} nomination ({Season(listing)}) has closed with a sale, and the buyer fee has been paid."),
            Para($"Buyer: {winner.DisplayName}, {winner.Email}"),
            Amounts(purchase, "Price", "Balance the buyer pays you directly"),
            Para("Please arrange the balance with the buyer under your own terms."),
            Link($"/admin/listings/{listing.Id}", "View the listing"));

    public async Task StudNoSaleAsync(Listing listing, ListingCloseReason reason) => await (listing is MakeAnOfferListing
        ? QueueAsync(await StudAddressAsync(listing), "StudNoSale", $"No sale: {Name(listing)} nomination", listing,
            Para($"The sale from the offer you accepted for the {Name(listing)} nomination ({Season(listing)}) has not gone ahead: the buyer's payment couldn't be completed."),
            Para("Your Make an Offer listing stays open for other offers."),
            Link($"/admin/listings/{listing.Id}", "View the listing"))
        : QueueAsync(await StudAddressAsync(listing), "StudNoSale", $"No sale: {Name(listing)} nomination", listing,
            Para($"The auction for the {Name(listing)} nomination ({Season(listing)}) has closed without a sale: {ReasonText(reason)}"),
            Para("The lot is marked Unsold in My Listings."),
            Link($"/admin/listings/{listing.Id}", "Offer it as Make an Offer")));
```

- [ ] **Step 5: Fixture** — in `AuctionCloserFixture` add a charger factory and pass it to the closer (same
  repositories, so the stale-context race test still uses its listing repository):

```csharp
    /// <summary>A charger on its own context, as the Accept action or a separate run would have.</summary>
    public BuyerFeeCharger CreateCharger(AppDbContext? db = null, IListingRepository? listings = null)
    {
        db ??= DbContextFactory.Create(DbName);
        var settings = new Mock<IPlatformSettingsRepository>();
        settings.Setup(s => s.GetAsync()).ReturnsAsync(Settings);
        return new BuyerFeeCharger(
            new PurchaseRepository(db), listings ?? new ListingRepository(db), new BidRepository(db), new SavedCardRepository(db),
            settings.Object, Audit.Object, Emails.Object, Provider.Object, new ClearingTransactionRunner(db), Clock,
            NullLogger<BuyerFeeCharger>.Instance);
    }

    public AuctionCloser CreateSut(AppDbContext? db = null, IListingRepository? listings = null)
    {
        db ??= DbContextFactory.Create(DbName);
        var listingRepo = listings ?? new ListingRepository(db);
        return new AuctionCloser(
            listingRepo, new BidRepository(db), new PurchaseRepository(db), Audit.Object, Emails.Object,
            CreateCharger(db, listingRepo), new ClearingTransactionRunner(db), Clock,
            Microsoft.Extensions.Options.Options.Create(new AuctionCloseOptions()), NullLogger<AuctionCloser>.Instance);
    }
```

(Add `using Stallions.Server.Sales;`.)

- [ ] **Step 6: Run green** — `dotnet test tests/Server.Tests --filter "FullyQualifiedName~AuctionCloser|FullyQualifiedName~BuyerFeeCharger|FullyQualifiedName~AuctionEmails"`: every pre-existing test passes **without edits**, plus the new ones. Then full build (0 warnings) and full test run.

- [ ] **Step 7: Commit**

```bash
git add src/Server/Sales src/Server/Auctions src/Server/Email tests/Server.Tests/Auctions/AuctionCloserFixture.cs tests/Server.Tests/Sales tests/Server.Tests/Email/AuctionEmailsTests.cs
git commit -m "refactor: shared BuyerFeeCharger for auction wins and accepted offers; offer email wording"
```

---

### Task 3: Shared buyer eligibility

**Files:**
- Create: `src/Server/Services/BuyerEligibility.cs`
- Modify: `src/Server/Services/BidService.cs`
- Test: `tests/Server.Tests/Services/BuyerEligibilityTests.cs` (BidServiceTests unchanged)

**Interfaces:**
- Produces: `enum BuyerAction { Bid, Offer }`; `sealed record EligibilityFailure(bool Forbidden, string Message)`;
  `sealed class BuyerEligibility(ITermsRepository terms, ICardService cards) { Task<EligibilityFailure?> CheckAsync(User caller, BuyerAction action) }`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Server.Tests/Services/BuyerEligibilityTests.cs
using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class BuyerEligibilityTests
{
    private readonly Mock<ITermsRepository> _terms = new();
    private readonly Mock<ICardService> _cards = new();

    private BuyerEligibility Sut() => new(_terms.Object, _cards.Object);

    private static User Buyer(UserStatus status = UserStatus.Active, int? terms = 2) =>
        new() { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = status, AcceptedTermsVersion = terms };

    public BuyerEligibilityTests()
    {
        _terms.Setup(t => t.GetCurrentAsync()).ReturnsAsync(new TermsDocument { Version = 2, Body = "x" });
        _cards.Setup(c => c.HasValidCardAsync(It.IsAny<Guid>())).ReturnsAsync(true);
    }

    [Fact]
    public async Task AVerifiedBuyerWithCurrentTermsAndACard_IsEligible() =>
        (await Sut().CheckAsync(Buyer(), BuyerAction.Offer)).Should().BeNull();

    [Fact]
    public async Task AnUnverifiedBuyer_IsForbidden()
    {
        var failure = await Sut().CheckAsync(Buyer(UserStatus.PendingVerification), BuyerAction.Offer);

        failure!.Forbidden.Should().BeTrue();
        failure.Message.Should().Be("Your account must be verified before you can make an offer.");
    }

    [Fact]
    public async Task OldTerms_AreRejected() =>
        (await Sut().CheckAsync(Buyer(terms: 1), BuyerAction.Offer))!.Message
            .Should().Be("You must accept the current Terms & Conditions before making an offer.");

    [Fact]
    public async Task NoValidCard_IsRejected()
    {
        _cards.Setup(c => c.HasValidCardAsync(It.IsAny<Guid>())).ReturnsAsync(false);

        (await Sut().CheckAsync(Buyer(), BuyerAction.Offer))!.Message.Should().Be("Save a card before making an offer.");
    }

    [Fact]
    public async Task BidMessages_AreTheExistingOnes()
    {
        (await Sut().CheckAsync(Buyer(UserStatus.PendingVerification), BuyerAction.Bid))!.Message
            .Should().Be("Your account must be verified before you can bid.");
        (await Sut().CheckAsync(Buyer(terms: 1), BuyerAction.Bid))!.Message
            .Should().Be("You must accept the current Terms & Conditions before placing a bid.");
        _cards.Setup(c => c.HasValidCardAsync(It.IsAny<Guid>())).ReturnsAsync(false);
        (await Sut().CheckAsync(Buyer(), BuyerAction.Bid))!.Message.Should().Be("Save a card before bidding.");
    }
}
```

- [ ] **Step 2: Run red** — compile error.

- [ ] **Step 3: Implement**

```csharp
// src/Server/Services/BuyerEligibility.cs
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public enum BuyerAction { Bid, Offer }

/// <summary>Why a buyer can't bid or offer. Forbidden = account not verified (403); otherwise 400.</summary>
public sealed record EligibilityFailure(bool Forbidden, string Message);

/// <summary>
/// The rules a buyer must meet before bidding or making an offer: a verified account, the current
/// T&amp;C accepted (skipped when none is published), and a valid saved card so the buyer fee can be
/// charged automatically on a sale.
/// </summary>
public sealed class BuyerEligibility(ITermsRepository terms, ICardService cards)
{
    public async Task<EligibilityFailure?> CheckAsync(User caller, BuyerAction action)
    {
        if (caller.Status != UserStatus.Active)
            return new(true, action == BuyerAction.Bid
                ? "Your account must be verified before you can bid."
                : "Your account must be verified before you can make an offer.");

        var current = await terms.GetCurrentAsync();
        if (current != null && caller.AcceptedTermsVersion != current.Version)
            return new(false, action == BuyerAction.Bid
                ? "You must accept the current Terms & Conditions before placing a bid."
                : "You must accept the current Terms & Conditions before making an offer.");

        if (!await cards.HasValidCardAsync(caller.Id))
            return new(false, action == BuyerAction.Bid ? "Save a card before bidding." : "Save a card before making an offer.");

        return null;
    }
}
```

In `BidService`: add a field `private readonly BuyerEligibility _eligibility;` set in the constructor as
`_eligibility = new BuyerEligibility(termsRepo, cards);` (constructor signature unchanged). In `PlaceBidAsync`, replace the
three checks (status, T&C, card) with:

```csharp
        if (await _eligibility.CheckAsync(caller, BuyerAction.Bid) is { } notEligible)
            return notEligible.Forbidden
                ? ServiceResult<BidDto>.Forbidden(notEligible.Message)
                : ServiceResult<BidDto>.BadRequest(notEligible.Message);
```

Remove `_termsRepo`/`_cards` fields if nothing else in `BidService` uses them (keep the constructor parameters).

- [ ] **Step 4: Run green** — `BuyerEligibilityTests` and **unchanged** `BidServiceTests` pass; full build (0 warnings) and full tests.

- [ ] **Step 5: Commit**

```bash
git add src/Server/Services/BuyerEligibility.cs src/Server/Services/BidService.cs tests/Server.Tests/Services/BuyerEligibilityTests.cs
git commit -m "refactor: shared buyer eligibility for bids and offers"
```

---
### Task 4: Converting an Unsold lot; Make an Offer listings in the API

**Files:**
- Create: `src/Server/Infrastructure/SydneyTime.cs`, `src/Shared/DTOs/Listings/MakeAnOfferListingDto.cs`
- Modify: `src/Shared/DTOs/Listings/ListingDto.cs`, `AuctionListingDto.cs`, `ListingCardDto.cs`, `src/Shared/StallionsJsonContext.cs`,
  `src/Server/Data/Repositories/IListingRepository.cs`, `ListingRepository.cs`, `src/Server/Services/IListingService.cs`,
  `src/Server/Services/ListingService.cs`, `src/Server/Controllers/ListingsController.cs`, `src/Server/Services/EnquiryService.cs`,
  `src/Server/Services/AdminService.cs`
- Test: `tests/Server.Tests/Infrastructure/SydneyTimeTests.cs`, `tests/Server.Tests/Services/ListingServiceTests.cs` (add tests; `CreateSut` gains one argument), `tests/Server.Tests/Data/Repositories/MakeAnOfferQueriesTests.cs`

**Interfaces:**
- Consumes: Task 1 entities.
- Produces: `SydneyTime.Today(DateTime utcNow) : DateOnly`, `SydneyTime.EndOfDayUtc(DateOnly date) : DateTime`;
  `MakeAnOfferListingDto : ListingDto { DateTime ExpiresAt; Guid SourceAuctionListingId }` (JSON discriminator `"MakeAnOffer"`);
  `AuctionListingDto.MakeAnOfferListingId (Guid?)` (owner view: the live Make an Offer listing made from this lot);
  `ListingCardDto.OffersUntil (DateTime?)`;
  `IListingRepository.GetActiveMakeAnOfferForAuctionAsync(Guid auctionId) : Task<MakeAnOfferListing?>`,
  `GetMakeAnOfferWithDetailsAsync(Guid id) : Task<MakeAnOfferListing?>` (Stallion, Season, StudFarm + owner),
  `GetMakeAnOfferIdsExpiredAsync(DateTime now, int max) : Task<IReadOnlyList<Guid>>`;
  `IListingService.ConvertToMakeAnOfferAsync(Guid auctionId) : Task<ServiceResult<ListingDto>>`;
  route `POST api/listings/{id}/make-an-offer` (StudFarmAdminOnly, 201).
  `ListingService` constructor gains `IAuditLogRepository audit` as its last parameter.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Server.Tests/Infrastructure/SydneyTimeTests.cs
using FluentAssertions;
using Stallions.Server.Infrastructure;

namespace Stallions.Server.Tests.Infrastructure;

public class SydneyTimeTests
{
    [Fact]
    public void EndOfDay_IsTheLastSecondOfThatDayInSydney_InUtc() =>
        // 11 Oct 2026 is daylight time (UTC+11): 23:59:59 Sydney = 12:59:59 UTC.
        SydneyTime.EndOfDayUtc(new DateOnly(2026, 10, 11))
            .Should().Be(new DateTime(2026, 10, 11, 12, 59, 59, DateTimeKind.Utc));

    [Fact]
    public void Today_FollowsTheSydneyCalendar() =>
        SydneyTime.Today(new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc))
            .Should().Be(new DateOnly(2026, 10, 11));
}
```

```csharp
// tests/Server.Tests/Data/Repositories/MakeAnOfferQueriesTests.cs
using FluentAssertions;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data.Repositories;

public class MakeAnOfferQueriesTests
{
    private static readonly DateTime Now = new(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
    private readonly string _name = Guid.NewGuid().ToString();
    private AppDbContext Db() => DbContextFactory.Create(_name);

    private MakeAnOfferListing Seed(ListingStatus status, DateTime expiresAt)
    {
        using var db = Db();
        var auction = AuctionTestData.Auction(db, Now.AddDays(-2), status: ListingStatus.Unsold);
        var mao = new MakeAnOfferListing
        {
            StallionId = auction.StallionId, SeasonId = auction.SeasonId, StudFarmId = auction.StudFarmId,
            ListingType = ListingType.MakeAnOffer, Status = status, SourceAuctionListingId = auction.Id, ExpiresAt = expiresAt
        };
        db.MakeAnOfferListings.Add(mao);
        db.SaveChanges();
        return mao;
    }

    [Fact]
    public async Task ActiveMakeAnOfferForAnAuction_IgnoresClosedOnes()
    {
        var closed = Seed(ListingStatus.Cancelled, Now.AddDays(10));

        (await new ListingRepository(Db()).GetActiveMakeAnOfferForAuctionAsync(closed.SourceAuctionListingId))
            .Should().BeNull();
    }

    [Fact]
    public async Task ExpiredIds_AreActiveListingsPastTheirExpiry()
    {
        var due = Seed(ListingStatus.Active, Now.AddMinutes(-1));
        Seed(ListingStatus.Active, Now.AddDays(1));
        Seed(ListingStatus.Cancelled, Now.AddDays(-1));

        (await new ListingRepository(Db()).GetMakeAnOfferIdsExpiredAsync(Now, 50)).Should().Equal(due.Id);
    }

    [Fact]
    public async Task WithDetails_LoadsStallionSeasonFarmAndOwner()
    {
        var mao = Seed(ListingStatus.Active, Now.AddDays(1));

        var loaded = await new ListingRepository(Db()).GetMakeAnOfferWithDetailsAsync(mao.Id);

        loaded!.Stallion.Name.Should().Be("Snitzel");
        loaded.Season.Name.Should().Be("2026 Season");
        loaded.StudFarm.User.Should().NotBeNull();
    }
}
```

In `ListingServiceTests`: add `private readonly Mock<IAuditLogRepository> _auditMock = new();`, pass
`_auditMock.Object` as the new last argument of `CreateSut()`, and add:

```csharp
    private static AuctionListing UnsoldAuction(Guid farmId, DateOnly seasonEnd) => new()
    {
        Id = Guid.NewGuid(), StudFarmId = farmId, StallionId = Guid.NewGuid(), SeasonId = Guid.NewGuid(),
        Status = ListingStatus.Unsold, CloseReason = ListingCloseReason.ReserveNotMet,
        EndDateTime = DateTime.UtcNow.AddDays(-1), BuyerFeeIncGst = 150m, PublishedAt = DateTime.UtcNow.AddDays(-8),
        TermsAndConditions = "Stud terms", Description = "Lovely type",
        Season = new Season { Name = "2026 Season", EndDate = seasonEnd }
    };

    private AuctionListing ConvertibleAuction(Guid farmId)
    {
        var auction = UnsoldAuction(farmId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60)));
        _listingRepoMock.Setup(r => r.GetByIdAsync(auction.Id)).ReturnsAsync(auction);
        _subscriptionsMock.Setup(s => s.HasActiveSubscriptionAsync(auction.StallionId, auction.SeasonId)).ReturnsAsync(true);
        _listingRepoMock.Setup(r => r.AddAsync(It.IsAny<Listing>())).ReturnsAsync((Listing l) => l);
        SetBuyerFeeSetting(175m);
        return auction;
    }

    [Fact]
    public async Task Convert_CreatesALiveMakeAnOfferListing_FromTheUnsoldLot()
    {
        var (_, farm) = SignedInFarm();
        var auction = ConvertibleAuction(farm.Id);
        Listing? added = null;
        _listingRepoMock.Setup(r => r.AddAsync(It.IsAny<Listing>())).Callback<Listing>(l => added = l).ReturnsAsync((Listing l) => l);

        var result = await CreateSut().ConvertToMakeAnOfferAsync(auction.Id);

        result.HttpStatusCode.Should().Be(201);
        var mao = added.Should().BeOfType<MakeAnOfferListing>().Subject;
        mao.SourceAuctionListingId.Should().Be(auction.Id);
        mao.Status.Should().Be(ListingStatus.Active);
        mao.ListingType.Should().Be(ListingType.MakeAnOffer);
        mao.BuyerFeeIncGst.Should().Be(175m, "the fee is snapshotted from current settings, like a publish");
        mao.PublishedAt.Should().NotBeNull();
        mao.TermsAndConditions.Should().Be("Stud terms");
        mao.Description.Should().Be("Lovely type");
        mao.ExpiresAt.Should().Be(SydneyTime.EndOfDayUtc(auction.Season.EndDate));
        result.Value.Should().BeOfType<MakeAnOfferListingDto>();
        _auditMock.Verify(a => a.LogAsync("Listing", mao.Id, "ConvertedToMakeAnOffer", It.IsAny<Guid?>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task Convert_SomeoneElsesLot_IsForbidden()
    {
        SignedInFarm();
        var auction = ConvertibleAuction(Guid.NewGuid());

        (await CreateSut().ConvertToMakeAnOfferAsync(auction.Id)).HttpStatusCode.Should().Be(403);
    }

    [Theory]
    [InlineData(ListingStatus.Active)]
    [InlineData(ListingStatus.Sold)]
    [InlineData(ListingStatus.Cancelled)]
    public async Task Convert_OnlyUnsoldLots(ListingStatus status)
    {
        var (_, farm) = SignedInFarm();
        var auction = ConvertibleAuction(farm.Id);
        auction.Status = status;

        (await CreateSut().ConvertToMakeAnOfferAsync(auction.Id)).HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Convert_AfterTheSeasonEnded_IsRejected()
    {
        var (_, farm) = SignedInFarm();
        var auction = ConvertibleAuction(farm.Id);
        auction.Season.EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3));

        var result = await CreateSut().ConvertToMakeAnOfferAsync(auction.Id);

        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Contain("season has ended");
    }

    [Fact]
    public async Task Convert_WithoutAPaidListingFee_IsRejected()
    {
        var (_, farm) = SignedInFarm();
        var auction = ConvertibleAuction(farm.Id);
        _subscriptionsMock.Setup(s => s.HasActiveSubscriptionAsync(auction.StallionId, auction.SeasonId)).ReturnsAsync(false);

        (await CreateSut().ConvertToMakeAnOfferAsync(auction.Id)).HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Convert_Twice_IsRejected()
    {
        var (_, farm) = SignedInFarm();
        var auction = ConvertibleAuction(farm.Id);
        _listingRepoMock.Setup(r => r.GetActiveMakeAnOfferForAuctionAsync(auction.Id))
            .ReturnsAsync(new MakeAnOfferListing { Id = Guid.NewGuid(), Status = ListingStatus.Active });

        var result = await CreateSut().ConvertToMakeAnOfferAsync(auction.Id);

        result.HttpStatusCode.Should().Be(400);
        _listingRepoMock.Verify(r => r.AddAsync(It.IsAny<Listing>()), Times.Never);
    }

    [Fact]
    public async Task Unpublish_AMakeAnOfferListing_IsRejected()
    {
        var (_, farm) = SignedInFarm();
        var mao = new MakeAnOfferListing { Id = Guid.NewGuid(), StudFarmId = farm.Id, Status = ListingStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddDays(10) };
        _listingRepoMock.Setup(r => r.GetByIdAsync(mao.Id)).ReturnsAsync(mao);

        var result = await CreateSut().UnpublishListingAsync(mao.Id);

        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Contain("close it instead");
    }

    [Fact]
    public async Task GetById_MapsAMakeAnOfferListing_WithoutAnyPrice()
    {
        var mao = new MakeAnOfferListing
        {
            Id = Guid.NewGuid(), Status = ListingStatus.Active, ListingType = ListingType.MakeAnOffer,
            ExpiresAt = new DateTime(2027, 1, 31, 12, 59, 59, DateTimeKind.Utc), BuyerFeeIncGst = 150m,
            Stallion = new Stallion { Name = "Snitzel" }, Season = new Season { Name = "2026 Season" }, StudFarm = new StudFarm { Name = "Arrowfield" }
        };
        _listingRepoMock.Setup(r => r.GetByIdAsync(mao.Id)).ReturnsAsync(mao);

        var dto = (await CreateSut().GetByIdAsync(mao.Id)).Value.Should().BeOfType<MakeAnOfferListingDto>().Subject;

        dto.ListingType.Should().Be("MakeAnOffer");
        dto.ExpiresAt.Should().Be(mao.ExpiresAt);
        dto.BuyerFeeIncGst.Should().Be(150m);
    }

    [Fact]
    public async Task ListingCards_IncludeMakeAnOfferListings()
    {
        var mao = new MakeAnOfferListing
        {
            Id = Guid.NewGuid(), Status = ListingStatus.Active, ListingType = ListingType.MakeAnOffer,
            ExpiresAt = DateTime.UtcNow.AddDays(30), Stallion = new Stallion { Name = "Snitzel" },
            StudFarm = new StudFarm { Name = "Arrowfield" }, Season = new Season { Name = "2026 Season" }
        };
        _listingRepoMock.Setup(r => r.GetActiveAsync(null, null, ListingType.MakeAnOffer)).ReturnsAsync(new List<Listing> { mao });

        var cards = (await CreateSut().GetListingCardsAsync(null, null, "MakeAnOffer")).Value!;

        cards.Single().ListingType.Should().Be("MakeAnOffer");
        cards.Single().OffersUntil.Should().Be(mao.ExpiresAt);
        cards.Single().CurrentHighestBidIncGst.Should().BeNull();
    }
```

(Add `using Stallions.Server.Infrastructure;` to `ListingServiceTests`. `Stallion.Images` is an empty list by default, so card mapping works.)

- [ ] **Step 2: Run red** — compile errors.

- [ ] **Step 3: Sydney time helper**

```csharp
// src/Server/Infrastructure/SydneyTime.cs
namespace Stallions.Server.Infrastructure;

/// <summary>Season dates are Australian calendar dates; this converts them using Sydney time.</summary>
public static class SydneyTime
{
    public static readonly TimeZoneInfo Zone = Find();

    public static DateOnly Today(DateTime utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), Zone));

    /// <summary>23:59:59 on that date in Sydney, as UTC.</summary>
    public static DateTime EndOfDayUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(23, 59, 59)), Zone);

    private static TimeZoneInfo Find()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time"); }
    }
}
```

- [ ] **Step 4: DTOs**

```csharp
// src/Shared/DTOs/Listings/MakeAnOfferListingDto.cs
namespace Stallions.Shared.DTOs.Listings;

/// <summary>A nomination open to private offers. Never carries a price or any offer data.</summary>
public class MakeAnOfferListingDto : ListingDto
{
    // The polymorphic reader consumes "listingType" as the discriminator; the concrete type sets it.
    public MakeAnOfferListingDto() => ListingType = "MakeAnOffer";

    /// <summary>When the listing stops taking offers (end of the season), UTC.</summary>
    public DateTime ExpiresAt { get; set; }
    public Guid SourceAuctionListingId { get; set; }
}
```

`ListingDto.cs`: add `[JsonDerivedType(typeof(MakeAnOfferListingDto), "MakeAnOffer")]` under the existing
`[JsonDerivedType(typeof(AuctionListingDto), "Auction")]`. `StallionsJsonContext.cs`: add
`[JsonSerializable(typeof(MakeAnOfferListingDto))]`. `AuctionListingDto`: add

```csharp
    /// <summary>Owner view only: the live Make an Offer listing created from this lot, if any.</summary>
    public Guid? MakeAnOfferListingId { get; set; }
```

`ListingCardDto`: add `public DateTime? OffersUntil { get; set; }` with the comment `// Make an Offer: when offers close.`

- [ ] **Step 5: Repository**

`IListingRepository` / `ListingRepository`:

```csharp
    /// <summary>The Active Make an Offer listing created from this auction, if any.</summary>
    Task<MakeAnOfferListing?> GetActiveMakeAnOfferForAuctionAsync(Guid auctionId);
    /// <summary>With Stallion, Season, StudFarm and the farm's owner (for emails).</summary>
    Task<MakeAnOfferListing?> GetMakeAnOfferWithDetailsAsync(Guid id);
    /// <summary>Active Make an Offer listings whose season has ended (ExpiresAt passed), oldest first.</summary>
    Task<IReadOnlyList<Guid>> GetMakeAnOfferIdsExpiredAsync(DateTime now, int max);
```

```csharp
    public async Task<MakeAnOfferListing?> GetActiveMakeAnOfferForAuctionAsync(Guid auctionId) =>
        await _db.MakeAnOfferListings
            .FirstOrDefaultAsync(m => m.SourceAuctionListingId == auctionId && m.Status == ListingStatus.Active);

    public async Task<MakeAnOfferListing?> GetMakeAnOfferWithDetailsAsync(Guid id) =>
        await _db.MakeAnOfferListings
            .Include(m => m.Stallion)
            .Include(m => m.Season)
            .Include(m => m.StudFarm).ThenInclude(f => f.User)
            .FirstOrDefaultAsync(m => m.Id == id);

    public async Task<IReadOnlyList<Guid>> GetMakeAnOfferIdsExpiredAsync(DateTime now, int max) =>
        await _db.MakeAnOfferListings
            .Where(m => m.Status == ListingStatus.Active && m.ExpiresAt <= now)
            .OrderBy(m => m.ExpiresAt)
            .Take(max)
            .Select(m => m.Id)
            .ToListAsync();
```

- [ ] **Step 6: `ListingService`**

Constructor: add `IAuditLogRepository audit` (last parameter, field `_audit`). Add to `IListingService`:
`Task<ServiceResult<ListingDto>> ConvertToMakeAnOfferAsync(Guid auctionId);`

```csharp
    public async Task<ServiceResult<ListingDto>> ConvertToMakeAnOfferAsync(Guid auctionId)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<ListingDto>.Forbidden("Caller identity could not be resolved.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult<ListingDto>.Forbidden("No stud farm found for the current user.");

        var listing = await _listingRepo.GetByIdAsync(auctionId);
        if (listing == null)
            return ServiceResult<ListingDto>.NotFound("Listing not found.");
        if (listing.StudFarmId != farm.Id)
            return ServiceResult<ListingDto>.Forbidden("You do not have permission to change this listing.");
        if (listing is not AuctionListing auction || auction.Status != ListingStatus.Unsold)
            return ServiceResult<ListingDto>.BadRequest("Only an auction lot that closed unsold can be offered as Make an Offer.");

        var season = auction.Season;
        if (season == null || SydneyTime.Today(DateTime.UtcNow) > season.EndDate)
            return ServiceResult<ListingDto>.BadRequest("The season has ended, so this lot can no longer be offered.");

        if (!await _subscriptions.HasActiveSubscriptionAsync(auction.StallionId, auction.SeasonId))
            return ServiceResult<ListingDto>.BadRequest(
                $"This stallion has no paid listing fee for {season.Name}. Contact Stallions Australia.");

        if (await _listingRepo.GetActiveMakeAnOfferForAuctionAsync(auction.Id) != null)
            return ServiceResult<ListingDto>.BadRequest("This lot is already open for offers.");

        // Goes live straight away; the buyer fee is locked in now, exactly as when an auction is published.
        var offerListing = new MakeAnOfferListing
        {
            StallionId = auction.StallionId,
            SeasonId = auction.SeasonId,
            StudFarmId = auction.StudFarmId,
            ListingType = ListingType.MakeAnOffer,
            Status = ListingStatus.Active,
            BuyerFeeIncGst = (await _settingsRepo.GetAsync()).BuyerFeeIncGst,
            Description = auction.Description,
            TermsAndConditions = auction.TermsAndConditions,
            PublishedAt = DateTime.UtcNow,
            SourceAuctionListingId = auction.Id,
            ExpiresAt = SydneyTime.EndOfDayUtc(season.EndDate)
        };
        var created = await _listingRepo.AddAsync(offerListing);
        await _audit.LogAsync("Listing", created.Id, "ConvertedToMakeAnOffer", caller.Id,
            JsonSerializer.Serialize(new { SourceAuctionListingId = auction.Id, created.BuyerFeeIncGst, offerListing.ExpiresAt }));

        created.Stallion = auction.Stallion;
        created.Season = season;
        created.StudFarm = auction.StudFarm;
        return ServiceResult<ListingDto>.Created(MapToDto(created, canSeeReserve: true, highestBid: null));
    }
```

(Add `using System.Text.Json;` and `using Stallions.Server.Infrastructure;`.)

`UnpublishListingAsync`: after the ownership check add

```csharp
        if (listing is MakeAnOfferListing)
            return ServiceResult.BadRequest("Make an Offer listings can't be unpublished — close it instead.");
```

`IsFinished`: add `or ListingStatus.Expired`.

`GetListingCardsAsync` type switch: add `"MakeAnOffer" => ListingType.MakeAnOffer,`.

`MapToCardDto`: before the `throw`, add

```csharp
        if (l is MakeAnOfferListing m)
        {
            return new ListingCardDto
            {
                Id               = m.Id,
                ListingType      = "MakeAnOffer",
                StallionId       = m.StallionId,
                StallionName     = m.Stallion?.Name ?? string.Empty,
                PrimaryImagePath = primaryImage,
                StudFarmId       = m.StudFarmId,
                StudFarmName     = m.StudFarm?.Name ?? string.Empty,
                SeasonName       = m.Season?.Name,
                OffersUntil      = m.ExpiresAt
            };
        }
```

`MapToDto`: add a case before `_ =>`:

```csharp
        MakeAnOfferListing m => new MakeAnOfferListingDto
        {
            Id = m.Id,
            StallionId = m.StallionId,
            StallionName = m.Stallion?.Name ?? string.Empty,
            SeasonId = m.SeasonId,
            SeasonName = m.Season?.Name ?? string.Empty,
            StudFarmId = m.StudFarmId,
            StudFarmName = m.StudFarm?.Name ?? string.Empty,
            ListingType = m.ListingType.ToString(),
            Status = m.Status.ToString(),
            BuyerFeeIncGst = m.BuyerFeeIncGst,
            CreatedAt = m.CreatedAt,
            PublishedAt = m.PublishedAt,
            ClosedAt = m.ClosedAt,
            CloseReason = m.CloseReason?.ToString(),
            Description = m.Description,
            TermsAndConditions = m.TermsAndConditions,
            ExpiresAt = m.ExpiresAt,
            SourceAuctionListingId = m.SourceAuctionListingId
        },
```

`GetMineByIdAsync`: after building the DTO, for an Unsold auction set the link:

```csharp
        var dto = await ToDtoAsync(listing, canSeeReserve: true);
        if (dto is AuctionListingDto auctionDto && listing.Status == ListingStatus.Unsold)
            auctionDto.MakeAnOfferListingId = (await _listingRepo.GetActiveMakeAnOfferForAuctionAsync(listing.Id))?.Id;
        return ServiceResult<ListingDto>.Ok(dto);
```

- [ ] **Step 7: Controller, enquiry title, Staff force-status**

`ListingsController`:

```csharp
    [HttpPost("{id:guid}/make-an-offer")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> ConvertToMakeAnOffer(Guid id)
    {
        var r = await _listings.ConvertToMakeAnOfferAsync(id);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
```

`EnquiryService.BuildListingTitle`: add `MakeAnOfferListing m => $"{m.Stallion?.Name} — Make an Offer ({m.Season?.Name})",`.

`AdminService.ForceListingStatusAsync`: next to the "ended auction can't be Active" check add

```csharp
        if (newStatus == ListingStatus.Active && listing is MakeAnOfferListing expired && expired.ExpiresAt <= DateTime.UtcNow)
            return ServiceResult.BadRequest("This listing's season has ended; it can't be made Active again.");
```

- [ ] **Step 8: Run green**, full build (0 warnings), full test run. (`ListingDtoSerializationTests` must still pass; add a
  round-trip case for `MakeAnOfferListingDto` there: serialize as `ListingDto`, deserialize with
  `StallionsJsonContext.Default.Options`, assert the type and `ExpiresAt`.)

- [ ] **Step 9: Commit**

```bash
git add src/Server/Infrastructure/SydneyTime.cs src/Shared src/Server/Data/Repositories/IListingRepository.cs src/Server/Data/Repositories/ListingRepository.cs src/Server/Services/IListingService.cs src/Server/Services/ListingService.cs src/Server/Controllers/ListingsController.cs src/Server/Services/EnquiryService.cs src/Server/Services/AdminService.cs tests/Server.Tests
git status --short   # only intended files
git commit -m "feat: convert an Unsold lot to Make an Offer; Make an Offer listings in the API"
```

---

### Task 5: Offers — make, withdraw, view, accept, decline

**Files:**
- Create: `src/Shared/DTOs/Offers/MakeOfferRequest.cs`, `OfferDto.cs`, `StudOfferDto.cs`, `AcceptOfferResultDto.cs`
- Create: `src/Server/Data/Repositories/IOfferRepository.cs`, `OfferRepository.cs`, `src/Server/Services/IOfferService.cs`,
  `src/Server/Services/OfferService.cs`, `src/Server/Controllers/OffersController.cs`
- Modify: `src/Server/Data/Repositories/IPurchaseRepository.cs`, `PurchaseRepository.cs`, `src/Server/Email/IAuctionEmails.cs`,
  `src/Server/Email/AuctionEmails.cs`, `src/Shared/DTOs/Checkout/PurchaseDto.cs`, `src/Server/Services/PurchaseService.cs`,
  `src/Server/Program.cs`
- Test: `tests/Server.Tests/Services/OfferServiceTests.cs`, `tests/Server.Tests/Email/AuctionEmailsTests.cs` (add), `tests/Server.Tests/Services/PurchaseServiceTests.cs` (add)

**Interfaces:**
- Consumes: Task 1 entities; Task 2 `IBuyerFeeCharger`, `ChargeOutcome`; Task 3 `BuyerEligibility`, `BuyerAction`; Task 4 `IListingRepository.GetMakeAnOfferWithDetailsAsync`.
- Produces: DTOs below; `IOfferRepository` (methods below, incl. `GetIdsToExpireAsync`, `GetPendingIdsForListingAsync` used by Task 6);
  `IOfferService` (below); routes below; `IAuctionEmails.OfferReceivedAsync(MakeAnOfferListing, Offer, User buyer)`,
  `OfferDeclinedAsync(MakeAnOfferListing, Offer, User buyer)`; `PurchaseDto.Source` ("Auction" | "Offer");
  `IPurchaseRepository.GetByOfferIdsAsync(IEnumerable<Guid>)`.

- [ ] **Step 1: DTOs**

```csharp
// src/Shared/DTOs/Offers/MakeOfferRequest.cs
namespace Stallions.Shared.DTOs.Offers;

public class MakeOfferRequest
{
    /// <summary>The offered price inc. GST; at least the listing's buyer fee.</summary>
    public decimal AmountIncGst { get; set; }
}
```

```csharp
// src/Shared/DTOs/Offers/OfferDto.cs
namespace Stallions.Shared.DTOs.Offers;

/// <summary>A buyer's view of their own offer.</summary>
public class OfferDto
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public string StallionName { get; set; } = string.Empty;
    public string SeasonName { get; set; } = string.Empty;
    public string StudFarmName { get; set; } = string.Empty;
    public decimal AmountIncGst { get; set; }
    /// <summary>Pending, Accepted, Declined, Withdrawn, Expired.</summary>
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    /// <summary>Accepted offers: the sale record and its state (Pending, Completed, Voided, Refunded).</summary>
    public Guid? PurchaseId { get; set; }
    public string? SaleStatus { get; set; }
    public DateTime? ChargeDueBy { get; set; }
    /// <summary>The listing is still taking offers.</summary>
    public bool ListingOpen { get; set; }
}
```

```csharp
// src/Shared/DTOs/Offers/StudOfferDto.cs
namespace Stallions.Shared.DTOs.Offers;

/// <summary>The stud's (or Staff's) view of an offer on their listing.</summary>
public class StudOfferDto
{
    public Guid Id { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public string BuyerEmail { get; set; } = string.Empty;
    public decimal AmountIncGst { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public Guid? PurchaseId { get; set; }
    public string? SaleStatus { get; set; }
    public DateTime? ChargeDueBy { get; set; }
}
```

```csharp
// src/Shared/DTOs/Offers/AcceptOfferResultDto.cs
namespace Stallions.Shared.DTOs.Offers;

/// <summary>What happened when the stud accepted an offer.</summary>
public class AcceptOfferResultDto
{
    public Guid OfferId { get; set; }
    public Guid PurchaseId { get; set; }
    /// <summary>"Paid", "PaymentFailed", "NoSale", "Skipped" or "Processing" (provider unreachable; retried shortly).</summary>
    public string Charge { get; set; } = string.Empty;
    public string SaleStatus { get; set; } = string.Empty;
    public DateTime? ChargeDueBy { get; set; }
    public string? LastChargeFailure { get; set; }
}
```

`PurchaseDto`: add `/// <summary>"Auction" or "Offer".</summary> public string Source { get; set; } = "Auction";`
and in `PurchaseService.MapToDto` set `Source = p.OfferId != null ? "Offer" : "Auction",`. Add to `PurchaseServiceTests`:

```csharp
    [Fact]
    public async Task GetPurchases_SaysWhetherTheSaleCameFromAnOffer()
    {
        var buyer = Buyer();
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        _purchases.Setup(r => r.GetByBuyerIdAsync(buyer.Id)).ReturnsAsync(new List<Purchase>
        {
            new() { BuyerUserId = buyer.Id, OfferId = Guid.NewGuid() },
            new() { BuyerUserId = buyer.Id, BidId = Guid.NewGuid() }
        });

        var dtos = (await CreateSut().GetPurchasesAsync()).Value!;

        dtos.Select(d => d.Source).Should().Equal("Offer", "Auction");
    }
```

- [ ] **Step 2: Write the failing service tests**

```csharp
// tests/Server.Tests/Services/OfferServiceTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Sales;
using Stallions.Server.Services;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.DTOs.Offers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class OfferServiceTests
{
    private readonly string _name = Guid.NewGuid().ToString();
    private readonly TestClock _clock = new();
    private readonly Mock<IUserService> _users = new();
    private readonly Mock<IBuyerFeeCharger> _charger = new();
    private readonly Mock<IAuctionEmails> _emails = new();
    private readonly Mock<IAuditLogRepository> _audit = new();
    private readonly Mock<ITermsRepository> _terms = new();
    private readonly Mock<ICardService> _cards = new();
    private readonly Mock<IPlatformSettingsRepository> _settings = new();

    public OfferServiceTests()
    {
        _cards.Setup(c => c.HasValidCardAsync(It.IsAny<Guid>())).ReturnsAsync(true);
        _settings.Setup(s => s.GetAsync()).ReturnsAsync(new PlatformSettings { BuyerFeeIncGst = 150m, OfferExpiryDays = 7 });
        _charger.Setup(c => c.ChargeAsync(It.IsAny<Guid>())).ReturnsAsync(ChargeOutcome.Paid);
    }

    private AppDbContext Db() => DbContextFactory.Create(_name);

    private OfferService Sut(AppDbContext? db = null, ITransactionRunner? runner = null)
    {
        db ??= Db();
        return new OfferService(new OfferRepository(db), new ListingRepository(db), new PurchaseRepository(db),
            new StudFarmRepository(db), _users.Object, _settings.Object, _audit.Object, _emails.Object, _charger.Object,
            runner ?? new InlineTransactionRunner(), _terms.Object, _cards.Object, _clock, NullLogger<OfferService>.Instance);
    }

    private void SignIn(User user) => _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(user);

    /// <summary>An Active Make an Offer listing, its stud owner, and a verified buyer.</summary>
    private (MakeAnOfferListing Listing, User Owner, User Buyer) Setup(DateTime? expiresAt = null)
    {
        using var db = Db();
        var auction = AuctionTestData.Auction(db, _clock.UtcNow.AddDays(-2), status: ListingStatus.Unsold);
        var listing = new MakeAnOfferListing
        {
            StallionId = auction.StallionId, SeasonId = auction.SeasonId, StudFarmId = auction.StudFarmId,
            ListingType = ListingType.MakeAnOffer, Status = ListingStatus.Active, BuyerFeeIncGst = 150m,
            SourceAuctionListingId = auction.Id, ExpiresAt = expiresAt ?? _clock.UtcNow.AddDays(30), PublishedAt = _clock.UtcNow
        };
        db.MakeAnOfferListings.Add(listing);
        db.SaveChanges();
        var owner = db.Users.Single(u => u.Id == db.StudFarms.Single(f => f.Id == auction.StudFarmId).UserId);
        var buyer = AuctionTestData.Buyer(db, "Offer Buyer");
        return (listing, owner, buyer);
    }

    private async Task<OfferDto> MakeOffer(MakeAnOfferListing listing, User buyer, decimal amount = 9000m)
    {
        SignIn(buyer);
        var r = await Sut().MakeAsync(listing.Id, new MakeOfferRequest { AmountIncGst = amount });
        r.HttpStatusCode.Should().Be(201, r.Error);
        return r.Value!;
    }

    [Fact]
    public async Task Make_CreatesAPendingOffer_ExpiringPerSettings_AndTellsTheStud()
    {
        var (listing, _, buyer) = Setup();

        var offer = await MakeOffer(listing, buyer);

        offer.Status.Should().Be("Pending");
        offer.ExpiresAt.Should().Be(_clock.UtcNow.AddDays(7));
        _emails.Verify(e => e.OfferReceivedAsync(It.Is<MakeAnOfferListing>(l => l.Id == listing.Id), It.IsAny<Offer>(),
            It.Is<User>(u => u.Id == buyer.Id)), Times.Once);
        _audit.Verify(a => a.LogAsync("Offer", offer.Id, "OfferMade", buyer.Id, It.IsAny<string?>()), Times.Once);
    }

    [Theory]
    [InlineData("150.00", 201)]
    [InlineData("149.99", 400)]
    [InlineData("150.001", 400)]
    [InlineData("10000000.01", 400)]
    public async Task Make_AmountRules(string amount, int expected)
    {
        var (listing, _, buyer) = Setup();
        SignIn(buyer);

        var r = await Sut().MakeAsync(listing.Id, new MakeOfferRequest { AmountIncGst = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture) });

        r.HttpStatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task Make_WithoutACard_IsRejected()
    {
        var (listing, _, buyer) = Setup();
        _cards.Setup(c => c.HasValidCardAsync(buyer.Id)).ReturnsAsync(false);
        SignIn(buyer);

        var r = await Sut().MakeAsync(listing.Id, new MakeOfferRequest { AmountIncGst = 9000m });

        r.Error.Should().Be("Save a card before making an offer.");
    }

    [Fact]
    public async Task Make_ASecondPendingOffer_Conflicts_ButAfterWithdrawingItIsAllowed()
    {
        var (listing, _, buyer) = Setup();
        var first = await MakeOffer(listing, buyer);

        (await Sut().MakeAsync(listing.Id, new MakeOfferRequest { AmountIncGst = 9500m })).HttpStatusCode.Should().Be(409);

        (await Sut().WithdrawAsync(first.Id)).Succeeded.Should().BeTrue();
        (await Sut().MakeAsync(listing.Id, new MakeOfferRequest { AmountIncGst = 9500m })).HttpStatusCode.Should().Be(201);
    }

    [Fact]
    public async Task Make_OnAListingPastItsExpiry_IsRejected()
    {
        var (listing, _, buyer) = Setup(expiresAt: _clock.UtcNow.AddMinutes(-1));
        SignIn(buyer);

        var r = await Sut().MakeAsync(listing.Id, new MakeOfferRequest { AmountIncGst = 9000m });

        r.Error.Should().Be("This listing is no longer taking offers.");
    }

    [Fact]
    public async Task Withdraw_SomeoneElsesOffer_IsForbidden()
    {
        var (listing, _, buyer) = Setup();
        var offer = await MakeOffer(listing, buyer);
        using (var db = Db()) SignIn(AuctionTestData.Buyer(db, "Someone Else"));

        (await Sut().WithdrawAsync(offer.Id)).HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task TheOwningStud_SeesOffersWithTheBuyer_AnotherStudCannot()
    {
        var (listing, owner, buyer) = Setup();
        await MakeOffer(listing, buyer);

        SignIn(owner);
        var mine = (await Sut().GetForListingAsync(listing.Id)).Value!;
        mine.Single().BuyerEmail.Should().Be(buyer.Email);

        SignIn(new User { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active });
        (await Sut().GetForListingAsync(listing.Id)).HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GetMine_ShowsOnlyTheBuyersOwnOffers()
    {
        var (listing, _, buyer) = Setup();
        await MakeOffer(listing, buyer);
        User other;
        using (var db = Db()) other = AuctionTestData.Buyer(db, "Other");
        await MakeOffer(listing, other, 8000m);

        SignIn(buyer);
        var mine = (await Sut().GetMineAsync()).Value!;

        mine.Should().ContainSingle().Which.AmountIncGst.Should().Be(9000m);
    }

    [Fact]
    public async Task Decline_SetsDeclined_AndEmailsTheBuyer()
    {
        var (listing, owner, buyer) = Setup();
        var offer = await MakeOffer(listing, buyer);
        SignIn(owner);

        (await Sut().DeclineAsync(offer.Id)).Succeeded.Should().BeTrue();

        (await Db().Offers.SingleAsync()).Status.Should().Be(OfferStatus.Declined);
        _emails.Verify(e => e.OfferDeclinedAsync(It.IsAny<MakeAnOfferListing>(), It.IsAny<Offer>(), It.Is<User>(u => u.Id == buyer.Id)), Times.Once);
    }

    [Fact]
    public async Task Accept_CreatesThePendingSaleRecord_AndChargesStraightAway()
    {
        var (listing, owner, buyer) = Setup();
        var offer = await MakeOffer(listing, buyer);
        SignIn(owner);

        var r = await Sut().AcceptAsync(offer.Id);

        r.Succeeded.Should().BeTrue(r.Error);
        r.Value!.Charge.Should().Be("Paid");
        var sale = await Db().Purchases.SingleAsync();
        sale.OfferId.Should().Be(offer.Id);
        sale.BidId.Should().BeNull();
        sale.TotalPriceIncGst.Should().Be(9000m);
        sale.BuyerFeeIncGst.Should().Be(150m);
        sale.BuyerFeeGst.Should().Be(13.64m);
        sale.BuyerFeeExGst.Should().Be(136.36m);
        sale.BalancePayableToStudIncGst.Should().Be(8850m);
        (await Db().Offers.SingleAsync()).Status.Should().Be(OfferStatus.Accepted);
        (await Db().Listings.SingleAsync(l => l.Id == listing.Id)).Status.Should().Be(ListingStatus.Active);
        _charger.Verify(c => c.ChargeAsync(sale.Id), Times.Once);
    }

    [Fact]
    public async Task Accept_WhenTheProviderIsUnreachable_ReportsProcessing()
    {
        var (listing, owner, buyer) = Setup();
        var offer = await MakeOffer(listing, buyer);
        _charger.Setup(c => c.ChargeAsync(It.IsAny<Guid>())).ThrowsAsync(new HttpRequestException("down"));
        SignIn(owner);

        var r = await Sut().AcceptAsync(offer.Id);

        r.Succeeded.Should().BeTrue();
        r.Value!.Charge.Should().Be("Processing");
    }

    [Fact]
    public async Task Accept_AnExpiredOffer_IsRejected_AndNothingIsCharged()
    {
        var (listing, owner, buyer) = Setup();
        var offer = await MakeOffer(listing, buyer);
        _clock.Advance(TimeSpan.FromDays(8));
        SignIn(owner);

        (await Sut().AcceptAsync(offer.Id)).HttpStatusCode.Should().Be(400);
        (await Db().Purchases.CountAsync()).Should().Be(0);
        _charger.Verify(c => c.ChargeAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Accept_Several_IsAllowed()
    {
        var (listing, owner, buyer) = Setup();
        var a = await MakeOffer(listing, buyer);
        User other;
        using (var db = Db()) other = AuctionTestData.Buyer(db, "Other");
        var b = await MakeOffer(listing, other, 8000m);
        SignIn(owner);

        (await Sut().AcceptAsync(a.Id)).Succeeded.Should().BeTrue();
        (await Sut().AcceptAsync(b.Id)).Succeeded.Should().BeTrue();

        (await Db().Purchases.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Accept_TwiceConcurrently_CreatesOneSaleRecord()
    {
        var (listing, owner, buyer) = Setup();
        var offer = await MakeOffer(listing, buyer);
        SignIn(owner);
        // The second request read the offer (still Pending) before the first accepted it.
        using var staleDb = Db();
        await staleDb.Offers.SingleAsync(o => o.Id == offer.Id);

        (await Sut().AcceptAsync(offer.Id)).Succeeded.Should().BeTrue();
        var second = await Sut(staleDb).AcceptAsync(offer.Id);

        second.HttpStatusCode.Should().Be(409);
        (await Db().Purchases.CountAsync()).Should().Be(1);
        _charger.Verify(c => c.ChargeAsync(It.IsAny<Guid>()), Times.Once);
    }

    [Fact]
    public async Task AnotherStud_CannotAcceptOrDecline()
    {
        var (listing, _, buyer) = Setup();
        var offer = await MakeOffer(listing, buyer);
        User stranger;
        using (var db = Db())
        {
            stranger = new User { ObjectId = "x", Role = UserRole.StudFarmAdmin, Status = UserStatus.Active, Email = "s@x" };
            db.Users.Add(stranger);
            db.StudFarms.Add(new StudFarm { Name = "Other Stud", UserId = stranger.Id });
            db.SaveChanges();
        }
        SignIn(stranger);

        (await Sut().AcceptAsync(offer.Id)).HttpStatusCode.Should().Be(403);
        (await Sut().DeclineAsync(offer.Id)).HttpStatusCode.Should().Be(403);
    }
}
```

- [ ] **Step 3: Run red** — compile errors.

- [ ] **Step 4: Repositories**

```csharp
// src/Server/Data/Repositories/IOfferRepository.cs
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IOfferRepository
{
    Task AddAsync(Offer offer);
    Task UpdateAsync(Offer offer);
    /// <summary>With the buyer and the listing's Stallion, Season, StudFarm and owner.</summary>
    Task<Offer?> GetByIdAsync(Guid id);
    Task<Offer?> GetPendingAsync(Guid listingId, Guid buyerUserId);
    /// <summary>The buyer's most recent offer on the listing (any status).</summary>
    Task<Offer?> GetLatestForBuyerAsync(Guid listingId, Guid buyerUserId);
    /// <summary>The buyer's offers, newest first, with listing details.</summary>
    Task<IReadOnlyList<Offer>> GetByBuyerAsync(Guid buyerUserId);
    /// <summary>All offers on the listing, newest first, with buyers.</summary>
    Task<IReadOnlyList<Offer>> GetForListingAsync(Guid listingId);
    /// <summary>Pending offers past their expiry, or whose listing is no longer Active.</summary>
    Task<IReadOnlyList<Guid>> GetIdsToExpireAsync(DateTime now, int max);
    Task<IReadOnlyList<Guid>> GetPendingIdsForListingAsync(Guid listingId);
}
```

```csharp
// src/Server/Data/Repositories/OfferRepository.cs
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public class OfferRepository : IOfferRepository
{
    private readonly AppDbContext _db;
    public OfferRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(Offer offer)
    {
        _db.Offers.Add(offer);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(Offer offer)
    {
        _db.Offers.Update(offer);
        await _db.SaveChangesAsync();
    }

    public async Task<Offer?> GetByIdAsync(Guid id) =>
        await WithDetails(_db.Offers).FirstOrDefaultAsync(o => o.Id == id);

    public async Task<Offer?> GetPendingAsync(Guid listingId, Guid buyerUserId) =>
        await _db.Offers.FirstOrDefaultAsync(o =>
            o.ListingId == listingId && o.BuyerUserId == buyerUserId && o.Status == OfferStatus.Pending);

    public async Task<Offer?> GetLatestForBuyerAsync(Guid listingId, Guid buyerUserId) =>
        await WithDetails(_db.Offers)
            .Where(o => o.ListingId == listingId && o.BuyerUserId == buyerUserId)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

    public async Task<IReadOnlyList<Offer>> GetByBuyerAsync(Guid buyerUserId) =>
        await WithDetails(_db.Offers)
            .Where(o => o.BuyerUserId == buyerUserId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

    public async Task<IReadOnlyList<Offer>> GetForListingAsync(Guid listingId) =>
        await _db.Offers.Include(o => o.Buyer)
            .Where(o => o.ListingId == listingId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

    public async Task<IReadOnlyList<Guid>> GetIdsToExpireAsync(DateTime now, int max) =>
        await _db.Offers
            .Where(o => o.Status == OfferStatus.Pending && (o.ExpiresAt <= now || o.Listing.Status != ListingStatus.Active))
            .OrderBy(o => o.ExpiresAt)
            .Take(max)
            .Select(o => o.Id)
            .ToListAsync();

    public async Task<IReadOnlyList<Guid>> GetPendingIdsForListingAsync(Guid listingId) =>
        await _db.Offers
            .Where(o => o.ListingId == listingId && o.Status == OfferStatus.Pending)
            .Select(o => o.Id)
            .ToListAsync();

    private static IQueryable<Offer> WithDetails(IQueryable<Offer> q) =>
        q.Include(o => o.Buyer)
         .Include(o => o.Listing).ThenInclude(l => l.Stallion)
         .Include(o => o.Listing).ThenInclude(l => l.Season)
         .Include(o => o.Listing).ThenInclude(l => l.StudFarm).ThenInclude(f => f.User);
}
```

`IPurchaseRepository` / `PurchaseRepository`:

```csharp
    /// <summary>Sale records created from these offers (newest first per offer).</summary>
    Task<IReadOnlyList<Purchase>> GetByOfferIdsAsync(IEnumerable<Guid> offerIds);
```

```csharp
    public async Task<IReadOnlyList<Purchase>> GetByOfferIdsAsync(IEnumerable<Guid> offerIds)
    {
        var ids = offerIds.ToList();
        return await _db.Purchases
            .Where(p => p.OfferId != null && ids.Contains(p.OfferId.Value))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }
```

- [ ] **Step 5: Emails** — `IAuctionEmails`: add

```csharp
    /// <summary>To the stud: a buyer made an offer.</summary>
    Task OfferReceivedAsync(MakeAnOfferListing listing, Offer offer, User buyer);
    /// <summary>To the buyer: the stud declined their offer.</summary>
    Task OfferDeclinedAsync(MakeAnOfferListing listing, Offer offer, User buyer);
```

`AuctionEmails`:

```csharp
    public async Task OfferReceivedAsync(MakeAnOfferListing listing, Offer offer, User buyer) =>
        await QueueAsync(await StudAddressAsync(listing), "OfferReceived", $"New offer: {Name(listing)} nomination", listing,
            Para($"{buyer.DisplayName} ({buyer.Email}) has offered {Money(offer.AmountIncGst)} for the {Name(listing)} nomination ({Season(listing)})."),
            Para($"The offer is open until {When(offer.ExpiresAt)}. Accepting it charges the buyer the buyer fee straight away."),
            Link($"/admin/listings/{listing.Id}", "Review the offer"));

    public Task OfferDeclinedAsync(MakeAnOfferListing listing, Offer offer, User buyer) =>
        QueueAsync(buyer.Email, "OfferDeclined", $"Offer declined: {Name(listing)}", listing,
            Para($"The stud has declined your offer of {Money(offer.AmountIncGst)} for the {Name(listing)} nomination ({Season(listing)})."),
            Link($"/listings/{listing.Id}", "View the listing"));
```

Add to `AuctionEmailsTests`:

```csharp
    [Fact]
    public async Task OfferReceived_GoesToTheStud_WithTheBuyerAndAmount()
    {
        var offer = new Offer { AmountIncGst = 9000m, ExpiresAt = new DateTime(2026, 10, 18, 1, 0, 0, DateTimeKind.Utc) };

        await CreateSut().OfferReceivedAsync(OfferListing(), offer, new User { DisplayName = "Jane Buyer", Email = "jane@example.com" });

        var email = _queued.Single();
        email.ToAddress.Should().Be("sales@arrowfield.example");
        email.TextBody.Should().Contain("Jane Buyer").And.Contain("$9,000.00").And.Contain("18 Oct 2026");
    }

    [Fact]
    public async Task OfferDeclined_GoesToTheBuyer()
    {
        await CreateSut().OfferDeclinedAsync(OfferListing(), new Offer { AmountIncGst = 9000m }, new User { Email = "jane@example.com" });

        var email = _queued.Single();
        email.ToAddress.Should().Be("jane@example.com");
        email.TextBody.Should().Contain("declined").And.Contain("$9,000.00");
    }
```

- [ ] **Step 6: `OfferService` and controller**

```csharp
// src/Server/Services/IOfferService.cs
using Stallions.Shared.DTOs.Offers;

namespace Stallions.Server.Services;

public interface IOfferService
{
    Task<ServiceResult<OfferDto>> MakeAsync(Guid listingId, MakeOfferRequest request);
    Task<ServiceResult> WithdrawAsync(Guid offerId);
    Task<ServiceResult<IReadOnlyList<OfferDto>>> GetMineAsync();
    /// <summary>The caller's latest offer on the listing; Ok(null) when they have none.</summary>
    Task<ServiceResult<OfferDto?>> GetMyOfferForListingAsync(Guid listingId);
    Task<ServiceResult<IReadOnlyList<StudOfferDto>>> GetForListingAsync(Guid listingId);
    Task<ServiceResult> DeclineAsync(Guid offerId);
    Task<ServiceResult<AcceptOfferResultDto>> AcceptAsync(Guid offerId);
}
```

```csharp
// src/Server/Services/OfferService.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Sales;
using Stallions.Shared;
using Stallions.Shared.DTOs.Offers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

/// <summary>
/// Private offers on Make an Offer listings. Buyers make and withdraw them; the owning stud accepts
/// (the buyer fee is charged at once through the shared BuyerFeeCharger) or declines. Only the stud,
/// Staff and the offering buyer ever see an offer.
/// </summary>
public class OfferService : IOfferService
{
    private const decimal MaxOfferIncGst = 10_000_000m;

    private readonly IOfferRepository _offers;
    private readonly IListingRepository _listings;
    private readonly IPurchaseRepository _purchases;
    private readonly IStudFarmRepository _farms;
    private readonly IUserService _users;
    private readonly IPlatformSettingsRepository _settings;
    private readonly IAuditLogRepository _audit;
    private readonly IAuctionEmails _emails;
    private readonly IBuyerFeeCharger _charger;
    private readonly ITransactionRunner _transactions;
    private readonly BuyerEligibility _eligibility;
    private readonly TimeProvider _clock;
    private readonly ILogger<OfferService> _log;

    public OfferService(
        IOfferRepository offers, IListingRepository listings, IPurchaseRepository purchases, IStudFarmRepository farms,
        IUserService users, IPlatformSettingsRepository settings, IAuditLogRepository audit, IAuctionEmails emails,
        IBuyerFeeCharger charger, ITransactionRunner transactions, ITermsRepository terms, ICardService cards,
        TimeProvider clock, ILogger<OfferService> log)
    {
        _offers = offers; _listings = listings; _purchases = purchases; _farms = farms; _users = users;
        _settings = settings; _audit = audit; _emails = emails; _charger = charger; _transactions = transactions;
        _eligibility = new BuyerEligibility(terms, cards); _clock = clock; _log = log;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private static bool IsOpen(MakeAnOfferListing listing, DateTime now) =>
        listing.Status == ListingStatus.Active && listing.ExpiresAt > now;

    public async Task<ServiceResult<OfferDto>> MakeAsync(Guid listingId, MakeOfferRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<OfferDto>.Forbidden();
        if (await _eligibility.CheckAsync(caller, BuyerAction.Offer) is { } notEligible)
            return notEligible.Forbidden
                ? ServiceResult<OfferDto>.Forbidden(notEligible.Message)
                : ServiceResult<OfferDto>.BadRequest(notEligible.Message);

        var amount = request.AmountIncGst;
        if (decimal.Round(amount, 2) != amount)
            return ServiceResult<OfferDto>.BadRequest("Offers are in dollars and cents.");
        if (amount > MaxOfferIncGst)
            return ServiceResult<OfferDto>.BadRequest($"Offers can't be more than {MaxOfferIncGst:C0}.");

        try
        {
            return await _transactions.RunAsync(async () =>
            {
                var now = Now;
                // Re-read inside the transaction: the listing may just have closed or expired.
                var listing = await _listings.GetMakeAnOfferWithDetailsAsync(listingId);
                if (listing == null) return ServiceResult<OfferDto>.NotFound("Listing not found.");
                if (!IsOpen(listing, now) || listing.BuyerFeeIncGst is not { } fee)
                    return ServiceResult<OfferDto>.BadRequest("This listing is no longer taking offers.");
                if (amount < fee)
                    return ServiceResult<OfferDto>.BadRequest(
                        $"Offers must be at least {fee:C2}, the buyer fee, which forms part of the price.");
                if (await _offers.GetPendingAsync(listingId, caller.Id) != null)
                    return ServiceResult<OfferDto>.Conflict(
                        "You already have an offer with the stud — withdraw it to make a new one.");

                var settings = await _settings.GetAsync();
                var offer = new Offer
                {
                    ListingId = listingId, BuyerUserId = caller.Id, AmountIncGst = amount, Status = OfferStatus.Pending,
                    CreatedAt = now, ExpiresAt = now.AddDays(settings.OfferExpiryDays)
                };
                await _offers.AddAsync(offer);
                await _audit.LogAsync("Offer", offer.Id, "OfferMade", caller.Id,
                    JsonSerializer.Serialize(new { ListingId = listingId, offer.AmountIncGst, offer.ExpiresAt }));
                await _emails.OfferReceivedAsync(listing, offer, caller);
                return ServiceResult<OfferDto>.Created(ToBuyerDto(offer, listing, null, now));
            });
        }
        catch (DbUpdateException)
        {
            // The unique "one pending offer per buyer per listing" index caught a simultaneous second offer.
            return ServiceResult<OfferDto>.Conflict("You already have an offer with the stud — withdraw it to make a new one.");
        }
    }

    public async Task<ServiceResult> WithdrawAsync(Guid offerId)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult.Forbidden();

        var offer = await _offers.GetByIdAsync(offerId);
        if (offer == null) return ServiceResult.NotFound("Offer not found.");
        if (offer.BuyerUserId != caller.Id) return ServiceResult.Forbidden();
        if (offer.Status != OfferStatus.Pending) return ServiceResult.BadRequest("Only a pending offer can be withdrawn.");

        offer.Status = OfferStatus.Withdrawn;
        offer.RespondedAt = Now;
        try { await _offers.UpdateAsync(offer); }
        catch (DbUpdateConcurrencyException) { return ServiceResult.Conflict("This offer has just changed — refresh and try again."); }
        await _audit.LogAsync("Offer", offer.Id, "OfferWithdrawn", caller.Id);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<IReadOnlyList<OfferDto>>> GetMineAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<IReadOnlyList<OfferDto>>.Forbidden();

        var offers = await _offers.GetByBuyerAsync(caller.Id);
        var sales = await SalesByOfferAsync(offers);
        var now = Now;
        return ServiceResult<IReadOnlyList<OfferDto>>.Ok(offers
            .Select(o => ToBuyerDto(o, o.Listing, sales.GetValueOrDefault(o.Id), now)).ToList());
    }

    public async Task<ServiceResult<OfferDto?>> GetMyOfferForListingAsync(Guid listingId)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<OfferDto?>.Forbidden();

        var offer = await _offers.GetLatestForBuyerAsync(listingId, caller.Id);
        if (offer == null) return ServiceResult<OfferDto?>.Ok(null);
        var sales = await SalesByOfferAsync(new[] { offer });
        return ServiceResult<OfferDto?>.Ok(ToBuyerDto(offer, offer.Listing, sales.GetValueOrDefault(offer.Id), Now));
    }

    public async Task<ServiceResult<IReadOnlyList<StudOfferDto>>> GetForListingAsync(Guid listingId)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<IReadOnlyList<StudOfferDto>>.Forbidden();

        var listing = await _listings.GetMakeAnOfferWithDetailsAsync(listingId);
        if (listing == null) return ServiceResult<IReadOnlyList<StudOfferDto>>.NotFound("Listing not found.");
        if (!await CanManageAsync(caller, listing, allowStaff: true)) return ServiceResult<IReadOnlyList<StudOfferDto>>.Forbidden();

        var offers = (await _offers.GetForListingAsync(listingId))
            .Where(o => o.Status is OfferStatus.Pending or OfferStatus.Accepted).ToList();
        var sales = await SalesByOfferAsync(offers);
        return ServiceResult<IReadOnlyList<StudOfferDto>>.Ok(offers.Select(o =>
        {
            var sale = sales.GetValueOrDefault(o.Id);
            return new StudOfferDto
            {
                Id = o.Id, BuyerName = o.Buyer?.DisplayName ?? string.Empty, BuyerEmail = o.Buyer?.Email ?? string.Empty,
                AmountIncGst = o.AmountIncGst, Status = o.Status.ToString(), CreatedAt = o.CreatedAt,
                ExpiresAt = o.ExpiresAt, RespondedAt = o.RespondedAt,
                PurchaseId = sale?.Id, SaleStatus = sale?.Status.ToString(), ChargeDueBy = sale?.ChargeDueBy
            };
        }).ToList());
    }

    public async Task<ServiceResult> DeclineAsync(Guid offerId)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult.Forbidden();

        var offer = await _offers.GetByIdAsync(offerId);
        if (offer == null) return ServiceResult.NotFound("Offer not found.");
        if (!await CanManageAsync(caller, offer.Listing, allowStaff: false)) return ServiceResult.Forbidden();
        if (offer.Status != OfferStatus.Pending) return ServiceResult.BadRequest("Only a pending offer can be declined.");

        offer.Status = OfferStatus.Declined;
        offer.RespondedAt = Now;
        try { await _offers.UpdateAsync(offer); }
        catch (DbUpdateConcurrencyException) { return ServiceResult.Conflict("This offer has just changed — refresh and try again."); }
        await _audit.LogAsync("Offer", offer.Id, "OfferDeclined", caller.Id);
        await _emails.OfferDeclinedAsync(offer.Listing, offer, offer.Buyer);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<AcceptOfferResultDto>> AcceptAsync(Guid offerId)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<AcceptOfferResultDto>.Forbidden();

        var check = await _offers.GetByIdAsync(offerId);
        if (check == null) return ServiceResult<AcceptOfferResultDto>.NotFound("Offer not found.");
        if (!await CanManageAsync(caller, check.Listing, allowStaff: false)) return ServiceResult<AcceptOfferResultDto>.Forbidden();

        Purchase? sale;
        try
        {
            sale = await _transactions.RunAsync(async () =>
            {
                var now = Now;
                var offer = await _offers.GetByIdAsync(offerId);
                if (offer is not { Status: OfferStatus.Pending } || offer.ExpiresAt <= now
                    || !IsOpen(offer.Listing, now) || offer.Listing.BuyerFeeIncGst is not { } fee)
                    return null;

                offer.Status = OfferStatus.Accepted;
                offer.RespondedAt = now;
                await _offers.UpdateAsync(offer); // a concurrent accept/withdraw makes this throw

                var split = GstBreakdown.FromIncGst(fee);
                var purchase = new Purchase
                {
                    ListingId = offer.ListingId, BuyerUserId = offer.BuyerUserId, OfferId = offer.Id,
                    TotalPriceIncGst = offer.AmountIncGst,
                    BuyerFeeIncGst = split.IncGst, BuyerFeeExGst = split.ExGst, BuyerFeeGst = split.Gst,
                    BalancePayableToStudIncGst = offer.AmountIncGst - split.IncGst,
                    Status = PurchaseStatus.Pending, CreatedAt = now
                };
                await _purchases.AddAsync(purchase);
                await _audit.LogAsync("Offer", offer.Id, "OfferAccepted", caller.Id,
                    JsonSerializer.Serialize(new { offer.AmountIncGst, PurchaseId = purchase.Id }));
                return purchase;
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<AcceptOfferResultDto>.Conflict("This offer has just changed — refresh and try again.");
        }
        if (sale == null)
            return ServiceResult<AcceptOfferResultDto>.BadRequest("This offer can no longer be accepted (it has expired, been withdrawn or answered, or the listing has closed).");

        string charge;
        try
        {
            charge = (await _charger.ChargeAsync(sale.Id)).ToString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The attempt stays interrupted; the background job repeats it with the same key.
            _log.LogWarning(ex, "Buyer-fee charge for accepted offer {OfferId} (sale record {PurchaseId}) could not reach the provider", offerId, sale.Id);
            charge = "Processing";
        }

        var current = await _purchases.GetByIdAsync(sale.Id) ?? sale;
        return ServiceResult<AcceptOfferResultDto>.Ok(new AcceptOfferResultDto
        {
            OfferId = offerId, PurchaseId = sale.Id, Charge = charge, SaleStatus = current.Status.ToString(),
            ChargeDueBy = current.ChargeDueBy, LastChargeFailure = current.LastChargeFailure
        });
    }

    private async Task<bool> CanManageAsync(User caller, Listing listing, bool allowStaff)
    {
        if (allowStaff && caller.Role == UserRole.Staff) return true;
        if (caller.Role != UserRole.StudFarmAdmin) return false;
        var farm = await _farms.GetByUserIdAsync(caller.Id);
        return farm != null && farm.Id == listing.StudFarmId;
    }

    private async Task<Dictionary<Guid, Purchase>> SalesByOfferAsync(IEnumerable<Offer> offers)
    {
        var accepted = offers.Where(o => o.Status == OfferStatus.Accepted).Select(o => o.Id).ToList();
        if (accepted.Count == 0) return new();
        return (await _purchases.GetByOfferIdsAsync(accepted))
            .GroupBy(p => p.OfferId!.Value).ToDictionary(g => g.Key, g => g.First());
    }

    private static OfferDto ToBuyerDto(Offer o, MakeAnOfferListing listing, Purchase? sale, DateTime now) => new()
    {
        Id = o.Id, ListingId = o.ListingId,
        StallionName = listing.Stallion?.Name ?? string.Empty, SeasonName = listing.Season?.Name ?? string.Empty,
        StudFarmName = listing.StudFarm?.Name ?? string.Empty,
        AmountIncGst = o.AmountIncGst, Status = o.Status.ToString(), CreatedAt = o.CreatedAt, ExpiresAt = o.ExpiresAt,
        RespondedAt = o.RespondedAt, PurchaseId = sale?.Id, SaleStatus = sale?.Status.ToString(),
        ChargeDueBy = sale?.ChargeDueBy, ListingOpen = IsOpen(listing, now)
    };
}
```

```csharp
// src/Server/Controllers/OffersController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Offers;

namespace Stallions.Server.Controllers;

/// <summary>Private offers on Make an Offer listings.</summary>
[ApiController]
[Route("api")]
public class OffersController : ControllerBase
{
    private readonly IOfferService _offers;
    public OffersController(IOfferService offers) => _offers = offers;

    [HttpPost("listings/{id:guid}/offers")]
    [Authorize(Policy = "BuyerOnly")]
    public async Task<IActionResult> Make(Guid id, [FromBody] MakeOfferRequest request)
    {
        var r = await _offers.MakeAsync(id, request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("listings/{id:guid}/offers")]
    [Authorize(Policy = "StudFarmOrStaff")]
    public async Task<IActionResult> ForListing(Guid id)
    {
        var r = await _offers.GetForListingAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("listings/{id:guid}/my-offer")]
    [Authorize(Policy = "BuyerOnly")]
    public async Task<IActionResult> MyOffer(Guid id)
    {
        var r = await _offers.GetMyOfferForListingAsync(id);
        if (!r.Succeeded) return StatusCode(r.HttpStatusCode, r.Error);
        return r.Value == null ? NoContent() : Ok(r.Value);
    }

    [HttpGet("offers/mine")]
    [Authorize(Policy = "BuyerOnly")]
    public async Task<IActionResult> Mine()
    {
        var r = await _offers.GetMineAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("offers/{id:guid}/withdraw")]
    [Authorize(Policy = "BuyerOnly")]
    public async Task<IActionResult> Withdraw(Guid id)
    {
        var r = await _offers.WithdrawAsync(id);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("offers/{id:guid}/accept")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> Accept(Guid id)
    {
        var r = await _offers.AcceptAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("offers/{id:guid}/decline")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> Decline(Guid id)
    {
        var r = await _offers.DeclineAsync(id);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }
}
```

`Program.cs`: register `builder.Services.AddScoped<IOfferRepository, OfferRepository>();` (with the repositories) and
`builder.Services.AddScoped<IOfferService, OfferService>();` (with the services).

- [ ] **Step 7: Run green**, full build (0 warnings), full test run.

- [ ] **Step 8: Commit**

```bash
git add src/Shared/DTOs/Offers src/Shared/DTOs/Checkout/PurchaseDto.cs src/Server/Data/Repositories src/Server/Services/IOfferService.cs src/Server/Services/OfferService.cs src/Server/Services/PurchaseService.cs src/Server/Controllers/OffersController.cs src/Server/Email src/Server/Program.cs tests/Server.Tests/Services/OfferServiceTests.cs tests/Server.Tests/Services/PurchaseServiceTests.cs tests/Server.Tests/Email/AuctionEmailsTests.cs
git commit -m "feat: offers — make, withdraw, view, accept (buyer fee charged at once), decline"
```

---

### Task 6: Expiry — offers, Make an Offer listings, stud close

**Files:**
- Create: `src/Server/Sales/OfferExpirer.cs` (with `IOfferExpirer`)
- Modify: `src/Server/Auctions/AuctionCloseService.cs`, `src/Server/Auctions/AuctionServiceCollectionExtensions.cs`,
  `src/Server/Email/IAuctionEmails.cs`, `src/Server/Email/AuctionEmails.cs`, `src/Server/Services/ListingService.cs`
- Test: `tests/Server.Tests/Sales/OfferExpirerTests.cs`, `tests/Server.Tests/Auctions/AuctionCloseServiceTests.cs` (registration only),
  `tests/Server.Tests/Services/ListingServiceTests.cs` (`CreateSut` gains one argument; add tests), `tests/Server.Tests/Email/AuctionEmailsTests.cs` (add)

**Interfaces:**
- Consumes: Task 5 `IOfferRepository.GetIdsToExpireAsync`, `GetPendingIdsForListingAsync`; Task 4 `IListingRepository.GetMakeAnOfferIdsExpiredAsync`, `GetMakeAnOfferWithDetailsAsync`.
- Produces: `interface IOfferExpirer { Task<int> RunAsync(CancellationToken ct = default); Task ExpirePendingOffersAsync(Guid listingId); }`;
  `IAuctionEmails.OfferExpiredAsync(MakeAnOfferListing listing, Offer offer, User buyer, bool listingClosed)`;
  `ListingService` constructor gains `IOfferExpirer offerExpirer` (after `audit`).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Server.Tests/Sales/OfferExpirerTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Sales;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Sales;

public class OfferExpirerTests
{
    private readonly string _name = Guid.NewGuid().ToString();
    private readonly TestClock _clock = new();
    private readonly Mock<IAuctionEmails> _emails = new();
    private readonly Mock<IAuditLogRepository> _audit = new();

    private AppDbContext Db() => DbContextFactory.Create(_name);

    private OfferExpirer Sut()
    {
        var db = Db();
        return new OfferExpirer(new OfferRepository(db), new ListingRepository(db), _audit.Object, _emails.Object,
            new InlineTransactionRunner(), _clock, NullLogger<OfferExpirer>.Instance);
    }

    private (Guid ListingId, Guid OfferId) Seed(DateTime listingExpires, DateTime offerExpires, ListingStatus status = ListingStatus.Active)
    {
        using var db = Db();
        var auction = AuctionTestData.Auction(db, _clock.UtcNow.AddDays(-2), status: ListingStatus.Unsold);
        var listing = new MakeAnOfferListing
        {
            StallionId = auction.StallionId, SeasonId = auction.SeasonId, StudFarmId = auction.StudFarmId,
            ListingType = ListingType.MakeAnOffer, Status = status, BuyerFeeIncGst = 150m,
            SourceAuctionListingId = auction.Id, ExpiresAt = listingExpires
        };
        db.MakeAnOfferListings.Add(listing);
        var buyer = AuctionTestData.Buyer(db, "Offer Buyer");
        var offer = new Offer { ListingId = listing.Id, BuyerUserId = buyer.Id, AmountIncGst = 9000m,
            CreatedAt = _clock.UtcNow.AddDays(-1), ExpiresAt = offerExpires };
        db.Offers.Add(offer);
        db.SaveChanges();
        return (listing.Id, offer.Id);
    }

    [Fact]
    public async Task AnOfferPastItsExpiry_IsExpired_AndTheBuyerToldIt()
    {
        var (_, offerId) = Seed(_clock.UtcNow.AddDays(30), _clock.UtcNow.AddMinutes(-1));

        await Sut().RunAsync();

        (await Db().Offers.SingleAsync(o => o.Id == offerId)).Status.Should().Be(OfferStatus.Expired);
        _emails.Verify(e => e.OfferExpiredAsync(It.IsAny<MakeAnOfferListing>(), It.IsAny<Offer>(), It.IsAny<User>(), false), Times.Once);
    }

    [Fact]
    public async Task AListingPastTheSeasonEnd_IsExpired_WithItsPendingOffers()
    {
        var (listingId, offerId) = Seed(_clock.UtcNow.AddMinutes(-1), _clock.UtcNow.AddDays(5));

        await Sut().RunAsync();

        using var db = Db();
        var listing = await db.Listings.SingleAsync(l => l.Id == listingId);
        listing.Status.Should().Be(ListingStatus.Expired);
        listing.ClosedAt.Should().Be(_clock.UtcNow);
        (await db.Offers.SingleAsync(o => o.Id == offerId)).Status.Should().Be(OfferStatus.Expired);
        _emails.Verify(e => e.OfferExpiredAsync(It.IsAny<MakeAnOfferListing>(), It.IsAny<Offer>(), It.IsAny<User>(), true), Times.Once);
    }

    [Fact]
    public async Task PendingOfferOnAClosedListing_IsExpired()
    {
        var (_, offerId) = Seed(_clock.UtcNow.AddDays(30), _clock.UtcNow.AddDays(5), ListingStatus.Cancelled);

        await Sut().RunAsync();

        (await Db().Offers.SingleAsync(o => o.Id == offerId)).Status.Should().Be(OfferStatus.Expired);
    }

    [Fact]
    public async Task ExpirePendingOffers_ForOneListing_LeavesOthersAlone()
    {
        var (listingId, offerId) = Seed(_clock.UtcNow.AddDays(30), _clock.UtcNow.AddDays(5));
        var (_, otherOffer) = Seed(_clock.UtcNow.AddDays(30), _clock.UtcNow.AddDays(5));

        await Sut().ExpirePendingOffersAsync(listingId);

        (await Db().Offers.SingleAsync(o => o.Id == offerId)).Status.Should().Be(OfferStatus.Expired);
        (await Db().Offers.SingleAsync(o => o.Id == otherOffer)).Status.Should().Be(OfferStatus.Pending);
    }

    [Fact]
    public async Task AnOfferStillInDate_IsLeftAlone()
    {
        var (_, offerId) = Seed(_clock.UtcNow.AddDays(30), _clock.UtcNow.AddDays(5));

        (await Sut().RunAsync()).Should().Be(0);
        (await Db().Offers.SingleAsync(o => o.Id == offerId)).Status.Should().Be(OfferStatus.Pending);
    }
}
```

In `ListingServiceTests` add `private readonly Mock<IOfferExpirer> _offerExpirerMock = new();` (with
`using Stallions.Server.Sales;`), pass `_offerExpirerMock.Object` as the new last argument of `CreateSut()`, and add:

```csharp
    [Fact]
    public async Task ClosingAMakeAnOfferListing_ExpiresItsPendingOffers()
    {
        var (_, farm) = SignedInFarm();
        var mao = new MakeAnOfferListing { Id = Guid.NewGuid(), StudFarmId = farm.Id, Status = ListingStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddDays(10) };
        _listingRepoMock.Setup(r => r.GetByIdAsync(mao.Id)).ReturnsAsync(mao);

        (await CreateSut().CloseByStudFarmAsync(mao.Id)).Succeeded.Should().BeTrue();

        mao.Status.Should().Be(ListingStatus.Cancelled);
        _offerExpirerMock.Verify(x => x.ExpirePendingOffersAsync(mao.Id), Times.Once);
    }

    [Fact]
    public async Task ClosingAnAuction_DoesNotTouchOffers()
    {
        var (_, farm) = SignedInFarm();
        var auction = DraftAuction(farm.Id);
        auction.Status = ListingStatus.Active;
        _listingRepoMock.Setup(r => r.GetByIdAsync(auction.Id)).ReturnsAsync(auction);

        await CreateSut().CloseByStudFarmAsync(auction.Id);

        _offerExpirerMock.Verify(x => x.ExpirePendingOffersAsync(It.IsAny<Guid>()), Times.Never);
    }
```

In `AuctionCloseServiceTests.CreateSut`, register a no-op expirer next to the closer:
`var expirer = new Mock<IOfferExpirer>(); services.AddScoped(_ => expirer.Object);` (with `using Stallions.Server.Sales;`).

Add to `AuctionEmailsTests`:

```csharp
    [Fact]
    public async Task OfferExpired_SaysWhetherTheListingClosed()
    {
        await CreateSut().OfferExpiredAsync(OfferListing(), new Offer { AmountIncGst = 9000m }, new User { Email = "jane@example.com" }, listingClosed: true);
        await CreateSut().OfferExpiredAsync(OfferListing(), new Offer { AmountIncGst = 9000m }, new User { Email = "jane@example.com" }, listingClosed: false);

        _queued[0].TextBody.Should().Contain("no longer taking offers");
        _queued[1].TextBody.Should().Contain("expired");
    }
```

- [ ] **Step 2: Run red** — compile errors.

- [ ] **Step 3: Email** — `IAuctionEmails`: add
  `Task OfferExpiredAsync(MakeAnOfferListing listing, Offer offer, User buyer, bool listingClosed);` and in `AuctionEmails`:

```csharp
    public Task OfferExpiredAsync(MakeAnOfferListing listing, Offer offer, User buyer, bool listingClosed) =>
        QueueAsync(buyer.Email, "OfferExpired", $"Offer closed: {Name(listing)}", listing,
            Para(listingClosed
                ? $"The {Name(listing)} nomination ({Season(listing)}) is no longer taking offers, so your offer of {Money(offer.AmountIncGst)} has closed without a response."
                : $"Your offer of {Money(offer.AmountIncGst)} for the {Name(listing)} nomination ({Season(listing)}) has expired without a response from the stud."),
            Link($"/listings/{listing.Id}", "View the listing"));
```

- [ ] **Step 4: `OfferExpirer`**

```csharp
// src/Server/Sales/OfferExpirer.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Shared.Enums;

namespace Stallions.Server.Sales;

public interface IOfferExpirer
{
    /// <summary>Expires Make an Offer listings past their season end, then pending offers that are past their expiry or on a closed listing.</summary>
    Task<int> RunAsync(CancellationToken ct = default);
    /// <summary>After a stud closes (or Staff cancel) a Make an Offer listing.</summary>
    Task ExpirePendingOffersAsync(Guid listingId);
}

/// <summary>
/// Closes offers and Make an Offer listings that have run out of time. Each row changes in its own
/// transaction; a row another instance changed first is skipped (concurrency stamp).
/// </summary>
public class OfferExpirer : IOfferExpirer
{
    private const int BatchSize = 50;

    private readonly IOfferRepository _offers;
    private readonly IListingRepository _listings;
    private readonly IAuditLogRepository _audit;
    private readonly IAuctionEmails _emails;
    private readonly ITransactionRunner _transactions;
    private readonly TimeProvider _clock;
    private readonly ILogger<OfferExpirer> _log;

    public OfferExpirer(IOfferRepository offers, IListingRepository listings, IAuditLogRepository audit, IAuctionEmails emails,
        ITransactionRunner transactions, TimeProvider clock, ILogger<OfferExpirer> log)
    {
        _offers = offers; _listings = listings; _audit = audit; _emails = emails;
        _transactions = transactions; _clock = clock; _log = log;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var changed = 0;
        foreach (var id in await _listings.GetMakeAnOfferIdsExpiredAsync(Now, BatchSize))
        {
            ct.ThrowIfCancellationRequested();
            if (await TryAsync(() => ExpireListingAsync(id), "expire listing", id)) changed++;
        }
        foreach (var id in await _offers.GetIdsToExpireAsync(Now, BatchSize))
        {
            ct.ThrowIfCancellationRequested();
            if (await TryAsync(() => ExpireOfferAsync(id), "expire offer", id)) changed++;
        }
        return changed;
    }

    public async Task ExpirePendingOffersAsync(Guid listingId)
    {
        foreach (var id in await _offers.GetPendingIdsForListingAsync(listingId))
            await TryAsync(() => ExpireOfferAsync(id), "expire offer", id);
    }

    private async Task<bool> ExpireListingAsync(Guid listingId)
    {
        var expired = await _transactions.RunAsync(async () =>
        {
            var now = Now;
            var listing = await _listings.GetMakeAnOfferWithDetailsAsync(listingId);
            if (listing is not { Status: ListingStatus.Active } || listing.ExpiresAt > now) return false;
            listing.Status = ListingStatus.Expired;
            listing.ClosedAt = now;
            await _listings.UpdateAsync(listing);
            await _audit.LogAsync("Listing", listing.Id, "MakeAnOfferExpired", null);
            return true;
        });
        // Each pending offer then closes in its own transaction (and the buyer is told).
        if (expired) await ExpirePendingOffersAsync(listingId);
        return expired;
    }

    private Task<bool> ExpireOfferAsync(Guid offerId) => _transactions.RunAsync(async () =>
    {
        var now = Now;
        var offer = await _offers.GetByIdAsync(offerId);
        if (offer is not { Status: OfferStatus.Pending }) return false;
        var listingClosed = offer.Listing.Status != ListingStatus.Active || offer.Listing.ExpiresAt <= now;
        if (!listingClosed && offer.ExpiresAt > now) return false;

        offer.Status = OfferStatus.Expired;
        await _offers.UpdateAsync(offer);
        await _audit.LogAsync("Offer", offer.Id, "OfferExpired", null, JsonSerializer.Serialize(new { ListingClosed = listingClosed }));
        await _emails.OfferExpiredAsync(offer.Listing, offer, offer.Buyer, listingClosed);
        return true;
    });

    private async Task<bool> TryAsync(Func<Task<bool>> step, string what, Guid id)
    {
        try { return await step(); }
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
}
```

- [ ] **Step 5: Wire up** — `AddAuctionClosing`: `services.AddScoped<IOfferExpirer, OfferExpirer>();`.
  `AuctionCloseService.RunOnceAsync`: after the closer run,

```csharp
        var expired = await services.GetRequiredService<IOfferExpirer>().RunAsync(ct);
        if (expired > 0) _log.LogInformation("Offer expiry run: {Expired} offers or listings expired", expired);
```

  `ListingService`: constructor gains `IOfferExpirer offerExpirer` (field `_offerExpirer`, `using Stallions.Server.Sales;`).
  In `CloseByStudFarmAsync` and `CancelListingAsync`, after `await _listingRepo.UpdateAsync(listing);` add
  `if (listing is MakeAnOfferListing) await _offerExpirer.ExpirePendingOffersAsync(listing.Id);`.

- [ ] **Step 6: Run green**, full build (0 warnings), full test run.

- [ ] **Step 7: Commit**

```bash
git add src/Server/Sales/OfferExpirer.cs src/Server/Auctions src/Server/Email src/Server/Services/ListingService.cs tests/Server.Tests/Sales/OfferExpirerTests.cs tests/Server.Tests/Auctions/AuctionCloseServiceTests.cs tests/Server.Tests/Services/ListingServiceTests.cs tests/Server.Tests/Email/AuctionEmailsTests.cs
git commit -m "feat: expire offers and Make an Offer listings; closing a listing closes its offers"
```

---
### Task 7: Client — buyer side (listing page, offer form, cards, My Offers)

**Files:**
- Create: `src/Client/Services/OfferApiService.cs`, `src/Client/Components/Offers/OfferConfirmationModal.razor`,
  `src/Client/Pages/MyOffers.razor`, `src/Client/Pages/MyOffers.razor.css`
- Modify: `src/Client/Program.cs`, `src/Client/Pages/ListingDetail.razor`, `src/Client/Pages/ListingDetail.razor.css`,
  `src/Client/Components/Listings/ListingCard.razor`, `src/Client/Components/Listings/FilterBar.razor`,
  `src/Client/Layout/NavBar.razor`, `src/Client/_Imports.razor`
- Test: `tests/Client.Tests/Pages/ListingDetailTests.cs` (register the new service; add tests), `tests/Client.Tests/Pages/MyOffersTests.cs`,
  `tests/Client.Tests/Components/ListingCardTests.cs` (create if absent)

**Interfaces:**
- Consumes: Task 4 `MakeAnOfferListingDto` (`ExpiresAt`), `ListingCardDto.OffersUntil`; Task 5 routes and DTOs (`OfferDto`, `MakeOfferRequest`).
- Produces: `OfferApiService` (virtual methods): `MakeOfferAsync(Guid listingId, decimal amount) : Task<OfferDto>`,
  `WithdrawAsync(Guid offerId)`, `GetMineAsync() : Task<List<OfferDto>>`, `GetMyOfferAsync(Guid listingId) : Task<OfferDto?>`,
  `GetForListingAsync(Guid listingId) : Task<List<StudOfferDto>>`, `AcceptAsync(Guid offerId) : Task<AcceptOfferResultDto>`,
  `DeclineAsync(Guid offerId)` (the stud methods are used in Task 8).

- [ ] **Step 1: `OfferApiService`**

```csharp
// src/Client/Services/OfferApiService.cs
using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Offers;

namespace Stallions.Client.Services;

/// <summary>Private offers on Make an Offer listings (buyer and stud calls).</summary>
public class OfferApiService
{
    private readonly HttpClient _http;
    public OfferApiService(HttpClient http) => _http = http;

    public virtual async Task<OfferDto> MakeOfferAsync(Guid listingId, decimal amount)
    {
        var r = await _http.PostAsJsonAsync($"api/listings/{listingId}/offers", new MakeOfferRequest { AmountIncGst = amount });
        await ThrowIfFailed(r, "Your offer could not be sent.");
        return await r.Content.ReadFromJsonAsync<OfferDto>() ?? throw new ApiException(500, "Empty response from server.");
    }

    public virtual async Task WithdrawAsync(Guid offerId) =>
        await ThrowIfFailed(await _http.PostAsync($"api/offers/{offerId}/withdraw", null), "Your offer could not be withdrawn.");

    public virtual async Task<List<OfferDto>> GetMineAsync()
    {
        var r = await _http.GetAsync("api/offers/mine");
        await ThrowIfFailed(r, "Failed to load your offers.");
        return await r.Content.ReadFromJsonAsync<List<OfferDto>>() ?? new List<OfferDto>();
    }

    /// <summary>The buyer's latest offer on the listing, or null when they have none.</summary>
    public virtual async Task<OfferDto?> GetMyOfferAsync(Guid listingId)
    {
        var r = await _http.GetAsync($"api/listings/{listingId}/my-offer");
        if (r.StatusCode == HttpStatusCode.NoContent) return null;
        await ThrowIfFailed(r, "Failed to load your offer.");
        return await r.Content.ReadFromJsonAsync<OfferDto>();
    }

    public virtual async Task<List<StudOfferDto>> GetForListingAsync(Guid listingId)
    {
        var r = await _http.GetAsync($"api/listings/{listingId}/offers");
        await ThrowIfFailed(r, "Failed to load offers.");
        return await r.Content.ReadFromJsonAsync<List<StudOfferDto>>() ?? new List<StudOfferDto>();
    }

    public virtual async Task<AcceptOfferResultDto> AcceptAsync(Guid offerId)
    {
        var r = await _http.PostAsync($"api/offers/{offerId}/accept", null);
        await ThrowIfFailed(r, "The offer could not be accepted.");
        return await r.Content.ReadFromJsonAsync<AcceptOfferResultDto>() ?? throw new ApiException(500, "Empty response from server.");
    }

    public virtual async Task DeclineAsync(Guid offerId) =>
        await ThrowIfFailed(await _http.PostAsync($"api/offers/{offerId}/decline", null), "The offer could not be declined.");

    private static async Task ThrowIfFailed(HttpResponseMessage r, string fallback)
    {
        if (r.IsSuccessStatusCode) return;
        throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r) is { Length: > 0 } msg ? msg : fallback);
    }
}
```

(If `ServiceHelpers.ExtractErrorMessageAsync` has a different signature, use it the way `PaymentsApiService` does.)
`Program.cs` — with the other authenticated clients:

```csharp
builder.Services.AddHttpClient<OfferApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
```

Add `@using Stallions.Shared.DTOs.Offers` and `@using Stallions.Client.Components.Offers` to `_Imports.razor`.

- [ ] **Step 2: Write the failing tests**

In `ListingDetailTests.RegisterServices` add a parameter `OfferDto? myOffer = null`, register the mock, and add the tests:

```csharp
        var offerMock = new Mock<OfferApiService>(MockBehavior.Loose, new HttpClient { BaseAddress = new Uri("https://localhost/") });
        offerMock.Setup(s => s.GetMyOfferAsync(listing.Id)).ReturnsAsync(myOffer);
        Services.AddSingleton(offerMock.Object);
        _offerMock = offerMock;
```

(with a field `private Mock<OfferApiService>? _offerMock;` and `using Stallions.Shared.DTOs.Offers;`)

```csharp
    private static MakeAnOfferListingDto OfferListing(int daysLeft = 30) => new()
    {
        Id = Guid.NewGuid(), StallionName = "Dundeel (NZ)", StudFarmName = "Arrowfield", Status = "Active",
        ExpiresAt = DateTime.UtcNow.AddDays(daysLeft), BuyerFeeIncGst = 150m
    };

    [Fact]
    public void AMakeAnOfferListing_ShowsTheOfferForm_AndNoPrice()
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        var listing = OfferListing();
        RegisterServices(listing, role: "Buyer", card: new SavedCardDto { IsValid = true });

        var cut = RenderComponent<ListingDetail>(p => p.Add(c => c.Id, listing.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Make an offer"));
        cut.Markup.Should().Contain("Minimum offer").And.Contain("$150")
            .And.NotContain("Place bid").And.NotContain("current high bid");
    }

    [Fact]
    public void WithAPendingOffer_TheBuyerSeesItAndCanWithdraw()
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        var listing = OfferListing();
        var pending = new OfferDto { Id = Guid.NewGuid(), ListingId = listing.Id, AmountIncGst = 9000m, Status = "Pending",
            ExpiresAt = DateTime.UtcNow.AddDays(7), ListingOpen = true };
        RegisterServices(listing, role: "Buyer", card: new SavedCardDto { IsValid = true }, myOffer: pending);

        var cut = RenderComponent<ListingDetail>(p => p.Add(c => c.Id, listing.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Your offer of $9,000 is with the stud"));
        cut.FindAll("button").Single(b => b.TextContent.Contains("Withdraw")).Click();
        _offerMock!.Verify(s => s.WithdrawAsync(pending.Id), Times.Once);
    }

    [Fact]
    public void MakingAnOffer_AsksForConfirmationWithTheDisclosure_ThenSendsIt()
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        var listing = OfferListing();
        RegisterServices(listing, role: "Buyer", card: new SavedCardDto { IsValid = true });
        _offerMock!.Setup(s => s.MakeOfferAsync(listing.Id, 9000m)).ReturnsAsync(new OfferDto { Id = Guid.NewGuid(),
            AmountIncGst = 9000m, Status = "Pending", ExpiresAt = DateTime.UtcNow.AddDays(7), ListingOpen = true });
        var cut = RenderComponent<ListingDetail>(p => p.Add(c => c.Id, listing.Id));
        cut.WaitForAssertion(() => cut.Find("input.offer-amount"));

        cut.Find("input.offer-amount").Change("9000");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Make an offer").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Confirm your offer"));
        cut.Markup.Should().Contain("CONFIGURED: the buyer fee forms part of the price.")
            .And.Contain("CONFIGURED: pay the stud directly.").And.Contain("$8,850");
        cut.FindAll("button").Single(b => b.TextContent.Contains("Send offer")).Click();
        _offerMock.Verify(s => s.MakeOfferAsync(listing.Id, 9000m), Times.Once);
    }

    [Fact]
    public void AnExpiredMakeAnOfferListing_TakesNoOffers()
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        var listing = OfferListing();
        listing.Status = "Expired";
        RegisterServices(listing, role: "Buyer", card: new SavedCardDto { IsValid = true });

        var cut = RenderComponent<ListingDetail>(p => p.Add(c => c.Id, listing.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No longer taking offers"));
        cut.FindAll("input.offer-amount").Should().BeEmpty();
    }
```

```csharp
// tests/Client.Tests/Pages/MyOffersTests.cs
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Offers;

namespace Stallions.Client.Tests.Pages;

public class MyOffersTests : TestContext
{
    [Fact]
    public void ListsOffersWithStatus_AndLinksAcceptedOnesToTheSaleRecord()
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        var api = new Mock<OfferApiService>(MockBehavior.Loose, new HttpClient { BaseAddress = new Uri("https://localhost/") });
        api.Setup(a => a.GetMineAsync()).ReturnsAsync(new List<OfferDto>
        {
            new() { Id = Guid.NewGuid(), StallionName = "Dundeel (NZ)", AmountIncGst = 9000m, Status = "Pending", ExpiresAt = DateTime.UtcNow.AddDays(5), ListingOpen = true },
            new() { Id = Guid.NewGuid(), StallionName = "Maurice (JPN)", AmountIncGst = 8000m, Status = "Accepted", SaleStatus = "Completed", PurchaseId = Guid.NewGuid() },
            new() { Id = Guid.NewGuid(), StallionName = "Snitzel", AmountIncGst = 7000m, Status = "Declined" }
        });
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<MyOffers>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Dundeel (NZ)"));
        cut.Markup.Should().Contain("With the stud").And.Contain("Accepted — paid").And.Contain("Declined");
        cut.Find("a[href='/my-purchases']").Should().NotBeNull();
        cut.FindAll("button").Count(b => b.TextContent.Contains("Withdraw")).Should().Be(1);
    }
}
```

```csharp
// tests/Client.Tests/Components/ListingCardTests.cs   (add the test to the file if it already exists)
using Bunit;
using FluentAssertions;
using Stallions.Client.Components.Listings;
using Stallions.Shared.DTOs.Listings;

namespace Stallions.Client.Tests.Components;

public class ListingCardTests : TestContext
{
    [Fact]
    public void AMakeAnOfferCard_ShowsTheBadgeAndButton_AndNoBidInfo()
    {
        var cut = RenderComponent<ListingCard>(p => p.Add(c => c.Listing, new ListingCardDto
        {
            Id = Guid.NewGuid(), ListingType = "MakeAnOffer", StallionName = "Dundeel (NZ)",
            StudFarmName = "Arrowfield", OffersUntil = DateTime.UtcNow.AddDays(30)
        }));

        cut.Markup.Should().Contain("Make an Offer").And.Contain("Make an offer").And.Contain("Open to offers")
            .And.NotContain("No bids yet").And.NotContain("Place a bid");
    }
}
```

(If `ListingCard` injects services beyond `NavigationManager`, register the same mocks the Home page tests use.)

- [ ] **Step 3: Run red** — `dotnet test tests/Client.Tests` → failures.

- [ ] **Step 4: `OfferConfirmationModal`**

```razor
@* src/Client/Components/Offers/OfferConfirmationModal.razor *@
<Modal IsOpen="true" Title="Confirm your offer" CloseOnBackdrop="false" OnClose="Cancel">
    <div class="bid-confirm-body">
        <p>You're offering <strong>@AmountIncGst.ToString("C0", AuCulture)</strong> inc. GST for this nomination.
           Only the stud sees your offer. If the stud accepts it, this is binding.</p>
        @if (BuyerFeeIncGst is decimal fee)
        {
            <p>If accepted, the buyer fee of <strong>@fee.ToString("C0", AuCulture)</strong> inc. GST is charged to your
               saved card straight away, and you pay the stud the balance of
               <strong>@((AmountIncGst - fee).ToString("C0", AuCulture))</strong> directly.</p>
        }
        @if (!string.IsNullOrWhiteSpace(BuyerFeeExplanation))
        {
            <p class="text-secondary text-sm">@BuyerFeeExplanation</p>
        }
        @if (!string.IsNullOrWhiteSpace(BalanceArrangement))
        {
            <p class="text-secondary text-sm">@BalanceArrangement</p>
        }
    </div>
    <div class="bid-confirm-actions">
        <button class="btn btn-outline" @onclick="Cancel" disabled="@_busy">Cancel</button>
        <button class="btn btn-gold" @onclick="Confirm" disabled="@_busy">@(_busy ? "Sending…" : "Send offer")</button>
    </div>
</Modal>

@code {
    [Parameter] public decimal AmountIncGst { get; set; }
    /// <summary>The listing's snapshotted buyer fee (inc. GST).</summary>
    [Parameter] public decimal? BuyerFeeIncGst { get; set; }
    /// <summary>Configured disclosure wording — never hardcoded here.</summary>
    [Parameter] public string? BuyerFeeExplanation { get; set; }
    [Parameter] public string? BalanceArrangement { get; set; }
    [Parameter] public EventCallback OnConfirm { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private static readonly System.Globalization.CultureInfo AuCulture = System.Globalization.CultureInfo.GetCultureInfo("en-AU");
    private bool _busy;

    private async Task Confirm()
    {
        _busy = true;
        await OnConfirm.InvokeAsync();
        _busy = false;
    }

    private Task Cancel() => OnCancel.InvokeAsync();
}
```

- [ ] **Step 5: Listing page** (`ListingDetail.razor`)

Add `@inject OfferApiService OfferApi`. In `Load()`, change `if (_listing is AuctionListingDto)` so Make an Offer
listings also load the disclosure and card, plus the buyer's offer:

```csharp
            if (_listing is AuctionListingDto)
            {
                await LoadBidHistoryAsync();
                await LoadDisclosureAsync();
                await LoadCardAsync();
                if (_listing is AuctionListingDto a && !IsOpen(a)) await LoadMyResultAsync();
            }
            else if (_listing is MakeAnOfferListingDto)
            {
                await LoadDisclosureAsync();
                await LoadCardAsync();
                await LoadMyOfferAsync();
            }
```

Hide the auction price block for Make an Offer listings: wrap the existing `<div class="listing-price-block">…</div>` in
`@if (_listing is AuctionListingDto) { … }`. Change the hero badge to
`<span class="badge badge-auction">@(_listing is MakeAnOfferListingDto ? "Make an Offer" : _listing.ListingType)</span>`.

After the `@if (_listing is AuctionListingDto auction) { … }` block, add:

```razor
            @if (_listing is MakeAnOfferListingDto offerListing)
            {
                <div class="listing-auction-block">
                    <div class="auction-meta">
                        <span class="text-secondary text-sm">Make an Offer — no asking price; the stud reviews each offer</span>
                        <span class="text-muted text-sm">Offers close @FormatLocal(offerListing.ExpiresAt)</span>
                    </div>

                    @if (offerListing.BuyerFeeIncGst is decimal offerFee)
                    {
                        <div class="buyer-fee-notice">
                            <p class="buyer-fee-amount">
                                Buyer fee: <strong>@FormatPrice(offerFee)</strong> inc. GST, paid to Stallions Australia if your offer is accepted
                            </p>
                            @if (!string.IsNullOrWhiteSpace(_disclosure?.BuyerFeeExplanation))
                            {
                                <p class="text-secondary text-sm">@_disclosure.BuyerFeeExplanation</p>
                            }
                        </div>
                    }

                    @if (!IsTakingOffers(offerListing))
                    {
                        <div class="auction-closed" role="status">
                            <p class="auction-closed-title">No longer taking offers</p>
                        </div>
                    }
                    else
                    {
                        <AuthorizeView>
                            <Authorized>
                                @if (UserState.IsBuyer && UserState.IsVerified && !_cardChecked)
                                {
                                    <p class="text-muted text-sm" role="status" aria-live="polite">Checking your payment card…</p>
                                }
                                else if (UserState.IsBuyer && UserState.IsVerified && _cardCheckFailed)
                                {
                                    <p class="text-muted text-sm" role="status" aria-live="polite">We couldn't check your payment card — refresh to try again.</p>
                                }
                                else if (UserState.IsBuyer && UserState.IsVerified && _card is not { IsValid: true })
                                {
                                    <a href="/account/card" class="btn btn-gold btn-full">Save a card to make an offer</a>
                                }
                                else if (UserState.IsBuyer && UserState.IsVerified)
                                {
                                    @if (_myOffer is { Status: "Pending" } pending)
                                    {
                                        <div class="offer-pending" role="status">
                                            <p>Your offer of @FormatPrice(pending.AmountIncGst) is with the stud until @FormatLocal(pending.ExpiresAt).</p>
                                            <button class="btn btn-outline" @onclick="WithdrawOffer" disabled="@_offerBusy">Withdraw</button>
                                        </div>
                                    }
                                    else
                                    {
                                        @if (_myOffer is not null)
                                        {
                                            <p class="text-muted text-sm" role="status">@OfferOutcomeText(_myOffer)
                                                @if (_myOffer is { Status: "Accepted", SaleStatus: "Completed" })
                                                {
                                                    <a href="/my-purchases"> View your sale record</a>
                                                }
                                                else if (_myOffer is { Status: "Accepted", SaleStatus: "Pending", ChargeDueBy: not null })
                                                {
                                                    <a href="/account/card"> Update your card</a>
                                                }
                                            </p>
                                        }
                                        <div class="bid-form">
                                            <p class="bid-form-hint">Minimum offer: <strong>@FormatPrice(offerListing.BuyerFeeIncGst ?? 0m)</strong></p>
                                            <div class="bid-form-row">
                                                <input class="form-input offer-amount" type="number" step="0.01"
                                                       min="@(offerListing.BuyerFeeIncGst ?? 0m)"
                                                       @bind="_offerAmount" placeholder="Your offer (inc. GST)" />
                                                <button class="btn btn-gold" @onclick="StartOffer" disabled="@_offerBusy">Make an offer</button>
                                            </div>
                                            @if (_offerError is not null) { <ErrorMessage Message="@_offerError" /> }
                                        </div>
                                    }
                                }
                                else if (UserState.IsBuyer && UserState.IsPendingVerification)
                                {
                                    <div class="pending-verification-notice">
                                        <p><strong>Account verification pending</strong></p>
                                        <p>You'll be able to make offers once your account has been verified.</p>
                                    </div>
                                }
                            </Authorized>
                            <NotAuthorized>
                                <a href="authentication/login" class="btn btn-gold btn-full">Sign in to make an offer</a>
                            </NotAuthorized>
                        </AuthorizeView>
                    }
                </div>
            }
```

Next to the bid confirmation modal:

```razor
@if (_showOfferConfirm && _listing is MakeAnOfferListingDto confirmListing)
{
    <OfferConfirmationModal AmountIncGst="@_offerAmount" BuyerFeeIncGst="@confirmListing.BuyerFeeIncGst"
                            BuyerFeeExplanation="@_disclosure?.BuyerFeeExplanation"
                            BalanceArrangement="@_disclosure?.BalanceArrangement"
                            OnConfirm="SendOffer" OnCancel="() => _showOfferConfirm = false" />
}
```

In `@code`:

```csharp
    private OfferDto? _myOffer;
    private decimal _offerAmount;
    private bool _offerBusy;
    private bool _showOfferConfirm;
    private string? _offerError;

    private static bool IsTakingOffers(MakeAnOfferListingDto listing) =>
        listing.Status == "Active" && AsUtc(listing.ExpiresAt) > DateTime.UtcNow;

    // The buyer's latest offer here. Secondary content: failures are ignored.
    private async Task LoadMyOfferAsync()
    {
        _myOffer = null;
        if (!UserState.IsBuyer) return;
        try { _myOffer = await OfferApi.GetMyOfferAsync(Id); }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or AccessTokenNotAvailableException) { }
    }

    private void StartOffer()
    {
        _offerError = null;
        var min = (_listing as MakeAnOfferListingDto)?.BuyerFeeIncGst ?? 0m;
        if (_offerAmount < min) { _offerError = $"Offers must be at least {FormatPrice(min)}, the buyer fee."; return; }
        _showOfferConfirm = true;
    }

    private async Task SendOffer()
    {
        _showOfferConfirm = false;
        _offerBusy = true;
        try { _myOffer = await OfferApi.MakeOfferAsync(Id, _offerAmount); }
        catch (ApiException ex) { _offerError = ex.Message; }
        finally { _offerBusy = false; }
    }

    private async Task WithdrawOffer()
    {
        if (_myOffer is null) return;
        _offerBusy = true;
        try
        {
            await OfferApi.WithdrawAsync(_myOffer.Id);
            _myOffer.Status = "Withdrawn";
        }
        catch (ApiException ex) { _offerError = ex.Message; }
        finally { _offerBusy = false; }
    }

    private static string OfferOutcomeText(OfferDto o) => (o.Status, o.SaleStatus) switch
    {
        ("Declined", _) => "The stud declined your last offer.",
        ("Expired", _) => "Your last offer closed without a response.",
        ("Withdrawn", _) => "You withdrew your last offer.",
        ("Accepted", "Completed") => "The stud accepted your offer and the buyer fee has been paid.",
        ("Accepted", "Pending") when o.ChargeDueBy is not null => $"The stud accepted your offer, but the buyer fee payment failed — update your card by {FormatLocal(o.ChargeDueBy)}.",
        ("Accepted", "Pending") => "The stud accepted your offer; we're charging the buyer fee.",
        ("Accepted", "Voided") => "The stud accepted your offer, but the buyer fee wasn't paid in time, so there was no sale.",
        _ => string.Empty
    };
```

`ListingDetail.razor.css`: add `.offer-pending { display:flex; gap:var(--space-3); align-items:center; flex-wrap:wrap; }`.

- [ ] **Step 6: Cards, filter, nav, My Offers**

`ListingCard.razor`:
- price area: when `Listing.ListingType == "MakeAnOffer"`, show `<span class="card-gst-label">Open to offers</span>` instead of the bid/“No bids yet” text;
- below the progress bar: `@if (Listing.ListingType == "MakeAnOffer" && Listing.OffersUntil.HasValue) { <span class="card-meta">Offers until @Listing.OffersUntil.Value.ToLocalTime().ToString("d MMM yyyy")</span> }`, and hide the progress bar for Make an Offer;
- button text: `@(Listing.ListingType switch { "Auction" => "Place a bid", "MakeAnOffer" => "Make an offer", _ => "View listing" })`;
- `GetBadgeText()`: `"MakeAnOffer" => "Make an Offer"`; `GetBadgeClass()`: `"MakeAnOffer" => "badge-offer"`; add to `app.css`
  next to `.badge-auction`: `.badge-offer { background: var(--forest, #1f4d3a); color: var(--white); }` (use the
  existing colour token closest to the site's green if `--forest` doesn't exist).

`FilterBar.razor`: add a pill after "Auctions":

```razor
        <button class="filter-pill @(ActiveFilter == "MakeAnOffer" ? "active" : "")"
                @onclick="@(() => SetFilter("MakeAnOffer"))">Make an Offer</button>
```

`NavBar.razor`: inside the `@if (UserState.IsBuyer)` block (desktop and mobile drawer), add
`<NavLink href="/my-offers" class="nav-link">My Offers</NavLink>` before "Payment card".

```razor
@* src/Client/Pages/MyOffers.razor *@
@page "/my-offers"
@attribute [Authorize]
@inject OfferApiService OfferApi

<PageTitle>My Offers — Stallions Australia</PageTitle>

<div class="container" style="padding-block: var(--space-10)">
    <h1 class="page-title">My Offers</h1>

    @if (_error is not null)
    {
        <ErrorMessage Message="@_error" OnRetry="Load" />
    }
    else if (_offers is null)
    {
        <LoadingSpinner />
    }
    else if (!_offers.Any())
    {
        <EmptyState Message="No offers yet" Icon="✉">
            <a href="/" class="btn btn-primary">Browse nominations</a>
        </EmptyState>
    }
    else
    {
        <div class="offers-list">
            @foreach (var o in _offers)
            {
                <div class="offer-row">
                    <div class="offer-info">
                        <a href="/listings/@o.ListingId" class="offer-stallion">@o.StallionName</a>
                        <div class="text-muted text-sm">@o.SeasonName · @o.StudFarmName</div>
                    </div>
                    <div class="offer-amount">@o.AmountIncGst.ToString("C0", AuCulture)</div>
                    <div class="offer-status">
                        <span class="badge @Badge(o)">@Label(o)</span>
                        @if (o.Status == "Pending")
                        {
                            <div class="text-muted text-xs">Until @o.ExpiresAt.ToLocalTime().ToString("d MMM, h:mm tt")</div>
                            <button class="btn btn-sm btn-outline" @onclick="() => Withdraw(o)" disabled="@_busy">Withdraw</button>
                        }
                        @if (o.Status == "Accepted")
                        {
                            <a href="/my-purchases" class="btn btn-sm btn-outline">Sale record</a>
                        }
                    </div>
                </div>
            }
        </div>
    }
</div>

@code {
    private List<OfferDto>? _offers;
    private string? _error;
    private bool _busy;
    private static readonly System.Globalization.CultureInfo AuCulture = System.Globalization.CultureInfo.GetCultureInfo("en-AU");

    protected override async Task OnInitializedAsync() => await Load();

    private async Task Load()
    {
        _error = null;
        _offers = null;
        try { _offers = await OfferApi.GetMineAsync(); }
        catch (ApiException) { _error = "Failed to load your offers. Please try again."; }
    }

    private async Task Withdraw(OfferDto offer)
    {
        _busy = true;
        try { await OfferApi.WithdrawAsync(offer.Id); offer.Status = "Withdrawn"; }
        catch (ApiException ex) { _error = ex.Message; }
        finally { _busy = false; }
    }

    private static string Label(OfferDto o) => (o.Status, o.SaleStatus) switch
    {
        ("Pending", _) => "With the stud",
        ("Accepted", "Completed") => "Accepted — paid",
        ("Accepted", "Pending") when o.ChargeDueBy is not null => "Accepted — payment failed",
        ("Accepted", "Pending") => "Accepted — charging",
        ("Accepted", "Voided") => "Accepted — no sale",
        _ => o.Status
    };

    private static string Badge(OfferDto o) => o.Status switch
    {
        "Pending" => "badge-auction",
        "Accepted" => o.SaleStatus == "Completed" ? "badge-won" : "badge-ending",
        _ => "badge-lost"
    };
}
```

`MyOffers.razor.css`: rows laid out like `MyBids` (copy the `.bid-row` rules from `MyBids.razor.css`, renamed to `.offer-row`,
`.offer-info`, `.offer-amount`, `.offer-status`; stack on narrow screens via a global media rule if needed — scoped `::deep`
doesn't work inside `@media`).

- [ ] **Step 7: Run green** — `dotnet test tests/Client.Tests`, then full build (0 warnings) and full test run.

- [ ] **Step 8: Commit**

```bash
git add src/Client tests/Client.Tests
git status --short   # only intended files
git commit -m "feat: client — Make an Offer listing page, offer form with disclosure, cards, My Offers"
```

---

### Task 8: Client — stud side (convert, offers panel, accept/decline)

**Files:**
- Create: `src/Client/Components/Offers/StudOffersPanel.razor`, `src/Client/Components/ListingTypeText.cs`
- Modify: `src/Client/Services/AdminApiService.cs`, `src/Client/Pages/Admin/AdminListingDetail.razor`,
  `src/Client/Pages/Admin/AdminListings.razor`, `src/Client/Pages/Staff/StaffListings.razor`, `src/Client/wwwroot/css/app.css`
- Test: `tests/Client.Tests/Components/StudOffersPanelTests.cs`, `tests/Client.Tests/Pages/AdminListingDetailTests.cs` (add),
  `tests/Client.Tests/Components/ListingTypeTextTests.cs`

**Interfaces:**
- Consumes: Task 4 route `POST api/listings/{id}/make-an-offer`, `AuctionListingDto.MakeAnOfferListingId`, `MakeAnOfferListingDto`;
  Task 7 `OfferApiService.GetForListingAsync/AcceptAsync/DeclineAsync`.
- Produces: `AdminApiService.ConvertToMakeAnOfferAsync(Guid auctionId) : Task<ListingDto>`; `ListingTypeText.Label(string type)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Client.Tests/Components/ListingTypeTextTests.cs
using FluentAssertions;
using Stallions.Client.Components;

namespace Stallions.Client.Tests.Components;

public class ListingTypeTextTests
{
    [Theory]
    [InlineData("Auction", "Auction")]
    [InlineData("MakeAnOffer", "Make an Offer")]
    public void Label(string type, string expected) => ListingTypeText.Label(type).Should().Be(expected);
}
```

```csharp
// tests/Client.Tests/Components/StudOffersPanelTests.cs
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Components.Offers;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Offers;

namespace Stallions.Client.Tests.Components;

public class StudOffersPanelTests : TestContext
{
    private readonly Mock<OfferApiService> _api = new(MockBehavior.Loose, new HttpClient { BaseAddress = new Uri("https://localhost/") });
    private readonly Guid _listingId = Guid.NewGuid();
    private readonly StudOfferDto _pending = new()
    {
        Id = Guid.NewGuid(), BuyerName = "Jane Buyer", BuyerEmail = "jane@example.com", AmountIncGst = 9000m,
        Status = "Pending", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(7)
    };

    private IRenderedComponent<StudOffersPanel> Render(params StudOfferDto[] offers)
    {
        _api.Setup(a => a.GetForListingAsync(_listingId)).ReturnsAsync(offers.ToList());
        Services.AddSingleton(_api.Object);
        return RenderComponent<StudOffersPanel>(p => p.Add(c => c.ListingId, _listingId).Add(c => c.BuyerFeeIncGst, 150m));
    }

    [Fact]
    public void ShowsPendingOffers_WithBuyerAndAmount()
    {
        var cut = Render(_pending);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Jane Buyer"));
        cut.Markup.Should().Contain("jane@example.com").And.Contain("$9,000");
    }

    [Fact]
    public void Accept_AsksForConfirmation_ThenReportsThePayment()
    {
        _api.Setup(a => a.AcceptAsync(_pending.Id)).ReturnsAsync(new AcceptOfferResultDto { OfferId = _pending.Id, Charge = "Paid", SaleStatus = "Completed" });
        var cut = Render(_pending);
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Accept"));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Accept").Click();
        cut.Markup.Should().Contain("charges the buyer the $150 buyer fee");
        cut.FindAll("button").Single(b => b.TextContent.Contains("Yes, accept")).Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Accepted — buyer fee paid"));
        _api.Verify(a => a.AcceptAsync(_pending.Id), Times.Once);
    }

    [Fact]
    public void Accept_WhenThePaymentFails_ShowsTheDeadline()
    {
        _api.Setup(a => a.AcceptAsync(_pending.Id)).ReturnsAsync(new AcceptOfferResultDto
        {
            OfferId = _pending.Id, Charge = "PaymentFailed", SaleStatus = "Pending", ChargeDueBy = DateTime.UtcNow.AddHours(2)
        });
        var cut = Render(_pending);
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Accept"));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Accept").Click();
        cut.FindAll("button").Single(b => b.TextContent.Contains("Yes, accept")).Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("payment failed").And.Contain("the buyer has until"));
    }

    [Fact]
    public void Decline_DeclinesTheOffer()
    {
        var cut = Render(_pending);
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Decline"));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Decline").Click();

        _api.Verify(a => a.DeclineAsync(_pending.Id), Times.Once);
    }
}
```

In `AdminListingDetailTests`, add a general render helper and three tests (the page now also needs an `OfferApiService`
for the offers panel; add `using Microsoft.AspNetCore.Components;` and `using Stallions.Shared.DTOs.Offers;`):

```csharp
    private Mock<AdminApiService> RenderWith(ListingDto listing, out IRenderedComponent<AdminListingDetail> cut)
    {
        this.AddTestAuthorization().SetAuthorized("stud@example.com");
        var http = new HttpClient { BaseAddress = new Uri("https://localhost/") };

        var adminMock = new Mock<AdminApiService>(MockBehavior.Loose, http);
        adminMock.Setup(s => s.GetListingAsync(listing.Id)).ReturnsAsync(listing);
        Services.AddSingleton(adminMock.Object);

        var subsMock = new Mock<SubscriptionApiService>(MockBehavior.Loose, http);
        subsMock.Setup(s => s.GetMineAsync()).ReturnsAsync(new List<SubscriptionDto>());
        Services.AddSingleton(subsMock.Object);

        var offersMock = new Mock<OfferApiService>(MockBehavior.Loose, http);
        offersMock.Setup(s => s.GetForListingAsync(It.IsAny<Guid>())).ReturnsAsync(new List<StudOfferDto>());
        Services.AddSingleton(offersMock.Object);

        var userApiMock = new Mock<UserApiService>(MockBehavior.Loose, http);
        userApiMock.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
        {
            Id = Guid.NewGuid(), DisplayName = "Stud", Email = "stud@example.com", Role = "StudFarmAdmin", Status = "Active"
        });
        Services.AddSingleton(new UserStateService(userApiMock.Object));

        cut = RenderComponent<AdminListingDetail>(p => p.Add(c => c.Id, listing.Id));
        return adminMock;
    }

    private AuctionListingDto UnsoldAuction(Guid? convertedTo = null) => new()
    {
        Id = Guid.NewGuid(), StallionName = "Dundeel (NZ)", SeasonName = "2026 Season", ListingType = "Auction",
        Status = "Unsold", CloseReason = "ReserveNotMet", EndDateTime = DateTime.UtcNow.AddDays(-1),
        PublishedAt = DateTime.UtcNow.AddDays(-8), MakeAnOfferListingId = convertedTo
    };

    [Fact]
    public void AnUnsoldAuction_CanBeOfferedAsMakeAnOffer()
    {
        var auction = UnsoldAuction();
        var newId = Guid.NewGuid();
        var admin = RenderWith(auction, out var cut);
        admin.Setup(a => a.ConvertToMakeAnOfferAsync(auction.Id)).ReturnsAsync(new MakeAnOfferListingDto { Id = newId });
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Offer as Make an Offer")));

        cut.FindAll("button").Single(b => b.TextContent.Contains("Offer as Make an Offer")).Click();
        cut.FindAll("button").Single(b => b.TextContent.Contains("Yes, open to offers")).Click();

        admin.Verify(a => a.ConvertToMakeAnOfferAsync(auction.Id), Times.Once);
        cut.WaitForAssertion(() =>
            Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith($"/admin/listings/{newId}"));
    }

    [Fact]
    public void AnUnsoldAuctionAlreadyConverted_LinksToItsMakeAnOfferListing()
    {
        var maoId = Guid.NewGuid();
        RenderWith(UnsoldAuction(convertedTo: maoId), out var cut);

        cut.WaitForAssertion(() => cut.Find($"a[href='/admin/listings/{maoId}']").TextContent
            .Should().Contain("View the Make an Offer listing"));
        cut.FindAll("button").Any(b => b.TextContent.Contains("Offer as Make an Offer")).Should().BeFalse();
    }

    [Fact]
    public void AMakeAnOfferListing_ShowsTheOffersPanel_AndCanBeClosedButNotUnpublished()
    {
        var mao = new MakeAnOfferListingDto
        {
            Id = Guid.NewGuid(), StallionName = "Dundeel (NZ)", SeasonName = "2026 Season", Status = "Active",
            ExpiresAt = DateTime.UtcNow.AddDays(30), BuyerFeeIncGst = 150m, PublishedAt = DateTime.UtcNow
        };
        RenderWith(mao, out var cut);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No offers yet"));
        cut.Markup.Should().Contain("Make an Offer").And.Contain("Offers close");
        cut.FindAll("button").Any(b => b.TextContent.Contains("Close Listing")).Should().BeTrue();
        cut.FindAll("button").Any(b => b.TextContent.Contains("Unpublish")).Should().BeFalse();
    }
```

The existing `Render(...)` helper also needs the `OfferApiService` mock registered (add the same three `offersMock` lines),
since the page injects it indirectly through `StudOffersPanel` only for Make an Offer listings — registering it keeps all
tests independent of the listing type.

- [ ] **Step 2: Run red.**

- [ ] **Step 3: Implement**

```csharp
// src/Client/Components/ListingTypeText.cs
namespace Stallions.Client.Components;

public static class ListingTypeText
{
    public static string Label(string type) => type switch
    {
        "MakeAnOffer" => "Make an Offer",
        _ => type
    };
}
```

`AdminApiService`:

```csharp
    /// <summary>Opens an Unsold auction lot to offers; returns the new Make an Offer listing.</summary>
    public virtual async Task<ListingDto> ConvertToMakeAnOfferAsync(Guid auctionId)
    {
        var r = await _http.PostAsync($"api/listings/{auctionId}/make-an-offer", null);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ReadError(r, "The lot could not be offered as Make an Offer."));
        return await r.Content.ReadFromJsonAsync<ListingDto>(Stallions.Shared.StallionsJsonContext.Default.Options)
               ?? throw new ApiException(500, "Empty response.");
    }
```

```razor
@* src/Client/Components/Offers/StudOffersPanel.razor *@
@inject OfferApiService OfferApi

<div class="form-section">
    <div class="form-section-title">Offers</div>
    @if (_error is not null) { <div class="alert alert-error">@_error</div> }
    @if (_offers is null)
    {
        <LoadingSpinner />
    }
    else if (!_offers.Any())
    {
        <p class="text-muted">No offers yet. Buyers' offers appear here; only you can see them.</p>
    }
    else
    {
        <table class="admin-table">
            <thead><tr><th>Buyer</th><th>Offer</th><th>Received</th><th>Status</th><th></th></tr></thead>
            <tbody>
                @foreach (var o in _offers)
                {
                    <tr>
                        <td>@o.BuyerName<div class="text-muted text-xs">@o.BuyerEmail</div></td>
                        <td>@o.AmountIncGst.ToString("C0", AuCulture)</td>
                        <td>@o.CreatedAt.ToLocalTime().ToString("d MMM, h:mm tt")</td>
                        <td>@StatusText(o)</td>
                        <td class="admin-table-actions">
                            @if (o.Status == "Pending" && _confirming == o.Id)
                            {
                                <span class="text-sm">Accepting charges the buyer the @BuyerFeeIncGst?.ToString("C0", AuCulture) buyer fee straight away.</span>
                                <button class="btn btn-sm btn-primary" @onclick="() => Accept(o)" disabled="@_busy">Yes, accept</button>
                                <button class="btn btn-sm btn-secondary" @onclick="() => _confirming = null" disabled="@_busy">Cancel</button>
                            }
                            else if (o.Status == "Pending")
                            {
                                <button class="btn btn-sm btn-primary" @onclick="() => _confirming = o.Id" disabled="@_busy">Accept</button>
                                <button class="btn btn-sm btn-secondary" @onclick="() => Decline(o)" disabled="@_busy">Decline</button>
                            }
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    }
    @if (_message is not null) { <p class="text-sm" role="status">@_message</p> }
</div>

@code {
    [Parameter, EditorRequired] public Guid ListingId { get; set; }
    [Parameter] public decimal? BuyerFeeIncGst { get; set; }

    private static readonly System.Globalization.CultureInfo AuCulture = System.Globalization.CultureInfo.GetCultureInfo("en-AU");
    private List<StudOfferDto>? _offers;
    private Guid? _confirming;
    private bool _busy;
    private string? _error;
    private string? _message;

    protected override async Task OnParametersSetAsync() => await Load();

    private async Task Load()
    {
        try { _offers = await OfferApi.GetForListingAsync(ListingId); }
        catch (ApiException ex) { _error = ex.Message; _offers = new(); }
    }

    private async Task Accept(StudOfferDto offer)
    {
        _busy = true; _error = null; _message = null;
        try
        {
            var result = await OfferApi.AcceptAsync(offer.Id);
            _message = result.Charge switch
            {
                "Paid" => "Accepted — buyer fee paid. The buyer and you have been emailed the sale details.",
                "PaymentFailed" => $"Accepted — the buyer's payment failed; the buyer has until {result.ChargeDueBy?.ToLocalTime():d MMM, h:mm tt} to update their card.",
                "Processing" => "Accepted — the payment is being processed; refresh in a minute.",
                _ => "Accepted."
            };
            _confirming = null;
            await Load();
        }
        catch (ApiException ex) { _error = ex.Message; }
        finally { _busy = false; }
    }

    private async Task Decline(StudOfferDto offer)
    {
        _busy = true; _error = null; _message = null;
        try { await OfferApi.DeclineAsync(offer.Id); _message = "Offer declined; the buyer has been told."; await Load(); }
        catch (ApiException ex) { _error = ex.Message; }
        finally { _busy = false; }
    }

    private static string StatusText(StudOfferDto o) => (o.Status, o.SaleStatus) switch
    {
        ("Pending", _) => $"Open until {o.ExpiresAt.ToLocalTime():d MMM}",
        ("Accepted", "Completed") => "Accepted — paid",
        ("Accepted", "Pending") when o.ChargeDueBy is not null => $"Accepted — payment failed (until {o.ChargeDueBy.Value.ToLocalTime():d MMM, h:mm tt})",
        ("Accepted", "Pending") => "Accepted — charging",
        ("Accepted", "Voided") => "Accepted — no sale",
        _ => o.Status
    };
}
```

`AdminListingDetail.razor` (status bar):
- show **Unpublish** only when `_listing is not MakeAnOfferListingDto` (keep the existing Active/not-ended condition);
- for an Unsold auction:

```razor
            @if (_listing is AuctionListingDto { Status: "Unsold" } unsold)
            {
                @if (unsold.MakeAnOfferListingId is Guid maoId)
                {
                    <a href="/admin/listings/@maoId" class="btn btn-secondary">View the Make an Offer listing</a>
                }
                else if (_confirmConvert)
                {
                    <span class="text-sm">This opens the lot to private offers straight away, until the end of @_listing.SeasonName.</span>
                    <button class="btn btn-primary" @onclick="HandleConvert" disabled="@_busy">@(_busy ? "Opening…" : "Yes, open to offers")</button>
                    <button class="btn btn-secondary" @onclick="() => _confirmConvert = false" disabled="@_busy">Cancel</button>
                }
                else
                {
                    <button class="btn btn-primary" @onclick="() => _confirmConvert = true">Offer as Make an Offer</button>
                }
            }
```

- in "Listing Details", the Type row becomes `<dd>@ListingTypeText.Label(_listing.ListingType)</dd>`;
- after the auction section:

```razor
        @if (_listing is MakeAnOfferListingDto mao)
        {
            <div class="form-section">
                <div class="form-section-title">Make an Offer</div>
                <dl class="detail-list">
                    <dt>Offers close</dt><dd>@mao.ExpiresAt.ToLocalTime().ToString("dd MMM yyyy HH:mm")</dd>
                </dl>
            </div>
            <StudOffersPanel ListingId="@mao.Id" BuyerFeeIncGst="@mao.BuyerFeeIncGst" />
        }
```

- `@code`: `private bool _confirmConvert;` and

```csharp
    private async Task HandleConvert()
    {
        _busy = true; _error = null;
        try
        {
            var created = await AdminApi.ConvertToMakeAnOfferAsync(Id);
            Nav.NavigateTo($"/admin/listings/{created.Id}");
        }
        catch (ApiException ex) { _error = ex.Message; }
        finally { _busy = false; _confirmConvert = false; }
    }
```

`AdminListings.razor`: type cell `@ListingTypeText.Label(listing.ListingType)`; for a `MakeAnOfferListingDto` show
`Offers until {ExpiresAt:dd MMM yyyy}` in the end-date column. `StaffListings.razor`: show
`ListingTypeText.Label(l.ListingType)` in the type badge; `app.css`: add
`.badge-type-makeanoffer { background: #dcfce7; color: #166534; }` next to `.badge-type-auction`.

- [ ] **Step 4: Run green** — client tests, then full build (0 warnings) and full test run.

- [ ] **Step 5: Commit**

```bash
git add src/Client tests/Client.Tests
git status --short
git commit -m "feat: client — convert to Make an Offer; stud offers panel with accept and decline"
```

---

### Task 9: Final check, push, dev rollout (David deploys)

- [ ] **Step 1: Whole-branch check** — `git status` clean; `dotnet build stallions-nominations-marketplace.slnx` (0 warnings);
  `dotnet test stallions-nominations-marketplace.slnx` twice (green); `dotnet ef migrations has-pending-model-changes --project src/Server --startup-project src/Server` → no changes.
- [ ] **Step 2: Docs** — spec **Status** → "Built on `feature/v2-phase4-make-an-offer` (date); not merged"; add a
  "Changes made during the build" block to this plan if reviews changed anything; commit `docs: phase 4 spec status`.
- [ ] **Step 3: Push** — `git push -u origin feature/v2-phase4-make-an-offer`. Don't merge or open a PR unless David asks.
- [ ] **Step 4: Dev rollout (David runs `azd deploy api --environment dev`; no infra change, no provision needed)** — then check from outside:
  site and `api/listings` 200; `GET api/offers/mine` 401 signed out; `POST api/listings/{id}/make-an-offer` 401 signed out.
- [ ] **Step 5: Dev click-through (fake provider; test users StallNomsManager01 / StallNomBuyer01 / PaddockSide):**
  1. As the stud, open the Unsold below-reserve Dundeel (`738db336`) → **Offer as Make an Offer** → the new listing is live; the browse page shows it with the "Make an Offer" badge.
  2. As StallNomBuyer01 (good card), offer $9,000 → stud gets "New offer"; the buyer sees "Your offer … is with the stud".
  3. Stud declines → buyer gets "Offer declined". Buyer offers again ($9,500).
  4. Stud accepts → "Accepted — buyer fee paid"; buyer gets "Your offer … was accepted" (price, fee, balance); stud gets "Sale confirmed"; My Offers / My sale records show it; the listing stays open.
  5. PaddockSide saves a declining card, offers; stud accepts → "payment failed, the buyer has until …"; PaddockSide saves a good card → paid within a minute.
