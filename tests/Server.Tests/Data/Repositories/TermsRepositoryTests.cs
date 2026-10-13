using FluentAssertions;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data.Repositories;

public class TermsRepositoryTests
{
    [Fact]
    public async Task Current_IsPerAudience_AndTheParameterlessOneIsTheBuyerTerms()
    {
        using var db = DbContextFactory.Create(Guid.NewGuid().ToString());
        var user = new User
        {
            Id = Guid.NewGuid(), ObjectId = "staff", Email = "staff@example.com", DisplayName = "Staff",
            Role = UserRole.Staff, Status = UserStatus.Active
        };
        db.Users.Add(user);
        db.TermsDocuments.AddRange(
            new TermsDocument { Audience = TermsAudience.Buyer, Version = 1, Body = "B1", CreatedByUserId = user.Id },
            new TermsDocument { Audience = TermsAudience.Buyer, Version = 2, Body = "B2", CreatedByUserId = user.Id },
            new TermsDocument { Audience = TermsAudience.Stud, Version = 3, Body = "S3", CreatedByUserId = user.Id });
        await db.SaveChangesAsync();
        var repo = new TermsRepository(db);

        (await repo.GetCurrentAsync())!.Body.Should().Be("B2");
        (await repo.GetCurrentAsync(TermsAudience.Buyer))!.Body.Should().Be("B2");
        (await repo.GetCurrentAsync(TermsAudience.Stud))!.Body.Should().Be("S3");
        (await repo.GetHistoryAsync(TermsAudience.Buyer)).Select(t => t.Version).Should().Equal(2, 1);
        (await repo.GetHistoryAsync(TermsAudience.Stud)).Select(t => t.Version).Should().Equal(3);
    }
}
