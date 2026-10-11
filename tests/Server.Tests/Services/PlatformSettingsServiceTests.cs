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

    private static UpdateBusinessDetailsRequest Business(string? abn = "12 345 678 901", string? bsb = "062-000",
        int terms = 14, string? legalName = "Stallions Australia Pty Ltd") => new()
    {
        LegalName = legalName, Abn = abn, Address = "1 Racecourse Road\nScone NSW 2337",
        Email = "accounts@stallions.example", BankAccountName = "Stallions Australia Pty Ltd", BankBsb = bsb,
        BankAccountNumber = "1234 5678", InvoicePaymentTermsDays = terms
    };

    private PlatformSettings SignedInStaffWithSettings(out User staff)
    {
        staff = Staff();
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(staff);
        var settings = Current();
        _repo.Setup(r => r.GetAsync()).ReturnsAsync(settings);
        return settings;
    }

    [Fact]
    public async Task UpdateBusinessDetails_AcceptsSpacesAndHyphens_AndStoresDigitsOnly()
    {
        var settings = SignedInStaffWithSettings(out var staff);

        var r = await CreateSut().UpdateBusinessDetailsAsync(Business(abn: " 12 345 678 901 "));

        r.Succeeded.Should().BeTrue(r.Error);
        settings.BusinessAbn.Should().Be("12345678901");
        settings.BankBsb.Should().Be("062000");
        settings.BankAccountNumber.Should().Be("12345678");
        settings.BusinessAddress.Should().Be("1 Racecourse Road\nScone NSW 2337");
        settings.InvoicePaymentTermsDays.Should().Be(14);
        r.Value!.CanIssueTaxDocuments.Should().BeTrue();
        r.Value.CanIssueUnpaidInvoices.Should().BeTrue();
        _repo.Verify(x => x.UpdateAsync(settings), Times.Once);
        _audit.Verify(a => a.LogAsync("PlatformSettings", PlatformSettings.SingletonId, "UpdateBusinessDetails", staff.Id,
            It.Is<string?>(d => d != null && d.Contains("12345678901") && d.Contains("Old"))), Times.Once);
    }

    [Theory]
    [InlineData("1234567890")]
    [InlineData("123456789012")]
    [InlineData("1234567890A")]
    [InlineData("12-345-678-90")]
    public async Task UpdateBusinessDetails_RejectsABadAbn(string abn)
    {
        SignedInStaffWithSettings(out _);

        var r = await CreateSut().UpdateBusinessDetailsAsync(Business(abn: abn));

        r.HttpStatusCode.Should().Be(400);
        r.Error.Should().Be("ABN must be 11 digits.");
        _repo.Verify(x => x.UpdateAsync(It.IsAny<PlatformSettings>()), Times.Never);
    }

    [Theory]
    [InlineData("06200")]
    [InlineData("0620000")]
    [InlineData("06A-000")]
    public async Task UpdateBusinessDetails_RejectsABadBsb(string bsb)
    {
        SignedInStaffWithSettings(out _);

        var r = await CreateSut().UpdateBusinessDetailsAsync(Business(bsb: bsb));

        r.Error.Should().Be("BSB must be 6 digits.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public async Task UpdateBusinessDetails_RejectsPaymentTermsOutOfRange(int terms)
    {
        SignedInStaffWithSettings(out _);

        var r = await CreateSut().UpdateBusinessDetailsAsync(Business(terms: terms));

        r.Error.Should().Be("Payment terms must be between 1 and 90 days.");
    }

    [Fact]
    public async Task UpdateBusinessDetails_BlankValues_AreStoredAsNull_AndCannotIssue()
    {
        var settings = SignedInStaffWithSettings(out _);

        var r = await CreateSut().UpdateBusinessDetailsAsync(Business(abn: "  ", bsb: "", legalName: "   "));

        settings.BusinessLegalName.Should().BeNull();
        settings.BusinessAbn.Should().BeNull();
        settings.BankBsb.Should().BeNull();
        r.Value!.CanIssueTaxDocuments.Should().BeFalse();
        r.Value.CanIssueUnpaidInvoices.Should().BeFalse();
    }

    [Fact]
    public async Task BusinessDetails_AsNonStaff_AreForbidden()
    {
        _users.Setup(u => u.GetOrCreateCurrentUserAsync())
            .ReturnsAsync(new User { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.Active });

        (await CreateSut().GetBusinessDetailsAsync()).HttpStatusCode.Should().Be(403);
        (await CreateSut().UpdateBusinessDetailsAsync(Business())).HttpStatusCode.Should().Be(403);
        _repo.Verify(x => x.UpdateAsync(It.IsAny<PlatformSettings>()), Times.Never);
    }
}
