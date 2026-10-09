using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Settings;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class PlatformSettingsServiceTests
{
    private readonly Mock<IPlatformSettingsRepository> _repo = new();
    private readonly Mock<IAuditLogRepository> _audit = new();
    private readonly Mock<IUserService> _users = new();

    private PlatformSettingsService CreateSut() => new(_repo.Object, _audit.Object, _users.Object);

    private static User Staff() => new() { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active };

    private static PlatformSettings Current() => new()
    {
        Id = PlatformSettings.SingletonId,
        BuyerFeeIncGst = 150m,
        StandardListingFeeIncGst = 990m,
        MinimumBidIncrement = 25m,
        ChargeGracePeriodHours = 2,
        OfferExpiryDays = 7
    };

    private static UpdatePlatformSettingsRequest ValidRequest() => new()
    {
        BuyerFeeIncGst = 200m,
        StandardListingFeeIncGst = 1100m,
        MinimumBidIncrement = 50m,
        ChargeGracePeriodHours = 4,
        OfferExpiryDays = 10
    };

    [Fact]
    public async Task Get_ReturnsCurrentValues()
    {
        _repo.Setup(r => r.GetAsync()).ReturnsAsync(Current());

        var result = await CreateSut().GetAsync();

        result.Succeeded.Should().BeTrue();
        result.Value!.BuyerFeeIncGst.Should().Be(150m);
        result.Value.StandardListingFeeIncGst.Should().Be(990m);
        result.Value.MinimumBidIncrement.Should().Be(25m);
        result.Value.ChargeGracePeriodHours.Should().Be(2);
        result.Value.OfferExpiryDays.Should().Be(7);
    }

    [Theory]
    [InlineData(UserRole.Buyer)]
    [InlineData(UserRole.StudFarmAdmin)]
    public async Task Update_WhenCallerNotStaff_ReturnsForbidden(UserRole role)
    {
        _users.Setup(u => u.GetOrCreateCurrentUserAsync())
            .ReturnsAsync(new User { Id = Guid.NewGuid(), Role = role, Status = UserStatus.Active });
        _repo.Setup(r => r.GetAsync()).ReturnsAsync(Current());

        var result = await CreateSut().UpdateAsync(ValidRequest());

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(403);
        _repo.Verify(r => r.UpdateAsync(It.IsAny<PlatformSettings>()), Times.Never);
    }

    [Fact]
    public async Task Update_WhenCallerUnresolved_ReturnsForbidden()
    {
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync((User?)null);

        var result = await CreateSut().UpdateAsync(ValidRequest());

        result.HttpStatusCode.Should().Be(403);
        _repo.Verify(r => r.UpdateAsync(It.IsAny<PlatformSettings>()), Times.Never);
    }

    public static TheoryData<Action<UpdatePlatformSettingsRequest>> InvalidChanges => new()
    {
        r => r.BuyerFeeIncGst = 0m,
        r => r.BuyerFeeIncGst = -1m,
        r => r.StandardListingFeeIncGst = 0m,
        r => r.MinimumBidIncrement = 0m,
        r => r.ChargeGracePeriodHours = 0,
        r => r.OfferExpiryDays = -3
    };

    [Theory]
    [MemberData(nameof(InvalidChanges))]
    public async Task Update_WhenAnyValueNotPositive_ReturnsBadRequest(Action<UpdatePlatformSettingsRequest> breakIt)
    {
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(Staff());
        _repo.Setup(r => r.GetAsync()).ReturnsAsync(Current());
        var request = ValidRequest();
        breakIt(request);

        var result = await CreateSut().UpdateAsync(request);

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
        _repo.Verify(r => r.UpdateAsync(It.IsAny<PlatformSettings>()), Times.Never);
    }

    [Fact]
    public async Task Update_WhenValid_SavesValuesAndStampsUpdater()
    {
        var staff = Staff();
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(staff);
        _repo.Setup(r => r.GetAsync()).ReturnsAsync(Current());
        PlatformSettings? saved = null;
        _repo.Setup(r => r.UpdateAsync(It.IsAny<PlatformSettings>()))
            .Callback<PlatformSettings>(s => saved = s)
            .Returns(Task.CompletedTask);

        var result = await CreateSut().UpdateAsync(ValidRequest());

        result.Succeeded.Should().BeTrue();
        saved!.BuyerFeeIncGst.Should().Be(200m);
        saved.StandardListingFeeIncGst.Should().Be(1100m);
        saved.MinimumBidIncrement.Should().Be(50m);
        saved.ChargeGracePeriodHours.Should().Be(4);
        saved.OfferExpiryDays.Should().Be(10);
        saved.UpdatedByUserId.Should().Be(staff.Id);
        saved.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
        result.Value!.BuyerFeeIncGst.Should().Be(200m);
    }

    [Fact]
    public async Task Update_WhenValid_AuditLogsOldAndNewValues()
    {
        var staff = Staff();
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(staff);
        _repo.Setup(r => r.GetAsync()).ReturnsAsync(Current());
        string? details = null;
        _audit.Setup(a => a.LogAsync("PlatformSettings", PlatformSettings.SingletonId,
                "UpdatePlatformSettings", staff.Id, It.IsAny<string?>()))
            .Callback<string, Guid, string, Guid?, string?>((_, _, _, _, d) => details = d)
            .Returns(Task.CompletedTask);

        await CreateSut().UpdateAsync(ValidRequest());

        _audit.Verify(a => a.LogAsync("PlatformSettings", PlatformSettings.SingletonId,
            "UpdatePlatformSettings", staff.Id, It.IsAny<string?>()), Times.Once);
        details.Should().Contain("\"Old\"").And.Contain("\"New\"");
        details.Should().Contain("150").And.Contain("200");
        details.Should().Contain("990").And.Contain("1100");
    }
}
