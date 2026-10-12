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
        new(new TermsRepository(_db), _users.Object, new StudFarmRepository(_db), new AuditLogRepository(_db),
            new InlineTransactionRunner(), _clock);

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
