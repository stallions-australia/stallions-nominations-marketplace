using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class NominationBindingServiceTests
{
    private readonly Mock<INominationBindingRepository> _bindingRepoMock = new();
    private readonly Mock<IPurchaseRepository> _purchaseRepoMock = new();
    private readonly Mock<IListingRepository> _listingRepoMock = new();
    private readonly Mock<IStudFarmRepository> _farmRepoMock = new();
    private readonly Mock<IUserService> _usersMock = new();

    private NominationBindingService CreateSut() => new(
        _bindingRepoMock.Object, _purchaseRepoMock.Object,
        _listingRepoMock.Object, _farmRepoMock.Object, _usersMock.Object);

    [Fact]
    public async Task Sign_WhenBuyerSigns_SetsBuyerSignedAtAndStatusBuyerSigned()
    {
        var buyer = new User { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.Active };
        var listing = new FixedPriceListing { Id = Guid.NewGuid(), StudFarmId = Guid.NewGuid() };
        var purchase = new Purchase { Id = Guid.NewGuid(), BuyerUserId = buyer.Id, ListingId = listing.Id };
        var binding = new NominationBinding { Id = Guid.NewGuid(), PurchaseId = purchase.Id,
            Status = BindingStatus.AwaitingSignatures };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        _bindingRepoMock.Setup(r => r.GetByIdAsync(binding.Id)).ReturnsAsync(binding);
        _purchaseRepoMock.Setup(r => r.GetByIdAsync(purchase.Id)).ReturnsAsync(purchase);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(buyer.Id)).ReturnsAsync((StudFarm?)null);

        var result = await CreateSut().SignAsync(binding.Id);

        result.Succeeded.Should().BeTrue();
        binding.BuyerSignedAt.Should().NotBeNull();
        binding.Status.Should().Be(BindingStatus.BuyerSigned);
        binding.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Sign_WhenBothPartiesHaveSigned_SetsStatusComplete()
    {
        var buyer = new User { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.Active };
        var farmId = Guid.NewGuid();
        var listing = new FixedPriceListing { Id = Guid.NewGuid(), StudFarmId = farmId };
        var purchase = new Purchase { Id = Guid.NewGuid(), BuyerUserId = buyer.Id, ListingId = listing.Id };
        var binding = new NominationBinding
        {
            Id = Guid.NewGuid(), PurchaseId = purchase.Id,
            Status = BindingStatus.FarmSigned,
            FarmSignedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        _bindingRepoMock.Setup(r => r.GetByIdAsync(binding.Id)).ReturnsAsync(binding);
        _purchaseRepoMock.Setup(r => r.GetByIdAsync(purchase.Id)).ReturnsAsync(purchase);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(buyer.Id)).ReturnsAsync((StudFarm?)null);

        var result = await CreateSut().SignAsync(binding.Id);

        result.Succeeded.Should().BeTrue();
        binding.BuyerSignedAt.Should().NotBeNull();
        binding.Status.Should().Be(BindingStatus.Complete);
        binding.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Sign_WhenBuyerAlreadySigned_ReturnsBadRequest()
    {
        var buyer = new User { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.Active };
        var listing = new FixedPriceListing { Id = Guid.NewGuid(), StudFarmId = Guid.NewGuid() };
        var purchase = new Purchase { Id = Guid.NewGuid(), BuyerUserId = buyer.Id, ListingId = listing.Id };
        var binding = new NominationBinding
        {
            Id = Guid.NewGuid(), PurchaseId = purchase.Id,
            Status = BindingStatus.BuyerSigned,
            BuyerSignedAt = DateTime.UtcNow.AddHours(-1)
        };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        _bindingRepoMock.Setup(r => r.GetByIdAsync(binding.Id)).ReturnsAsync(binding);
        _purchaseRepoMock.Setup(r => r.GetByIdAsync(purchase.Id)).ReturnsAsync(purchase);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(buyer.Id)).ReturnsAsync((StudFarm?)null);

        var result = await CreateSut().SignAsync(binding.Id);

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Sign_WhenCallerIsNeitherPartyNorStaff_ReturnsForbidden()
    {
        var stranger = new User { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.Active };
        var listing = new FixedPriceListing { Id = Guid.NewGuid(), StudFarmId = Guid.NewGuid() };
        var purchase = new Purchase { Id = Guid.NewGuid(), BuyerUserId = Guid.NewGuid(), ListingId = listing.Id };
        var binding = new NominationBinding { Id = Guid.NewGuid(), PurchaseId = purchase.Id,
            Status = BindingStatus.AwaitingSignatures };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(stranger);
        _bindingRepoMock.Setup(r => r.GetByIdAsync(binding.Id)).ReturnsAsync(binding);
        _purchaseRepoMock.Setup(r => r.GetByIdAsync(purchase.Id)).ReturnsAsync(purchase);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(stranger.Id)).ReturnsAsync((StudFarm?)null);

        var result = await CreateSut().SignAsync(binding.Id);

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Acknowledge_WhenFarmAdminAcknowledges_TransitionsToAwaitingSignatures()
    {
        var farmId = Guid.NewGuid();
        var farm = new StudFarm { Id = farmId };
        var admin = new User { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };
        var listing = new FixedPriceListing { Id = Guid.NewGuid(), StudFarmId = farmId };
        var purchase = new Purchase { Id = Guid.NewGuid(), BuyerUserId = Guid.NewGuid(), ListingId = listing.Id };
        var binding = new NominationBinding
        {
            Id = Guid.NewGuid(), PurchaseId = purchase.Id,
            Status = BindingStatus.PendingAcknowledgement
        };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(admin);
        _bindingRepoMock.Setup(r => r.GetByIdAsync(binding.Id)).ReturnsAsync(binding);
        _purchaseRepoMock.Setup(r => r.GetByIdAsync(purchase.Id)).ReturnsAsync(purchase);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(admin.Id)).ReturnsAsync(farm);

        var result = await CreateSut().AcknowledgeAsync(binding.Id);

        result.Succeeded.Should().BeTrue();
        binding.Status.Should().Be(BindingStatus.AwaitingSignatures);
        binding.AcknowledgedAt.Should().NotBeNull();
        binding.AcknowledgedByUserId.Should().Be(admin.Id);
    }

    [Fact]
    public async Task Acknowledge_WhenBuyerTries_ReturnsForbidden()
    {
        var buyer = new User { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.Active };
        var listing = new FixedPriceListing { Id = Guid.NewGuid(), StudFarmId = Guid.NewGuid() };
        var purchase = new Purchase { Id = Guid.NewGuid(), BuyerUserId = buyer.Id, ListingId = listing.Id };
        var binding = new NominationBinding
        {
            Id = Guid.NewGuid(), PurchaseId = purchase.Id,
            Status = BindingStatus.PendingAcknowledgement
        };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        _bindingRepoMock.Setup(r => r.GetByIdAsync(binding.Id)).ReturnsAsync(binding);
        _purchaseRepoMock.Setup(r => r.GetByIdAsync(purchase.Id)).ReturnsAsync(purchase);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(buyer.Id)).ReturnsAsync((StudFarm?)null);

        var result = await CreateSut().AcknowledgeAsync(binding.Id);

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Dispute_WhenCallerIsNull_ReturnsForbidden()
    {
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync((User?)null);

        var result = await CreateSut().DisputeAsync(Guid.NewGuid());

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Dispute_WhenStaffDisputes_SetsStatusDisputed()
    {
        var staff = new User { Id = Guid.NewGuid(), Role = UserRole.Staff, Status = UserStatus.Active };
        var binding = new NominationBinding
        {
            Id = Guid.NewGuid(), PurchaseId = Guid.NewGuid(),
            Status = BindingStatus.AwaitingSignatures
        };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(staff);
        _bindingRepoMock.Setup(r => r.GetByIdAsync(binding.Id)).ReturnsAsync(binding);

        var result = await CreateSut().DisputeAsync(binding.Id);

        result.Succeeded.Should().BeTrue();
        binding.Status.Should().Be(BindingStatus.Disputed);
    }
}
