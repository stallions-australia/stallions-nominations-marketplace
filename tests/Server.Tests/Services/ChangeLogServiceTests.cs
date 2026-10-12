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

    private ChangeLogService CreateSut() =>
        new(new AuditLogRepository(_db), _users.Object, new StudFarmRepository(_db), new SeasonRepository(_db));

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
    public async Task IntroOffer_StudFarmAndSeasonChanges_ShowNames_NeverGuids()
    {
        var farmA = new StudFarm { Name = "Arrowfield" };
        var farmB = new StudFarm { Name = "Widden" };
        var season = new Season { Name = "2027 Season" };
        var goneFarm = Guid.NewGuid();
        _db.StudFarms.AddRange(farmA, farmB);
        _db.Seasons.Add(season);
        _db.SaveChanges();
        Audit("IntroOffer", "UpdateIntroOffer", new
        {
            Old = new { Name = "Launch", StudFarmId = farmA.Id, SeasonId = (Guid?)null },
            New = new { Name = "Launch", StudFarmId = farmB.Id, SeasonId = (Guid?)season.Id }
        }, Noon12Oct);
        Audit("IntroOffer", "UpdateIntroOffer", new
        {
            Old = new { Name = "Launch", StudFarmId = goneFarm },
            New = new { Name = "Launch", StudFarmId = farmA.Id }
        }, Noon12Oct.AddHours(1));

        var entries = (await GetAsync()).Entries;

        var gone = entries[0].Changes.Single();
        gone.Field.Should().Be("Stud farm");
        gone.OldValue.Should().Be("changed");
        gone.NewValue.Should().Be("Arrowfield");
        var resolved = entries[1].Changes;
        resolved.Single(c => c.Field == "Stud farm").Should().BeEquivalentTo(
            new ChangeLogChangeDto { Field = "Stud farm", OldValue = "Arrowfield", NewValue = "Widden" });
        resolved.Single(c => c.Field == "Season").Should().BeEquivalentTo(
            new ChangeLogChangeDto { Field = "Season", OldValue = null, NewValue = "2027 Season" });
        entries.SelectMany(e => e.Changes).SelectMany(c => new[] { c.OldValue, c.NewValue })
            .Should().NotContain(v => v != null && v.Length == 36 && v.Count(ch => ch == (char)45) == 4);
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
            new AuditLogRepository(_db), new InlineTransactionRunner(), TimeProvider.System);
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
