using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stallions.Client.Pages.Admin;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Listings;
using Stallions.Shared.DTOs.Offers;
using Stallions.Shared.DTOs.Subscriptions;
using Stallions.Shared.DTOs.Terms;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Pages;

public class AdminListingDetailTests : TestContext
{
    private readonly Mock<TermsApiService> _terms = new(MockBehavior.Loose, new HttpClient { BaseAddress = new Uri("https://localhost/") });
    private Mock<AdminApiService>? _admin;

    private readonly AuctionListingDto _draft = new()
    {
        Id = Guid.NewGuid(), StallionId = Guid.NewGuid(), SeasonId = Guid.NewGuid(),
        StallionName = "Snitzel", SeasonName = "2026 Season",
        ListingType = "Auction", Status = "Draft",
        ReservePrice = 20000m, MinimumBidIncrement = 25m,
        EndDateTime = DateTime.UtcNow.AddDays(7)
    };

    private void Render(List<SubscriptionDto> subscriptions, out IRenderedComponent<AdminListingDetail> cut)
    {
        this.AddTestAuthorization().SetAuthorized("stud@example.com");
        var http = new HttpClient { BaseAddress = new Uri("https://localhost/") };

        var adminMock = new Mock<AdminApiService>(MockBehavior.Loose, http);
        adminMock.Setup(s => s.GetListingAsync(_draft.Id)).ReturnsAsync(_draft);
        _admin = adminMock;
        Services.AddSingleton(adminMock.Object);
        Services.AddSingleton(_terms.Object);

        var subsMock = new Mock<SubscriptionApiService>(MockBehavior.Loose, http);
        subsMock.Setup(s => s.GetMineAsync()).ReturnsAsync(subscriptions);
        Services.AddSingleton(subsMock.Object);

        var offersMock = new Mock<OfferApiService>(MockBehavior.Loose, http);
        offersMock.Setup(s => s.GetForListingAsync(It.IsAny<Guid>())).ReturnsAsync(new List<StudOfferDto>());
        Services.AddSingleton(offersMock.Object);

        var userApiMock = new Mock<UserApiService>(MockBehavior.Loose, http);
        userApiMock.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
        {
            Id = Guid.NewGuid(), DisplayName = "Stud", Email = "stud@example.com",
            Role = "StudFarmAdmin", Status = "Active"
        });
        var userState = new UserStateService(userApiMock.Object);
        Services.AddSingleton(userState);

