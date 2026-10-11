using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data.Repositories;

public class TaxDocumentRepositoryTests
{
    private static readonly DateTime T0 = new(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Makes every save fail the way SQL Server does (a SqlException with this number).</summary>
    private sealed class FailingSaves(int sqlNumber) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("insert failed", SqlExceptionFactory.WithNumber(sqlNumber));
    }

    private static AppDbContext FailingDb(int sqlNumber) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .AddInterceptors(new FailingSaves(sqlNumber))
        .Options);

    private static TaxDocument Doc(string number, TaxDocumentKind kind = TaxDocumentKind.ListingFeeInvoice,
        TaxDocumentStatus status = TaxDocumentStatus.Paid, DateTime? issuedAt = null, Guid? buyer = null, Guid? farm = null) => new()
    {
        Number = number, Kind = kind, Status = status, IssuedAt = issuedAt ?? T0, Description = "Fee",
        AmountIncGst = 990m, AmountExGst = 900m, GstAmount = 90m, SupplierName = "SA", SupplierAbn = "12345678901",
        SupplierAddress = "Scone", RecipientName = "R", RecipientEmail = "r@x", BuyerUserId = buyer, StudFarmId = farm
    };

    [Fact]
    public async Task TryAdd_OnAUniqueViolation_ReturnsFalse_AndStopsTracking()
    {
        using var db = FailingDb(2601);
        var doc = Doc("SA-000001");

        (await new TaxDocumentRepository(db).TryAddAsync(doc)).Should().BeFalse();

        db.Entry(doc).State.Should().Be(EntityState.Detached);
    }

    [Fact]
    public async Task TryAdd_OnAnyOtherFailure_Throws()
    {
        using var db = FailingDb(547);

        var act = () => new TaxDocumentRepository(db).TryAddAsync(Doc("SA-000001"));

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Lists_AreScopedToTheirOwner_NewestFirst()
    {
        using var db = DbContextFactory.Create(Guid.NewGuid().ToString());
        var buyer = Guid.NewGuid();
        var farm = Guid.NewGuid();
        var repo = new TaxDocumentRepository(db);
        await repo.TryAddAsync(Doc("SA-000001", TaxDocumentKind.BuyerFeeInvoice, buyer: buyer, issuedAt: T0));
        await repo.TryAddAsync(Doc("SA-000002", TaxDocumentKind.BuyerFeeInvoice, buyer: buyer, issuedAt: T0.AddDays(1)));
        await repo.TryAddAsync(Doc("SA-000003", TaxDocumentKind.BuyerFeeInvoice, buyer: Guid.NewGuid()));
        await repo.TryAddAsync(Doc("SA-000004", farm: farm));

        (await repo.GetForBuyerAsync(buyer)).Select(d => d.Number).Should().Equal("SA-000002", "SA-000001");
        (await repo.GetForStudFarmAsync(farm)).Select(d => d.Number).Should().Equal("SA-000004");
    }

    [Fact]
    public async Task Search_FiltersByKindStatusAndIssueTime()
    {
        using var db = DbContextFactory.Create(Guid.NewGuid().ToString());
        var repo = new TaxDocumentRepository(db);
        await repo.TryAddAsync(Doc("SA-000001", issuedAt: T0));
        await repo.TryAddAsync(Doc("SA-000002", status: TaxDocumentStatus.Unpaid, issuedAt: T0.AddDays(1)));
        await repo.TryAddAsync(Doc("SA-000003", TaxDocumentKind.BuyerFeeInvoice, issuedAt: T0.AddDays(2)));

        (await repo.SearchAsync(TaxDocumentKind.ListingFeeInvoice, null, null, null)).Should().HaveCount(2);
        (await repo.SearchAsync(null, TaxDocumentStatus.Unpaid, null, null)).Single().Number.Should().Be("SA-000002");
        (await repo.SearchAsync(null, null, T0.AddDays(1), T0.AddDays(2))).Single().Number.Should().Be("SA-000002");
    }
}
