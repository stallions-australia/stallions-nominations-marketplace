using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Payments;
using Stallions.Server.Services;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.DTOs.Payments;
using Stallions.Shared.DTOs.Subscriptions;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

/// <summary>
/// Runs against the real repositories on the in-memory provider (seeded PlatformSettings
/// included), so farm isolation and duplicate detection are exercised end to end.
/// </summary>
public class SubscriptionServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IUserService> _users = new();
    private readonly Mock<IPaymentProvider> _provider = new();

    private readonly User _staff = new() { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active, ObjectId = "staff", Email = "staff@x", DisplayName = "Staff" };
    private readonly User _studUserA = new() { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active, ObjectId = "a", Email = "a@x", DisplayName = "A" };
    private readonly User _studUserB = new() { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active, ObjectId = "b", Email = "b@x", DisplayName = "B" };
    private readonly StudFarm _farmA;
    private readonly StudFarm _farmB;
    private readonly Stallion _stallionA;
    private readonly Stallion _stallionB;
    private readonly Season _season;

    public SubscriptionServiceTests()
    {
        _db = DbContextFactory.Create(Guid.NewGuid().ToString());
        _db.Database.EnsureCreated(); // seeds PlatformSettings (standard listing fee $990)

        _farmA = new StudFarm { UserId = _studUserA.Id, Name = "Farm A" };
        _farmB = new StudFarm { UserId = _studUserB.Id, Name = "Farm B" };
        _stallionA = new Stallion { StudFarmId = _farmA.Id, Name = "Stallion A" };
        _stallionB = new Stallion { StudFarmId = _farmB.Id, Name = "Stallion B" };
        _season = new Season { Name = "2026 Season", IsOpen = true };
        _db.Users.AddRange(_staff, _studUserA, _studUserB);
        _db.StudFarms.AddRange(_farmA, _farmB);
        _db.Stallions.AddRange(_stallionA, _stallionB);
        _db.Seasons.Add(_season);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private SubscriptionService CreateSut() => new(
        new SubscriptionRepository(_db),
        new StallionRepository(_db),
        new SeasonRepository(_db),
        new StudFarmRepository(_db),
        new PlatformSettingsRepository(_db),
        new AuditLogRepository(_db),
        _users.Object,
        _provider.Object,
        NullLogger<SubscriptionService>.Instance);

    private void SignInAs(User user) =>
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(user);

    private async Task<SubscriptionDto> CreatePendingAsync(Stallion stallion, decimal? feeOverride = null)
    {
        SignInAs(_staff);
        var result = await CreateSut().CreateAsync(new CreateSubscriptionRequest
        {
            StallionId = stallion.Id, SeasonId = _season.Id, FeeIncGstOverride = feeOverride
        });
        result.Succeeded.Should().BeTrue(result.Error);
        return result.Value!;
    }

    // ── Create ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_UsesStandardListingFeeFromSettings()
    {
        var dto = await CreatePendingAsync(_stallionA);

        dto.FeeIncGst.Should().Be(990m);
        dto.FeeExGst.Should().Be(900m);
        dto.GstAmount.Should().Be(90m);
        dto.Status.Should().Be("Pending");
        dto.StudFarmId.Should().Be(_farmA.Id);
        dto.StallionName.Should().Be("Stallion A");
        dto.SeasonName.Should().Be("2026 Season");
    }

    [Fact]
    public async Task Create_RespectsFeeOverride()
    {
        var dto = await CreatePendingAsync(_stallionA, feeOverride: 495m);

        dto.FeeIncGst.Should().Be(495m);
        dto.FeeExGst.Should().Be(450m);
        dto.GstAmount.Should().Be(45m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task Create_RejectsNonPositiveOverride(decimal fee)
    {
        SignInAs(_staff);
        var result = await CreateSut().CreateAsync(new CreateSubscriptionRequest
        {
            StallionId = _stallionA.Id, SeasonId = _season.Id, FeeIncGstOverride = fee
        });

        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Create_WhenStallionAlreadySubscribedForSeason_ReturnsConflict()
    {
        await CreatePendingAsync(_stallionA);

        var result = await CreateSut().CreateAsync(new CreateSubscriptionRequest
        {
            StallionId = _stallionA.Id, SeasonId = _season.Id
        });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(409);
        (await _db.StallionSeasonSubscriptions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Create_WhenStallionOrSeasonMissing_ReturnsNotFound()
    {
        SignInAs(_staff);
        var noStallion = await CreateSut().CreateAsync(new CreateSubscriptionRequest
            { StallionId = Guid.NewGuid(), SeasonId = _season.Id });
        var noSeason = await CreateSut().CreateAsync(new CreateSubscriptionRequest
            { StallionId = _stallionA.Id, SeasonId = Guid.NewGuid() });

        noStallion.HttpStatusCode.Should().Be(404);
        noSeason.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Create_WhenCallerIsStudAdmin_ReturnsForbidden()
    {
        SignInAs(_studUserA);

        var result = await CreateSut().CreateAsync(new CreateSubscriptionRequest
        {
            StallionId = _stallionA.Id, SeasonId = _season.Id
        });

        result.HttpStatusCode.Should().Be(403);
        (await _db.StallionSeasonSubscriptions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Create_WritesAuditLog()
    {
        var dto = await CreatePendingAsync(_stallionA);

        var log = await _db.AuditLogs.SingleAsync(a => a.EntityId == dto.Id);
        log.EntityType.Should().Be("StallionSeasonSubscription");
        log.Action.Should().Be("CreateSubscription");
        log.UserId.Should().Be(_staff.Id);
    }

    // ── Mark paid ───────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkPaid_SetsStatusMethodReferenceAndDate()
    {
        var created = await CreatePendingAsync(_stallionA);

        var result = await CreateSut().MarkPaidAsync(created.Id,
            new MarkSubscriptionPaidRequest { PaymentMethod = "BankTransfer", PaymentReference = "INV-1001" });

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.Status.Should().Be("Paid");
        result.Value.PaymentMethod.Should().Be("BankTransfer");
        result.Value.PaymentReference.Should().Be("INV-1001");
        result.Value.PaidAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
        result.Value.FeeIncGst.Should().Be(990m);
        (await _db.AuditLogs.CountAsync(a => a.EntityId == created.Id && a.Action == "MarkSubscriptionPaid"))
            .Should().Be(1);
    }

    [Theory]
    [InlineData("Waived")]
    [InlineData("Cheque")]
    [InlineData("")]
    public async Task MarkPaid_RejectsInvalidMethod(string method)
    {
        var created = await CreatePendingAsync(_stallionA);

        var result = await CreateSut().MarkPaidAsync(created.Id,
            new MarkSubscriptionPaidRequest { PaymentMethod = method });

        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task MarkPaid_WhenAlreadyWaived_ReturnsBadRequest()
    {
        var created = await CreatePendingAsync(_stallionA);
        await CreateSut().WaiveAsync(created.Id, new WaiveSubscriptionRequest { Reason = "Founding stud" });

        var result = await CreateSut().MarkPaidAsync(created.Id,
            new MarkSubscriptionPaidRequest { PaymentMethod = "Invoice" });

        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task MarkPaid_WhenCallerIsStudAdmin_ReturnsForbidden()
    {
        var created = await CreatePendingAsync(_stallionA);
        SignInAs(_studUserA);

        var result = await CreateSut().MarkPaidAsync(created.Id,
            new MarkSubscriptionPaidRequest { PaymentMethod = "Invoice" });

        result.HttpStatusCode.Should().Be(403);
    }

    // ── Waive ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Waive_ZeroesFeeAndSetsWaivedStatusMethodAndReason()
    {
        var created = await CreatePendingAsync(_stallionA);

        var result = await CreateSut().WaiveAsync(created.Id,
            new WaiveSubscriptionRequest { Reason = "Founding stud — first season free" });

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.Status.Should().Be("Waived");
        result.Value.PaymentMethod.Should().Be("Waived");
        result.Value.WaiverReason.Should().Be("Founding stud — first season free");
        result.Value.FeeIncGst.Should().Be(0m);
        result.Value.FeeExGst.Should().Be(0m);
        result.Value.GstAmount.Should().Be(0m);
        (await _db.AuditLogs.CountAsync(a => a.EntityId == created.Id && a.Action == "WaiveSubscription"))
            .Should().Be(1);
    }

    [Fact]
    public async Task Waive_RequiresReason()
    {
        var created = await CreatePendingAsync(_stallionA);

        var result = await CreateSut().WaiveAsync(created.Id, new WaiveSubscriptionRequest { Reason = "  " });

        result.HttpStatusCode.Should().Be(400);
    }

    // ── Reads ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetForStudFarm_ReturnsOnlyCallersOwnFarm()
    {
        await CreatePendingAsync(_stallionA);
        await CreatePendingAsync(_stallionB);
        SignInAs(_studUserA);

        var result = await CreateSut().GetForStudFarmAsync();

        result.Succeeded.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.StallionId.Should().Be(_stallionA.Id);
    }

    [Fact]
    public async Task GetAll_WhenCallerIsStudAdmin_ReturnsForbidden()
    {
        await CreatePendingAsync(_stallionB);
        SignInAs(_studUserA);

        var result = await CreateSut().GetAllAsync(null, null);

        result.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GetAll_FiltersByStatus()
    {
        var a = await CreatePendingAsync(_stallionA);
        await CreatePendingAsync(_stallionB);
        await CreateSut().MarkPaidAsync(a.Id, new MarkSubscriptionPaidRequest { PaymentMethod = "Invoice" });

        var paid = await CreateSut().GetAllAsync(_season.Id, "Paid");
        var all = await CreateSut().GetAllAsync(null, null);

        paid.Value.Should().ContainSingle().Which.Id.Should().Be(a.Id);
        all.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task HasActiveSubscription_TrueOnlyForPaidOrWaived()
    {
        var sut = CreateSut();
        (await sut.HasActiveSubscriptionAsync(_stallionA.Id, _season.Id)).Should().BeFalse("no subscription");

        var a = await CreatePendingAsync(_stallionA);
        (await sut.HasActiveSubscriptionAsync(_stallionA.Id, _season.Id)).Should().BeFalse("pending");

        await sut.MarkPaidAsync(a.Id, new MarkSubscriptionPaidRequest { PaymentMethod = "Card" });
        (await sut.HasActiveSubscriptionAsync(_stallionA.Id, _season.Id)).Should().BeTrue("paid");

        var b = await CreatePendingAsync(_stallionB);
        await sut.WaiveAsync(b.Id, new WaiveSubscriptionRequest { Reason = "Intro offer" });
        (await sut.HasActiveSubscriptionAsync(_stallionB.Id, _season.Id)).Should().BeTrue("waived");
    }

    [Fact]
    public void Model_HasUniqueIndexOnStallionAndSeason()
    {
        var index = _db.Model.FindEntityType(typeof(StallionSeasonSubscription))!
            .GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { "StallionId", "SeasonId" }));

        index.IsUnique.Should().BeTrue();
    }

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

    private async Task SetPendingCheckoutAsync(Guid subscriptionId, string url, DateTime expiresAt)
    {
        var sub = await _db.StallionSeasonSubscriptions.SingleAsync(s => s.Id == subscriptionId);
        sub.PendingCheckoutUrl = url;
        sub.PendingCheckoutExpiresAt = expiresAt;
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Activate_ReusingStaffPending_CallsProviderWithTheExistingSubscriptionId()
    {
        var created = await CreatePendingAsync(_stallionA, feeOverride: 495m);
        PaymentPageReturns("https://pay/fee");

        await ActivateAs(_studUserA, _stallionA);

        _provider.Verify(p => p.CreateListingFeeSessionAsync(created.Id, 495m,
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Activate_StoresTheCheckoutUrl_AndASecondClickReusesIt()
    {
        PaymentPageReturns("https://pay/fee");

        await ActivateAs(_studUserA, _stallionA);
        var sub = await _db.StallionSeasonSubscriptions.SingleAsync();
        sub.PendingCheckoutUrl.Should().Be("https://pay/fee");
        sub.PendingCheckoutExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(1), TimeSpan.FromMinutes(1));

        PaymentPageReturns("https://pay/other");
        var second = await ActivateAs(_studUserA, _stallionA);

        second.Value!.Url.Should().Be("https://pay/fee");
        _provider.Verify(p => p.CreateListingFeeSessionAsync(It.IsAny<Guid>(), It.IsAny<decimal>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Activate_WithAnExpiredOrNearlyExpiredCheckout_StartsANewSession()
    {
        var created = await CreatePendingAsync(_stallionA);
        await SetPendingCheckoutAsync(created.Id, "https://pay/old", DateTime.UtcNow.AddMinutes(2));
        PaymentPageReturns("https://pay/new");

        var result = await ActivateAs(_studUserA, _stallionA);

        result.Value!.Url.Should().Be("https://pay/new");
        (await _db.StallionSeasonSubscriptions.SingleAsync()).PendingCheckoutUrl.Should().Be("https://pay/new");
    }

    [Fact]
    public async Task Activate_InactiveStallion_IsBadRequest()
    {
        _stallionA.IsActive = false;
        await _db.SaveChangesAsync();

        (await ActivateAs(_studUserA, _stallionA)).HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Activate_Waived_IsBadRequest()
    {
        var created = await CreatePendingAsync(_stallionA);
        await CreateSut().WaiveAsync(created.Id, new WaiveSubscriptionRequest { Reason = "Founding stud" });

        (await ActivateAs(_studUserA, _stallionA)).HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Activate_WithZeroFee_IsBadRequestAndStartsNoPayment()
    {
        var created = await CreatePendingAsync(_stallionA);
        var sub = await _db.StallionSeasonSubscriptions.SingleAsync(s => s.Id == created.Id);
        sub.FeeIncGst = 0m;
        await _db.SaveChangesAsync();

        (await ActivateAs(_studUserA, _stallionA)).HttpStatusCode.Should().Be(400);
        _provider.Verify(p => p.CreateListingFeeSessionAsync(It.IsAny<Guid>(), It.IsAny<decimal>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Activate_WithNoFarm_IsForbidden()
    {
        var orphan = new User { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active, ObjectId = "o", Email = "o@x", DisplayName = "O" };
        _db.Users.Add(orphan);
        await _db.SaveChangesAsync();

        (await ActivateAs(orphan, _stallionA)).HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Activate_WhenTheProviderFails_IsBadRequestAndKeepsThePendingSubscription()
    {
        _provider.Setup(p => p.CreateListingFeeSessionAsync(It.IsAny<Guid>(), It.IsAny<decimal>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("stripe down"));

        var result = await ActivateAs(_studUserA, _stallionA);

        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Be("The payment page is unavailable right now. Please try again shortly.");
        var sub = await _db.StallionSeasonSubscriptions.SingleAsync();
        sub.Status.Should().Be(SubscriptionStatus.Pending);
        sub.PendingCheckoutUrl.Should().BeNull();
    }

    [Fact]
    public async Task MarkPaid_ClearsAnOpenCheckout()
    {
        var created = await CreatePendingAsync(_stallionA);
        await SetPendingCheckoutAsync(created.Id, "https://pay/open", DateTime.UtcNow.AddMinutes(30));
        SignInAs(_staff);

        await CreateSut().MarkPaidAsync(created.Id, new MarkSubscriptionPaidRequest { PaymentMethod = "Invoice" });

        var sub = await _db.StallionSeasonSubscriptions.SingleAsync();
        sub.PendingCheckoutUrl.Should().BeNull();
        sub.PendingCheckoutExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Waive_ClearsAnOpenCheckout()
    {
        var created = await CreatePendingAsync(_stallionA);
        await SetPendingCheckoutAsync(created.Id, "https://pay/open", DateTime.UtcNow.AddMinutes(30));
        SignInAs(_staff);

        await CreateSut().WaiveAsync(created.Id, new WaiveSubscriptionRequest { Reason = "Founding stud" });

        var sub = await _db.StallionSeasonSubscriptions.SingleAsync();
        sub.PendingCheckoutUrl.Should().BeNull();
        sub.PendingCheckoutExpiresAt.Should().BeNull();
    }
}
