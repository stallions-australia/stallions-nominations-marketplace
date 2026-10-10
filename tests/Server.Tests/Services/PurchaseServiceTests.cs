using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class PurchaseServiceTests
{
    private readonly Mock<IPurchaseRepository> _purchases = new();
    private readonly Mock<IAuditLogRepository> _audit = new();
    private readonly Mock<IUserService> _users = new();

    private PurchaseService CreateSut() => new(_purchases.Object, _audit.Object, _users.Object);

    private static User Buyer() => new() { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.Active };

    [Fact]
    public async Task Refund_RefundsTheFullBuyerFee()
    {
        var staff = new User { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active };
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(staff);
        var purchase = new Purchase
        {
            Id = Guid.NewGuid(), Status = PurchaseStatus.Completed,
            TotalPriceIncGst = 10000m, BuyerFeeIncGst = 150m, BalancePayableToStudIncGst = 9850m
        };
        _purchases.Setup(r => r.GetByIdAsync(purchase.Id)).ReturnsAsync(purchase);

        var result = await CreateSut().RefundAsync(purchase.Id);

        result.Succeeded.Should().BeTrue();
        purchase.RefundAmount.Should().Be(150m);
        purchase.Status.Should().Be(PurchaseStatus.Refunded);
    }

    [Fact]
    public async Task Refund_ByABuyer_IsForbidden()
    {
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(Buyer());

        var result = await CreateSut().RefundAsync(Guid.NewGuid());

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GetPurchaseById_ForAnotherBuyersSaleRecord_IsForbidden()
    {
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(Buyer());
        var purchase = new Purchase { Id = Guid.NewGuid(), BuyerUserId = Guid.NewGuid() };
        _purchases.Setup(r => r.GetByIdAsync(purchase.Id)).ReturnsAsync(purchase);

        var result = await CreateSut().GetPurchaseByIdAsync(purchase.Id);

        result.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GetPurchases_MapsTheChargeState()
    {
        var buyer = Buyer();
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var dueBy = new DateTime(2026, 10, 10, 4, 0, 0, DateTimeKind.Utc);
        _purchases.Setup(r => r.GetByBuyerIdAsync(buyer.Id)).ReturnsAsync(new List<Purchase>
        {
            new()
            {
                BuyerUserId = buyer.Id, Status = PurchaseStatus.Pending, ChargeDueBy = dueBy,
                LastChargeFailure = "Your card was declined.",
                Listing = new AuctionListing
                {
                    Stallion = new Stallion { Name = "Snitzel" },
                    Season = new Season { Name = "2026 Season" },
                    StudFarm = new StudFarm { Name = "Arrowfield" }
                }
            }
        });

        var result = await CreateSut().GetPurchasesAsync();

        var dto = result.Value!.Single();
        dto.StallionName.Should().Be("Snitzel");
        dto.SeasonName.Should().Be("2026 Season");
        dto.StudFarmName.Should().Be("Arrowfield");
        dto.ChargeDueBy.Should().Be(dueBy);
        dto.LastChargeFailure.Should().Be("Your card was declined.");
    }
}
