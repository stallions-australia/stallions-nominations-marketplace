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
        new SubscriptionRepository(_db), _provider.Object, new AuditLogRepository(_db), new InlineTransactionRunner(),
        NullLogger<PaymentEventProcessor>.Instance);

    private CardSavedEvent CardSaved(string eventId, string pm = "pm_1", string last4 = "4242") =>
        new(eventId, _buyer.Id, "cus_1", pm, "visa", last4, 8, 2028);

    private StallionSeasonSubscription PendingSubscription(decimal fee = 990m)
    {
        // The repository loads the stallion, season and stud with the subscription, so they must exist.
        var stud = new StudFarm { UserId = _buyer.Id };
        var stallion = new Stallion { Name = "Test Sire" };
        var season = new Season { Name = "2027 Season" };
        _db.AddRange(stud, stallion, season);
        _db.SaveChanges();
        var sub = new StallionSeasonSubscription
        {
            StallionId = stallion.Id, SeasonId = season.Id, StudFarmId = stud.Id,
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
        sub.PendingCheckoutUrl = "https://pay/open";
        sub.PendingCheckoutExpiresAt = DateTime.UtcNow.AddMinutes(30);
        await _db.SaveChangesAsync();

        var outcome = await CreateSut().ProcessAsync(
            new ListingFeePaidEvent("evt_p", sub.Id, 99000, "aud", "pi_123"));

        outcome.Should().Be(PaymentEventOutcome.Processed);
        var saved = await _db.StallionSeasonSubscriptions.SingleAsync();
        saved.Status.Should().Be(SubscriptionStatus.Paid);
        saved.PaymentMethod.Should().Be(SubscriptionPaymentMethod.Card);
        saved.PaymentReference.Should().Be("pi_123");
        saved.PendingCheckoutUrl.Should().BeNull();
        saved.PendingCheckoutExpiresAt.Should().BeNull();
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
            new SubscriptionRepository(_db), _provider.Object, new AuditLogRepository(_db), new InlineTransactionRunner(),
            NullLogger<PaymentEventProcessor>.Instance);

        await FluentActions.Awaiting(() => failing.ProcessAsync(CardSaved("evt_1")))
            .Should().ThrowAsync<InvalidOperationException>();

        (await CreateSut().ProcessAsync(CardSaved("evt_1"))).Should().Be(PaymentEventOutcome.Processed);
    }

    private PaymentEventProcessor CreateSutWith(
        IProcessedPaymentEventRepository? processed = null, ISavedCardRepository? cards = null) => new(
        processed ?? new ProcessedPaymentEventRepository(_db), cards ?? new SavedCardRepository(_db),
        new UserRepository(_db), new SubscriptionRepository(_db), _provider.Object,
        new AuditLogRepository(_db), new InlineTransactionRunner(), NullLogger<PaymentEventProcessor>.Instance);

    [Fact]
    public async Task Detach_ThatThrows_IsSwallowedAndTheCardIsStillSaved()
    {
        await CreateSut().ProcessAsync(CardSaved("evt_1", pm: "pm_old"));
        _provider.Setup(p => p.DetachCardAsync("pm_old")).ThrowsAsync(new InvalidOperationException("provider down"));

        var outcome = await CreateSut().ProcessAsync(CardSaved("evt_2", pm: "pm_new"));

        outcome.Should().Be(PaymentEventOutcome.Processed);
        (await _db.SavedCards.SingleAsync()).ProviderPaymentMethodId.Should().Be("pm_new");
    }

    [Fact]
    public async Task ResavingTheSamePaymentMethod_DoesNotDetach()
    {
        await CreateSut().ProcessAsync(CardSaved("evt_1", pm: "pm_1"));

        await CreateSut().ProcessAsync(CardSaved("evt_2", pm: "pm_1"));

        _provider.Verify(p => p.DetachCardAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Detach_HappensAfterTheNewCardIsSaved()
    {
        await CreateSut().ProcessAsync(CardSaved("evt_1", pm: "pm_old"));
        string? storedWhenDetached = null;
        _provider.Setup(p => p.DetachCardAsync("pm_old"))
            .Callback(() => storedWhenDetached = _db.SavedCards.AsNoTracking().Single().ProviderPaymentMethodId)
            .Returns(Task.CompletedTask);

        await CreateSut().ProcessAsync(CardSaved("evt_2", pm: "pm_new"));

        storedWhenDetached.Should().Be("pm_new");
    }

    [Fact]
    public async Task CardSaved_ForUnknownUser_IsRejectedAndCompleted()
    {
        var evt = new CardSavedEvent("evt_1", Guid.NewGuid(), "cus_1", "pm_1", "visa", "4242", 8, 2028);

        var outcome = await CreateSut().ProcessAsync(evt);

        outcome.Should().Be(PaymentEventOutcome.Rejected);
        (await _db.SavedCards.CountAsync()).Should().Be(0);
        (await _db.ProcessedPaymentEvents.SingleAsync()).CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ListingFeePaid_ForUnknownSubscription_IsRejectedAndAudited()
    {
        var id = Guid.NewGuid();

        var outcome = await CreateSut().ProcessAsync(new ListingFeePaidEvent("evt_p", id, 99000, "aud", "pi_9"));

        outcome.Should().Be(PaymentEventOutcome.Rejected);
        var audit = await _db.AuditLogs.SingleAsync();
        audit.Action.Should().Be("ListingFeePaymentForUnknownSubscription");
        audit.EntityId.Should().Be(id);
        audit.Details.Should().Contain("pi_9");
    }

    [Fact]
    public async Task ListingFeePaid_ForAWaivedSubscription_IsIgnoredAndAuditedAsDuplicatePayment()
    {
        var sub = PendingSubscription(990m);
        sub.Status = SubscriptionStatus.Waived;
        await _db.SaveChangesAsync();

        var outcome = await CreateSut().ProcessAsync(new ListingFeePaidEvent("evt_p", sub.Id, 99000, "aud", "pi_3"));

        outcome.Should().Be(PaymentEventOutcome.Ignored);
        var audit = await _db.AuditLogs.SingleAsync();
        audit.Action.Should().Be("ListingFeeDuplicatePayment");
        audit.EntityId.Should().Be(sub.Id);
        audit.Details.Should().Contain("pi_3").And.Contain("Waived");
    }

    [Fact]
    public async Task CompletedAt_IsSetOnProcessedAndRejectedPaths()
    {
        await CreateSut().ProcessAsync(CardSaved("evt_1"));
        await CreateSut().ProcessAsync(new ListingFeePaidEvent("evt_2", Guid.NewGuid(), 1, "aud", "pi"));

        (await _db.ProcessedPaymentEvents.CountAsync(p => p.CompletedAt != null)).Should().Be(2);
    }

    [Fact]
    public async Task Duplicate_LeavesTheCardUnchanged()
    {
        await CreateSut().ProcessAsync(CardSaved("evt_1", pm: "pm_1", last4: "4242"));

        var second = await CreateSut().ProcessAsync(CardSaved("evt_1", pm: "pm_2", last4: "9999"));

        second.Should().Be(PaymentEventOutcome.Duplicate);
        var card = await _db.SavedCards.SingleAsync();
        card.ProviderPaymentMethodId.Should().Be("pm_1");
        card.Last4.Should().Be("4242");
    }

    [Fact]
    public async Task FreshIncompleteClaim_ReturnsInProgressAndChangesNothing()
    {
        _db.ProcessedPaymentEvents.Add(new ProcessedPaymentEvent
        {
            EventId = "evt_1", Provider = "Fake", Type = "CardSavedEvent", ProcessedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var outcome = await CreateSut().ProcessAsync(CardSaved("evt_1"));

        outcome.Should().Be(PaymentEventOutcome.InProgress);
        (await _db.SavedCards.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task WhenReleaseAlsoFails_TheOriginalExceptionPropagates()
    {
        var processed = new Mock<IProcessedPaymentEventRepository>();
        processed.Setup(p => p.ClaimAsync(It.IsAny<ProcessedPaymentEvent>())).ReturnsAsync(ClaimResult.Claimed);
        processed.Setup(p => p.ReleaseAsync(It.IsAny<string>())).ThrowsAsync(new IOException("release failed"));
        var cards = new Mock<ISavedCardRepository>();
        cards.Setup(c => c.GetByUserIdAsync(It.IsAny<Guid>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        await FluentActions.Awaiting(() => CreateSutWith(processed.Object, cards.Object).ProcessAsync(CardSaved("evt_1")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("database unavailable");
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
