using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Checkout;
using Stallions.Shared.DTOs.Payments;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class AccountCardTests : TestContext
{
    private static HttpClient Http() => new() { BaseAddress = new Uri("https://localhost/") };
    private readonly Mock<PaymentsApiService> _payments = new(MockBehavior.Loose, Http());

    private IRenderedComponent<AccountCard> Render(string? query = null)
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        Services.AddSingleton(_payments.Object);

        var listingApi = new Mock<ListingApiService>(MockBehavior.Loose, Http());
        listingApi.Setup(s => s.GetBuyerFeeDisclosureAsync()).ReturnsAsync(new BuyerFeeDisclosureDto
            { SavedCardExplanation = "CONFIGURED: charged the buyer fee automatically." });
        Services.AddSingleton(listingApi.Object);

        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "B", Email = "b@x", Role = "Buyer", Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));

        if (query != null) Services.GetRequiredService<NavigationManager>().NavigateTo($"/account/card?{query}");
        return RenderComponent<AccountCard>(p => p.Add(c => c.PollInterval, TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public void NoCard_ShowsAddCardAndTheConfiguredDisclosure()
    {
        _payments.Setup(s => s.GetMyCardAsync()).ReturnsAsync((SavedCardDto?)null);

        var cut = Render();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No card saved"));
        cut.Markup.Should().Contain("Add card").And.Contain("CONFIGURED: charged the buyer fee automatically.");
    }

    [Fact]
    public void SavedCard_ShowsBrandLast4AndExpiry()
    {
        _payments.Setup(s => s.GetMyCardAsync()).ReturnsAsync(new SavedCardDto
            { Brand = "visa", Last4 = "4242", ExpMonth = 8, ExpYear = 2028, IsValid = true });

        var cut = Render();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("•••• 4242"));
        cut.Markup.Should().Contain("08/28").And.Contain("Replace card");
    }

    [Fact]
    public void ExpiredCard_IsFlagged()
    {
        _payments.Setup(s => s.GetMyCardAsync()).ReturnsAsync(new SavedCardDto
            { Brand = "visa", Last4 = "4242", ExpMonth = 1, ExpYear = 2020, IsValid = false });

        var cut = Render();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("expired"));
    }

    [Fact]
    public void AddCard_SendsTheBrowserToTheHostedPage()
    {
        _payments.Setup(s => s.GetMyCardAsync()).ReturnsAsync((SavedCardDto?)null);
        _payments.Setup(s => s.StartCardSetupAsync()).ReturnsAsync("https://checkout.example/setup");
        var cut = Render();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Add card"));

        cut.FindAll("button").Single(b => b.TextContent.Contains("Add card")).Click();

        cut.WaitForAssertion(() =>
            Services.GetRequiredService<NavigationManager>().Uri.Should().Be("https://checkout.example/setup"));
    }

    [Fact]
    public void ReturningFromHostedPage_PollsUntilTheCardArrives()
    {
        _payments.SetupSequence(s => s.GetMyCardAsync())
            .ReturnsAsync((SavedCardDto?)null)
            .ReturnsAsync((SavedCardDto?)null)
            .ReturnsAsync(new SavedCardDto { Brand = "visa", Last4 = "4242", ExpMonth = 8, ExpYear = 2028, IsValid = true });

        var cut = Render("result=success");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("•••• 4242"), TimeSpan.FromSeconds(5));
    }
}
