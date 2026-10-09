using FluentAssertions;
using Moq;
using Stallions.Server.Auth;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Admin;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class AdminServiceTests
{
    private readonly Mock<IListingRepository> _listingRepoMock = new();
    private readonly Mock<IPurchaseRepository> _purchaseRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IStudFarmRepository> _studFarmRepoMock = new();
    private readonly Mock<IStudDirectoryRepository> _studDirRepoMock = new();
    private readonly Mock<IAuditLogRepository> _auditRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<IUserService> _userServiceMock = new();

    private AdminService CreateSut() => new(
        _listingRepoMock.Object,
        _purchaseRepoMock.Object,
        _userRepoMock.Object,
        _studFarmRepoMock.Object,
        _studDirRepoMock.Object,
        _auditRepoMock.Object,
        _currentUserMock.Object,
        _userServiceMock.Object);

    [Fact]
    public async Task GetAllStudFarmsAsync_ReturnsMappedDtos()
    {
        var user = new User { Id = Guid.NewGuid(), DisplayName = "Alice", Email = "alice@test.com",
            ObjectId = "oid1", Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };
        var farm = new StudFarm
        {
            Id = Guid.NewGuid(), Name = "Alpha Stud", ABN = "123", ContactEmail = "farm@test.com",
            UserId = user.Id, User = user, IsActive = true, CreatedAt = DateTime.UtcNow
        };
        _studFarmRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<StudFarm> { farm });

        var result = await CreateSut().GetAllStudFarmsAsync();

        result.Succeeded.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value![0].Name.Should().Be("Alpha Stud");
        result.Value![0].LinkedUserDisplayName.Should().Be("Alice");
        result.Value![0].LinkedUserEmail.Should().Be("alice@test.com");
    }

    [Fact]
    public async Task CreateStudFarmAsync_WhenUserNotFound_ReturnsNotFound()
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

        var result = await CreateSut().CreateStudFarmAsync(new CreateStudFarmRequest
        {
            UserId = Guid.NewGuid(), Name = "New Farm"
        });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task CreateStudFarmAsync_WhenUserIsNotStudFarmAdmin_ReturnsBadRequest()
    {
        var user = new User { Id = Guid.NewGuid(), ObjectId = "oid2", Role = UserRole.Buyer,
            Status = UserStatus.Active, DisplayName = "Bob", Email = "bob@test.com" };
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

        var result = await CreateSut().CreateStudFarmAsync(new CreateStudFarmRequest
        {
            UserId = user.Id, Name = "New Farm"
        });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CreateStudFarmAsync_WhenUserAlreadyHasFarm_ReturnsBadRequest()
    {
        var user = new User { Id = Guid.NewGuid(), ObjectId = "oid3", Role = UserRole.StudFarmAdmin,
            Status = UserStatus.Active, DisplayName = "Carol", Email = "carol@test.com" };
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
        _studFarmRepoMock.Setup(r => r.GetByUserIdAsync(user.Id))
            .ReturnsAsync(new StudFarm { Id = Guid.NewGuid(), UserId = user.Id, Name = "Existing" });

        var result = await CreateSut().CreateStudFarmAsync(new CreateStudFarmRequest
        {
            UserId = user.Id, Name = "New Farm"
        });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CreateStudFarmAsync_WhenValid_CreatesFarmAndAuditLogs()
    {
        var user = new User
        {
            Id = Guid.NewGuid(), DisplayName = "Alice", Email = "alice@test.com",
            ObjectId = "oid4", Role = UserRole.StudFarmAdmin, Status = UserStatus.Active
        };
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
        _studFarmRepoMock.Setup(r => r.GetByUserIdAsync(user.Id)).ReturnsAsync((StudFarm?)null);
        _studFarmRepoMock.Setup(r => r.AddAsync(It.IsAny<StudFarm>()))
            .ReturnsAsync((StudFarm f) => f);
        _userServiceMock.Setup(u => u.GetOrCreateCurrentUserAsync())
            .ReturnsAsync(new User { Id = Guid.NewGuid(), ObjectId = "oid-staff",
                Role = UserRole.Staff, Status = UserStatus.Active, DisplayName = "Staff", Email = "staff@test.com" });

        var result = await CreateSut().CreateStudFarmAsync(new CreateStudFarmRequest
        {
            UserId = user.Id, Name = "Alpha Stud", ABN = "123456789"
        });

        result.Succeeded.Should().BeTrue();
        result.Value!.Name.Should().Be("Alpha Stud");
        _auditRepoMock.Verify(r => r.LogAsync("StudFarm", It.IsAny<Guid>(), "CreateStudFarm",
            It.IsAny<Guid?>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ForceListingStatusAsync_WhenListingNotFound_ReturnsNotFound()
    {
        _listingRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Listing?)null);
        _userServiceMock.Setup(u => u.GetOrCreateCurrentUserAsync())
            .ReturnsAsync(new User { Id = Guid.NewGuid(), ObjectId = "oid-staff2",
                Role = UserRole.Staff, Status = UserStatus.Active, DisplayName = "Staff", Email = "staff@test.com" });

        var result = await CreateSut().ForceListingStatusAsync(Guid.NewGuid(),
            new ForceListingStatusRequest { Status = "Cancelled" });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ForceListingStatusAsync_WhenInvalidStatus_ReturnsBadRequest()
    {
        var listing = new AuctionListing { Id = Guid.NewGuid(), Status = ListingStatus.Draft };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        _userServiceMock.Setup(u => u.GetOrCreateCurrentUserAsync())
            .ReturnsAsync(new User { Id = Guid.NewGuid(), ObjectId = "oid-staff3",
                Role = UserRole.Staff, Status = UserStatus.Active, DisplayName = "Staff", Email = "staff@test.com" });

        var result = await CreateSut().ForceListingStatusAsync(listing.Id,
            new ForceListingStatusRequest { Status = "NotAStatus" });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ForceListingStatusAsync_WhenValid_SetsStatusAndAuditLogs()
    {
        var listing = new AuctionListing { Id = Guid.NewGuid(), Status = ListingStatus.Draft };
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        _userServiceMock.Setup(u => u.GetOrCreateCurrentUserAsync())
            .ReturnsAsync(new User { Id = Guid.NewGuid(), ObjectId = "oid-staff4",
                Role = UserRole.Staff, Status = UserStatus.Active, DisplayName = "Staff", Email = "staff@test.com" });

        var result = await CreateSut().ForceListingStatusAsync(listing.Id,
            new ForceListingStatusRequest { Status = "Cancelled", Reason = "Quality issue" });

        result.Succeeded.Should().BeTrue();
        listing.Status.Should().Be(ListingStatus.Cancelled);
        _auditRepoMock.Verify(r => r.LogAsync("Listing", listing.Id, "ForceListingStatus",
            It.IsAny<Guid?>(), It.Is<string?>(s => s!.Contains("Quality issue"))), Times.Once);
    }

    // ── LinkStudFarmToDirectoryAsync ─────────────────────────────────────────

    [Fact]
    public async Task LinkStudFarmToDirectoryAsync_ReturnsNotFound_WhenFarmMissing()
    {
        _studFarmRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((StudFarm?)null);

        var result = await CreateSut().LinkStudFarmToDirectoryAsync(Guid.NewGuid(), Guid.NewGuid());

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task LinkStudFarmToDirectoryAsync_SetsStudDirectoryId_AndAudits()
    {
        var farm = new StudFarm { Id = Guid.NewGuid(), Name = "Oak", StudDirectoryId = null };
        var studDirId = Guid.NewGuid();
        _studFarmRepoMock.Setup(r => r.GetByIdAsync(farm.Id)).ReturnsAsync(farm);
        _studDirRepoMock.Setup(r => r.GetByIdAsync(studDirId))
            .ReturnsAsync(new StudDirectory { Id = studDirId, Name = "Oak Directory" });
        _userServiceMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(new User { Id = Guid.NewGuid() });

        var result = await CreateSut().LinkStudFarmToDirectoryAsync(farm.Id, studDirId);

        result.Succeeded.Should().BeTrue();
        farm.StudDirectoryId.Should().Be(studDirId);
        _studFarmRepoMock.Verify(r => r.UpdateAsync(farm), Times.Once);
        _auditRepoMock.Verify(r => r.LogAsync("StudFarm", farm.Id, "LinkStudDirectory",
            It.IsAny<Guid?>(), It.IsAny<string?>()), Times.Once);
    }

    // ── CreateStudFarm with StudDirectoryId ──────────────────────────────────

    [Fact]
    public async Task CreateStudFarmAsync_SetsStudDirectoryId_WhenProvided()
    {
        var userId = Guid.NewGuid();
        var studDirId = Guid.NewGuid();
        var user = new User { Id = userId, Role = UserRole.StudFarmAdmin };
        _userRepoMock.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(user);
        _studFarmRepoMock.Setup(r => r.GetByUserIdAsync(userId)).ReturnsAsync((StudFarm?)null);
        _studFarmRepoMock.Setup(r => r.AddAsync(It.IsAny<StudFarm>()))
            .ReturnsAsync((StudFarm f) => f);
        _userServiceMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(new User { Id = Guid.NewGuid() });

        var request = new CreateStudFarmRequest
        {
            UserId = userId,
            Name = "New Farm",
            StudDirectoryId = studDirId
        };

        var result = await CreateSut().CreateStudFarmAsync(request);

        result.Succeeded.Should().BeTrue();
        _studFarmRepoMock.Verify(
            r => r.AddAsync(It.Is<StudFarm>(f => f.StudDirectoryId == studDirId)),
            Times.Once);
    }

    private static Purchase CompletedSale(StudFarm farm, decimal price, decimal buyerFee) => new()
    {
        Id = Guid.NewGuid(),
        Status = PurchaseStatus.Completed,
        PaidAt = DateTime.UtcNow.AddDays(-1),
        TotalPriceIncGst = price,
        BuyerFeeIncGst = buyerFee,
        BuyerFeeExGst = buyerFee - Math.Round(buyerFee / 11m, 2),
        BuyerFeeGst = Math.Round(buyerFee / 11m, 2),
        BalancePayableToStudIncGst = price - buyerFee,
        Listing = new AuctionListing
        {
            StudFarmId = farm.Id, StudFarm = farm,
            Stallion = new Stallion { Name = "Snitzel" }
        },
        Buyer = new User { DisplayName = "Jane Buyer" }
    };

    [Fact]
    public async Task GetInvoices_ShowsBuyerFeesAndBalancePayableToStud()
    {
        var farm = new StudFarm { Id = Guid.NewGuid(), Name = "Arrowfield" };
        _purchaseRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Purchase>
        {
            CompletedSale(farm, 10000m, 150m),
            CompletedSale(farm, 20000m, 150m)
        });

        var result = await CreateSut().GetInvoicesAsync();

        var invoice = result.Value!.Should().ContainSingle().Subject;
        invoice.TotalSalesIncGst.Should().Be(30000m);
        invoice.TotalBuyerFeesIncGst.Should().Be(300m);
        invoice.TotalBalancePayableToStudIncGst.Should().Be(29700m);
        invoice.Lines.Should().Contain(l => l.BuyerFeeIncGst == 150m && l.BalancePayableToStudIncGst == 9850m);
    }

    [Fact]
    public async Task GetTransactions_MapsBuyerFeeGstSplitAndBalance()
    {
        var farm = new StudFarm { Id = Guid.NewGuid(), Name = "Arrowfield" };
        _purchaseRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Purchase>
        {
            CompletedSale(farm, 10000m, 150m)
        });

        var result = await CreateSut().GetTransactionsAsync();

        var t = result.Value!.Should().ContainSingle().Subject;
        t.BuyerFeeIncGst.Should().Be(150m);
        t.BuyerFeeExGst.Should().Be(136.36m);
        t.BuyerFeeGst.Should().Be(13.64m);
        t.BalancePayableToStudIncGst.Should().Be(9850m);
    }

    [Fact]
    public async Task GetDashboard_FeeRevenueSumsRecentBuyerFees()
    {
        var farm = new StudFarm { Id = Guid.NewGuid(), Name = "Arrowfield" };
        _listingRepoMock.Setup(r => r.GetActiveAsync(null, null, null)).ReturnsAsync(new List<Listing>());
        _userRepoMock.Setup(r => r.GetAllAsync(It.IsAny<UserRole?>(), It.IsAny<UserStatus?>()))
            .ReturnsAsync(new List<User>());
        _purchaseRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Purchase>
        {
            CompletedSale(farm, 10000m, 150m),
            CompletedSale(farm, 20000m, 150m)
        });

        var result = await CreateSut().GetDashboardAsync();

        result.Value!.RecentFeeRevenueIncGst.Should().Be(300m);
    }
}
