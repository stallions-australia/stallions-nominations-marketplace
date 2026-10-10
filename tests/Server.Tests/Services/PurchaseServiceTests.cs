using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class PurchaseServiceTests
{
    private readonly Mock<IPurchaseRepository> _purchases = new();
    private readonly Mock<IAuditLogRepository> _audit = new();
    private readonly Mock<IUserService> _users = new();

    private readonly Mock<IListingRepository> _listings = new();
    private readonly Mock<IBidRepository> _bids = new();

    private PurchaseService CreateSut() =>
        new(_purchases.Object, _audit.Object, _users.Object, _listings.Object, _bids.Object);

    private (User Buyer, AuctionListing Listing) BuyerAndAuction(ListingStatus status, ListingCloseReason? reason = null)
    {
        var buyer = Buyer();
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var listing = new AuctionListing { Id = Guid.NewGuid(), Status = status, CloseReason = reason };
        _listings.Setup(r => r.GetAuctionByIdAsync(listing.Id)).ReturnsAsync(listing);
        return (buyer, listing);
    }

    [Theory]
    [InlineData(PurchaseStatus.Completed, false, AuctionOutcomes.Won)]
    [InlineData(PurchaseStatus.Pending, false, AuctionOutcomes.PaymentPending)]
    [InlineData(PurchaseStatus.Pending, true, AuctionOutcomes.PaymentFailed)]
    [InlineData(PurchaseStatus.Voided, true, AuctionOutcomes.NoSale)]
    public async Task MyAuctionResult_ForTheWinner_FollowsTheSaleRecord(PurchaseStatus status, bool failed, string expected)
    {
        var (buyer, listing) = BuyerAndAuction(ListingStatus.AwaitingPayment);
        var dueBy = failed ? DateTime.UtcNow.AddHours(1) : (DateTime?)null;
        var sale = new Purchase { Id = Guid.NewGuid(), Status = status, ChargeDueBy = dueBy };
        _purchases.Setup(r => r.GetByListingAndBuyerAsync(listing.Id, buyer.Id)).ReturnsAsync(sale);

        var result = (await CreateSut().GetMyAuctionResultAsync(listing.Id)).Value!;

        result.Outcome.Should().Be(expected);
        result.PurchaseId.Should().Be(sale.Id);
        result.ChargeDueBy.Should().Be(dueBy);
    }

    [Theory]
    [InlineData(ListingStatus.Sold, null, AuctionOutcomes.Lost)]
    [InlineData(ListingStatus.Unsold, ListingCloseReason.ReserveNotMet, AuctionOutcomes.EndedWithoutSale)]
    [InlineData(ListingStatus.Unsold, ListingCloseReason.ChargeFailed, AuctionOutcomes.Lost)]
    public async Task MyAuctionResult_ForAnotherBidder(ListingStatus status, ListingCloseReason? reason, string expected)
    {
        var (buyer, listing) = BuyerAndAuction(status, reason);
        _bids.Setup(r => r.GetByAuctionListingIdAsync(listing.Id))
            .ReturnsAsync(new List<Bid> { new() { BuyerUserId = buyer.Id, AmountIncGst = 5000m } });

        (await CreateSut().GetMyAuctionResultAsync(listing.Id)).Value!.Outcome.Should().Be(expected);
    }

    [Fact]
    public async Task MyAuctionResult_WhileOpen_OrWithoutBids_IsNone()
    {
        var (_, open) = BuyerAndAuction(ListingStatus.Active);
        _bids.Setup(r => r.GetByAuctionListingIdAsync(It.IsAny<Guid>())).ReturnsAsync(new List<Bid>());

        (await CreateSut().GetMyAuctionResultAsync(open.Id)).Value!.Outcome.Should().Be(AuctionOutcomes.None);
    }

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
