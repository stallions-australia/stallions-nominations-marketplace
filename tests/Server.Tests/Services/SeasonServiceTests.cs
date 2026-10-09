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
    private readonly Mock<IAuditLogRepository> _auditMock = new();
    private SeasonService CreateSut() => new(_repoMock.Object, _usersMock.Object, _auditMock.Object);

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

    // ── Audit log + Staff re-check ──────────────────────────────────────────

    private static CreateSeasonRequest NewSeason() => new()
    {
        Name = "2026 Season", StartDate = new DateOnly(2026, 8, 1), EndDate = new DateOnly(2027, 1, 31)
    };

    [Fact]
    public async Task Create_WritesAuditLogWithCaller()
    {
        var staff = StaffUser();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(staff);
        _repoMock.Setup(r => r.AddAsync(It.IsAny<Season>())).ReturnsAsync((Season s) => s);

        var result = await CreateSut().CreateAsync(NewSeason());

        result.Succeeded.Should().BeTrue();
        _auditMock.Verify(a => a.LogAsync("Season", result.Value!.Id, "CreateSeason", staff.Id,
            It.Is<string?>(d => d!.Contains("2026 Season"))), Times.Once);
    }

    [Fact]
    public async Task Update_WritesAuditLogWithOldAndNewValues()
    {
        var staff = StaffUser();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(staff);
        var season = new Season
        {
            Id = Guid.NewGuid(), Name = "2026 Seasn",
            StartDate = new DateOnly(2026, 8, 1), EndDate = new DateOnly(2027, 1, 31)
        };
        _repoMock.Setup(r => r.GetByIdAsync(season.Id)).ReturnsAsync(season);
        string? details = null;
        _auditMock.Setup(a => a.LogAsync("Season", season.Id, "UpdateSeason", staff.Id, It.IsAny<string?>()))
            .Callback<string, Guid, string, Guid?, string?>((_, _, _, _, d) => details = d)
            .Returns(Task.CompletedTask);

        await CreateSut().UpdateAsync(season.Id, new UpdateSeasonRequest
        {
            Name = "2026 Season", StartDate = new DateOnly(2026, 8, 1), EndDate = new DateOnly(2027, 2, 28)
        });

        details.Should().Contain("2026 Seasn").And.Contain("2026 Season");
        details.Should().Contain("2027-01-31").And.Contain("2027-02-28");
    }

    [Fact]
    public async Task OpenAndClose_WriteAuditLogs()
    {
        var staff = StaffUser();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(staff);
        _repoMock.Setup(r => r.GetCurrentOpenSeasonAsync()).ReturnsAsync((Season?)null);
        var season = new Season { Id = Guid.NewGuid(), Name = "2026 Season" };
        _repoMock.Setup(r => r.GetByIdAsync(season.Id)).ReturnsAsync(season);

        await CreateSut().OpenSeasonAsync(season.Id);
        await CreateSut().CloseSeasonAsync(season.Id);

        _auditMock.Verify(a => a.LogAsync("Season", season.Id, "OpenSeason", staff.Id, It.IsAny<string?>()), Times.Once);
        _auditMock.Verify(a => a.LogAsync("Season", season.Id, "CloseSeason", staff.Id, It.IsAny<string?>()), Times.Once);
    }

    [Theory]
    [InlineData(Stallions.Shared.Enums.UserRole.Buyer)]
    [InlineData(Stallions.Shared.Enums.UserRole.StudFarmAdmin)]
    public async Task Writes_WhenCallerNotStaff_ReturnForbiddenAndSaveNothing(Stallions.Shared.Enums.UserRole role)
    {
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(new Stallions.Server.Data.Entities.User
        {
            Id = Guid.NewGuid(), Role = role, Status = Stallions.Shared.Enums.UserStatus.Active
        });
        var season = new Season { Id = Guid.NewGuid(), Name = "2026 Season" };
        _repoMock.Setup(r => r.GetByIdAsync(season.Id)).ReturnsAsync(season);
        var sut = CreateSut();

        (await sut.CreateAsync(NewSeason())).HttpStatusCode.Should().Be(403);
        (await sut.UpdateAsync(season.Id, new UpdateSeasonRequest
            { Name = "x", StartDate = new DateOnly(2026, 8, 1), EndDate = new DateOnly(2027, 1, 31) }))
            .HttpStatusCode.Should().Be(403);
        (await sut.OpenSeasonAsync(season.Id)).HttpStatusCode.Should().Be(403);
        (await sut.CloseSeasonAsync(season.Id)).HttpStatusCode.Should().Be(403);

        _repoMock.Verify(r => r.AddAsync(It.IsAny<Season>()), Times.Never);
        _repoMock.Verify(r => r.UpdateAsync(It.IsAny<Season>()), Times.Never);
        _auditMock.Verify(a => a.LogAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<string?>()), Times.Never);
    }
}
