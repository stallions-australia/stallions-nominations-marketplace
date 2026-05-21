using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Enquiries;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class EnquiryServiceTests
{
    private readonly Mock<IEnquiryRepository> _enquiryRepoMock = new();
    private readonly Mock<IListingRepository> _listingRepoMock = new();
    private readonly Mock<IStudFarmRepository> _studFarmRepoMock = new();
    private readonly Mock<IUserService> _usersMock = new();

    private EnquiryService CreateSut() => new(
        _enquiryRepoMock.Object, _listingRepoMock.Object,
        _studFarmRepoMock.Object, _usersMock.Object);

    private static User ActiveBuyer() => new()
        { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.Active };

    private static User ActiveFarmAdmin() => new()
        { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };

    [Fact]
    public async Task PostMessage_WhenCallerIsNotParticipant_ReturnsForbidden()
    {
        var stranger = new User { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.Active };
        var enquiry = new Enquiry
        {
            Id = Guid.NewGuid(), BuyerUserId = Guid.NewGuid(),
            StudFarmUserId = Guid.NewGuid(),
            Status = EnquiryStatus.Open, Messages = new List<EnquiryMessage>()
        };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(stranger);
        _enquiryRepoMock.Setup(r => r.GetByIdAsync(enquiry.Id)).ReturnsAsync(enquiry);

        var result = await CreateSut().PostMessageAsync(enquiry.Id, new SendMessageRequest { Body = "Hello" });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task PostMessage_WhenEnquiryClosed_ReturnsBadRequest()
    {
        var buyer = ActiveBuyer();
        var enquiry = new Enquiry
        {
            Id = Guid.NewGuid(), BuyerUserId = buyer.Id,
            StudFarmUserId = Guid.NewGuid(),
            Status = EnquiryStatus.Closed, Messages = new List<EnquiryMessage>()
        };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        _enquiryRepoMock.Setup(r => r.GetByIdAsync(enquiry.Id)).ReturnsAsync(enquiry);

        var result = await CreateSut().PostMessageAsync(enquiry.Id, new SendMessageRequest { Body = "Hello" });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CloseEnquiry_WhenCallerIsBuyer_Succeeds()
    {
        var buyer = ActiveBuyer();
        var enquiry = new Enquiry
        {
            Id = Guid.NewGuid(), BuyerUserId = buyer.Id,
            StudFarmUserId = Guid.NewGuid(),
            Status = EnquiryStatus.Open, Messages = new List<EnquiryMessage>()
        };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        _enquiryRepoMock.Setup(r => r.GetByIdAsync(enquiry.Id)).ReturnsAsync(enquiry);

        var result = await CreateSut().CloseAsync(enquiry.Id);

        result.Succeeded.Should().BeTrue();
        enquiry.Status.Should().Be(EnquiryStatus.Closed);
    }

    [Fact]
    public async Task CloseEnquiry_WhenCallerIsStrangerBuyer_ReturnsForbidden()
    {
        var stranger = new User { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.Active };
        var enquiry = new Enquiry
        {
            Id = Guid.NewGuid(), BuyerUserId = Guid.NewGuid(),
            StudFarmUserId = Guid.NewGuid(),
            Status = EnquiryStatus.Open, Messages = new List<EnquiryMessage>()
        };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(stranger);
        _enquiryRepoMock.Setup(r => r.GetByIdAsync(enquiry.Id)).ReturnsAsync(enquiry);

        var result = await CreateSut().CloseAsync(enquiry.Id);

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Create_WhenBuyerIsActive_CreatesEnquiry()
    {
        var buyer = ActiveBuyer();
        var farmId = Guid.NewGuid();
        var studFarm = new StudFarm { Id = farmId, UserId = Guid.NewGuid() };
        var listing = new FixedPriceListing { Id = Guid.NewGuid(), StudFarmId = farmId, Status = ListingStatus.Active };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        _studFarmRepoMock.Setup(r => r.GetByIdAsync(farmId)).ReturnsAsync(studFarm);
        _enquiryRepoMock.Setup(r => r.GetByBuyerIdAsync(buyer.Id)).ReturnsAsync(new List<Enquiry>());
        _enquiryRepoMock.Setup(r => r.AddAsync(It.IsAny<Enquiry>()))
            .ReturnsAsync((Enquiry e) => e);

        var result = await CreateSut().CreateAsync(listing.Id, new OpenEnquiryRequest { Body = "Interested" });

        result.Succeeded.Should().BeTrue();
        result.HttpStatusCode.Should().Be(201);
    }

    [Fact]
    public async Task Create_WhenAccountNotActive_ReturnsForbidden()
    {
        var buyer = new User { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = UserStatus.PendingVerification };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);

        var result = await CreateSut().CreateAsync(Guid.NewGuid(), new OpenEnquiryRequest { Body = "Hello" });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Create_WhenListingNotActive_ReturnsBadRequest()
    {
        var buyer = ActiveBuyer();
        var listing = new FixedPriceListing { Id = Guid.NewGuid(), StudFarmId = Guid.NewGuid(), Status = ListingStatus.Draft };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);

        var result = await CreateSut().CreateAsync(listing.Id, new OpenEnquiryRequest { Body = "Hello" });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Create_WhenDuplicateOpenEnquiryExists_ReturnsConflict()
    {
        var buyer = ActiveBuyer();
        var farmId = Guid.NewGuid();
        var studFarm = new StudFarm { Id = farmId, UserId = Guid.NewGuid() };
        var listing = new FixedPriceListing { Id = Guid.NewGuid(), StudFarmId = farmId, Status = ListingStatus.Active };
        var existing = new Enquiry { Id = Guid.NewGuid(), ListingId = listing.Id, BuyerUserId = buyer.Id, Status = EnquiryStatus.Open, Messages = new List<EnquiryMessage>() };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(buyer);
        _listingRepoMock.Setup(r => r.GetByIdAsync(listing.Id)).ReturnsAsync(listing);
        _studFarmRepoMock.Setup(r => r.GetByIdAsync(farmId)).ReturnsAsync(studFarm);
        _enquiryRepoMock.Setup(r => r.GetByBuyerIdAsync(buyer.Id)).ReturnsAsync(new List<Enquiry> { existing });

        var result = await CreateSut().CreateAsync(listing.Id, new OpenEnquiryRequest { Body = "Again" });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(409);
    }
}
