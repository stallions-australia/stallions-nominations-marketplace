using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Moq;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using CheckoutOptions = Stallions.Server.Options.CheckoutOptions;
using Stallions.Shared.DTOs.Checkout;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class CheckoutServiceTests
{
    private readonly Mock<IListingRepository> _listingRepoMock = new();
    private readonly Mock<IBidRepository> _bidRepoMock = new();
    private readonly Mock<IPurchaseRepository> _purchaseRepoMock = new();
    private readonly Mock<INominationBindingRepository> _bindingRepoMock = new();
    private readonly Mock<IAuditLogRepository> _auditRepoMock = new();
    private readonly Mock<IUserService> _usersMock = new();
    private readonly IOptions<CheckoutOptions> _options = Microsoft.Extensions.Options.Options.Create(new CheckoutOptions
    {
        WebhookSecret = "test-secret",
        StudFarmBalanceArrangement = "Farm will contact you."
    });

    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private CheckoutService CreateSut() => new(
        _listingRepoMock.Object, _bidRepoMock.Object, _purchaseRepoMock.Object,
        _bindingRepoMock.Object, _auditRepoMock.Object, _usersMock.Object, _options,
        CreateInMemoryDb());

    private static User VerifiedBuyer() => new()
        { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.Active };

    private static AuctionListing EndedAuction(decimal? buyerFee) => new()
    {
        Id = Guid.NewGuid(), Status = ListingStatus.Active,
        BuyerFeeIncGst = buyerFee,
        EndDateTime = DateTime.UtcNow.AddHours(-1)
    };

    private void BuyerWonAt(AuctionListing listing, User buyer, decimal amount) =>
        _bidRepoMock.Setup(r => r.GetHighestBidAsync(listing.Id)).ReturnsAsync(new Bid
        {
            Id = Guid.NewGuid(), AuctionListingId = listing.Id, BuyerUserId = buyer.Id, AmountIncGst = amount
        });

    [Fact]
    public async Task Initiate_DoesNotRequireMareDetails()
    {
        var buyer = VerifiedBuyer();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var listing = EndedAuction(buyerFee: 150m);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        BuyerWonAt(listing, buyer, 10000m);
        _purchaseRepoMock.Setup(r => r.AddAsync(It.IsAny<Purchase>())).ReturnsAsync((Purchase p) => p);

        var result = await CreateSut().InitiateCheckoutAsync(listing.Id, new CheckoutRequest());

        result.Succeeded.Should().BeTrue(result.Error);
        result.HttpStatusCode.Should().Be(201);
    }

    [Fact]
    public async Task Initiate_RecordsBalancePayableToStud_AsPriceLessBuyerFee()
    {
        // Winning bid $10,000, buyer fee $150 → the stud invoices the buyer $9,850.
        var buyer = VerifiedBuyer();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var listing = EndedAuction(buyerFee: 150m);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        BuyerWonAt(listing, buyer, 10000m);
        Purchase? captured = null;
        _purchaseRepoMock.Setup(r => r.AddAsync(It.IsAny<Purchase>()))
            .Callback<Purchase>(p => captured = p)
            .ReturnsAsync((Purchase p) => p);

        var result = await CreateSut().InitiateCheckoutAsync(listing.Id, new CheckoutRequest());

        captured!.TotalPriceIncGst.Should().Be(10000m);
        captured.BuyerFeeIncGst.Should().Be(150m);
        captured.BalancePayableToStudIncGst.Should().Be(9850m);
        result.Value!.Disclosure.TotalPriceIncGst.Should().Be(10000m);
        result.Value.Disclosure.BuyerFeeIncGst.Should().Be(150m);
        result.Value.Disclosure.BalancePayableToStudIncGst.Should().Be(9850m);
    }

    [Fact]
    public async Task Initiate_WhenFeeNotSet_ReturnsBadRequest()
    {
        var buyer = VerifiedBuyer();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var listing = EndedAuction(buyerFee: null);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        var result = await CreateSut().InitiateCheckoutAsync(listing.Id, new CheckoutRequest());

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Initiate_CalculatesGstCorrectly()
    {
        // $150 buyer fee snapshotted on the listing: FeeGst=$150/11=$13.64, FeeExGst=$136.36
        var buyer = VerifiedBuyer();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var listing = EndedAuction(buyerFee: 150m);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        _bidRepoMock.Setup(r => r.GetHighestBidAsync(listing.Id)).ReturnsAsync(new Bid
        {
            Id = Guid.NewGuid(), AuctionListingId = listing.Id, BuyerUserId = buyer.Id, AmountIncGst = 10000m
        });
        Purchase? captured = null;
        _purchaseRepoMock.Setup(r => r.AddAsync(It.IsAny<Purchase>()))
            .Callback<Purchase>(p => captured = p)
            .ReturnsAsync((Purchase p) => p);

        var result = await CreateSut().InitiateCheckoutAsync(listing.Id, new CheckoutRequest());

        result.Succeeded.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.BuyerFeeIncGst.Should().Be(150.00m);
        captured.BuyerFeeGst.Should().Be(13.64m);
        captured.BuyerFeeExGst.Should().Be(136.36m);
        result.Value!.Disclosure.BuyerFeeIncGst.Should().Be(150.00m);
    }

    [Fact]
    public async Task Complete_WhenWrongWebhookSecret_ReturnsForbidden()
    {
        var purchase = new Purchase { Id = Guid.NewGuid(), Status = PurchaseStatus.Pending };
        _purchaseRepoMock.Setup(r => r.GetByIdAsync(purchase.Id)).ReturnsAsync(purchase);

        var result = await CreateSut().CompleteCheckoutAsync(purchase.Id, "wrong-secret");

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Complete_WhenValid_CreatesNominationBinding()
    {
        var listing = new AuctionListing
        {
            Id = Guid.NewGuid(), Status = ListingStatus.Active,
            EndDateTime = DateTime.UtcNow.AddHours(-1)
        };
        var purchase = new Purchase
        {
            Id = Guid.NewGuid(), ListingId = listing.Id, Status = PurchaseStatus.Pending,
            BuyerFeeIncGst = 150m
        };
        _purchaseRepoMock.Setup(r => r.GetByIdAsync(purchase.Id)).ReturnsAsync(purchase);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        var result = await CreateSut().CompleteCheckoutAsync(purchase.Id, "test-secret");

        result.Succeeded.Should().BeTrue();
        _bindingRepoMock.Verify(r => r.AddAsync(It.Is<NominationBinding>(b =>
            b.PurchaseId == purchase.Id && b.Status == BindingStatus.PendingAcknowledgement)), Times.Once);
        _auditRepoMock.Verify(r => r.LogAsync("Purchase", purchase.Id, "PurchaseCompleted",
            null, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task Refund_RefundsTheFullBuyerFee()
    {
        var staff = new User { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(staff);
        var purchase = new Purchase
        {
            Id = Guid.NewGuid(), Status = PurchaseStatus.Completed,
            TotalPriceIncGst = 10000m, BuyerFeeIncGst = 150m, BalancePayableToStudIncGst = 9850m
        };
        _purchaseRepoMock.Setup(r => r.GetByIdAsync(purchase.Id)).ReturnsAsync(purchase);

        var result = await CreateSut().RefundAsync(purchase.Id);

        result.Succeeded.Should().BeTrue();
        purchase.RefundAmount.Should().Be(150m);
        purchase.Status.Should().Be(PurchaseStatus.Refunded);
        _auditRepoMock.Verify(r => r.LogAsync("Purchase", purchase.Id, "PurchaseRefunded",
            staff.Id, It.IsAny<string?>()), Times.Once);
    }
}
