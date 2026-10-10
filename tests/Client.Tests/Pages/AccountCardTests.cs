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

    private IRenderedComponent<AccountCard> Render(string? query = null, TimeSpan? pollInterval = null, List<PurchaseDto>? purchases = null)
    {
        this.AddTestAuthorization().SetAuthorized("buyer@example.com");
        Services.AddSingleton(_payments.Object);

        var listingApi = new Mock<ListingApiService>(MockBehavior.Loose, Http());
        listingApi.Setup(s => s.GetBuyerFeeDisclosureAsync()).ReturnsAsync(new BuyerFeeDisclosureDto
            { SavedCardExplanation = "CONFIGURED: charged the buyer fee automatically." });
        Services.AddSingleton(listingApi.Object);

        var purchaseApi = new Mock<PurchaseApiService>(MockBehavior.Loose, Http());
        purchaseApi.Setup(s => s.GetMyPurchasesAsync()).ReturnsAsync(purchases ?? new List<PurchaseDto>());
        Services.AddSingleton(purchaseApi.Object);

        var userApi = new Mock<UserApiService>(MockBehavior.Loose, Http());
        userApi.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
            { Id = Guid.NewGuid(), DisplayName = "B", Email = "b@x", Role = "Buyer", Status = "Active" });
        Services.AddSingleton(new UserStateService(userApi.Object));

        if (query != null) Services.GetRequiredService<NavigationManager>().NavigateTo($"/account/card?{query}");
        return RenderComponent<AccountCard>(p => p.Add(c => c.PollInterval, pollInterval ?? TimeSpan.FromMilliseconds(10)));
    }

    // WaitForAssertion only re-checks after a render; a pending API call doesn't render.
    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++) await Task.Delay(10);
        condition().Should().BeTrue("the condition should hold within 5 s");
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

    [Fact]
    public async Task ReturningFromReplace_KeepsConfirmingUntilTheNewCardIsSaved()
    {
        var since = new DateTimeOffset(2026, 10, 9, 1, 0, 0, TimeSpan.Zero);
        var oldCard = new SavedCardDto { Brand = "visa", Last4 = "1111", ExpMonth = 8, ExpYear = 2028, IsValid = true,
            UpdatedAt = since.UtcDateTime.AddDays(-30) };
        var newCard = new TaskCompletionSource<SavedCardDto?>();
        var calls = 0;
        _payments.Setup(s => s.GetMyCardAsync())
            .Returns(() => ++calls <= 2 ? Task.FromResult<SavedCardDto?>(oldCard) : newCard.Task);

        var cut = Render($"result=success&since={since.ToUnixTimeSeconds()}");

        await Until(() => calls >= 3);
        cut.Markup.Should().Contain("Confirming your card…");

        newCard.SetResult(new SavedCardDto { Brand = "visa", Last4 = "4242", ExpMonth = 8, ExpYear = 2029, IsValid = true,
            UpdatedAt = since.UtcDateTime.AddSeconds(5) });

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("•••• 4242").And.NotContain("Confirming your card"));
        cut.Markup.Should().NotContain("1111");
    }

    [Fact]
    public void ReturningCancelled_ShowsTheNotice()
    {
        _payments.Setup(s => s.GetMyCardAsync()).ReturnsAsync((SavedCardDto?)null);

        var cut = Render("result=cancelled");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Card not saved"));
        _payments.Verify(s => s.GetMyCardAsync(), Times.Once);
    }

    [Fact]
    public void ReturningFromHostedPage_StripsTheQuery()
    {
        _payments.Setup(s => s.GetMyCardAsync()).ReturnsAsync(new SavedCardDto
            { Brand = "visa", Last4 = "4242", ExpMonth = 8, ExpYear = 2028, IsValid = true });

        var cut = Render("result=success&since=1");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("•••• 4242"));
        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/account/card");
    }

    [Fact]
    public async Task LeavingThePageMidPoll_ThrowsNothing()
    {
        var slow = new TaskCompletionSource<SavedCardDto?>();
        var calls = 0;
        _payments.Setup(s => s.GetMyCardAsync())
            .Returns(() => ++calls == 1 ? Task.FromResult<SavedCardDto?>(null) : slow.Task);
        // A zero interval keeps every poll on the renderer's queue (no timers), so the test
        // needs no waits: the first poll's call is already pending when the render returns,
        // and a poll that kept going after the page was left would call again at once.
        Render("result=success", TimeSpan.Zero);
        calls.Should().Be(2);

        DisposeComponents();
        slow.SetResult(null);
        // SetResult queues the page's continuation on the renderer; this runs after it.
        await Renderer.Dispatcher.InvokeAsync(() => { });

        Renderer.UnhandledException.IsCompleted.Should().BeFalse();
        calls.Should().Be(2, "polling stops once the page is gone");
    }

    [Fact]
    public void AfterAFailedCharge_SavingACardIsSaidToRetryThePayment()
    {
        _payments.Setup(s => s.GetMyCardAsync()).ReturnsAsync(new SavedCardDto
            { Brand = "visa", Last4 = "0002", ExpMonth = 8, ExpYear = 2028, IsValid = true });

        var cut = Render(purchases: new List<PurchaseDto>
        {
            new() { StallionName = "Snitzel", Status = "Pending", ChargeDueBy = DateTime.UtcNow.AddHours(1) }
        });

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Saving a new card will retry the payment for Snitzel"));
    }
}
