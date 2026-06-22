using FluentAssertions;
using Moq;
using Stallions.Server.Auth;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Terms;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class UserServiceTermsTests
{
    private readonly Mock<IUserRepository> _repo = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<IAuditLogRepository> _audit = new();
    private readonly Mock<ITermsRepository> _terms = new();

    private UserService CreateSut() =>
        new(_repo.Object, _currentUser.Object, _audit.Object, _terms.Object);

    private User SetupActiveBuyer(int? acceptedVersion = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(), ObjectId = "oid-1", Email = "b@x.com",
            Role = UserRole.Buyer, Status = UserStatus.Active,
            AcceptedTermsVersion = acceptedVersion
        };
        _currentUser.Setup(c => c.ObjectId).Returns("oid-1");
        _currentUser.Setup(c => c.IsAuthenticated).Returns(true);
        _repo.Setup(r => r.GetByObjectIdAsync("oid-1")).ReturnsAsync(user);
        return user;
    }

    [Fact]
    public async Task AcceptTerms_WhenVersionMatchesCurrent_SetsFields()
    {
        var user = SetupActiveBuyer();
        _terms.Setup(t => t.GetCurrentAsync())
            .ReturnsAsync(new TermsDocument { Version = 4, Body = "x" });

        var result = await CreateSut().AcceptTermsAsync(new AcceptTermsRequest { Version = 4 });

        result.Succeeded.Should().BeTrue();
        user.AcceptedTermsVersion.Should().Be(4);
        user.AcceptedTermsAt.Should().NotBeNull();
        _repo.Verify(r => r.UpdateAsync(user), Times.Once);
        _audit.Verify(a => a.LogAsync("User", user.Id, "TermsAccepted", user.Id,
            It.Is<string>(s => s.Contains("4"))), Times.Once);
    }

    [Fact]
    public async Task AcceptTerms_WhenVersionStale_ReturnsBadRequest()
    {
        SetupActiveBuyer();
        _terms.Setup(t => t.GetCurrentAsync())
            .ReturnsAsync(new TermsDocument { Version = 5, Body = "x" });

        var result = await CreateSut().AcceptTermsAsync(new AcceptTermsRequest { Version = 4 });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task AcceptTerms_WhenNoTermsPublished_ReturnsBadRequest()
    {
        SetupActiveBuyer();
        _terms.Setup(t => t.GetCurrentAsync()).ReturnsAsync((TermsDocument?)null);

        var result = await CreateSut().AcceptTermsAsync(new AcceptTermsRequest { Version = 1 });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task SuppressBidConfirmation_SetsFlag()
    {
        var user = SetupActiveBuyer();

        var result = await CreateSut().SuppressBidConfirmationAsync();

        result.Succeeded.Should().BeTrue();
        user.SuppressBidConfirmation.Should().BeTrue();
        _repo.Verify(r => r.UpdateAsync(user), Times.Once);
    }
}
