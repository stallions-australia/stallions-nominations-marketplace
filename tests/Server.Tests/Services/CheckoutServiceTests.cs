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
        StudFarmBalanceArrangement = "Farm will contact you.",
        RefundPolicy = "90% refund policy."
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

    [Fact]
    public async Task Initiate_WhenMareMissing_ReturnsBadRequest()
    {
        var buyer = VerifiedBuyer();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var listing = EndedAuction(buyerFee: 150m);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        var result = await CreateSut().InitiateCheckoutAsync(listing.Id,
            new CheckoutRequest { MareName = "" });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Initiate_WhenFeeNotSet_ReturnsBadRequest()
    {
        var buyer = VerifiedBuyer();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        var listing = EndedAuction(buyerFee: null);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        var result = await CreateSut().InitiateCheckoutAsync(listing.Id,
            new CheckoutRequest { MareName = "Bella" });

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

        var result = await CreateSut().InitiateCheckoutAsync(listing.Id,
            new CheckoutRequest { MareName = "Bella" });

        result.Succeeded.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.PlatformFeeIncGst.Should().Be(150.00m);
        captured.PlatformFeeGst.Should().Be(13.64m);
        captured.PlatformFeeExGst.Should().Be(136.36m);
        result.Value!.Disclosure.PlatformFeeIncGst.Should().Be(150.00m);
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
            PlatformFeeIncGst = 250m
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
}
