using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Payments;
using Stallions.Server.Services;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class CardServiceTests
{
    private readonly Mock<ISavedCardRepository> _cards = new();
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IUserService> _users = new();
    private readonly Mock<IPaymentProvider> _provider = new();

    private CardService CreateSut() => new(_cards.Object, _userRepo.Object, _users.Object, _provider.Object);

    public CardServiceTests() => _provider.SetupGet(p => p.Name).Returns("Stripe");

    private User SignedIn(UserRole role, string? customerId = null, string customerProvider = "Stripe")
    {
        var user = new User
        {
            Id = Guid.NewGuid(), Email = "b@x", DisplayName = "B", Role = role,
            Status = UserStatus.Active, PaymentCustomerId = customerId,
            PaymentCustomerProvider = customerId == null ? null : customerProvider
        };
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(user);
        return user;
    }

    [Fact]
    public async Task StartSetup_CustomerFromAnotherProvider_IsReplaced()
    {
        // e.g. dev data created with the fake provider, now running on Stripe
        var buyer = SignedIn(UserRole.Buyer, customerId: "cus_fake_1", customerProvider: "Fake");
        _provider.Setup(p => p.CreateCustomerAsync(buyer.Id, "b@x", "B")).ReturnsAsync("cus_real");
        _provider.Setup(p => p.CreateCardSetupSessionAsync(buyer.Id, "cus_real", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("https://pay/setup");

        await CreateSut().StartSetupAsync("https://ok", "https://no");

        buyer.PaymentCustomerId.Should().Be("cus_real");
        buyer.PaymentCustomerProvider.Should().Be("Stripe");
    }

    [Fact]
    public async Task StartSetup_FirstTime_CreatesAndStoresTheCustomer()
    {
        var buyer = SignedIn(UserRole.Buyer);
        _provider.Setup(p => p.CreateCustomerAsync(buyer.Id, "b@x", "B")).ReturnsAsync("cus_new");
        _provider.Setup(p => p.CreateCardSetupSessionAsync(buyer.Id, "cus_new", "https://ok", "https://no"))
            .ReturnsAsync("https://pay/setup");

        var result = await CreateSut().StartSetupAsync("https://ok", "https://no");

        result.Value!.Url.Should().Be("https://pay/setup");
        buyer.PaymentCustomerId.Should().Be("cus_new");
        buyer.PaymentCustomerProvider.Should().Be("Stripe");
        _userRepo.Verify(r => r.UpdateAsync(buyer), Times.Once);
    }

    [Fact]
    public async Task StartSetup_ReusesAnExistingCustomer()
    {
        var buyer = SignedIn(UserRole.Buyer, customerId: "cus_old");
        _provider.Setup(p => p.CreateCardSetupSessionAsync(buyer.Id, "cus_old", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("https://pay/setup");

        await CreateSut().StartSetupAsync("https://ok", "https://no");

        _provider.Verify(p => p.CreateCustomerAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(UserRole.StudFarmAdmin)]
    [InlineData(UserRole.Staff)]
    public async Task StartSetup_NonBuyer_IsForbidden(UserRole role)
    {
        SignedIn(role);

        (await CreateSut().StartSetupAsync("https://ok", "https://no")).HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GetMine_ReturnsCardWithValidity()
    {
        var buyer = SignedIn(UserRole.Buyer);
        _cards.Setup(c => c.GetByUserIdAsync(buyer.Id)).ReturnsAsync(new SavedCard
            { UserId = buyer.Id, Provider = "Stripe", Brand = "visa", Last4 = "4242", ExpMonth = 1, ExpYear = 2020 });

        var result = await CreateSut().GetMineAsync();

        result.Value!.Last4.Should().Be("4242");
        result.Value.IsValid.Should().BeFalse("it expired in January 2020");
    }

    [Fact]
    public async Task GetMine_ReturnsWhenTheCardWasLastSaved()
    {
        var buyer = SignedIn(UserRole.Buyer);
        var updatedAt = new DateTime(2026, 10, 9, 3, 4, 5, DateTimeKind.Utc);
        _cards.Setup(c => c.GetByUserIdAsync(buyer.Id)).ReturnsAsync(new SavedCard
            { UserId = buyer.Id, Provider = "Stripe", Brand = "visa", Last4 = "4242", ExpMonth = 8, ExpYear = 2028, UpdatedAt = updatedAt });

        (await CreateSut().GetMineAsync()).Value!.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public async Task GetMine_WithoutCard_IsNotFound()
    {
        SignedIn(UserRole.Buyer);

        (await CreateSut().GetMineAsync()).HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task HasValidCard_FalseWhenNoneOrExpired_TrueWhenCurrent()
    {
        var none = Guid.NewGuid();
        var expired = Guid.NewGuid();
        var current = Guid.NewGuid();
        var otherProvider = Guid.NewGuid();
        _cards.Setup(c => c.GetByUserIdAsync(expired)).ReturnsAsync(new SavedCard { Provider = "Stripe", ExpMonth = 1, ExpYear = 2020 });
        _cards.Setup(c => c.GetByUserIdAsync(current)).ReturnsAsync(new SavedCard { Provider = "Stripe", ExpMonth = 12, ExpYear = DateTime.UtcNow.Year + 2 });
        _cards.Setup(c => c.GetByUserIdAsync(otherProvider)).ReturnsAsync(new SavedCard { Provider = "Fake", ExpMonth = 12, ExpYear = DateTime.UtcNow.Year + 2 });

        (await CreateSut().HasValidCardAsync(none)).Should().BeFalse();
        (await CreateSut().HasValidCardAsync(expired)).Should().BeFalse();
        (await CreateSut().HasValidCardAsync(current)).Should().BeTrue();
        (await CreateSut().HasValidCardAsync(otherProvider)).Should().BeFalse("a card saved with another provider can't be charged by this one");
    }

    [Fact]
    public async Task GetMine_CardFromAnotherProvider_IsNotValid()
    {
        var buyer = SignedIn(UserRole.Buyer);
        _cards.Setup(c => c.GetByUserIdAsync(buyer.Id)).ReturnsAsync(new SavedCard
            { UserId = buyer.Id, Provider = "Fake", Brand = "visa", Last4 = "4242", ExpMonth = 12, ExpYear = DateTime.UtcNow.Year + 2 });

        (await CreateSut().GetMineAsync()).Value!.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task NullCaller_IsForbidden_ForGetMineAndStartSetup()
    {
        _users.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync((User?)null);

        (await CreateSut().GetMineAsync()).HttpStatusCode.Should().Be(403);
        (await CreateSut().StartSetupAsync("https://ok", "https://no")).HttpStatusCode.Should().Be(403);
    }

    [Theory]
    [InlineData(UserRole.StudFarmAdmin)]
    [InlineData(UserRole.Staff)]
    public async Task GetMine_NonBuyer_IsForbidden(UserRole role)
    {
        SignedIn(role);

        (await CreateSut().GetMineAsync()).HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task StartSetup_ReusePath_DoesNotUpdateTheUser()
    {
        var buyer = SignedIn(UserRole.Buyer, customerId: "cus_old");
        _provider.Setup(p => p.CreateCardSetupSessionAsync(buyer.Id, "cus_old", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("https://pay/setup");

        await CreateSut().StartSetupAsync("https://ok", "https://no");

        _userRepo.Verify(r => r.UpdateAsync(It.IsAny<User>()), Times.Never);
    }
}
