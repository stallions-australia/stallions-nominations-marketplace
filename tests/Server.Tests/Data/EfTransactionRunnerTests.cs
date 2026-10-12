using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data;

public class EfTransactionRunnerTests
{
    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task RunAsync_StartsEachAttemptWithAClearChangeTracker_AndReturnsTheResult()
    {
        await using var db = CreateInMemoryDb();
        db.Users.Add(new User { ObjectId = "x", Email = "x@x", DisplayName = "X", Role = UserRole.Buyer });
        db.ChangeTracker.Entries().Should().NotBeEmpty();
        var trackedInside = -1;

        var result = await new EfTransactionRunner(db).RunAsync(() =>
        {
            trackedInside = db.ChangeTracker.Entries().Count();
            return Task.FromResult(42);
        });

        result.Should().Be(42);
        trackedInside.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_WhenTheWorkThrows_ClearsTheChangeTracker_SoALaterSaveDoesNotPersistIt()
    {
        await using var db = CreateInMemoryDb();
        var runner = new EfTransactionRunner(db);

        // Like one item of "Issue missing tax invoices": it tracks a row, then fails.
        var act = () => runner.RunAsync<bool>(() =>
        {
            db.Users.Add(new User { ObjectId = "failed", Email = "failed@x", DisplayName = "Failed", Role = UserRole.Buyer });
            throw new InvalidOperationException("the item failed");
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("the item failed");
        db.ChangeTracker.Entries().Should().BeEmpty();

        // The caller carries on and saves its aggregate audit row: the failed item's row must not ride along.
        db.AuditLogs.Add(new AuditLog { EntityType = "TaxDocument", Action = "IssueMissingTaxDocuments" });
        await db.SaveChangesAsync();
        (await db.Users.CountAsync()).Should().Be(0);
        (await db.AuditLogs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_WhenTheWorkFailsAfterAnAwait_ClearsTheChangeTrackerToo()
    {
        await using var db = CreateInMemoryDb();

        var act = () => new EfTransactionRunner(db).RunAsync<int>(async () =>
        {
            db.Users.Add(new User { ObjectId = "later", Email = "later@x", DisplayName = "Later", Role = UserRole.Buyer });
            await Task.Yield();
            throw new InvalidOperationException("failed later");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }
}
