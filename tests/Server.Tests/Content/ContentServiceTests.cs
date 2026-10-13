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

    private ContentService CreateSut(IContentRepository? repo = null, ITransactionRunner? transactions = null) =>
        new(repo ?? new ContentRepository(_db), _cache,
            new AuditLogRepository(_db), _users.Object, transactions ?? new InlineTransactionRunner(), _clock, _log);

    /// <summary>Runs the work twice, like an execution strategy retrying after a transient failure.</summary>
    private sealed class RetryingTransactionRunner(Action betweenAttempts) : ITransactionRunner
    {
        public async Task<T> RunAsync<T>(Func<Task<T>> work)
        {
            await work();
            betweenAttempts();
            return await work();
        }
    }

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
    public async Task Save_StoresTheTrimmedNormalisedText_NotTheRawInput()
    {
        var result = await CreateSut().SaveAsync(ContentKeys.Outbid, Request(OutbidSubject + "\n", "  " + OutbidBody + "   \r\n\r\n"));

        result.Succeeded.Should().BeTrue(result.Error);
        var stored = _db.ContentItems.Single(c => c.Key == ContentKeys.Outbid && c.Version == 2);
        stored.Subject.Should().Be(OutbidSubject);
        stored.Body.Should().Be(OutbidBody);
    }

    [Fact]
    public async Task Save_ASubjectWithAnEmbeddedNewline_IsRejected()
    {
        var result = await CreateSut().SaveAsync(ContentKeys.Outbid, Request("Outbid\non {StallionName}", OutbidBody));

        result.HttpStatusCode.Should().Be(400);
        result.Error.Should().Contain("The subject must be on one line.");
        _db.ContentItems.Count(c => c.Key == ContentKeys.Outbid).Should().Be(1);
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
    public async Task Save_RetriedAfterAnUnchangedFirstAttempt_ReportsTheRetrysResult()
    {
        var entry = ContentCatalog.Find(ContentKeys.Outbid)!;
        var runner = new RetryingTransactionRunner(() => ContentTestData.SetWording(_db, ContentKeys.Outbid, OutbidSubject, OutbidBody));

        var result = await CreateSut(transactions: runner).SaveAsync(ContentKeys.Outbid, Request(entry.DefaultSubject, entry.DefaultBody));

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.Version.Should().Be(3);
    }

    [Fact]
    public async Task Save_RetriedAfterAConflictedFirstAttempt_ReportsTheRetrysResult()
    {
        var repo = new LosingOnceRepository(_db);
        var runner = new RetryingTransactionRunner(() => { });

        var result = await CreateSut(repo, runner).SaveAsync(ContentKeys.Outbid, Request(OutbidSubject, OutbidBody));

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.Version.Should().Be(2);
    }

    private sealed class LosingOnceRepository(AppDbContext db) : ContentRepository(db)
    {
        private bool _lost;
        public override Task<bool> TryAddAsync(ContentItem item)
        {
            if (_lost) return base.TryAddAsync(item);
            _lost = true;
            return Task.FromResult(false);
        }
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
    public async Task Restore_StoresTheTrimmedText_WhenTheOldVersionHadStrayWhitespace()
    {
        ContentTestData.SetWording(_db, ContentKeys.Outbid, OutbidSubject + " ", OutbidBody + "  \n"); // v2, untrimmed
        ContentTestData.SetWording(_db, ContentKeys.Outbid, "Outbid again on {StallionName}", OutbidBody); // v3

        var result = await CreateSut().RestoreAsync(ContentKeys.Outbid, 2);

        result.Succeeded.Should().BeTrue(result.Error);
        var stored = _db.ContentItems.Single(c => c.Key == ContentKeys.Outbid && c.Version == 4);
        stored.Subject.Should().Be(OutbidSubject);
        stored.Body.Should().Be(OutbidBody);
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
