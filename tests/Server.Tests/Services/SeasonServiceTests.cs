using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Seasons;

namespace Stallions.Server.Tests.Services;

public class SeasonServiceTests
{
    private readonly Mock<ISeasonRepository> _repoMock = new();
    private readonly Mock<IUserService> _usersMock = new();
    private SeasonService CreateSut() => new(_repoMock.Object, _usersMock.Object);

    private static Stallions.Server.Data.Entities.User StaffUser() => new()
    {
        Id = Guid.NewGuid(),
        Role = Stallions.Shared.Enums.UserRole.Staff,
        Status = Stallions.Shared.Enums.UserStatus.Active
    };

    [Fact]
    public async Task OpenSeason_WhenAnotherSeasonIsAlreadyOpen_ReturnsBadRequest()
    {
        var staff = StaffUser();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(staff);
        var openSeason = new Season { Id = Guid.NewGuid(), IsOpen = true };
        _repoMock.Setup(r => r.GetCurrentOpenSeasonAsync()).ReturnsAsync(openSeason);
        var target = new Season { Id = Guid.NewGuid(), IsOpen = false };
        _repoMock.Setup(r => r.GetByIdAsync(target.Id)).ReturnsAsync(target);

        var result = await CreateSut().OpenSeasonAsync(target.Id);

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task OpenSeason_WhenNoOtherOpen_SetsIsOpenAndRecordsWhoOpened()
    {
        var staff = StaffUser();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(staff);
        _repoMock.Setup(r => r.GetCurrentOpenSeasonAsync()).ReturnsAsync((Season?)null);
        var season = new Season { Id = Guid.NewGuid(), IsOpen = false };
        _repoMock.Setup(r => r.GetByIdAsync(season.Id)).ReturnsAsync(season);

        var result = await CreateSut().OpenSeasonAsync(season.Id);

        result.Succeeded.Should().BeTrue();
        _repoMock.Verify(r => r.UpdateAsync(It.Is<Season>(s =>
            s.Id == season.Id && s.IsOpen && s.OpenedAt.HasValue && s.OpenedByUserId == staff.Id)), Times.Once);
    }

    [Fact]
    public async Task CloseSeason_WhenSeasonIsOpen_SetsIsOpenFalse()
    {
        var staff = StaffUser();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(staff);
        var season = new Season { Id = Guid.NewGuid(), IsOpen = true };
        _repoMock.Setup(r => r.GetByIdAsync(season.Id)).ReturnsAsync(season);

        var result = await CreateSut().CloseSeasonAsync(season.Id);

        result.Succeeded.Should().BeTrue();
        _repoMock.Verify(r => r.UpdateAsync(It.Is<Season>(s =>
            s.Id == season.Id && !s.IsOpen)), Times.Once);
    }
}
