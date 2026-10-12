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
