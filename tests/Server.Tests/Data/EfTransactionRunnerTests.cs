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
}
