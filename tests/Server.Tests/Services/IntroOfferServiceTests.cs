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