        cut = RenderComponent<AdminListingDetail>(p => p.Add(c => c.Id, _draft.Id));
    }

    private SubscriptionDto Subscription(string status) => new()
    {
        Id = Guid.NewGuid(), StallionId = _draft.StallionId, SeasonId = _draft.SeasonId, Status = status
    };

    private static AngleSharp.Dom.IElement PublishButton(IRenderedComponent<AdminListingDetail> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Contains("Publish"));

    [Theory]
    [InlineData(7, true)]
    [InlineData(-1, false)]
    public void CloseAndUnpublish_AreOfferedOnlyBeforeTheAuctionEnds(int endsInDays, bool expected)
    {
        _draft.Status = "Active";
        _draft.EndDateTime = DateTime.UtcNow.AddDays(endsInDays);
        Render([Subscription("Paid")], out var cut);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Snitzel"));
        cut.FindAll("button").Any(b => b.TextContent.Contains("Close Listing")).Should().Be(expected);
        cut.FindAll("button").Any(b => b.TextContent.Contains("Unpublish")).Should().Be(expected);
    }

    [Fact]
    public void PublishIsDisabled_WhenStallionHasNoSubscription()
    {
        Render([], out var cut);

        cut.WaitForAssertion(() => PublishButton(cut).HasAttribute("disabled").Should().BeTrue());
        cut.Markup.Should().Contain("Listing fee required");
    }

    [Fact]
    public void PublishIsDisabled_WhenSubscriptionIsPending()
    {
        Render([Subscription("Pending")], out var cut);

        cut.WaitForAssertion(() => PublishButton(cut).HasAttribute("disabled").Should().BeTrue());
    }

    [Theory]
    [InlineData("Paid")]
    [InlineData("Waived")]
    public void PublishIsEnabled_WhenSubscriptionIsActive(string status)
    {
        Render([Subscription(status)], out var cut);

        cut.WaitForAssertion(() => PublishButton(cut).HasAttribute("disabled").Should().BeFalse());
        cut.Markup.Should().NotContain("Listing fee required");
    }

    [Fact]
    public void OwningStudSeesTheReserveAmount()
    {
        Render([Subscription("Paid")], out var cut);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("$20,000"));
    }

    private Mock<AdminApiService> RenderWith(ListingDto listing, out IRenderedComponent<AdminListingDetail> cut)
    {
        this.AddTestAuthorization().SetAuthorized("stud@example.com");
        var http = new HttpClient { BaseAddress = new Uri("https://localhost/") };

        var adminMock = new Mock<AdminApiService>(MockBehavior.Loose, http);
        adminMock.Setup(s => s.GetListingAsync(listing.Id)).ReturnsAsync(listing);
        _admin = adminMock;
        Services.AddSingleton(adminMock.Object);
        Services.AddSingleton(_terms.Object);

        var subsMock = new Mock<SubscriptionApiService>(MockBehavior.Loose, http);
        subsMock.Setup(s => s.GetMineAsync()).ReturnsAsync(new List<SubscriptionDto>());
        Services.AddSingleton(subsMock.Object);

        var offersMock = new Mock<OfferApiService>(MockBehavior.Loose, http);
        offersMock.Setup(s => s.GetForListingAsync(It.IsAny<Guid>())).ReturnsAsync(new List<StudOfferDto>());
        Services.AddSingleton(offersMock.Object);

        var userApiMock = new Mock<UserApiService>(MockBehavior.Loose, http);
        userApiMock.Setup(s => s.GetMeAsync()).ReturnsAsync(new UserDto
        {
            Id = Guid.NewGuid(), DisplayName = "Stud", Email = "stud@example.com", Role = "StudFarmAdmin", Status = "Active"
        });
        Services.AddSingleton(new UserStateService(userApiMock.Object));

        cut = RenderComponent<AdminListingDetail>(p => p.Add(c => c.Id, listing.Id));
        return adminMock;
    }

    private AuctionListingDto UnsoldAuction(Guid? convertedTo = null) => new()
    {
        Id = Guid.NewGuid(), StallionName = "Dundeel (NZ)", SeasonName = "2026 Season", ListingType = "Auction",
        Status = "Unsold", CloseReason = "ReserveNotMet", EndDateTime = DateTime.UtcNow.AddDays(-1),
        PublishedAt = DateTime.UtcNow.AddDays(-8), MakeAnOfferListingId = convertedTo
    };

    [Fact]
    public void AnUnsoldAuction_CanBeOfferedAsMakeAnOffer()
    {
        var auction = UnsoldAuction();
        var newId = Guid.NewGuid();
        var admin = RenderWith(auction, out var cut);
        admin.Setup(a => a.ConvertToMakeAnOfferAsync(auction.Id)).ReturnsAsync(new MakeAnOfferListingDto { Id = newId });
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Offer as Make an Offer")));

        cut.FindAll("button").Single(b => b.TextContent.Contains("Offer as Make an Offer")).Click();
        cut.FindAll("button").Single(b => b.TextContent.Contains("Yes, open to offers")).Click();

        admin.Verify(a => a.ConvertToMakeAnOfferAsync(auction.Id), Times.Once);
        cut.WaitForAssertion(() =>
            Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith($"/admin/listings/{newId}"));
    }

    [Fact]
    public void AnUnsoldAuctionAlreadyConverted_LinksToItsMakeAnOfferListing()
    {
        var maoId = Guid.NewGuid();
        RenderWith(UnsoldAuction(convertedTo: maoId), out var cut);

        cut.WaitForAssertion(() => cut.Find($"a[href='/admin/listings/{maoId}']").TextContent
            .Should().Contain("View the Make an Offer listing"));
        cut.FindAll("button").Any(b => b.TextContent.Contains("Offer as Make an Offer")).Should().BeFalse();
    }

    [Fact]
    public void AMakeAnOfferListing_ShowsTheOffersPanel_AndCanBeClosedButNotUnpublished()
    {
        var mao = new MakeAnOfferListingDto
        {
            Id = Guid.NewGuid(), StallionName = "Dundeel (NZ)", SeasonName = "2026 Season", Status = "Active",
            ExpiresAt = DateTime.UtcNow.AddDays(30), BuyerFeeIncGst = 150m, PublishedAt = DateTime.UtcNow
        };
        RenderWith(mao, out var cut);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No offers yet"));
        cut.Markup.Should().Contain("Make an Offer").And.Contain("Offers close");
        cut.FindAll("button").Any(b => b.TextContent.Contains("Close Listing")).Should().BeTrue();
        cut.FindAll("button").Any(b => b.TextContent.Contains("Unpublish")).Should().BeFalse();
    }

    [Fact]
    public void NavigatingToAnotherListing_ReloadsThePage()
    {
        var auction = UnsoldAuction();
        var admin = RenderWith(auction, out var cut);
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Offer as Make an Offer")));
        var mao = new MakeAnOfferListingDto
        {
            Id = Guid.NewGuid(), StallionName = "Dundeel (NZ)", SeasonName = "2026 Season", Status = "Active",
            ExpiresAt = DateTime.UtcNow.AddDays(30), BuyerFeeIncGst = 150m, PublishedAt = DateTime.UtcNow
        };
        admin.Setup(a => a.GetListingAsync(mao.Id)).ReturnsAsync(mao);

        cut.SetParametersAndRender(p => p.Add(c => c.Id, mao.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Offers close"));
        cut.FindAll("button").Any(b => b.TextContent.Contains("Offer as Make an Offer")).Should().BeFalse();
    }

    private static TermsDocumentDto StudTerms() => new() { Id = Guid.NewGuid(), Audience = "Stud", Version = 2, Body = "Stud listing **terms**" };

    [Fact]
    public void Publish_WhenTheStudTermsNeedAccepting_ShowsThem_ThenPublishesAfterAccepting()
    {
        _terms.Setup(t => t.GetStudStatusAsync()).ReturnsAsync(new StudTermsStatusDto { Current = StudTerms(), NeedsAcceptance = true });
        _terms.Setup(t => t.AcceptStudTermsAsync(2)).ReturnsAsync(new StudTermsStatusDto { Current = StudTerms(), AcceptedVersion = 2 });
        Render([Subscription("Paid")], out var cut);
        cut.WaitForAssertion(() => PublishButton(cut).HasAttribute("disabled").Should().BeFalse());

        PublishButton(cut).Click();

        cut.WaitForAssertion(() => cut.FindAll(".stud-terms-body").Should().ContainSingle());
        _admin!.Verify(a => a.PublishListingAsync(It.IsAny<Guid>()), Times.Never);
        cut.Find(".stud-terms-agree input").Change(true);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "I accept").Click();
        cut.WaitForAssertion(() => _admin.Verify(a => a.PublishListingAsync(_draft.Id), Times.Once));
        _terms.Verify(t => t.AcceptStudTermsAsync(2), Times.Once);
    }

    [Fact]
    public void Publish_WhenTheStudTermsAreAlreadyAccepted_PublishesStraightAway()
    {
        _terms.Setup(t => t.GetStudStatusAsync()).ReturnsAsync(new StudTermsStatusDto { Current = StudTerms(), AcceptedVersion = 2 });
        Render([Subscription("Paid")], out var cut);
        cut.WaitForAssertion(() => PublishButton(cut).HasAttribute("disabled").Should().BeFalse());

        PublishButton(cut).Click();

        cut.WaitForAssertion(() => _admin!.Verify(a => a.PublishListingAsync(_draft.Id), Times.Once));
        cut.FindAll(".stud-terms-body").Should().BeEmpty();
    }

    [Fact]
    public void Convert_WhenTheStudTermsNeedAccepting_ShowsThem_ThenConvertsAfterAccepting()
    {
        var auction = UnsoldAuction();
        var newId = Guid.NewGuid();
        _terms.Setup(t => t.GetStudStatusAsync()).ReturnsAsync(new StudTermsStatusDto { Current = StudTerms(), NeedsAcceptance = true });
        _terms.Setup(t => t.AcceptStudTermsAsync(2)).ReturnsAsync(new StudTermsStatusDto { Current = StudTerms(), AcceptedVersion = 2 });
        var admin = RenderWith(auction, out var cut);
        admin.Setup(a => a.ConvertToMakeAnOfferAsync(auction.Id)).ReturnsAsync(new MakeAnOfferListingDto { Id = newId });
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Offer as Make an Offer")));

        cut.FindAll("button").Single(b => b.TextContent.Contains("Offer as Make an Offer")).Click();
        cut.FindAll("button").Single(b => b.TextContent.Contains("Yes, open to offers")).Click();

        cut.WaitForAssertion(() => cut.FindAll(".stud-terms-body").Should().ContainSingle());
        admin.Verify(a => a.ConvertToMakeAnOfferAsync(It.IsAny<Guid>()), Times.Never);
        cut.Find(".stud-terms-agree input").Change(true);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "I accept").Click();

        cut.WaitForAssertion(() => admin.Verify(a => a.ConvertToMakeAnOfferAsync(auction.Id), Times.Once));
    }

    [Fact]
    public void Publish_ClickedTwiceWhileTheTermsStatusIsLoading_PublishesOnce()
    {
        var status = new TaskCompletionSource<StudTermsStatusDto>();
        _terms.Setup(t => t.GetStudStatusAsync()).Returns(status.Task);
        Render([Subscription("Paid")], out var cut);
        cut.WaitForAssertion(() => PublishButton(cut).HasAttribute("disabled").Should().BeFalse());

        PublishButton(cut).Click();
        PublishButton(cut).HasAttribute("disabled").Should().BeTrue();
        PublishButton(cut).Click();
        cut.InvokeAsync(() => status.SetResult(new StudTermsStatusDto { Current = StudTerms(), AcceptedVersion = 2 }));

        cut.WaitForAssertion(() => _admin!.Verify(a => a.PublishListingAsync(_draft.Id), Times.Once));
        _terms.Verify(t => t.GetStudStatusAsync(), Times.Once);
    }

    [Fact]
    public void Publish_AfterCancellingTheStudTerms_CanBeClickedAgain()
    {
        _terms.Setup(t => t.GetStudStatusAsync()).ReturnsAsync(new StudTermsStatusDto { Current = StudTerms(), NeedsAcceptance = true });
        Render([Subscription("Paid")], out var cut);
        cut.WaitForAssertion(() => PublishButton(cut).HasAttribute("disabled").Should().BeFalse());

        PublishButton(cut).Click();
        cut.WaitForAssertion(() => cut.FindAll(".stud-terms-body").Should().ContainSingle());
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Cancel").Click();

        PublishButton(cut).HasAttribute("disabled").Should().BeFalse();
        _admin!.Verify(a => a.PublishListingAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public void Convert_ClickedTwiceWhileTheTermsStatusIsLoading_CreatesOneMakeAnOfferListing()
    {
        var auction = UnsoldAuction();
        var status = new TaskCompletionSource<StudTermsStatusDto>();
        _terms.Setup(t => t.GetStudStatusAsync()).Returns(status.Task);
        var admin = RenderWith(auction, out var cut);
        admin.Setup(a => a.ConvertToMakeAnOfferAsync(auction.Id)).ReturnsAsync(new MakeAnOfferListingDto { Id = Guid.NewGuid() });
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Offer as Make an Offer")));
        cut.FindAll("button").Single(b => b.TextContent.Contains("Offer as Make an Offer")).Click();
        AngleSharp.Dom.IElement Confirm() =>
            cut.FindAll("button").Single(b => b.TextContent.Contains("Yes, open to offers") || b.TextContent.Contains("Opening"));

        Confirm().Click();
        Confirm().HasAttribute("disabled").Should().BeTrue();
        Confirm().Click();
        cut.InvokeAsync(() => status.SetResult(new StudTermsStatusDto { Current = StudTerms(), AcceptedVersion = 2 }));

        cut.WaitForAssertion(() => admin.Verify(a => a.ConvertToMakeAnOfferAsync(auction.Id), Times.Once));
        _terms.Verify(t => t.GetStudStatusAsync(), Times.Once);
    }
}
