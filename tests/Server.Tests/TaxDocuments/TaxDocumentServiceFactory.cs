using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Data;
using Stallions.Server.Data.Repositories;
using Stallions.Server.TaxDocuments;
using Stallions.Server.Tests.Helpers;

namespace Stallions.Server.Tests.TaxDocuments;

/// <summary>
/// Builds a real <see cref="TaxDocumentService"/> over a test <see cref="AppDbContext"/> with real repositories,
/// so tests don't repeat its ten-argument constructor. Tests own and keep the fakes they pass in:
/// the number source (<see cref="FakeTaxDocumentNumberSource"/>), the email mock and the clock.
/// Pass <paramref name="repo"/> to swap the tax document repository (e.g. a racing subclass); pass a second
/// <see cref="AppDbContext"/> over the same in-memory database to simulate another app instance.
/// </summary>
public static class TaxDocumentServiceFactory
{
    public static TaxDocumentService Create(AppDbContext db, TestClock clock, FakeTaxDocumentNumberSource numbers,
        Mock<ITaxDocumentEmails> emails, ITaxDocumentRepository? repo = null) =>
        new(repo ?? new TaxDocumentRepository(db), numbers, new PlatformSettingsRepository(db),
            new StudFarmRepository(db), new UserRepository(db), new IntroOfferRepository(db), emails.Object,
            new AuditLogRepository(db), clock, NullLogger<TaxDocumentService>.Instance);
}
