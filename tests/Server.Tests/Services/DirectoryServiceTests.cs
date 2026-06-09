using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Directory;

namespace Stallions.Server.Tests.Services;

public class DirectoryServiceTests
{
    private readonly Mock<IStudDirectoryRepository> _studRepo = new();
    private readonly Mock<IStallionDirectoryRepository> _stallionRepo = new();

    private DirectoryService CreateSut() => new(_studRepo.Object, _stallionRepo.Object);

    [Fact]
    public async Task GetStudDirectoryAsync_ReturnsNotFound_WhenEntryMissing()
    {
        _studRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((StudDirectory?)null);

        var result = await CreateSut().GetStudDirectoryAsync(Guid.NewGuid());

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetStudDirectoryAsync_ReturnsMappedDto_WhenFound()
    {
        var id = Guid.NewGuid();
        var entry = new StudDirectory
        {
            Id = id,
            Name = "Coolmore",
            State = "NSW",
            IsActive = true,
            Stallions = new List<StallionDirectory>()
        };
        _studRepo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(entry);

        var result = await CreateSut().GetStudDirectoryAsync(id);

        result.Succeeded.Should().BeTrue();
        result.Value!.Name.Should().Be("Coolmore");
        result.Value!.State.Should().Be("NSW");
    }

    [Fact]
    public async Task CreateStudDirectoryAsync_ReturnsCreated_WithCorrectFields()
    {
        var request = new CreateStudDirectoryRequest
        {
            Name = "Darley",
            State = "QLD",
            ArionStudId = 42
        };
        _studRepo.Setup(r => r.AddAsync(It.IsAny<StudDirectory>()))
            .ReturnsAsync((StudDirectory s) => s);

        var result = await CreateSut().CreateStudDirectoryAsync(request);

        result.Succeeded.Should().BeTrue();
        result.Value!.Name.Should().Be("Darley");
        result.Value!.ArionStudId.Should().Be(42);
    }

    [Fact]
    public async Task UpdateStudDirectoryAsync_ReturnsNotFound_WhenEntryMissing()
    {
        _studRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((StudDirectory?)null);

        var result = await CreateSut().UpdateStudDirectoryAsync(Guid.NewGuid(), new UpdateStudDirectoryRequest { Name = "X" });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetStallionDirectoryAsync_ReturnsNotFound_WhenEntryMissing()
    {
        _stallionRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((StallionDirectory?)null);

        var result = await CreateSut().GetStallionDirectoryAsync(Guid.NewGuid());

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task CreateStallionDirectoryAsync_RequiresExistingStud()
    {
        var studId = Guid.NewGuid();
        _studRepo.Setup(r => r.GetByIdAsync(studId)).ReturnsAsync((StudDirectory?)null);

        var result = await CreateSut().CreateStallionDirectoryAsync(new CreateStallionDirectoryRequest
        {
            StudDirectoryId = studId,
            Name = "Frankel",
            YearOfBirth = 2008
        });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetStudDirectoriesAsync_ReturnsMappedList()
    {
        var entries = new List<StudDirectory>
        {
            new() { Id = Guid.NewGuid(), Name = "Active", IsActive = true, Stallions = new List<StallionDirectory>() },
            new() { Id = Guid.NewGuid(), Name = "Inactive", IsActive = false, Stallions = new List<StallionDirectory>() }
        };
        _studRepo.Setup(r => r.GetAllAsync(true)).ReturnsAsync(entries);

        var result = await CreateSut().GetStudDirectoriesAsync(includeInactive: true);

        result.Succeeded.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateStudDirectoryAsync_ReturnsBadRequest_WhenNameIsWhitespace()
    {
        var result = await CreateSut().CreateStudDirectoryAsync(
            new CreateStudDirectoryRequest { Name = "   " });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }
}
