# v2 Phase 2 — Payments Foundation (Stripe) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Buyers save a card (required to bid), studs pay the listing fee by card when they activate a stallion, and a signed webhook is the single source of truth — behind a provider interface with Stripe and a dev-only fake.

**Architecture:** `IPaymentProvider` (Stripe-hosted Checkout via Stripe.net, or `FakePaymentProvider` with a simulated approve/decline page) creates hosted sessions and turns provider callbacks into provider-neutral `PaymentEvent`s. `PaymentEventProcessor` is the only code that changes data because of a payment: idempotent on the provider event id, audit-logged. Configuration chooses the provider; the server refuses to start with the fake in Production.

**Tech Stack:** .NET 9, ASP.NET Core, EF Core (SQL Server), Blazor WASM, Stripe.net, xUnit + Moq + FluentAssertions, bUnit.

**Spec:** `docs/superpowers/specs/2026-10-09-v2-phase2-payments-design.md`. Read it and CLAUDE.md first.
**Branch:** `feature/v2-phase2-payments` (already created from master).

> **Amendments after the Task 1–2 reviews (2026-10-09):**
> - `appsettings.Development.json` is gitignored, so the local `Payments:Provider=Fake` default
>   lives in `src/Server/Properties/launchSettings.json` (both profiles).
> - Provider ids are `nvarchar(255)`; `User` also stores `PaymentCustomerProvider` so a customer id
>   or card from another provider (e.g. the dev fake) is never reused or treated as valid.
> - `SavedCard.IsValidOn` returns false (doesn't throw) for an impossible month/year.
> - Idempotency is claim-first: `IProcessedPaymentEventRepository.TryClaimAsync` inserts the event
>   id (false = already claimed, including a concurrent duplicate) and `ReleaseAsync` removes it when
>   processing fails so the provider's retry is processed. Tasks 3 and 6 below reflect this.
> - A failed save is only treated as "already claimed" if the row really exists; anything else
>   rethrows. `ReleaseAsync` clears the change tracker first. Each claim records `CompletedAt` via
>   `MarkCompletedAsync`; an incomplete claim older than 10 minutes (a crash mid-processing) can be
>   re-claimed by the provider's retry.

---

## Ground rules

- **Test first** for every service change: write the failing test, run it red, implement, run it green.
- After each task: `dotnet build stallions-nominations-marketplace.slnx` (0 warnings) and
  `dotnet test stallions-nominations-marketplace.slnx` must be green. `tests/Server.Tests` builds the
  client too (Server references Client), so keep the client compiling at every task.
- **One commit per task**, conventional message, ending with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Line endings:** the repo stores LF, the working copy is CRLF. Stage only the files you changed.
- **No secrets anywhere in the repo.** Stripe keys only ever exist in Key Vault (David loads them).
- **No floating package versions.** Pin Stripe.net to an exact version (a floating `3.*` caused the
  Phase 1 My Listings 500).
- **Database:** migrations go to the local dev DB (`(localdb)\mssqllocaldb`, `StallionsNomsDev`);
  Azure dev migrates itself on deploy. `azd provision` / `azd deploy` only with David's go-ahead, dev only.
- Existing helper patterns to follow: `ServiceResult` / `ServiceResult<T>`, repositories in
  `Server/Data/Repositories`, `IAuditLogRepository.LogAsync(entityType, entityId, action, userId, details)`,
  `IUserService.GetOrCreateCurrentUserAsync()`, client services built on `ServiceHelpers.ExtractErrorMessageAsync`.

## File map

| File | Responsibility |
|---|---|
| `src/Server/Payments/PaymentOptions.cs` | `Payments` config section + `PaymentOptionsValidator` |
| `src/Server/Payments/PaymentEvents.cs` | Provider-neutral event records + `PaymentSignatureException` |
| `src/Server/Payments/IPaymentProvider.cs` | Provider interface |
| `src/Server/Payments/PaymentEventProcessor.cs` (+ `IPaymentEventProcessor`) | Applies events to data, idempotently |
| `src/Server/Payments/FakePaymentProvider.cs` | Dev-only provider with in-memory sessions |
| `src/Server/Payments/Stripe/IStripeApi.cs`, `StripeApi.cs` | Thin wrapper over the Stripe.net SDK calls we use |
| `src/Server/Payments/Stripe/StripePaymentProvider.cs` | Session options + webhook verification/mapping |
| `src/Server/Payments/PaymentServiceCollectionExtensions.cs` | `AddPayments(...)` DI + startup validation |
| `src/Server/Controllers/PaymentsController.cs` | Card endpoints + Stripe webhook |
| `src/Server/Controllers/FakePaymentController.cs` | Simulated payment page (Fake only) |
| `src/Server/Services/CardService.cs` (+ `ICardService`) | Saved card read, setup session, validity |
| `src/Server/Data/Entities/SavedCard.cs`, `ProcessedPaymentEvent.cs` | New entities |
| `src/Server/Data/Repositories/SavedCardRepository.cs`, `ProcessedPaymentEventRepository.cs` (+ interfaces) | Data access |
| `src/Shared/DTOs/Payments/SavedCardDto.cs`, `PaymentRedirectDto.cs` | DTOs |
| `src/Shared/DTOs/Subscriptions/ActivateStallionRequest.cs` | DTO |
| `src/Client/Services/PaymentsApiService.cs` | Client API for cards |
| `src/Client/Pages/AccountCard.razor` | `/account/card` |
| `infra/modules/appservice.bicep` | Three new app settings |

---

### Task 1: Stripe.net package, payment options and the startup guard

**Files:**
- Modify: `src/Server/Stallions.Server.csproj`
- Create: `src/Server/Payments/PaymentOptions.cs`
- Modify: `src/Server/appsettings.json`, `src/Server/appsettings.Development.json`
- Test: `tests/Server.Tests/Payments/PaymentOptionsValidatorTests.cs`

- [ ] **Step 1: Add Stripe.net and pin it**

Run: `dotnet add src/Server package Stripe.net`
Then open `src/Server/Stallions.Server.csproj` and confirm the new `<PackageReference Include="Stripe.net" Version="..." />` has an **exact** version (e.g. `48.x.y`, never `*`). Note the version in the commit message.

- [ ] **Step 2: Write the failing validator tests**

```csharp
// tests/Server.Tests/Payments/PaymentOptionsValidatorTests.cs
using FluentAssertions;
using Stallions.Server.Payments;

namespace Stallions.Server.Tests.Payments;

public class PaymentOptionsValidatorTests
{
    private static PaymentOptions Options(string provider, string key = "", string secret = "") => new()
    {
        Provider = provider,
        Stripe = new StripeSettings { SecretKey = key, WebhookSigningSecret = secret }
    };

    [Fact]
    public void Fake_IsAllowedOutsideProduction() =>
        PaymentOptionsValidator.Validate(Options("Fake"), isProduction: false).Should().BeNull();

    [Fact]
    public void Fake_IsRefusedInProduction() =>
        PaymentOptionsValidator.Validate(Options("Fake"), isProduction: true)
            .Should().Contain("cannot run in Production");

    [Fact]
    public void Stripe_WithBothSecrets_IsValid() =>
        PaymentOptionsValidator.Validate(Options("Stripe", "sk_test_x", "whsec_x"), isProduction: true)
            .Should().BeNull();

    [Theory]
    [InlineData("", "whsec_x")]
    [InlineData("sk_test_x", "")]
    [InlineData("@Microsoft.KeyVault(SecretUri=https://kv/secrets/StripeSecretKey/)", "whsec_x")]
    public void Stripe_WithMissingOrUnresolvedSecret_IsRefused(string key, string secret) =>
        PaymentOptionsValidator.Validate(Options("Stripe", key, secret), isProduction: false)
            .Should().Contain("Stripe");

    [Theory]
    [InlineData("")]
    [InlineData("PayPal")]
    public void UnknownProvider_IsRefused(string provider) =>
        PaymentOptionsValidator.Validate(Options(provider), isProduction: false)
            .Should().Contain("Unknown payment provider");
}
```

- [ ] **Step 3: Run to confirm red** — `dotnet test tests/Server.Tests --filter PaymentOptionsValidatorTests` → compile error (types missing).

- [ ] **Step 4: Implement**

```csharp
// src/Server/Payments/PaymentOptions.cs
namespace Stallions.Server.Payments;

/// <summary>The "Payments" configuration section.</summary>
public class PaymentOptions
{
    public const string Section = "Payments";
    public const string ProviderFake = "Fake";
    public const string ProviderStripe = "Stripe";

    /// <summary>"Fake" (dev only) or "Stripe".</summary>
    public string Provider { get; set; } = string.Empty;
    public StripeSettings Stripe { get; set; } = new();
}

/// <summary>Values come only from Key Vault references in App Service settings — never from a committed file.</summary>
public class StripeSettings
{
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSigningSecret { get; set; } = string.Empty;
}

public static class PaymentOptionsValidator
{
    /// <summary>Returns an error message, or null when the configuration is usable.</summary>
    public static string? Validate(PaymentOptions options, bool isProduction)
    {
        switch (options.Provider)
        {
            case PaymentOptions.ProviderFake:
                return isProduction ? "The fake payment provider cannot run in Production." : null;
            case PaymentOptions.ProviderStripe:
                if (!IsUsable(options.Stripe.SecretKey))
                    return "Payments:Stripe:SecretKey is missing or its Key Vault reference did not resolve.";
                if (!IsUsable(options.Stripe.WebhookSigningSecret))
                    return "Payments:Stripe:WebhookSigningSecret is missing or its Key Vault reference did not resolve.";
                return null;
            default:
                return $"Unknown payment provider '{options.Provider}'. Use Fake or Stripe.";
        }
    }

    // App Service leaves the literal "@Microsoft.KeyVault(...)" in place when it can't resolve a reference.
    private static bool IsUsable(string value) =>
        !string.IsNullOrWhiteSpace(value) && !value.StartsWith("@Microsoft.KeyVault(", StringComparison.OrdinalIgnoreCase);
}
```

Add to `src/Server/appsettings.json` (top level, after `"Checkout"`) — the safe default for any environment that doesn't override it:

```json
  "Payments": {
    "Provider": "Stripe"
  }
```

Add to `src/Server/appsettings.Development.json` (top level):

```json
  "Payments": {
    "Provider": "Fake"
  }
```

- [ ] **Step 5: Run green** — `dotnet test tests/Server.Tests --filter PaymentOptionsValidatorTests` → 8 passed. Full build + test green.

- [ ] **Step 6: Commit**

```bash
git add src/Server/Stallions.Server.csproj src/Server/Payments/PaymentOptions.cs src/Server/appsettings.json src/Server/appsettings.Development.json tests/Server.Tests/Payments/PaymentOptionsValidatorTests.cs
git commit -m "feat: payment options and startup guard; add Stripe.net <version>"
```

---

### Task 2: Saved cards, processed events, customer id — entities and repositories

**Files:**
- Create: `src/Server/Data/Entities/SavedCard.cs`, `src/Server/Data/Entities/ProcessedPaymentEvent.cs`
- Modify: `src/Server/Data/Entities/User.cs`, `src/Server/Data/AppDbContext.cs`, `src/Server/Program.cs`
- Create: `src/Server/Data/Repositories/ISavedCardRepository.cs`, `SavedCardRepository.cs`, `IProcessedPaymentEventRepository.cs`, `ProcessedPaymentEventRepository.cs`
- Test: `tests/Server.Tests/Data/Entities/SavedCardTests.cs`, `tests/Server.Tests/Data/Repositories/PaymentRepositoriesTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Server.Tests/Data/Entities/SavedCardTests.cs
using FluentAssertions;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Tests.Data.Entities;

public class SavedCardTests
{
    private static SavedCard Card(int month, int year) => new() { ExpMonth = month, ExpYear = year };

    [Theory]
    [InlineData(2026, 10, 9, true)]   // well before expiry
    [InlineData(2026, 10, 31, true)]  // last day of the expiry month
    [InlineData(2026, 11, 1, false)]  // first day after
    public void IsValidOn_IsTrueUntilTheEndOfTheExpiryMonth(int y, int m, int d, bool expected) =>
        Card(10, 2026).IsValidOn(new DateOnly(y, m, d)).Should().Be(expected);

    [Fact]
    public void IsValidOn_HandlesFebruaryInALeapYear() =>
        Card(2, 2028).IsValidOn(new DateOnly(2028, 2, 29)).Should().BeTrue();
}
```

```csharp
// tests/Server.Tests/Data/Repositories/PaymentRepositoriesTests.cs
using FluentAssertions;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;

namespace Stallions.Server.Tests.Data.Repositories;

public class PaymentRepositoriesTests
{
    [Fact]
    public async Task SavedCards_AddThenGetByUser_RoundTrips()
    {
        await using var db = DbContextFactory.Create(nameof(SavedCards_AddThenGetByUser_RoundTrips));
        var userId = Guid.NewGuid();
        var repo = new SavedCardRepository(db);
        await repo.AddAsync(new SavedCard
        {
            UserId = userId, Provider = "Fake", ProviderCustomerId = "cus_1",
            ProviderPaymentMethodId = "pm_1", Brand = "visa", Last4 = "4242", ExpMonth = 8, ExpYear = 2028
        });

        var card = await repo.GetByUserIdAsync(userId);

        card!.Last4.Should().Be("4242");
        (await repo.GetByUserIdAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task ProcessedEvents_ExistsAfterAdd()
    {
        await using var db = DbContextFactory.Create(nameof(ProcessedEvents_ExistsAfterAdd));
        var repo = new ProcessedPaymentEventRepository(db);

        (await repo.ExistsAsync("evt_1")).Should().BeFalse();
        await repo.AddAsync(new ProcessedPaymentEvent { EventId = "evt_1", Provider = "Fake", Type = "CardSavedEvent" });
        (await repo.ExistsAsync("evt_1")).Should().BeTrue();
    }

    [Fact]
    public void Model_SavedCardUserIdIsUnique()
    {
        using var db = DbContextFactory.Create(nameof(Model_SavedCardUserIdIsUnique));
        db.Model.FindEntityType(typeof(SavedCard))!.GetIndexes()
            .Single(i => i.Properties.Single().Name == nameof(SavedCard.UserId))
            .IsUnique.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run red** — compile errors (types missing).

- [ ] **Step 3: Implement entities**

```csharp
// src/Server/Data/Entities/SavedCard.cs
namespace Stallions.Server.Data.Entities;

/// <summary>
/// A buyer's saved card at the payment provider — one per buyer. Holds provider references and
/// display details only; raw card data is never stored.
/// </summary>
public class SavedCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderCustomerId { get; set; } = string.Empty;
    public string ProviderPaymentMethodId { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public int ExpMonth { get; set; }
    public int ExpYear { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User User { get; set; } = null!;

    /// <summary>A card is usable until the last day of its expiry month.</summary>
    public bool IsValidOn(DateOnly date) =>
        date <= new DateOnly(ExpYear, ExpMonth, DateTime.DaysInMonth(ExpYear, ExpMonth));
}
```

```csharp
// src/Server/Data/Entities/ProcessedPaymentEvent.cs
namespace Stallions.Server.Data.Entities;

/// <summary>Idempotency record: a provider event id is processed at most once.</summary>
public class ProcessedPaymentEvent
{
    public string EventId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
```

In `User.cs`, after `SuppressBidConfirmation`:

```csharp
    // Payment provider customer reference (created on first card setup). Never card data.
    public string? PaymentCustomerId { get; set; }
```

- [ ] **Step 4: Map in `AppDbContext`**

Add DbSets after `StallionSeasonSubscriptions`:

```csharp
    public DbSet<SavedCard> SavedCards => Set<SavedCard>();
    public DbSet<ProcessedPaymentEvent> ProcessedPaymentEvents => Set<ProcessedPaymentEvent>();
```

In the `User` mapping block add `e.Property(u => u.PaymentCustomerId).HasMaxLength(100);`, and at the end of `OnModelCreating`:

```csharp
        // ── SavedCards (one per buyer) ────────────────────────────────────────
        modelBuilder.Entity<SavedCard>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.UserId).IsUnique();
            e.Property(c => c.Provider).HasMaxLength(20).IsRequired();
            e.Property(c => c.ProviderCustomerId).HasMaxLength(100).IsRequired();
            e.Property(c => c.ProviderPaymentMethodId).HasMaxLength(100).IsRequired();
            e.Property(c => c.Brand).HasMaxLength(30).IsRequired();
            e.Property(c => c.Last4).HasMaxLength(4).IsRequired();

            e.HasOne(c => c.User)
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ProcessedPaymentEvents (idempotency) ──────────────────────────────
        modelBuilder.Entity<ProcessedPaymentEvent>(e =>
        {
            e.HasKey(p => p.EventId);
            e.Property(p => p.EventId).HasMaxLength(255);
            e.Property(p => p.Provider).HasMaxLength(20).IsRequired();
            e.Property(p => p.Type).HasMaxLength(100).IsRequired();
        });
```

- [ ] **Step 5: Repositories**

```csharp
// src/Server/Data/Repositories/ISavedCardRepository.cs
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface ISavedCardRepository
{
    Task<SavedCard?> GetByUserIdAsync(Guid userId);
    Task<SavedCard> AddAsync(SavedCard card);
    Task UpdateAsync(SavedCard card);
}
```

```csharp
// src/Server/Data/Repositories/SavedCardRepository.cs
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class SavedCardRepository : ISavedCardRepository
{
    private readonly AppDbContext _db;
    public SavedCardRepository(AppDbContext db) => _db = db;

    public async Task<SavedCard?> GetByUserIdAsync(Guid userId) =>
        await _db.SavedCards.FirstOrDefaultAsync(c => c.UserId == userId);

    public async Task<SavedCard> AddAsync(SavedCard card)
    {
        _db.SavedCards.Add(card);
        await _db.SaveChangesAsync();
        return card;
    }

    public async Task UpdateAsync(SavedCard card)
    {
        _db.SavedCards.Update(card);
        await _db.SaveChangesAsync();
    }
}
```

```csharp
// src/Server/Data/Repositories/IProcessedPaymentEventRepository.cs
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IProcessedPaymentEventRepository
{
    Task<bool> ExistsAsync(string eventId);
    Task AddAsync(ProcessedPaymentEvent processed);
}
```

```csharp
// src/Server/Data/Repositories/ProcessedPaymentEventRepository.cs
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class ProcessedPaymentEventRepository : IProcessedPaymentEventRepository
{
    private readonly AppDbContext _db;
    public ProcessedPaymentEventRepository(AppDbContext db) => _db = db;

    public async Task<bool> ExistsAsync(string eventId) =>
        await _db.ProcessedPaymentEvents.AnyAsync(p => p.EventId == eventId);

    public async Task AddAsync(ProcessedPaymentEvent processed)
    {
        _db.ProcessedPaymentEvents.Add(processed);
        await _db.SaveChangesAsync();
    }
}
```

Register in `Program.cs` with the other repositories:

```csharp
builder.Services.AddScoped<ISavedCardRepository, SavedCardRepository>();
builder.Services.AddScoped<IProcessedPaymentEventRepository, ProcessedPaymentEventRepository>();
```

- [ ] **Step 6: Run green** — the new tests pass; full build + test green.

- [ ] **Step 7: Commit**

```bash
git add src/Server/Data/Entities/SavedCard.cs src/Server/Data/Entities/ProcessedPaymentEvent.cs src/Server/Data/Entities/User.cs src/Server/Data/AppDbContext.cs src/Server/Data/Repositories/ISavedCardRepository.cs src/Server/Data/Repositories/SavedCardRepository.cs src/Server/Data/Repositories/IProcessedPaymentEventRepository.cs src/Server/Data/Repositories/ProcessedPaymentEventRepository.cs src/Server/Program.cs tests/Server.Tests/Data/Entities/SavedCardTests.cs tests/Server.Tests/Data/Repositories/PaymentRepositoriesTests.cs
git commit -m "feat: saved card, processed payment event and payment customer id"
```

---

### Task 3: Provider interface, payment events and the event processor

**Files:**
- Create: `src/Server/Payments/PaymentEvents.cs`, `src/Server/Payments/IPaymentProvider.cs`, `src/Server/Payments/IPaymentEventProcessor.cs`, `src/Server/Payments/PaymentEventProcessor.cs`
- Test: `tests/Server.Tests/Payments/PaymentEventProcessorTests.cs`

- [ ] **Step 1: Create the interface and event types** (no logic yet — needed so tests compile)

```csharp
// src/Server/Payments/PaymentEvents.cs
namespace Stallions.Server.Payments;

/// <summary>A provider callback, translated into terms the platform understands.</summary>
public abstract record PaymentEvent(string EventId);

/// <summary>A buyer finished saving a card on the hosted page.</summary>
public sealed record CardSavedEvent(
    string EventId, Guid UserId, string CustomerId, string PaymentMethodId,
    string Brand, string Last4, int ExpMonth, int ExpYear) : PaymentEvent(EventId);

/// <summary>A stud paid a listing-fee subscription on the hosted page. Amount in cents.</summary>
public sealed record ListingFeePaidEvent(
    string EventId, Guid SubscriptionId, long AmountCents, string Currency, string PaymentReference)
    : PaymentEvent(EventId);

/// <summary>Any event the platform doesn't act on; acknowledged and ignored.</summary>
public sealed record UnhandledPaymentEvent(string EventId, string Type) : PaymentEvent(EventId);

/// <summary>Thrown when a webhook's signature can't be verified.</summary>
public class PaymentSignatureException(string message) : Exception(message);
```

```csharp
// src/Server/Payments/IPaymentProvider.cs
namespace Stallions.Server.Payments;

/// <summary>
/// The payment provider (Stripe, or the dev-only fake). Hosted pages only — card data never
/// reaches the app. Amounts are always passed in by the server, never taken from the browser.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>"Stripe" or "Fake" — recorded on saved cards and processed events.</summary>
    string Name { get; }

    Task<string> CreateCustomerAsync(Guid userId, string email, string name);

    /// <summary>Hosted page that saves a card for later off-session use. Returns the redirect URL.</summary>
    Task<string> CreateCardSetupSessionAsync(Guid userId, string customerId, string successUrl, string cancelUrl);

    /// <summary>Hosted page that takes a listing-fee payment in AUD. Returns the redirect URL.</summary>
    Task<string> CreateListingFeeSessionAsync(
        Guid subscriptionId, decimal amountIncGst, string description, string? payerEmail,
        string successUrl, string cancelUrl);

    /// <summary>Verifies the signature and maps the callback. Throws <see cref="PaymentSignatureException"/>.</summary>
    Task<PaymentEvent> ParseWebhookAsync(string rawBody, string? signatureHeader);

    Task DetachCardAsync(string paymentMethodId);
}
```

```csharp
// src/Server/Payments/IPaymentEventProcessor.cs
namespace Stallions.Server.Payments;

public enum PaymentEventOutcome { Processed, Duplicate, Ignored, Rejected }

public interface IPaymentEventProcessor
{
    Task<PaymentEventOutcome> ProcessAsync(PaymentEvent paymentEvent);
}
```

- [ ] **Step 2: Write the failing processor tests** (real repositories on the in-memory DB, mocked provider)

```csharp
// tests/Server.Tests/Payments/PaymentEventProcessorTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Payments;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Payments;

public class PaymentEventProcessorTests : IDisposable
{
    private readonly AppDbContext _db = DbContextFactory.Create(Guid.NewGuid().ToString());
    private readonly Mock<IPaymentProvider> _provider = new();
    private readonly User _buyer = new()
    {
        ObjectId = "b", Email = "buyer@x", DisplayName = "Buyer", Role = UserRole.Buyer, Status = UserStatus.Active
    };

    public PaymentEventProcessorTests()
    {
        _provider.SetupGet(p => p.Name).Returns("Fake");
        _db.Users.Add(_buyer);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private PaymentEventProcessor CreateSut() => new(
        new ProcessedPaymentEventRepository(_db), new SavedCardRepository(_db), new UserRepository(_db),
        new SubscriptionRepository(_db), _provider.Object, new AuditLogRepository(_db),
        NullLogger<PaymentEventProcessor>.Instance);

    private CardSavedEvent CardSaved(string eventId, string pm = "pm_1", string last4 = "4242") =>
        new(eventId, _buyer.Id, "cus_1", pm, "visa", last4, 8, 2028);

    private StallionSeasonSubscription PendingSubscription(decimal fee = 990m)
    {
        var sub = new StallionSeasonSubscription
        {
            StallionId = Guid.NewGuid(), SeasonId = Guid.NewGuid(), StudFarmId = Guid.NewGuid(),
            FeeIncGst = fee, FeeExGst = fee - Math.Round(fee / 11m, 2), GstAmount = Math.Round(fee / 11m, 2),
            Status = SubscriptionStatus.Pending, CreatedByUserId = _buyer.Id
        };
        _db.StallionSeasonSubscriptions.Add(sub);
        _db.SaveChanges();
        return sub;
    }

    [Fact]
    public async Task CardSaved_CreatesTheCardAndRecordsTheCustomer()
    {
        var outcome = await CreateSut().ProcessAsync(CardSaved("evt_1"));

        outcome.Should().Be(PaymentEventOutcome.Processed);
        var card = await _db.SavedCards.SingleAsync();
        card.UserId.Should().Be(_buyer.Id);
        card.Last4.Should().Be("4242");
        card.Provider.Should().Be("Fake");
        var user = await _db.Users.SingleAsync(u => u.Id == _buyer.Id);
        user.PaymentCustomerId.Should().Be("cus_1");
        user.PaymentCustomerProvider.Should().Be("Fake");
        (await _db.AuditLogs.SingleAsync()).Action.Should().Be("SaveCard");
    }

    [Fact]
    public async Task CardSaved_Again_ReplacesTheCardAndDetachesTheOldOne()
    {
        await CreateSut().ProcessAsync(CardSaved("evt_1", pm: "pm_old", last4: "1111"));

        await CreateSut().ProcessAsync(CardSaved("evt_2", pm: "pm_new", last4: "4242"));

        var card = await _db.SavedCards.SingleAsync();
        card.ProviderPaymentMethodId.Should().Be("pm_new");
        card.Last4.Should().Be("4242");
        _provider.Verify(p => p.DetachCardAsync("pm_old"), Times.Once);
        (await _db.AuditLogs.CountAsync(a => a.Action == "ReplaceCard")).Should().Be(1);
    }

    [Fact]
    public async Task SameEventTwice_IsProcessedOnce()
    {
        await CreateSut().ProcessAsync(CardSaved("evt_1"));

        var second = await CreateSut().ProcessAsync(CardSaved("evt_1"));

        second.Should().Be(PaymentEventOutcome.Duplicate);
        (await _db.AuditLogs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ListingFeePaid_MarksThePendingSubscriptionPaidByCard()
    {
        var sub = PendingSubscription(990m);

        var outcome = await CreateSut().ProcessAsync(
            new ListingFeePaidEvent("evt_p", sub.Id, 99000, "aud", "pi_123"));

        outcome.Should().Be(PaymentEventOutcome.Processed);
        var saved = await _db.StallionSeasonSubscriptions.SingleAsync();
        saved.Status.Should().Be(SubscriptionStatus.Paid);
        saved.PaymentMethod.Should().Be(SubscriptionPaymentMethod.Card);
        saved.PaymentReference.Should().Be("pi_123");
        saved.PaidAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
        (await _db.AuditLogs.SingleAsync()).Action.Should().Be("ListingFeePaidByCard");
    }

    [Theory]
    [InlineData(98999, "aud")]   // short by a cent
    [InlineData(99000, "nzd")]   // wrong currency
    public async Task ListingFeePaid_WithWrongAmountOrCurrency_IsNotMarkedPaid(long cents, string currency)
    {
        var sub = PendingSubscription(990m);

        var outcome = await CreateSut().ProcessAsync(new ListingFeePaidEvent("evt_p", sub.Id, cents, currency, "pi_1"));

        outcome.Should().Be(PaymentEventOutcome.Rejected);
        (await _db.StallionSeasonSubscriptions.SingleAsync()).Status.Should().Be(SubscriptionStatus.Pending);
        (await _db.AuditLogs.SingleAsync()).Action.Should().Be("ListingFeePaymentMismatch");
    }

    [Fact]
    public async Task ListingFeePaid_ForAnAlreadyPaidSubscription_IsIgnored()
    {
        var sub = PendingSubscription(990m);
        sub.Status = SubscriptionStatus.Paid;
        sub.PaymentReference = "INV-1";
        await _db.SaveChangesAsync();

        var outcome = await CreateSut().ProcessAsync(new ListingFeePaidEvent("evt_p", sub.Id, 99000, "aud", "pi_2"));

        outcome.Should().Be(PaymentEventOutcome.Ignored);
        (await _db.StallionSeasonSubscriptions.SingleAsync()).PaymentReference.Should().Be("INV-1");
    }

    [Fact]
    public async Task ProcessingFailure_ReleasesTheEventSoTheRetryIsProcessed()
    {
        var failingCards = new Mock<ISavedCardRepository>();
        failingCards.Setup(c => c.GetByUserIdAsync(It.IsAny<Guid>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var failing = new PaymentEventProcessor(
            new ProcessedPaymentEventRepository(_db), failingCards.Object, new UserRepository(_db),
            new SubscriptionRepository(_db), _provider.Object, new AuditLogRepository(_db),
            NullLogger<PaymentEventProcessor>.Instance);

        await FluentActions.Awaiting(() => failing.ProcessAsync(CardSaved("evt_1")))
            .Should().ThrowAsync<InvalidOperationException>();

        (await CreateSut().ProcessAsync(CardSaved("evt_1"))).Should().Be(PaymentEventOutcome.Processed);
    }

    [Fact]
    public async Task UnhandledEvent_IsIgnoredButRecorded()
    {
        var outcome = await CreateSut().ProcessAsync(new UnhandledPaymentEvent("evt_x", "customer.created"));

        outcome.Should().Be(PaymentEventOutcome.Ignored);
        var processed = await _db.ProcessedPaymentEvents.SingleAsync();
        processed.EventId.Should().Be("evt_x");
        processed.CompletedAt.Should().NotBeNull();
    }
}
```

- [ ] **Step 3: Run red** — compile error: `PaymentEventProcessor` missing.

- [ ] **Step 4: Implement the processor**

```csharp
// src/Server/Payments/PaymentEventProcessor.cs
using System.Text.Json;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.Enums;

namespace Stallions.Server.Payments;

/// <summary>
/// The only code that changes data because of a payment. Provider-neutral and idempotent:
/// each provider event id is processed once. An exception leaves the event unrecorded, so the
/// provider's retry processes it again.
/// </summary>
public class PaymentEventProcessor : IPaymentEventProcessor
{
    private readonly IProcessedPaymentEventRepository _processed;
    private readonly ISavedCardRepository _cards;
    private readonly IUserRepository _users;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IPaymentProvider _provider;
    private readonly IAuditLogRepository _audit;
    private readonly ILogger<PaymentEventProcessor> _log;

    public PaymentEventProcessor(
        IProcessedPaymentEventRepository processed, ISavedCardRepository cards, IUserRepository users,
        ISubscriptionRepository subscriptions, IPaymentProvider provider, IAuditLogRepository audit,
        ILogger<PaymentEventProcessor> log)
    {
        _processed = processed; _cards = cards; _users = users; _subscriptions = subscriptions;
        _provider = provider; _audit = audit; _log = log;
    }

    public async Task<PaymentEventOutcome> ProcessAsync(PaymentEvent paymentEvent)
    {
        // Claim the event id first: a repeated or concurrent delivery loses the claim and is a
        // duplicate. If processing then fails, release the claim so the provider's retry runs again.
        var claimed = await _processed.TryClaimAsync(new ProcessedPaymentEvent
        {
            EventId = paymentEvent.EventId,
            Provider = _provider.Name,
            Type = paymentEvent.GetType().Name,
            ProcessedAt = DateTime.UtcNow
        });
        if (!claimed) return PaymentEventOutcome.Duplicate;

        try
        {
            var outcome = paymentEvent switch
            {
                CardSavedEvent card => await SaveCardAsync(card),
                ListingFeePaidEvent fee => await MarkListingFeePaidAsync(fee),
                _ => PaymentEventOutcome.Ignored
            };
            // Rejected and Ignored are final too — a retry wouldn't change them.
            await _processed.MarkCompletedAsync(paymentEvent.EventId);
            return outcome;
        }
        catch
        {
            await _processed.ReleaseAsync(paymentEvent.EventId);
            throw;
        }
    }

    private async Task<PaymentEventOutcome> SaveCardAsync(CardSavedEvent e)
    {
        var user = await _users.GetByIdAsync(e.UserId);
        if (user == null)
        {
            _log.LogError("Card saved for unknown user {UserId} (event {EventId})", e.UserId, e.EventId);
            return PaymentEventOutcome.Rejected;
        }

        var existing = await _cards.GetByUserIdAsync(e.UserId);
        if (existing != null && existing.ProviderPaymentMethodId != e.PaymentMethodId)
        {
            try { await _provider.DetachCardAsync(existing.ProviderPaymentMethodId); }
            catch (Exception ex)
            {
                // The new card is still saved; a stale card left at the provider is harmless.
                _log.LogWarning(ex, "Could not detach replaced card {PaymentMethodId}", existing.ProviderPaymentMethodId);
            }
        }

        var card = existing ?? new SavedCard { UserId = e.UserId };
        card.Provider = _provider.Name;
        card.ProviderCustomerId = e.CustomerId;
        card.ProviderPaymentMethodId = e.PaymentMethodId;
        card.Brand = e.Brand;
        card.Last4 = e.Last4;
        card.ExpMonth = e.ExpMonth;
        card.ExpYear = e.ExpYear;
        card.UpdatedAt = DateTime.UtcNow;
        if (existing == null) await _cards.AddAsync(card); else await _cards.UpdateAsync(card);

        if (user.PaymentCustomerId != e.CustomerId || user.PaymentCustomerProvider != _provider.Name)
        {
            user.PaymentCustomerId = e.CustomerId;
            user.PaymentCustomerProvider = _provider.Name;
            await _users.UpdateAsync(user);
        }

        await _audit.LogAsync("SavedCard", card.Id, existing == null ? "SaveCard" : "ReplaceCard", e.UserId,
            JsonSerializer.Serialize(new { e.Brand, e.Last4, e.ExpMonth, e.ExpYear }));
        return PaymentEventOutcome.Processed;
    }

    private async Task<PaymentEventOutcome> MarkListingFeePaidAsync(ListingFeePaidEvent e)
    {
        var subscription = await _subscriptions.GetByIdAsync(e.SubscriptionId);
        if (subscription == null)
        {
            _log.LogError("Listing fee paid for unknown subscription {SubscriptionId} (event {EventId})",
                e.SubscriptionId, e.EventId);
            return PaymentEventOutcome.Rejected;
        }

        if (subscription.Status is SubscriptionStatus.Paid or SubscriptionStatus.Waived)
        {
            _log.LogWarning("Listing fee paid for subscription {SubscriptionId} that is already {Status} (event {EventId})",
                e.SubscriptionId, subscription.Status, e.EventId);
            return PaymentEventOutcome.Ignored;
        }

        var expectedCents = (long)Math.Round(subscription.FeeIncGst * 100m, MidpointRounding.AwayFromZero);
        if (e.AmountCents != expectedCents || !string.Equals(e.Currency, "aud", StringComparison.OrdinalIgnoreCase))
        {
            _log.LogError(
                "Listing fee payment mismatch for subscription {SubscriptionId}: expected {Expected} AUD cents, got {Amount} {Currency} (event {EventId})",
                e.SubscriptionId, expectedCents, e.AmountCents, e.Currency, e.EventId);
            await _audit.LogAsync("StallionSeasonSubscription", subscription.Id, "ListingFeePaymentMismatch", null,
                JsonSerializer.Serialize(new { ExpectedCents = expectedCents, e.AmountCents, e.Currency, e.PaymentReference }));
            return PaymentEventOutcome.Rejected;
        }

        subscription.Status = SubscriptionStatus.Paid;
        subscription.PaymentMethod = SubscriptionPaymentMethod.Card;
        subscription.PaymentReference = e.PaymentReference;
        subscription.PaidAt = DateTime.UtcNow;
        await _subscriptions.UpdateAsync(subscription);

        await _audit.LogAsync("StallionSeasonSubscription", subscription.Id, "ListingFeePaidByCard", null,
            JsonSerializer.Serialize(new { subscription.FeeIncGst, e.PaymentReference }));
        return PaymentEventOutcome.Processed;
    }
}
```

- [ ] **Step 5: Run green** — `dotnet test tests/Server.Tests --filter PaymentEventProcessorTests` → 9 passed. Full build + test green.

- [ ] **Step 6: Commit**

```bash
git add src/Server/Payments/PaymentEvents.cs src/Server/Payments/IPaymentProvider.cs src/Server/Payments/IPaymentEventProcessor.cs src/Server/Payments/PaymentEventProcessor.cs tests/Server.Tests/Payments/PaymentEventProcessorTests.cs
git commit -m "feat: payment provider interface and idempotent payment event processor"
```

---

### Task 4: Fake provider, simulated payment page, DI wiring

**Files:**
- Create: `src/Server/Payments/FakePaymentProvider.cs`, `src/Server/Controllers/FakePaymentController.cs`, `src/Server/Payments/PaymentServiceCollectionExtensions.cs`
- Modify: `src/Server/Program.cs`
- Test: `tests/Server.Tests/Payments/FakePaymentProviderTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Server.Tests/Payments/FakePaymentProviderTests.cs
using FluentAssertions;
using Stallions.Server.Payments;

namespace Stallions.Server.Tests.Payments;

public class FakePaymentProviderTests
{
    private readonly FakePaymentProvider _fake = new();

    [Fact]
    public async Task CardSetup_ApproveProducesCardSavedForThatUser()
    {
        var userId = Guid.NewGuid();
        var url = await _fake.CreateCardSetupSessionAsync(userId, "cus_fake_1", "https://app/ok", "https://app/no");
        var sessionId = url.Split('/').Last();

        var (evt, redirect) = _fake.Approve(sessionId)!.Value;

        url.Should().StartWith("/payments/fake/");
        redirect.Should().Be("https://app/ok");
        var saved = evt.Should().BeOfType<CardSavedEvent>().Subject;
        saved.UserId.Should().Be(userId);
        saved.CustomerId.Should().Be("cus_fake_1");
        saved.Last4.Should().Be("4242");
        saved.ExpYear.Should().BeGreaterThan(DateTime.UtcNow.Year);
    }

    [Fact]
    public async Task ListingFee_ApproveProducesPaymentForTheSessionAmountInCents()
    {
        var subscriptionId = Guid.NewGuid();
        var url = await _fake.CreateListingFeeSessionAsync(subscriptionId, 990m, "Listing fee", "stud@x",
            "https://app/ok", "https://app/no");

        var (evt, _) = _fake.Approve(url.Split('/').Last())!.Value;

        var paid = evt.Should().BeOfType<ListingFeePaidEvent>().Subject;
        paid.SubscriptionId.Should().Be(subscriptionId);
        paid.AmountCents.Should().Be(99000);
        paid.Currency.Should().Be("aud");
    }

    [Fact]
    public async Task Session_CanOnlyBeCompletedOnce()
    {
        var url = await _fake.CreateCardSetupSessionAsync(Guid.NewGuid(), "cus", "https://ok", "https://no");
        var id = url.Split('/').Last();

        _fake.Decline(id).Should().Be("https://no");
        _fake.Approve(id).Should().BeNull();
        _fake.GetSession(id).Should().BeNull();
    }

    [Fact]
    public async Task ParseWebhook_IsNotSupported() =>
        await FluentActions.Awaiting(() => _fake.ParseWebhookAsync("{}", null))
            .Should().ThrowAsync<NotSupportedException>();
}
```

- [ ] **Step 2: Run red** — compile error.

- [ ] **Step 3: Implement the fake provider**

```csharp
// src/Server/Payments/FakePaymentProvider.cs
using System.Collections.Concurrent;

namespace Stallions.Server.Payments;

/// <summary>
/// Dev-only stand-in for Stripe (Payments:Provider = Fake). Sessions live in memory, which suits
/// single-instance dev. Its "hosted page" is FakePaymentController; approving a session produces
/// the same PaymentEvent the Stripe webhook would. Refused in Production by PaymentOptionsValidator.
/// </summary>
public class FakePaymentProvider : IPaymentProvider
{
    public sealed record FakeSession(
        string Id, bool IsCardSetup, Guid? UserId, string? CustomerId, Guid? SubscriptionId,
        long AmountCents, string Description, string SuccessUrl, string CancelUrl);

    private readonly ConcurrentDictionary<string, FakeSession> _sessions = new();

    public string Name => PaymentOptions.ProviderFake;

    public Task<string> CreateCustomerAsync(Guid userId, string email, string name) =>
        Task.FromResult($"cus_fake_{Guid.NewGuid():N}");

    public Task<string> CreateCardSetupSessionAsync(Guid userId, string customerId, string successUrl, string cancelUrl) =>
        Task.FromResult(Start(new FakeSession(NewId(), true, userId, customerId, null, 0,
            "Save a card", successUrl, cancelUrl)));

    public Task<string> CreateListingFeeSessionAsync(
        Guid subscriptionId, decimal amountIncGst, string description, string? payerEmail,
        string successUrl, string cancelUrl) =>
        Task.FromResult(Start(new FakeSession(NewId(), false, null, null, subscriptionId,
            (long)Math.Round(amountIncGst * 100m, MidpointRounding.AwayFromZero), description, successUrl, cancelUrl)));

    public Task<PaymentEvent> ParseWebhookAsync(string rawBody, string? signatureHeader) =>
        throw new NotSupportedException("The fake payment provider has no webhook; approve sessions on its page.");

    public Task DetachCardAsync(string paymentMethodId) => Task.CompletedTask;

    public FakeSession? GetSession(string id) => _sessions.GetValueOrDefault(id);

    /// <summary>Completes the session successfully. Null if the session doesn't exist (or was already used).</summary>
    public (PaymentEvent Event, string RedirectUrl)? Approve(string id)
    {
        if (!_sessions.TryRemove(id, out var s)) return null;
        var eventId = $"evt_fake_{Guid.NewGuid():N}";
        PaymentEvent evt = s.IsCardSetup
            ? new CardSavedEvent(eventId, s.UserId!.Value, s.CustomerId!, $"pm_fake_{Guid.NewGuid():N}",
                "visa", "4242", 12, DateTime.UtcNow.Year + 3)
            : new ListingFeePaidEvent(eventId, s.SubscriptionId!.Value, s.AmountCents, "aud", $"pi_fake_{Guid.NewGuid():N}");
        return (evt, s.SuccessUrl);
    }

    /// <summary>Abandons the session. Returns the cancel URL, or null if it doesn't exist.</summary>
    public string? Decline(string id) => _sessions.TryRemove(id, out var s) ? s.CancelUrl : null;

    private string Start(FakeSession session)
    {
        _sessions[session.Id] = session;
        return $"/payments/fake/{session.Id}";
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
```

- [ ] **Step 4: Simulated payment page**

```csharp
// src/Server/Controllers/FakePaymentController.cs
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Payments;

namespace Stallions.Server.Controllers;

/// <summary>
/// The fake provider's "hosted page". Returns 404 unless Payments:Provider = Fake, which the
/// startup guard refuses in Production. Session ids are unguessable and single-use.
/// </summary>
[ApiController]
[Route("payments/fake")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public class FakePaymentController : ControllerBase
{
    private readonly IPaymentProvider _provider;
    private readonly IPaymentEventProcessor _processor;

    public FakePaymentController(IPaymentProvider provider, IPaymentEventProcessor processor)
    {
        _provider = provider;
        _processor = processor;
    }

    [HttpGet("{id}")]
    public IActionResult Show(string id)
    {
        if (_provider is not FakePaymentProvider fake || fake.GetSession(id) is not { } s) return NotFound();
        var what = s.IsCardSetup
            ? "Save a card (simulated Visa •••• 4242)"
            : $"{WebUtility.HtmlEncode(s.Description)} — {s.AmountCents / 100m:C} AUD";
        var html = $$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Simulated payment</title>
            <style>body{font-family:system-ui,sans-serif;max-width:480px;margin:48px auto;padding:0 16px}
            .note{background:#fff8e1;padding:12px;border-radius:8px}button{padding:10px 18px;margin-right:8px}</style>
            </head><body>
            <h1>Simulated payment</h1>
            <p class="note">Dev only — no real card is charged. Stripe replaces this page once its keys are configured.</p>
            <p><strong>{{what}}</strong></p>
            <form method="post" action="/payments/fake/{{id}}/approve" style="display:inline"><button type="submit">Approve</button></form>
            <form method="post" action="/payments/fake/{{id}}/decline" style="display:inline"><button type="submit">Decline</button></form>
            </body></html>
            """;
        return Content(html, "text/html");
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(string id)
    {
        if (_provider is not FakePaymentProvider fake || fake.Approve(id) is not { } result) return NotFound();
        await _processor.ProcessAsync(result.Event);
        return Redirect(result.RedirectUrl);
    }

    [HttpPost("{id}/decline")]
    public IActionResult Decline(string id)
    {
        if (_provider is not FakePaymentProvider fake || fake.Decline(id) is not { } cancelUrl) return NotFound();
        return Redirect(cancelUrl);
    }
}
```

- [ ] **Step 5: DI wiring with the startup guard** (Stripe registration is added in Task 5)

```csharp
// src/Server/Payments/PaymentServiceCollectionExtensions.cs
namespace Stallions.Server.Payments;

public static class PaymentServiceCollectionExtensions
{
    /// <summary>Registers the configured payment provider. Throws at startup on an invalid configuration.</summary>
    public static IServiceCollection AddPayments(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(PaymentOptions.Section);
        services.Configure<PaymentOptions>(section);
        var options = section.Get<PaymentOptions>() ?? new PaymentOptions();

        var error = PaymentOptionsValidator.Validate(options, environment.IsProduction());
        if (error != null) throw new InvalidOperationException($"Payment configuration: {error}");

        if (options.Provider == PaymentOptions.ProviderFake)
        {
            services.AddSingleton<FakePaymentProvider>();
            services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<FakePaymentProvider>());
        }

        services.AddScoped<IPaymentEventProcessor, PaymentEventProcessor>();
        return services;
    }
}
```

In `Program.cs`, after `builder.Services.Configure<CheckoutOptions>(...)`:

```csharp
// Payments — Stripe or the dev-only fake (refused in Production)
builder.Services.AddPayments(builder.Configuration, builder.Environment);
```

with `using Stallions.Server.Payments;` at the top.

- [ ] **Step 6: Run green** — new tests pass; full build + test green. Start the server locally
(`dotnet run --project src/Server --launch-profile https`) and confirm it starts (Development → Fake).

- [ ] **Step 7: Commit**

```bash
git add src/Server/Payments/FakePaymentProvider.cs src/Server/Controllers/FakePaymentController.cs src/Server/Payments/PaymentServiceCollectionExtensions.cs src/Server/Program.cs tests/Server.Tests/Payments/FakePaymentProviderTests.cs
git commit -m "feat: dev-only fake payment provider with simulated payment page"
```

---

### Task 5: Stripe provider (hosted Checkout + verified webhook)

**Files:**
- Create: `src/Server/Payments/Stripe/IStripeApi.cs`, `src/Server/Payments/Stripe/StripeApi.cs`, `src/Server/Payments/Stripe/StripePaymentProvider.cs`
- Modify: `src/Server/Payments/PaymentServiceCollectionExtensions.cs`
- Test: `tests/Server.Tests/Payments/StripePaymentProviderTests.cs`

- [ ] **Step 1: The SDK wrapper interface** (lets tests run without network calls)

```csharp
// src/Server/Payments/Stripe/IStripeApi.cs
using Stripe;
using Stripe.Checkout;

namespace Stallions.Server.Payments.Stripe;

/// <summary>The handful of Stripe.net calls the platform makes — wrapped so the provider is unit-testable.</summary>
public interface IStripeApi
{
    Task<string> CreateCustomerAsync(CustomerCreateOptions options);
    /// <summary>Creates a hosted Checkout session and returns its URL.</summary>
    Task<string> CreateCheckoutSessionAsync(SessionCreateOptions options);
    Task<SetupIntent> GetSetupIntentAsync(string setupIntentId);
    Task<PaymentMethod> GetPaymentMethodAsync(string paymentMethodId);
    Task DetachPaymentMethodAsync(string paymentMethodId);
}
```

- [ ] **Step 2: Write the failing tests** — webhooks are signed exactly as Stripe does (HMAC-SHA256 of `"{timestamp}.{payload}"`).

```csharp
// tests/Server.Tests/Payments/StripePaymentProviderTests.cs
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Stallions.Server.Payments;
using Stallions.Server.Payments.Stripe;
using Stripe;
using Stripe.Checkout;

namespace Stallions.Server.Tests.Payments;

public class StripePaymentProviderTests
{
    private const string Secret = "whsec_test_secret";
    private readonly Mock<IStripeApi> _api = new();

    private StripePaymentProvider CreateSut() => new(_api.Object, Options.Create(new PaymentOptions
    {
        Provider = "Stripe",
        Stripe = new StripeSettings { SecretKey = "sk_test_unused", WebhookSigningSecret = Secret }
    }));

    private static string Sign(string payload, long? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var hash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{ts}.{payload}"))).ToLowerInvariant();
        return $"t={ts},v1={hash}";
    }

    private static string SessionCompleted(string sessionJson, string type = "checkout.session.completed") => $$"""
        {"id":"evt_123","object":"event","api_version":"2024-06-20","created":{{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
         "livemode":false,"type":"{{type}}","data":{"object":{{sessionJson}}}}
        """;

    private static string PaymentSession(Guid subscriptionId, string status = "paid") => $$"""
        {"id":"cs_1","object":"checkout.session","mode":"payment","payment_status":"{{status}}",
         "amount_total":99000,"currency":"aud","payment_intent":"pi_1",
         "metadata":{"kind":"listing-fee","subscriptionId":"{{subscriptionId}}"}}
        """;

    [Fact]
    public async Task Webhook_PaidListingFeeSession_MapsToListingFeePaid()
    {
        var subscriptionId = Guid.NewGuid();
        var payload = SessionCompleted(PaymentSession(subscriptionId));

        var evt = await CreateSut().ParseWebhookAsync(payload, Sign(payload));

        var paid = evt.Should().BeOfType<ListingFeePaidEvent>().Subject;
        paid.EventId.Should().Be("evt_123");
        paid.SubscriptionId.Should().Be(subscriptionId);
        paid.AmountCents.Should().Be(99000);
        paid.Currency.Should().Be("aud");
        paid.PaymentReference.Should().Be("pi_1");
    }

    [Fact]
    public async Task Webhook_UnpaidSession_IsUnhandled()
    {
        var payload = SessionCompleted(PaymentSession(Guid.NewGuid(), status: "unpaid"));

        (await CreateSut().ParseWebhookAsync(payload, Sign(payload))).Should().BeOfType<UnhandledPaymentEvent>();
    }

    [Fact]
    public async Task Webhook_CardSetupSession_LooksUpTheCardAndMapsToCardSaved()
    {
        var userId = Guid.NewGuid();
        var payload = SessionCompleted($$"""
            {"id":"cs_2","object":"checkout.session","mode":"setup","customer":"cus_9","setup_intent":"seti_1",
             "metadata":{"kind":"card-setup","userId":"{{userId}}"}}
            """);
        _api.Setup(a => a.GetSetupIntentAsync("seti_1")).ReturnsAsync(new SetupIntent { PaymentMethodId = "pm_9" });
        _api.Setup(a => a.GetPaymentMethodAsync("pm_9")).ReturnsAsync(new PaymentMethod
        {
            Id = "pm_9",
            Card = new PaymentMethodCard { Brand = "visa", Last4 = "4242", ExpMonth = 8, ExpYear = 2028 }
        });

        var evt = await CreateSut().ParseWebhookAsync(payload, Sign(payload));

        var saved = evt.Should().BeOfType<CardSavedEvent>().Subject;
        saved.UserId.Should().Be(userId);
        saved.CustomerId.Should().Be("cus_9");
        saved.PaymentMethodId.Should().Be("pm_9");
        (saved.Brand, saved.Last4, saved.ExpMonth, saved.ExpYear).Should().Be(("visa", "4242", 8, 2028));
    }

    [Fact]
    public async Task Webhook_OtherEventType_IsUnhandled()
    {
        var payload = SessionCompleted("""{"id":"cus_1","object":"customer"}""", type: "customer.created");

        (await CreateSut().ParseWebhookAsync(payload, Sign(payload))).Should().BeOfType<UnhandledPaymentEvent>();
    }

    [Fact]
    public async Task Webhook_TamperedBody_IsRejected()
    {
        var payload = SessionCompleted(PaymentSession(Guid.NewGuid()));
        var signature = Sign(payload);

        await FluentActions.Awaiting(() => CreateSut().ParseWebhookAsync(payload.Replace("99000", "1"), signature))
            .Should().ThrowAsync<PaymentSignatureException>();
    }

    [Fact]
    public async Task Webhook_StaleTimestamp_IsRejected()
    {
        var payload = SessionCompleted(PaymentSession(Guid.NewGuid()));
        var tenMinutesAgo = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();

        await FluentActions.Awaiting(() => CreateSut().ParseWebhookAsync(payload, Sign(payload, tenMinutesAgo)))
            .Should().ThrowAsync<PaymentSignatureException>();
    }

    [Fact]
    public async Task Webhook_MissingSignature_IsRejected() =>
        await FluentActions.Awaiting(() => CreateSut().ParseWebhookAsync("{}", null))
            .Should().ThrowAsync<PaymentSignatureException>();

    [Fact]
    public async Task ListingFeeSession_ChargesTheServerAmountInAudCents()
    {
        SessionCreateOptions? sent = null;
        _api.Setup(a => a.CreateCheckoutSessionAsync(It.IsAny<SessionCreateOptions>()))
            .Callback<SessionCreateOptions>(o => sent = o)
            .ReturnsAsync("https://checkout.stripe.com/c/pay/cs_1");
        var subscriptionId = Guid.NewGuid();

        var url = await CreateSut().CreateListingFeeSessionAsync(subscriptionId, 990m, "Listing fee — Snitzel, 2026 Season",
            "stud@x", "https://app/ok", "https://app/no");

        url.Should().Be("https://checkout.stripe.com/c/pay/cs_1");
        sent!.Mode.Should().Be("payment");
        sent.CustomerEmail.Should().Be("stud@x");
        var line = sent.LineItems.Should().ContainSingle().Subject;
        line.PriceData.Currency.Should().Be("aud");
        line.PriceData.UnitAmount.Should().Be(99000);
        sent.Metadata.Should().Contain("kind", "listing-fee").And.Contain("subscriptionId", subscriptionId.ToString());
    }

    [Fact]
    public async Task CardSetupSession_IsSetupModeForTheCustomer()
    {
        SessionCreateOptions? sent = null;
        _api.Setup(a => a.CreateCheckoutSessionAsync(It.IsAny<SessionCreateOptions>()))
            .Callback<SessionCreateOptions>(o => sent = o)
            .ReturnsAsync("https://checkout.stripe.com/c/setup/cs_2");
        var userId = Guid.NewGuid();

        await CreateSut().CreateCardSetupSessionAsync(userId, "cus_9", "https://app/ok", "https://app/no");

        sent!.Mode.Should().Be("setup");
        sent.Customer.Should().Be("cus_9");
        sent.Currency.Should().Be("aud");
        sent.Metadata.Should().Contain("kind", "card-setup").And.Contain("userId", userId.ToString());
    }
}
```

- [ ] **Step 3: Run red** — compile error.

- [ ] **Step 4: Implement the SDK wrapper**

```csharp
// src/Server/Payments/Stripe/StripeApi.cs
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Stallions.Server.Payments.Stripe;

public class StripeApi : IStripeApi
{
    private readonly IStripeClient _client;
    public StripeApi(IOptions<PaymentOptions> options) => _client = new StripeClient(options.Value.Stripe.SecretKey);

    public async Task<string> CreateCustomerAsync(CustomerCreateOptions options) =>
        (await new CustomerService(_client).CreateAsync(options)).Id;

    public async Task<string> CreateCheckoutSessionAsync(SessionCreateOptions options) =>
        (await new SessionService(_client).CreateAsync(options)).Url;

    public Task<SetupIntent> GetSetupIntentAsync(string setupIntentId) =>
        new SetupIntentService(_client).GetAsync(setupIntentId);

    public Task<PaymentMethod> GetPaymentMethodAsync(string paymentMethodId) =>
        new PaymentMethodService(_client).GetAsync(paymentMethodId);

    public async Task DetachPaymentMethodAsync(string paymentMethodId) =>
        await new PaymentMethodService(_client).DetachAsync(paymentMethodId);
}
```

- [ ] **Step 5: Implement the provider**

```csharp
// src/Server/Payments/Stripe/StripePaymentProvider.cs
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Stallions.Server.Payments.Stripe;

/// <summary>Stripe-hosted Checkout for card setup and listing-fee payments; verified webhooks.</summary>
public class StripePaymentProvider : IPaymentProvider
{
    private const string KindKey = "kind";
    private const string KindCardSetup = "card-setup";
    private const string KindListingFee = "listing-fee";

    private readonly IStripeApi _api;
    private readonly PaymentOptions _options;

    public StripePaymentProvider(IStripeApi api, IOptions<PaymentOptions> options)
    {
        _api = api;
        _options = options.Value;
    }

    public string Name => PaymentOptions.ProviderStripe;

    public Task<string> CreateCustomerAsync(Guid userId, string email, string name) =>
        _api.CreateCustomerAsync(new CustomerCreateOptions
        {
            Email = email,
            Name = name,
            Metadata = new Dictionary<string, string> { ["userId"] = userId.ToString() }
        });

    public Task<string> CreateCardSetupSessionAsync(Guid userId, string customerId, string successUrl, string cancelUrl)
    {
        var metadata = new Dictionary<string, string> { [KindKey] = KindCardSetup, ["userId"] = userId.ToString() };
        return _api.CreateCheckoutSessionAsync(new SessionCreateOptions
        {
            Mode = "setup",
            Customer = customerId,
            Currency = "aud",
            PaymentMethodTypes = new List<string> { "card" },
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            Metadata = metadata,
            SetupIntentData = new SessionSetupIntentDataOptions { Metadata = metadata }
        });
    }

    public Task<string> CreateListingFeeSessionAsync(
        Guid subscriptionId, decimal amountIncGst, string description, string? payerEmail,
        string successUrl, string cancelUrl)
    {
        var metadata = new Dictionary<string, string>
        {
            [KindKey] = KindListingFee,
            ["subscriptionId"] = subscriptionId.ToString()
        };
        return _api.CreateCheckoutSessionAsync(new SessionCreateOptions
        {
            Mode = "payment",
            CustomerEmail = payerEmail,
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = new List<SessionLineItemOptions>
            {
                new()
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "aud",
                        UnitAmount = (long)Math.Round(amountIncGst * 100m, MidpointRounding.AwayFromZero),
                        ProductData = new SessionLineItemPriceDataProductDataOptions { Name = description }
                    }
                }
            },
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            Metadata = metadata,
            PaymentIntentData = new SessionPaymentIntentDataOptions { Metadata = metadata, Description = description }
        });
    }

    public async Task<PaymentEvent> ParseWebhookAsync(string rawBody, string? signatureHeader)
    {
        Event stripeEvent;
        try
        {
            // Verifies the HMAC signature and rejects timestamps older than 5 minutes.
            stripeEvent = EventUtility.ConstructEvent(rawBody, signatureHeader ?? string.Empty,
                _options.Stripe.WebhookSigningSecret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            throw new PaymentSignatureException(ex.Message);
        }

        if (stripeEvent.Type != "checkout.session.completed" || stripeEvent.Data.Object is not Session session)
            return new UnhandledPaymentEvent(stripeEvent.Id, stripeEvent.Type);

        session.Metadata.TryGetValue(KindKey, out var kind);

        if (session.Mode == "setup" && kind == KindCardSetup)
        {
            var setupIntent = await _api.GetSetupIntentAsync(session.SetupIntentId);
            var method = await _api.GetPaymentMethodAsync(setupIntent.PaymentMethodId);
            return new CardSavedEvent(stripeEvent.Id, Guid.Parse(session.Metadata["userId"]), session.CustomerId,
                method.Id, method.Card.Brand, method.Card.Last4, (int)method.Card.ExpMonth, (int)method.Card.ExpYear);
        }

        if (session.Mode == "payment" && kind == KindListingFee && session.PaymentStatus == "paid")
            return new ListingFeePaidEvent(stripeEvent.Id, Guid.Parse(session.Metadata["subscriptionId"]),
                session.AmountTotal ?? 0, session.Currency, session.PaymentIntentId);

        return new UnhandledPaymentEvent(stripeEvent.Id, $"{stripeEvent.Type}:{session.Mode}:{session.PaymentStatus}");
    }

    public Task DetachCardAsync(string paymentMethodId) => _api.DetachPaymentMethodAsync(paymentMethodId);
}
```

> If the pinned Stripe.net version names any of these members differently (e.g. `SetupIntentId`,
> `PaymentIntentId`, `CustomerId` on `Session`; `ExpMonth`/`ExpYear` on `PaymentMethodCard`), use the
> SDK's names — the tests pin the behaviour, not the member names.

- [ ] **Step 6: Register Stripe in `AddPayments`** — add after the Fake branch:

```csharp
        else
        {
            services.AddSingleton<Stripe.IStripeApi, Stripe.StripeApi>();
            services.AddScoped<IPaymentProvider, Stripe.StripePaymentProvider>();
        }
```

(change the existing `if` to `if … else`; add `using Stallions.Server.Payments.Stripe;` and use the
short names if you prefer).

- [ ] **Step 7: Run green** — `dotnet test tests/Server.Tests --filter StripePaymentProviderTests` → 9 passed. Full build + test green.

- [ ] **Step 8: Commit**

```bash
git add src/Server/Payments/Stripe src/Server/Payments/PaymentServiceCollectionExtensions.cs tests/Server.Tests/Payments/StripePaymentProviderTests.cs
git commit -m "feat: Stripe payment provider with hosted Checkout and verified webhooks"
```

---

### Task 6: Card service, card endpoints and the Stripe webhook endpoint

**Files:**
- Create: `src/Shared/DTOs/Payments/SavedCardDto.cs`, `src/Shared/DTOs/Payments/PaymentRedirectDto.cs`
- Create: `src/Server/Services/ICardService.cs`, `src/Server/Services/CardService.cs`, `src/Server/Controllers/PaymentsController.cs`
- Modify: `src/Server/Program.cs`
- Test: `tests/Server.Tests/Services/CardServiceTests.cs`, `tests/Server.Tests/Controllers/PaymentsControllerWebhookTests.cs`

- [ ] **Step 1: DTOs**

```csharp
// src/Shared/DTOs/Payments/SavedCardDto.cs
namespace Stallions.Shared.DTOs.Payments;

public class SavedCardDto
{
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public int ExpMonth { get; set; }
    public int ExpYear { get; set; }
    /// <summary>False once the card is past the end of its expiry month — bidding is then blocked.</summary>
    public bool IsValid { get; set; }
}
```

```csharp
// src/Shared/DTOs/Payments/PaymentRedirectDto.cs
namespace Stallions.Shared.DTOs.Payments;

/// <summary>Where to send the browser next: the provider's hosted page.</summary>
public class PaymentRedirectDto
{
    public string Url { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Write the failing service tests**

```csharp
// tests/Server.Tests/Services/CardServiceTests.cs
using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Payments;
using Stallions.Server.Services;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class CardServiceTests
{
    private readonly Mock<ISavedCardRepository> _cards = new();
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IUserService> _users = new();
    private readonly Mock<IPaymentProvider> _provider = new();

    private CardService CreateSut() => new(_cards.Object, _userRepo.Object, _users.Object, _provider.Object);

    public CardServiceTests() => _provider.SetupGet(p => p.Name).Returns("Stripe");

    private User SignedIn(UserRole role, string? customerId = null, string customerProvider = "Stripe")
    {
        var user = new User
        {
            Id = Guid.NewGuid(), Email = "b@x", DisplayName = "B", Role = role,
            Status = UserStatus.Active, PaymentCustomerId = customerId,
            PaymentCustomerProvider = customerId == null ? null : customerProvider
        };
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(user);
        return user;
    }

    [Fact]
    public async Task StartSetup_CustomerFromAnotherProvider_IsReplaced()
    {
        // e.g. dev data created with the fake provider, now running on Stripe
        var buyer = SignedIn(UserRole.Buyer, customerId: "cus_fake_1", customerProvider: "Fake");
        _provider.Setup(p => p.CreateCustomerAsync(buyer.Id, "b@x", "B")).ReturnsAsync("cus_real");
        _provider.Setup(p => p.CreateCardSetupSessionAsync(buyer.Id, "cus_real", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("https://pay/setup");

        await CreateSut().StartSetupAsync("https://ok", "https://no");

        buyer.PaymentCustomerId.Should().Be("cus_real");
        buyer.PaymentCustomerProvider.Should().Be("Stripe");
    }

    [Fact]
    public async Task StartSetup_FirstTime_CreatesAndStoresTheCustomer()
    {
        var buyer = SignedIn(UserRole.Buyer);
        _provider.Setup(p => p.CreateCustomerAsync(buyer.Id, "b@x", "B")).ReturnsAsync("cus_new");
        _provider.Setup(p => p.CreateCardSetupSessionAsync(buyer.Id, "cus_new", "https://ok", "https://no"))
            .ReturnsAsync("https://pay/setup");

        var result = await CreateSut().StartSetupAsync("https://ok", "https://no");

        result.Value!.Url.Should().Be("https://pay/setup");
        buyer.PaymentCustomerId.Should().Be("cus_new");
        buyer.PaymentCustomerProvider.Should().Be("Stripe");
        _userRepo.Verify(r => r.UpdateAsync(buyer), Times.Once);
    }

    [Fact]
    public async Task StartSetup_ReusesAnExistingCustomer()
    {
        var buyer = SignedIn(UserRole.Buyer, customerId: "cus_old");
        _provider.Setup(p => p.CreateCardSetupSessionAsync(buyer.Id, "cus_old", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("https://pay/setup");

        await CreateSut().StartSetupAsync("https://ok", "https://no");

        _provider.Verify(p => p.CreateCustomerAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(UserRole.StudFarmAdmin)]
    [InlineData(UserRole.Staff)]
    public async Task StartSetup_NonBuyer_IsForbidden(UserRole role)
    {
        SignedIn(role);

        (await CreateSut().StartSetupAsync("https://ok", "https://no")).HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GetMine_ReturnsCardWithValidity()
    {
        var buyer = SignedIn(UserRole.Buyer);
        _cards.Setup(c => c.GetByUserIdAsync(buyer.Id)).ReturnsAsync(new SavedCard
            { UserId = buyer.Id, Provider = "Stripe", Brand = "visa", Last4 = "4242", ExpMonth = 1, ExpYear = 2020 });

        var result = await CreateSut().GetMineAsync();

        result.Value!.Last4.Should().Be("4242");
        result.Value.IsValid.Should().BeFalse("it expired in January 2020");
    }

    [Fact]
    public async Task GetMine_WithoutCard_IsNotFound()
    {
        SignedIn(UserRole.Buyer);

        (await CreateSut().GetMineAsync()).HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task HasValidCard_FalseWhenNoneOrExpired_TrueWhenCurrent()
    {
        var none = Guid.NewGuid();
        var expired = Guid.NewGuid();
        var current = Guid.NewGuid();
        var otherProvider = Guid.NewGuid();
        _cards.Setup(c => c.GetByUserIdAsync(expired)).ReturnsAsync(new SavedCard { Provider = "Stripe", ExpMonth = 1, ExpYear = 2020 });
        _cards.Setup(c => c.GetByUserIdAsync(current)).ReturnsAsync(new SavedCard { Provider = "Stripe", ExpMonth = 12, ExpYear = DateTime.UtcNow.Year + 2 });
        _cards.Setup(c => c.GetByUserIdAsync(otherProvider)).ReturnsAsync(new SavedCard { Provider = "Fake", ExpMonth = 12, ExpYear = DateTime.UtcNow.Year + 2 });

        (await CreateSut().HasValidCardAsync(none)).Should().BeFalse();
        (await CreateSut().HasValidCardAsync(expired)).Should().BeFalse();
        (await CreateSut().HasValidCardAsync(current)).Should().BeTrue();
        (await CreateSut().HasValidCardAsync(otherProvider)).Should().BeFalse("a card saved with another provider can't be charged by this one");
    }
}
```

- [ ] **Step 3: Write the failing webhook endpoint tests**

```csharp
// tests/Server.Tests/Controllers/PaymentsControllerWebhookTests.cs
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Stallions.Server.Controllers;
using Stallions.Server.Payments;
using Stallions.Server.Services;

namespace Stallions.Server.Tests.Controllers;

public class PaymentsControllerWebhookTests
{
    private readonly Mock<IPaymentProvider> _provider = new();
    private readonly Mock<IPaymentEventProcessor> _processor = new();

    private PaymentsController CreateSut(string body, string? signature)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        if (signature != null) context.Request.Headers["Stripe-Signature"] = signature;
        return new PaymentsController(new Mock<ICardService>().Object, _provider.Object, _processor.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    [Fact]
    public async Task ValidEvent_IsProcessedAndAcknowledged()
    {
        _provider.SetupGet(p => p.Name).Returns("Stripe");
        var evt = new UnhandledPaymentEvent("evt_1", "x");
        _provider.Setup(p => p.ParseWebhookAsync("{\"a\":1}", "sig")).ReturnsAsync(evt);

        var result = await CreateSut("{\"a\":1}", "sig").StripeWebhook();

        result.Should().BeOfType<OkResult>();
        _processor.Verify(p => p.ProcessAsync(evt), Times.Once);
    }

    [Fact]
    public async Task BadSignature_Returns400AndProcessesNothing()
    {
        _provider.SetupGet(p => p.Name).Returns("Stripe");
        _provider.Setup(p => p.ParseWebhookAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ThrowsAsync(new PaymentSignatureException("bad"));

        var result = await CreateSut("{}", "bad").StripeWebhook();

        result.Should().BeOfType<BadRequestObjectResult>();
        _processor.Verify(p => p.ProcessAsync(It.IsAny<PaymentEvent>()), Times.Never);
    }

    [Fact]
    public async Task WhenProviderIsNotStripe_Returns404()
    {
        _provider.SetupGet(p => p.Name).Returns("Fake");

        (await CreateSut("{}", "sig").StripeWebhook()).Should().BeOfType<NotFoundResult>();
    }
}
```

- [ ] **Step 4: Run red** — compile errors.

- [ ] **Step 5: Implement the card service**

```csharp
// src/Server/Services/ICardService.cs
using Stallions.Shared.DTOs.Payments;

namespace Stallions.Server.Services;

public interface ICardService
{
    Task<ServiceResult<SavedCardDto>> GetMineAsync();
    Task<ServiceResult<PaymentRedirectDto>> StartSetupAsync(string successUrl, string cancelUrl);
    /// <summary>True when the user has a saved card that hasn't passed the end of its expiry month.</summary>
    Task<bool> HasValidCardAsync(Guid userId);
}
```

```csharp
// src/Server/Services/CardService.cs
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Payments;
using Stallions.Shared.DTOs.Payments;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

/// <summary>
/// Buyers' saved cards. Reads and starts the hosted setup page only — the card itself is saved by
/// PaymentEventProcessor when the provider confirms it.
/// </summary>
public class CardService : ICardService
{
    private readonly ISavedCardRepository _cards;
    private readonly IUserRepository _userRepo;
    private readonly IUserService _users;
    private readonly IPaymentProvider _provider;

    public CardService(ISavedCardRepository cards, IUserRepository userRepo, IUserService users, IPaymentProvider provider)
    {
        _cards = cards; _userRepo = userRepo; _users = users; _provider = provider;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<ServiceResult<SavedCardDto>> GetMineAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<SavedCardDto>.Forbidden();

        var card = await _cards.GetByUserIdAsync(caller.Id);
        return card == null
            ? ServiceResult<SavedCardDto>.NotFound("No card saved.")
            : ServiceResult<SavedCardDto>.Ok(new SavedCardDto
            {
                Brand = card.Brand, Last4 = card.Last4, ExpMonth = card.ExpMonth, ExpYear = card.ExpYear,
                IsValid = IsUsable(card)
            });
    }

    public async Task<ServiceResult<PaymentRedirectDto>> StartSetupAsync(string successUrl, string cancelUrl)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.Buyer)
            return ServiceResult<PaymentRedirectDto>.Forbidden("Only buyers can save a card.");

        // A customer id issued by a different provider (e.g. the dev fake) can't be used here.
        if (string.IsNullOrEmpty(caller.PaymentCustomerId) || caller.PaymentCustomerProvider != _provider.Name)
        {
            caller.PaymentCustomerId = await _provider.CreateCustomerAsync(caller.Id, caller.Email, caller.DisplayName);
            caller.PaymentCustomerProvider = _provider.Name;
            await _userRepo.UpdateAsync(caller);
        }

        var url = await _provider.CreateCardSetupSessionAsync(caller.Id, caller.PaymentCustomerId, successUrl, cancelUrl);
        return ServiceResult<PaymentRedirectDto>.Ok(new PaymentRedirectDto { Url = url });
    }

    public async Task<bool> HasValidCardAsync(Guid userId)
    {
        var card = await _cards.GetByUserIdAsync(userId);
        return card != null && IsUsable(card);
    }

    // Unexpired, and saved with the provider that will charge it.
    private bool IsUsable(SavedCard card) => card.Provider == _provider.Name && card.IsValidOn(Today);
}
```

- [ ] **Step 6: Implement the controller**

```csharp
// src/Server/Controllers/PaymentsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Payments;
using Stallions.Server.Services;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly ICardService _cards;
    private readonly IPaymentProvider _provider;
    private readonly IPaymentEventProcessor _processor;

    public PaymentsController(ICardService cards, IPaymentProvider provider, IPaymentEventProcessor processor)
    {
        _cards = cards;
        _provider = provider;
        _processor = processor;
    }

    [HttpGet("card")]
    [Authorize(Policy = "BuyerOnly")]
    public async Task<IActionResult> GetCard()
    {
        var r = await _cards.GetMineAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("card/setup-session")]
    [Authorize(Policy = "BuyerOnly")]
    public async Task<IActionResult> StartCardSetup()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var r = await _cards.StartSetupAsync($"{baseUrl}/account/card?result=success", $"{baseUrl}/account/card?result=cancelled");
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    // Public, but nothing is processed unless the provider's signature verifies. Processing
    // errors propagate as 500 so Stripe retries; duplicates are ignored by the processor.
    [HttpPost("webhook/stripe")]
    [AllowAnonymous]
    public async Task<IActionResult> StripeWebhook()
    {
        if (_provider.Name != PaymentOptions.ProviderStripe) return NotFound();

        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync();

        PaymentEvent paymentEvent;
        try
        {
            paymentEvent = await _provider.ParseWebhookAsync(rawBody, Request.Headers["Stripe-Signature"].FirstOrDefault());
        }
        catch (PaymentSignatureException)
        {
            return BadRequest("Invalid signature.");
        }

        await _processor.ProcessAsync(paymentEvent);
        return Ok();
    }
}
```

Register in `Program.cs` with the other services: `builder.Services.AddScoped<ICardService, CardService>();`

- [ ] **Step 7: Run green** — the new tests pass; full build + test green.

- [ ] **Step 8: Commit**

```bash
git add src/Shared/DTOs/Payments src/Server/Services/ICardService.cs src/Server/Services/CardService.cs src/Server/Controllers/PaymentsController.cs src/Server/Program.cs tests/Server.Tests/Services/CardServiceTests.cs tests/Server.Tests/Controllers/PaymentsControllerWebhookTests.cs
git commit -m "feat: buyer card endpoints and signed Stripe webhook endpoint"
```

---

### Task 7: Bid gate — a valid saved card is required to bid

**Files:**
- Modify: `src/Server/Services/BidService.cs`, `tests/Server.Tests/Services/BidServiceTests.cs`

- [ ] **Step 1: Write the failing tests** — in `BidServiceTests`, add a card-service mock that defaults to "has a valid card" so existing tests keep passing, and pass it to the service:

```csharp
    private readonly Mock<ICardService> _cardsMock = new();

    public BidServiceTests()
    {
        _cardsMock.Setup(c => c.HasValidCardAsync(It.IsAny<Guid>())).ReturnsAsync(true);
    }

    private BidService CreateSut() =>
        new(_bidRepoMock.Object, _listingRepoMock.Object, _usersMock.Object, CreateInMemoryDb(),
            _termsRepoMock.Object, _cardsMock.Object);
```

(replace the existing `CreateSut`), then add:

```csharp
    [Fact]
    public async Task PlaceBid_WithoutAValidSavedCard_ReturnsBadRequest()
    {
        var buyer = ActiveBuyer();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        _cardsMock.Setup(c => c.HasValidCardAsync(buyer.Id)).ReturnsAsync(false);
        var auction = OpenAuction();
        _listingRepoMock.Setup(r => r.GetAuctionByIdAsync(auction.Id)).ReturnsAsync(auction);

        var result = await CreateSut().PlaceBidAsync(auction.Id, new PlaceBidRequest { AmountIncGst = 5000m });

        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Be("Save a card before bidding.");
        _bidRepoMock.Verify(r => r.AddAsync(It.IsAny<Bid>()), Times.Never);
    }
```

- [ ] **Step 2: Run red** — compile error (constructor).

- [ ] **Step 3: Implement** — `BidService` takes `ICardService cards` as a new last constructor parameter (store in `_cards`), and in `PlaceBidAsync`, right after the T&C check:

```csharp
        // A valid saved card is required so the buyer fee can be charged automatically on a win.
        if (!await _cards.HasValidCardAsync(caller.Id))
            return ServiceResult<BidDto>.BadRequest("Save a card before bidding.");
```

- [ ] **Step 4: Run green**; full build + test green.

- [ ] **Step 5: Commit**

```bash
git add src/Server/Services/BidService.cs tests/Server.Tests/Services/BidServiceTests.cs
git commit -m "feat: require a valid saved card to bid"
```

---

### Task 8: Stud self-serve activation (listing fee by card)

**Files:**
- Create: `src/Shared/DTOs/Subscriptions/ActivateStallionRequest.cs`
- Modify: `src/Server/Services/ISubscriptionService.cs`, `src/Server/Services/SubscriptionService.cs`, `src/Server/Controllers/SubscriptionsController.cs`
- Test: `tests/Server.Tests/Services/SubscriptionServiceTests.cs`

- [ ] **Step 1: DTO**

```csharp
// src/Shared/DTOs/Subscriptions/ActivateStallionRequest.cs
namespace Stallions.Shared.DTOs.Subscriptions;

// StudFarmAdmin: activate one of your stallions for the open season by paying the listing fee.
public class ActivateStallionRequest
{
    public Guid StallionId { get; set; }
}
```

- [ ] **Step 2: Write the failing tests** — in `SubscriptionServiceTests` add a provider mock and pass it as the new last constructor argument:

```csharp
    private readonly Mock<IPaymentProvider> _provider = new();
```

```csharp
    private SubscriptionService CreateSut() => new(
        new SubscriptionRepository(_db),
        new StallionRepository(_db),
        new SeasonRepository(_db),
        new StudFarmRepository(_db),
        new PlatformSettingsRepository(_db),
        new AuditLogRepository(_db),
        _users.Object,
        _provider.Object);
```

(add `using Stallions.Server.Payments;`), then add the tests:

```csharp
    // ── Self-serve activation ───────────────────────────────────────────────

    private void PaymentPageReturns(string url) =>
        _provider.Setup(p => p.CreateListingFeeSessionAsync(It.IsAny<Guid>(), It.IsAny<decimal>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(url);

    private Task<ServiceResult<PaymentRedirectDto>> ActivateAs(User user, Stallion stallion)
    {
        SignInAs(user);
        return CreateSut().ActivateAsync(new ActivateStallionRequest { StallionId = stallion.Id },
            "https://app/ok", "https://app/no");
    }

    [Fact]
    public async Task Activate_OwnStallion_CreatesPendingAtStandardFeeAndStartsPayment()
    {
        PaymentPageReturns("https://pay/fee");

        var result = await ActivateAs(_studUserA, _stallionA);

        result.Value!.Url.Should().Be("https://pay/fee");
        var sub = await _db.StallionSeasonSubscriptions.SingleAsync();
        sub.Status.Should().Be(SubscriptionStatus.Pending);
        (sub.FeeIncGst, sub.FeeExGst, sub.GstAmount).Should().Be((990m, 900m, 90m));
        _provider.Verify(p => p.CreateListingFeeSessionAsync(sub.Id, 990m,
            "Listing fee — Stallion A, 2026 Season", _studUserA.Email, "https://app/ok", "https://app/no"), Times.Once);
    }

    [Fact]
    public async Task Activate_ReusesStaffPendingSubscriptionWithItsDiscount()
    {
        await CreatePendingAsync(_stallionA, feeOverride: 495m);
        PaymentPageReturns("https://pay/fee");

        await ActivateAs(_studUserA, _stallionA);

        (await _db.StallionSeasonSubscriptions.CountAsync()).Should().Be(1);
        _provider.Verify(p => p.CreateListingFeeSessionAsync(It.IsAny<Guid>(), 495m,
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Activate_AnotherFarmsStallion_IsNotFound()
    {
        (await ActivateAs(_studUserA, _stallionB)).HttpStatusCode.Should().Be(404);
        (await _db.StallionSeasonSubscriptions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Activate_AlreadyPaid_IsBadRequest()
    {
        var created = await CreatePendingAsync(_stallionA);
        await CreateSut().MarkPaidAsync(created.Id, new MarkSubscriptionPaidRequest { PaymentMethod = "Invoice" });

        var result = await ActivateAs(_studUserA, _stallionA);

        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Contain("already active");
    }

    [Fact]
    public async Task Activate_WithNoOpenSeason_IsBadRequest()
    {
        _season.IsOpen = false;
        await _db.SaveChangesAsync();

        (await ActivateAs(_studUserA, _stallionA)).HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Activate_AsStaff_IsForbidden() =>
        (await ActivateAs(_staff, _stallionA)).HttpStatusCode.Should().Be(403);
```

(add `using Stallions.Shared.DTOs.Payments;` and `using Stallions.Server.Services;` if missing). The
test fixture's `_season` is named "2026 Season" and is open, and `_stallionA` belongs to `_farmA` / `_studUserA`.

- [ ] **Step 3: Run red** — compile errors.

- [ ] **Step 4: Implement** — `ISubscriptionService` gains:

```csharp
    Task<ServiceResult<PaymentRedirectDto>> ActivateAsync(ActivateStallionRequest request, string successUrl, string cancelUrl);
```

`SubscriptionService` takes `IPaymentProvider provider` as a new last constructor parameter (store in
`_provider`; add `using Stallions.Server.Payments;` and `using Stallions.Shared.DTOs.Payments;`), and adds:

```csharp
    public async Task<ServiceResult<PaymentRedirectDto>> ActivateAsync(
        ActivateStallionRequest request, string successUrl, string cancelUrl)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.StudFarmAdmin)
            return ServiceResult<PaymentRedirectDto>.Forbidden("Only stud farm admins can activate stallions.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult<PaymentRedirectDto>.Forbidden("No stud farm found for the current user.");

        var stallion = await _stallionRepo.GetByIdAsync(request.StallionId);
        if (stallion == null || stallion.StudFarmId != farm.Id)
            return ServiceResult<PaymentRedirectDto>.NotFound("Stallion not found.");
        if (!stallion.IsActive)
            return ServiceResult<PaymentRedirectDto>.BadRequest($"{stallion.Name} is inactive.");

        var season = await _seasonRepo.GetCurrentOpenSeasonAsync();
        if (season == null)
            return ServiceResult<PaymentRedirectDto>.BadRequest("No season is open. Contact Stallions Australia.");

        var subscription = await _repo.GetByStallionAndSeasonAsync(stallion.Id, season.Id);
        if (subscription?.Status is SubscriptionStatus.Paid or SubscriptionStatus.Waived)
            return ServiceResult<PaymentRedirectDto>.BadRequest($"{stallion.Name} is already active for {season.Name}.");

        if (subscription == null)
        {
            // Self-serve: the standard fee. A Pending subscription Staff created (e.g. with an
            // intro-offer discount) is reused instead, so its amount and notes are kept.
            var fee = GstBreakdown.FromIncGst((await _settingsRepo.GetAsync()).StandardListingFeeIncGst);
            subscription = new StallionSeasonSubscription
            {
                StallionId = stallion.Id,
                SeasonId = season.Id,
                StudFarmId = farm.Id,
                FeeIncGst = fee.IncGst,
                FeeExGst = fee.ExGst,
                GstAmount = fee.Gst,
                Status = SubscriptionStatus.Pending,
                CreatedByUserId = caller.Id
            };
            try
            {
                await _repo.AddAsync(subscription);
                await _auditRepo.LogAsync(AuditEntity, subscription.Id, "CreateSubscription", caller.Id,
                    JsonSerializer.Serialize(new { subscription.StallionId, subscription.SeasonId, subscription.FeeIncGst, SelfServe = true }));
            }
            catch (DbUpdateException)
            {
                // A concurrent click created it first — use that one.
                subscription = await _repo.GetByStallionAndSeasonAsync(stallion.Id, season.Id);
                if (subscription == null) throw;
            }
        }

        if (subscription.FeeIncGst <= 0)
            return ServiceResult<PaymentRedirectDto>.BadRequest("This subscription has no fee to pay. Contact Stallions Australia.");

        var url = await _provider.CreateListingFeeSessionAsync(subscription.Id, subscription.FeeIncGst,
            $"Listing fee — {stallion.Name}, {season.Name}", caller.Email, successUrl, cancelUrl);
        return ServiceResult<PaymentRedirectDto>.Ok(new PaymentRedirectDto { Url = url });
    }
```

Add the endpoint to `SubscriptionsController`:

```csharp
    [HttpPost("activate")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> Activate([FromBody] ActivateStallionRequest request)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var r = await _subscriptions.ActivateAsync(request,
            $"{baseUrl}/admin/stallions?payment=success", $"{baseUrl}/admin/stallions?payment=cancelled");
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
```

Also fix the `ListingServiceTests` / anything else constructing `SubscriptionService` (only
`SubscriptionServiceTests` does at the time of writing).

- [ ] **Step 5: Run green**; full build + test green.

- [ ] **Step 6: Commit**

```bash
git add src/Shared/DTOs/Subscriptions/ActivateStallionRequest.cs src/Server/Services/ISubscriptionService.cs src/Server/Services/SubscriptionService.cs src/Server/Controllers/SubscriptionsController.cs tests/Server.Tests/Services/SubscriptionServiceTests.cs
git commit -m "feat: studs activate a stallion by paying the listing fee by card"
```

---

### Task 9: Client — card page, bid gate prompt, stud Activate button

**Files:**
- Modify: `src/Server/Options/CheckoutOptions.cs`, `src/Server/appsettings.json`, `src/Shared/DTOs/Checkout/BuyerFeeDisclosureDto.cs`, `src/Server/Controllers/DisclosuresController.cs`
- Create: `src/Client/Services/PaymentsApiService.cs`, `src/Client/Pages/AccountCard.razor`, `src/Client/Pages/AccountCard.razor.css`
- Modify: `src/Client/Program.cs`, `src/Client/_Imports.razor`, `src/Client/Layout/NavBar.razor`, `src/Client/Pages/ListingDetail.razor`, `src/Client/Services/SubscriptionApiService.cs`, `src/Client/Pages/Admin/AdminStallions.razor`
- Test: `tests/Client.Tests/Pages/AccountCardTests.cs`, `tests/Client.Tests/Pages/AdminStallionsTests.cs`, `tests/Client.Tests/Pages/ListingDetailTests.cs`

- [ ] **Step 1: Configured card wording** (never hardcoded in the client)

`CheckoutOptions`: add `public string SavedCardExplanation { get; set; } = string.Empty;`

`appsettings.json` `"Checkout"` section: add

```json
    "SavedCardExplanation": "Your saved card is charged the buyer fee automatically when you win an auction or a stud accepts your offer. The nomination price is not charged to this card — you pay the balance directly to the stud, under the stud's own terms."
```

`BuyerFeeDisclosureDto`: add `public string SavedCardExplanation { get; set; } = string.Empty;`
`DisclosuresController.GetBuyerFee`: add `SavedCardExplanation = _options.Value.SavedCardExplanation`.

- [ ] **Step 2: Client API services**

```csharp
// src/Client/Services/PaymentsApiService.cs
using System.Net;
using System.Net.Http.Json;
using Stallions.Shared.DTOs.Payments;

namespace Stallions.Client.Services;

/// <summary>The signed-in buyer's saved card.</summary>
public class PaymentsApiService
{
    private readonly HttpClient _http;
    public PaymentsApiService(HttpClient http) => _http = http;

    /// <summary>The saved card, or null if none.</summary>
    public virtual async Task<SavedCardDto?> GetMyCardAsync()
    {
        var r = await _http.GetAsync("api/payments/card");
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return await r.Content.ReadFromJsonAsync<SavedCardDto>();
    }

    /// <summary>Starts the hosted card page; returns the URL to send the browser to.</summary>
    public virtual async Task<string> StartCardSetupAsync()
    {
        var r = await _http.PostAsync("api/payments/card/setup-session", null);
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return (await r.Content.ReadFromJsonAsync<PaymentRedirectDto>())?.Url
               ?? throw new ApiException(500, "Empty response.");
    }
}
```

Add to `SubscriptionApiService`:

```csharp
    /// <summary>Starts the listing-fee payment for one of the stud's stallions; returns the URL to send the browser to.</summary>
    public virtual async Task<string> ActivateAsync(Guid stallionId)
    {
        var r = await _http.PostAsJsonAsync("api/subscriptions/activate", new ActivateStallionRequest { StallionId = stallionId });
        if (!r.IsSuccessStatusCode)
            throw new ApiException((int)r.StatusCode, await ServiceHelpers.ExtractErrorMessageAsync(r));
        return (await r.Content.ReadFromJsonAsync<Stallions.Shared.DTOs.Payments.PaymentRedirectDto>())?.Url
               ?? throw new ApiException(500, "Empty response.");
    }
```

`Program.cs` (client), with the other authenticated services:

```csharp
builder.Services.AddHttpClient<PaymentsApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
```

`_Imports.razor`: add `@using Stallions.Shared.DTOs.Payments`.

- [ ] **Step 3: Write the failing bUnit tests**

```csharp
// tests/Client.Tests/Pages/AccountCardTests.cs
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Checkout;
using Stallions.Shared.DTOs.Payments;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class AccountCardTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };
    private readonly Mock<PaymentsApiService> _payments = new(MockBehavior.Loose, Http());

    private IRenderedComponent<AccountCard> Render(string? query = null)
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        Services.AddSingleton(_payments.Object);

        var listingApi = new Mock<ListingApiService>(MockBehavior.Loose, Http());
        listingApi.Setup(s => s.GetBuyerFeeDisclosureAsync()).ReturnsAsync(new BuyerFeeDisclosureDto
            { SavedCardExplanation = "CONFIGURED: charged the buyer fee automatically." });
        Services.AddSingleton(listingApi.Object);

        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "B", Email = "b@x", Role = "Buyer", Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));

        if (query != null) Services.GetRequiredService<NavigationManager>().NavigateTo($"/account/card?{query}");
        return RenderComponent<AccountCard>(p => p.Add(c => c.PollInterval, TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public void NoCard_ShowsAddCardAndTheConfiguredDisclosure()
    {
        _payments.Setup(s => s.GetMyCardAsync()).ReturnsAsync((SavedCardDto?)null);

        var cut = Render();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No card saved"));
        cut.Markup.Should().Contain("Add card").And.Contain("CONFIGURED: charged the buyer fee automatically.");
    }

    [Fact]
    public void SavedCard_ShowsBrandLast4AndExpiry()
    {
        _payments.Setup(s => s.GetMyCardAsync()).ReturnsAsync(new SavedCardDto
            { Brand = "visa", Last4 = "4242", ExpMonth = 8, ExpYear = 2028, IsValid = true });

        var cut = Render();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("•••• 4242"));
        cut.Markup.Should().Contain("08/28").And.Contain("Replace card");
    }

    [Fact]
    public void ExpiredCard_IsFlagged()
    {
        _payments.Setup(s => s.GetMyCardAsync()).ReturnsAsync(new SavedCardDto
            { Brand = "visa", Last4 = "4242", ExpMonth = 1, ExpYear = 2020, IsValid = false });

        var cut = Render();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("expired"));
    }

    [Fact]
    public void AddCard_SendsTheBrowserToTheHostedPage()
    {
        _payments.Setup(s => s.GetMyCardAsync()).ReturnsAsync((SavedCardDto?)null);
        _payments.Setup(s => s.StartCardSetupAsync()).ReturnsAsync("https://checkout.example/setup");
        var cut = Render();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Add card"));

        cut.FindAll("button").Single(b => b.TextContent.Contains("Add card")).Click();

        cut.WaitForAssertion(() =>
            Services.GetRequiredService<NavigationManager>().Uri.Should().Be("https://checkout.example/setup"));
    }

    [Fact]
    public void ReturningFromHostedPage_PollsUntilTheCardArrives()
    {
        _payments.SetupSequence(s => s.GetMyCardAsync())
            .ReturnsAsync((SavedCardDto?)null)
            .ReturnsAsync((SavedCardDto?)null)
            .ReturnsAsync(new SavedCardDto { Brand = "visa", Last4 = "4242", ExpMonth = 8, ExpYear = 2028, IsValid = true });

        var cut = Render("result=success");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("•••• 4242"), TimeSpan.FromSeconds(5));
    }
}
```

```csharp
// tests/Client.Tests/Pages/AdminStallionsTests.cs
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Admin;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Directory;
using Stallions.Shared.DTOs.Seasons;
using Stallions.Shared.DTOs.Settings;
using Stallions.Shared.DTOs.Stallions;
using Stallions.Shared.DTOs.Subscriptions;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class AdminStallionsTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };
    private readonly Mock<SubscriptionApiService> _subs = new(MockBehavior.Loose, Http());
    private readonly StallionSummaryDto _snitzel = new() { Id = Guid.NewGuid(), Name = "Snitzel", IsActive = true };
    private readonly SeasonDto _season = new() { Id = Guid.NewGuid(), Name = "2026 Season", IsOpen = true };

    private IRenderedComponent<AdminStallions> Render(params SubscriptionDto[] subscriptions)
    {
        this.AddTestAuthorization().SetAuthorized("stud@example.com");
        var admin = new Mock<AdminApiService>(MockBehavior.Loose, Http());
        admin.Setup(a => a.GetMyStallionsAsync()).ReturnsAsync([_snitzel]);
        admin.Setup(a => a.GetAuthorizedStallionsAsync()).ReturnsAsync(new AuthorizedStallionsDto { IsLinked = false });
        admin.Setup(a => a.GetSeasonsAsync()).ReturnsAsync([_season]);
        Services.AddSingleton(admin.Object);

        _subs.Setup(s => s.GetMineAsync()).ReturnsAsync(subscriptions.ToList());
        Services.AddSingleton(_subs.Object);

        var settings = new Mock<PlatformSettingsApiService>(MockBehavior.Loose, Http());
        settings.Setup(s => s.GetAsync()).ReturnsAsync(new PlatformSettingsDto { StandardListingFeeIncGst = 990m });
        Services.AddSingleton(settings.Object);

        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "S", Email = "s@x", Role = "StudFarmAdmin", Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));

        var cut = RenderComponent<AdminStallions>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Snitzel"));
        return cut;
    }

    private SubscriptionDto Subscription(string status, decimal fee) => new()
        { StallionId = _snitzel.Id, SeasonId = _season.Id, Status = status, FeeIncGst = fee };

    [Fact]
    public void NoSubscription_OffersActivationAtTheStandardFee()
    {
        var cut = Render();

        cut.Markup.Should().Contain("Activate for 2026 Season — $990");
    }

    [Fact]
    public void PendingSubscription_OffersActivationAtItsOwnFee()
    {
        var cut = Render(Subscription("Pending", 495m));

        cut.Markup.Should().Contain("Activate for 2026 Season — $495");
    }

    [Fact]
    public void PaidSubscription_ShowsPaidAndNoActivateButton()
    {
        var cut = Render(Subscription("Paid", 990m));

        cut.Markup.Should().Contain("Paid").And.NotContain("Activate for");
    }

    [Fact]
    public void Activate_SendsTheBrowserToThePaymentPage()
    {
        _subs.Setup(s => s.ActivateAsync(_snitzel.Id)).ReturnsAsync("https://checkout.example/pay");
        var cut = Render();

        cut.FindAll("button").Single(b => b.TextContent.Contains("Activate for")).Click();

        cut.WaitForAssertion(() =>
            Services.GetRequiredService<NavigationManager>().Uri.Should().Be("https://checkout.example/pay"));
    }
}
```

In `ListingDetailTests.RegisterServices`, add a `PaymentsApiService` mock with an optional card:

```csharp
    private void RegisterServices(ListingDto listing, string? role = null, List<PublicBidDto>? history = null,
        SavedCardDto? card = null)
    {
        // ... existing body ...
        var paymentsMock = new Mock<PaymentsApiService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://localhost/") });
        paymentsMock.Setup(s => s.GetMyCardAsync()).ReturnsAsync(card);
        Services.AddSingleton(paymentsMock.Object);
    }
```

and add (with `using Stallions.Shared.DTOs.Payments;`):

```csharp
    [Fact]
    public void VerifiedBuyerWithoutACard_SeesSaveACardInsteadOfTheBidForm()
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        var listing = ActiveAuction();
        RegisterServices(listing, role: "Buyer", card: null);

        var cut = RenderComponent<ListingDetail>(p => p.Add(c => c.Id, listing.Id));

        cut.WaitForAssertion(() => cut.Find("a[href='/account/card']").TextContent.Should().Contain("Save a card to bid"));
        cut.FindAll(".bid-form").Should().BeEmpty();
    }

    [Fact]
    public void VerifiedBuyerWithAValidCard_SeesTheBidForm()
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        var listing = ActiveAuction();
        RegisterServices(listing, role: "Buyer",
            card: new SavedCardDto { Brand = "visa", Last4 = "4242", ExpMonth = 8, ExpYear = 2028, IsValid = true });

        var cut = RenderComponent<ListingDetail>(p => p.Add(c => c.Id, listing.Id));

        cut.WaitForAssertion(() => cut.FindAll(".bid-form").Should().ContainSingle());
    }
```

- [ ] **Step 4: Run red** — compile errors (`AccountCard`, `PaymentsApiService` usage, `ActivateAsync`).

- [ ] **Step 5: The card page**

```razor
@* src/Client/Pages/AccountCard.razor *@
@page "/account/card"
@attribute [Authorize]
@inject PaymentsApiService PaymentsApi
@inject ListingApiService ListingApi
@inject UserStateService UserState
@inject NavigationManager Nav

<PageTitle>Payment card — Stallions Australia</PageTitle>

<div class="container account-card">
    <h1>Payment card</h1>

    @if (!string.IsNullOrWhiteSpace(_disclosure?.SavedCardExplanation))
    {
        <p class="account-card-disclosure">@_disclosure.SavedCardExplanation</p>
    }

    @if (_loading)
    {
        <LoadingSpinner />
    }
    else if (_confirming)
    {
        <p class="text-secondary">Confirming your card…</p>
    }
    else
    {
        @if (_notice is not null) { <p class="account-card-notice">@_notice</p> }
        @if (_error is not null) { <ErrorMessage Message="@_error" /> }

        @if (_card is null)
        {
            <p class="account-card-none">No card saved.</p>
        }
        else
        {
            <div class="account-card-saved">
                <span class="account-card-brand">@BrandName(_card.Brand)</span>
                <span>•••• @_card.Last4</span>
                <span class="text-secondary">expires @($"{_card.ExpMonth:00}/{_card.ExpYear % 100:00}")</span>
                @if (!_card.IsValid)
                {
                    <span class="account-card-expired">This card has expired — replace it to keep bidding.</span>
                }
            </div>
        }

        <button class="btn btn-gold" @onclick="StartSetupAsync" disabled="@_busy">
            @(_busy ? "Opening…" : _card is null ? "Add card" : "Replace card")
        </button>
    }
</div>

@code {
    /// <summary>How often to re-check after returning from the hosted page (tests shorten it).</summary>
    [Parameter] public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
    private const int MaxPolls = 30; // ~60 s at the default interval

    private SavedCardDto? _card;
    private BuyerFeeDisclosureDto? _disclosure;
    private bool _loading = true;
    private bool _confirming;
    private bool _busy;
    private string? _notice;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        await UserState.LoadAsync();
        if (!UserState.IsBuyer) { Nav.NavigateTo("/", replace: true); return; }

        try { _disclosure = await ListingApi.GetBuyerFeeDisclosureAsync(); }
        catch (ApiException) { _disclosure = null; }

        var result = new Uri(Nav.Uri).Query.Contains("result=success") ? "success"
                   : new Uri(Nav.Uri).Query.Contains("result=cancelled") ? "cancelled" : null;
        if (result == "cancelled") _notice = "Card not saved — please try again.";

        _card = await LoadCardAsync();
        _loading = false;

        if (result == "success" && _card is null)
        {
            // The provider's webhook saves the card; wait for it to land.
            _confirming = true;
            StateHasChanged();
            for (var i = 0; i < MaxPolls && _card is null; i++)
            {
                await Task.Delay(PollInterval);
                _card = await LoadCardAsync();
            }
            _confirming = false;
            if (_card is null) _notice = "Your card is still being confirmed — check back shortly.";
        }
    }

    private async Task<SavedCardDto?> LoadCardAsync()
    {
        try { return await PaymentsApi.GetMyCardAsync(); }
        catch (ApiException ex) { _error = ex.Message; return null; }
    }

    private async Task StartSetupAsync()
    {
        _busy = true; _error = null;
        try { Nav.NavigateTo(await PaymentsApi.StartCardSetupAsync(), forceLoad: true); }
        catch (ApiException ex) { _error = ex.Message; }
        finally { _busy = false; }
    }

    private static string BrandName(string brand) => brand switch
    {
        "visa" => "Visa",
        "mastercard" => "Mastercard",
        "amex" => "American Express",
        _ => brand
    };
}
```

```css
/* src/Client/Pages/AccountCard.razor.css */
.account-card { max-width: 560px; padding-block: var(--space-12); display: flex; flex-direction: column; gap: var(--space-4); }
.account-card-disclosure { border-left: 3px solid var(--gold); background: var(--cream); padding: var(--space-3) var(--space-4); border-radius: var(--radius-sm); font-size: var(--font-size-sm); }
.account-card-saved { display: flex; flex-wrap: wrap; gap: var(--space-3); align-items: baseline; padding: var(--space-4); border: 1px solid var(--border); border-radius: var(--radius-md); }
.account-card-brand { font-weight: var(--font-weight-semibold); }
.account-card-expired { flex-basis: 100%; color: var(--danger); font-size: var(--font-size-sm); }
.account-card-notice { color: var(--text-secondary); }
.account-card-none { color: var(--text-secondary); }
```

- [ ] **Step 6: Nav link** — in `NavBar.razor`, in both the desktop `<Authorized>` block and the drawer, after the My Purchases link:

```razor
                    @if (UserState.IsBuyer)
                    {
                        <NavLink href="/account/card" class="nav-link">Payment card</NavLink>
                    }
```

(drawer version uses `class="drawer-link" @onclick="CloseDrawer"`).

- [ ] **Step 7: Listing page prompt** — in `ListingDetail.razor`:
  - `@inject PaymentsApiService PaymentsApi`; field `private SavedCardDto? _card;`
  - In `Load()`, after loading the listing: `if (UserState.IsBuyer) { try { _card = await PaymentsApi.GetMyCardAsync(); } catch (ApiException) { _card = null; } }` (call `await UserState.LoadAsync()` first if the page doesn't already).
  - Change the bid-form condition from `@if (UserState.IsBuyer && UserState.IsVerified)` to:

```razor
                            @if (UserState.IsBuyer && UserState.IsVerified && _card is not { IsValid: true })
                            {
                                <a href="/account/card" class="btn btn-gold btn-full">Save a card to bid</a>
                            }
                            else if (UserState.IsBuyer && UserState.IsVerified)
                            {
                                @* existing bid-form markup, unchanged *@
                            }
```

- [ ] **Step 8: Stud Activate button** — in `AdminStallions.razor`:
  - `@inject PlatformSettingsApiService SettingsApi`; fields `private decimal? _standardFee; private Guid? _activating; private bool _confirmingPayment;`
  - In `Load()`, after loading subscriptions: `try { _standardFee = (await SettingsApi.GetAsync()).StandardListingFeeIncGst; } catch (ApiException) { _standardFee = null; }`
  - Replace the "Listing fee required" notice in the listing-fee cell with:

```razor
                                @if (status is not ("Paid" or "Waived"))
                                {
                                    var fee = FeeFor(s.Id);
                                    @if (fee is decimal amount)
                                    {
                                        <button class="btn btn-sm btn-gold" disabled="@(_activating == s.Id)"
                                                @onclick="() => ActivateAsync(s.Id)">
                                            @(_activating == s.Id ? "Opening…" : $"Activate for {_openSeason.Name} — {amount.ToString("C0", AuCulture)}")
                                        </button>
                                    }
                                    else
                                    {
                                        <div class="listing-fee-notice">Listing fee required. Contact Stallions Australia to activate.</div>
                                    }
                                }
```

  - Add to `@code`:

```csharp
    private static readonly System.Globalization.CultureInfo AuCulture =
        System.Globalization.CultureInfo.GetCultureInfo("en-AU");
    [Parameter] public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    // A Pending subscription keeps its own amount (e.g. a Staff discount); otherwise the standard fee.
    private decimal? FeeFor(Guid stallionId) =>
        _subscriptions.FirstOrDefault(x => x.StallionId == stallionId && x.SeasonId == _openSeason?.Id)?.FeeIncGst
        ?? _standardFee;

    private async Task ActivateAsync(Guid stallionId)
    {
        _activating = stallionId; _error = null;
        try { Nav.NavigateTo(await SubscriptionApi.ActivateAsync(stallionId), forceLoad: true); }
        catch (ApiException ex) { _error = ex.Message; }
        finally { _activating = null; }
    }
```

  - In `OnInitializedAsync`, after `await Load();`, poll while returning from a successful payment:

```csharp
        if (new Uri(Nav.Uri).Query.Contains("payment=success"))
        {
            // The webhook marks the fee Paid; re-check for up to ~60 s until one more stallion
            // is Paid for the open season than when the page loaded.
            int PaidCount() => _subscriptions.Count(x => x.Status == "Paid" && x.SeasonId == _openSeason?.Id);
            var paidBefore = PaidCount();
            for (var i = 0; i < 30 && PaidCount() == paidBefore; i++)
            {
                await Task.Delay(PollInterval);
                _subscriptions = await SubscriptionApi.GetMineAsync() ?? [];
                StateHasChanged();
            }
        }
```

- [ ] **Step 9: Run green** — `dotnet test tests/Client.Tests` green; full build (0 warnings) + all tests green.

- [ ] **Step 10: Commit**

```bash
git add src/Server/Options/CheckoutOptions.cs src/Server/appsettings.json src/Shared/DTOs/Checkout/BuyerFeeDisclosureDto.cs src/Server/Controllers/DisclosuresController.cs src/Client/Services/PaymentsApiService.cs src/Client/Services/SubscriptionApiService.cs src/Client/Program.cs src/Client/_Imports.razor src/Client/Pages/AccountCard.razor src/Client/Pages/AccountCard.razor.css src/Client/Layout/NavBar.razor src/Client/Pages/ListingDetail.razor src/Client/Pages/Admin/AdminStallions.razor tests/Client.Tests/Pages/AccountCardTests.cs tests/Client.Tests/Pages/AdminStallionsTests.cs tests/Client.Tests/Pages/ListingDetailTests.cs
git commit -m "feat: payment card page, save-a-card bid prompt and stud listing-fee activation"
```

---

### Task 10: Migration `V2Phase2Payments`

- [ ] **Step 1: Create** — `dotnet ef migrations add V2Phase2Payments --project src/Server --startup-project src/Server --output-dir Data/Migrations`
- [ ] **Step 2: Read it.** Expect: `AddColumn PaymentCustomerId` (nullable, 255) and `PaymentCustomerProvider` (nullable, 20) on `Users`, `CreateTable SavedCards` (unique index on `UserId`, FK to `Users` cascade, provider ids 255), `CreateTable ProcessedPaymentEvents` (PK `EventId` nvarchar(255), `ProcessedAt` = claimed at, nullable `CompletedAt`). Nothing should be dropped or renamed.
- [ ] **Step 3: Check the model and snapshot agree** — `dotnet ef migrations has-pending-model-changes --project src/Server --startup-project src/Server` → "No changes have been made…".
- [ ] **Step 4: Apply to the local dev DB** —
  `dotnet ef database update --project src/Server --startup-project src/Server --connection "Server=(localdb)\mssqllocaldb;Database=StallionsNomsDev;Trusted_Connection=True;MultipleActiveResultSets=true"`
- [ ] **Step 5: Full build + test green.**
- [ ] **Step 6: Commit**

```bash
git add src/Server/Data/Migrations
git commit -m "feat: V2Phase2Payments migration"
```

---

### Task 11: Infra settings and dev verification

**Files:** Modify `infra/modules/appservice.bicep`

- [ ] **Step 1: App settings** — add to the `appSettings` array (dev is not Production, so it runs the fake until switched):

```bicep
        {
          // Fake (simulated payment page) until Stripe keys exist; Stripe in Production.
          // Switch dev to Stripe by changing this line once David has loaded the Key Vault secrets.
          name: 'Payments__Provider'
          value: isProduction ? 'Stripe' : 'Fake'
        }
        {
          name: 'Payments__Stripe__SecretKey'
          value: '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/StripeSecretKey/)'
        }
        {
          name: 'Payments__Stripe__WebhookSigningSecret'
          value: '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/StripeWebhookSigningSecret/)'
        }
```

(`infra/main.json` and `infra/modules/*.json` are stale compiled copies — azd builds from the `.bicep`
files; leave the JSON alone.)

- [ ] **Step 2: Commit**

```bash
git add infra/modules/appservice.bicep
git commit -m "chore: App Service payment settings (Fake on dev, Stripe Key Vault references)"
```

- [ ] **Step 3: Push** — `git push -u origin feature/v2-phase2-payments`.
- [ ] **Step 4: With David's go-ahead**, `azd provision --environment dev` (adds the settings), then
  `azd deploy api --environment dev`. Confirm the app starts (`GET /api/disclosures/buyer-fee` → 200 and
  includes `savedCardExplanation`).
- [ ] **Step 5: Click-through on dev with the fake provider** (David signs in; Claude never enters credentials):
  - **Buyer:** Payment card → Add card → *Simulated payment* → Approve → back on the card page showing
    Visa •••• 4242. A listing now shows the bid form; before saving a card it showed "Save a card to bid".
  - **Stud admin:** My Stallions → **Activate for {season} — $990** → Approve → back on My Stallions showing
    **Paid**; Decline leaves it Pending. A Staff-discounted Pending subscription shows its own amount.
  - **Staff:** Listing Fees shows the activated subscription as Paid, method Card, reference `pi_fake_…`;
    the audit log has `ListingFeePaidByCard`.
- [ ] **Step 6: Summary for David** — what changed, deviations, and what's needed to switch dev to Stripe:
  create the Stripe account (test mode), add Key Vault secrets `StripeSecretKey` and
  `StripeWebhookSigningSecret` to `kv-stallions-noms-dev`, add the webhook endpoint
  `https://app-stallions-noms-dev.azurewebsites.net/api/payments/webhook/stripe` (event
  `checkout.session.completed`), enable card receipt emails, then change the Bicep line to `'Stripe'`
  for dev, provision and deploy. Do **not** merge to master — David reviews first.
