using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Terms;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class TermsServiceTests
{
    private readonly Mock<ITermsRepository> _repo = new();
    private readonly Mock<IUserService> _users = new();

    private TermsService CreateSut() => new(_repo.Object, _users.Object);

    private static User Staff() => new() { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active };

    [Fact]
    public async Task GetCurrent_WhenNonePublished_ReturnsNotFound()
    {
        _repo.Setup(r => r.GetCurrentAsync()).ReturnsAsync((TermsDocument?)null);
        var result = await CreateSut().GetCurrentAsync();
        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetCurrent_WhenPublished_ReturnsDto()
    {
        _repo.Setup(r => r.GetCurrentAsync())
            .ReturnsAsync(new TermsDocument { Id = Guid.NewGuid(), Version = 3, Body = "Hi" });
        var result = await CreateSut().GetCurrentAsync();
        result.Succeeded.Should().BeTrue();
        result.Value!.Version.Should().Be(3);
        result.Value.Body.Should().Be("Hi");
    }

    [Fact]
    public async Task Publish_WhenNoExisting_CreatesVersion1()
    {
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(Staff());
        _repo.Setup(r => r.GetCurrentAsync()).ReturnsAsync((TermsDocument?)null);
        TermsDocument? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<TermsDocument>()))
            .ReturnsAsync((TermsDocument d) => { added = d; return d; });

        var result = await CreateSut().PublishAsync(new PublishTermsRequest { Body = "Terms v1" });

        result.Succeeded.Should().BeTrue();
        added!.Version.Should().Be(1);
        added.Body.Should().Be("Terms v1");
    }

    [Fact]
    public async Task Publish_WhenExistingV2_CreatesVersion3()
    {
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(Staff());
        _repo.Setup(r => r.GetCurrentAsync())
            .ReturnsAsync(new TermsDocument { Version = 2, Body = "old" });
        TermsDocument? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<TermsDocument>()))
            .ReturnsAsync((TermsDocument d) => { added = d; return d; });

        var result = await CreateSut().PublishAsync(new PublishTermsRequest { Body = "Terms v3" });

        result.Succeeded.Should().BeTrue();
        added!.Version.Should().Be(3);
    }

    [Fact]
    public async Task Publish_WhenBodyEmpty_ReturnsBadRequest()
    {
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(Staff());
        var result = await CreateSut().PublishAsync(new PublishTermsRequest { Body = "   " });
        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }
}
