using FluentAssertions;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class BuyerEligibilityTests
{
    private readonly Mock<ITermsRepository> _terms = new();
    private readonly Mock<ICardService> _cards = new();

    private BuyerEligibility Sut() => new(_terms.Object, _cards.Object);

    private static User Buyer(UserStatus status = UserStatus.Active, int? terms = 2) =>
        new() { Id = Guid.NewGuid(), Role = UserRole.Buyer, Status = status, AcceptedTermsVersion = terms };

    public BuyerEligibilityTests()
    {
        _terms.Setup(t => t.GetCurrentAsync()).ReturnsAsync(new TermsDocument { Version = 2, Body = "x" });
        _cards.Setup(c => c.HasValidCardAsync(It.IsAny<Guid>())).ReturnsAsync(true);
    }

    [Fact]
    public async Task AVerifiedBuyerWithCurrentTermsAndACard_IsEligible() =>
        (await Sut().CheckAsync(Buyer(), BuyerAction.Offer)).Should().BeNull();

    [Fact]
    public async Task AnUnverifiedBuyer_IsForbidden()
    {
        var failure = await Sut().CheckAsync(Buyer(UserStatus.PendingVerification), BuyerAction.Offer);

        failure!.Forbidden.Should().BeTrue();
        failure.Message.Should().Be("Your account must be verified before you can make an offer.");
    }

    [Fact]
    public async Task OldTerms_AreRejected() =>
        (await Sut().CheckAsync(Buyer(terms: 1), BuyerAction.Offer))!.Message
            .Should().Be("You must accept the current Terms & Conditions before making an offer.");

    [Fact]
    public async Task NoValidCard_IsRejected()
    {
        _cards.Setup(c => c.HasValidCardAsync(It.IsAny<Guid>())).ReturnsAsync(false);

        (await Sut().CheckAsync(Buyer(), BuyerAction.Offer))!.Message.Should().Be("Save a card before making an offer.");
    }

    [Fact]
    public async Task BidMessages_AreTheExistingOnes()
    {
        (await Sut().CheckAsync(Buyer(UserStatus.PendingVerification), BuyerAction.Bid))!.Message
            .Should().Be("Your account must be verified before you can bid.");
        (await Sut().CheckAsync(Buyer(terms: 1), BuyerAction.Bid))!.Message
            .Should().Be("You must accept the current Terms & Conditions before placing a bid.");
        _cards.Setup(c => c.HasValidCardAsync(It.IsAny<Guid>())).ReturnsAsync(false);
        (await Sut().CheckAsync(Buyer(), BuyerAction.Bid))!.Message.Should().Be("Save a card before bidding.");
    }
}
