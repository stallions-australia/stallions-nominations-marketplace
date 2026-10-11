using FluentAssertions;
using Stallions.Server.Data.Entities;
using Stallions.Server.Services;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class IntroOfferResolverTests
{
    private static readonly DateTime Now = new(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Farm = Guid.NewGuid();
    private static readonly Guid Season = Guid.NewGuid();

    private static IntroOffer Offer(IntroOfferType type = IntroOfferType.AmountOff, decimal off = 495m, Guid? farm = null,
        Guid? season = null, DateTime? endsAt = null, bool retired = false, DateTime? createdAt = null, string name = "Offer") => new()
    {
        Name = name, Type = type, AmountOffIncGst = type == IntroOfferType.Waiver ? null : off,
        StudFarmId = farm, SeasonId = season, EndsAt = endsAt ?? Now.AddDays(30), IsRetired = retired,
        CreatedAt = createdAt ?? Now.AddDays(-1)
    };

    private static IntroOfferMatch Resolve(params IntroOffer[] offers) =>
        IntroOfferResolver.Resolve(Farm, Season, 990m, Now, offers);

    [Fact]
    public void NoOffers_LeavesTheStandardFee()
    {
        var match = Resolve();

        match.Offer.Should().BeNull();
        match.FeeIncGst.Should().Be(990m);
        match.StandardFeeIncGst.Should().Be(990m);
    }

    [Fact]
    public void AWaiver_MakesTheFeeZero() =>
        Resolve(Offer(IntroOfferType.Waiver)).FeeIncGst.Should().Be(0m);

    [Fact]
    public void AmountOff_TakesThoseDollarsOff() =>
        Resolve(Offer(off: 495m)).FeeIncGst.Should().Be(495m);

    [Theory]
    [InlineData(990)]
    [InlineData(1500)]
    public void AmountOffAtOrAboveTheStandardFee_MakesItZero(int off) =>
        Resolve(Offer(off: off)).FeeIncGst.Should().Be(0m);

    [Fact]
    public void AnOfferForThisStud_Applies_ButNotOneForAnotherStud()
    {
        Resolve(Offer(farm: Farm)).Offer.Should().NotBeNull();
        Resolve(Offer(farm: Guid.NewGuid())).Offer.Should().BeNull();
    }

    [Fact]
    public void AnOfferForThisSeason_Applies_ButNotOneForAnotherSeason()
    {
        Resolve(Offer(season: Season)).Offer.Should().NotBeNull();
        Resolve(Offer(season: Guid.NewGuid())).Offer.Should().BeNull();
    }

    [Fact]
    public void EndsAt_IsExclusive()
    {
        Resolve(Offer(endsAt: Now)).Offer.Should().BeNull();
        Resolve(Offer(endsAt: Now.AddTicks(1))).Offer.Should().NotBeNull();
    }

    [Fact]
    public void ARetiredOffer_NeverApplies() =>
        Resolve(Offer(IntroOfferType.Waiver, retired: true)).Offer.Should().BeNull();

    [Fact]
    public void TheLowestFeeWins()
    {
        var small = Offer(off: 100m, name: "Small");
        var big = Offer(off: 600m, name: "Big");

        var match = Resolve(small, big);

        match.Offer!.Name.Should().Be("Big");
        match.FeeIncGst.Should().Be(390m);
    }

    [Fact]
    public void ATie_GoesToTheOfferEndingSoonest()
    {
        var later = Offer(IntroOfferType.Waiver, endsAt: Now.AddDays(60), name: "Later");
        var sooner = Offer(off: 2000m, endsAt: Now.AddDays(10), name: "Sooner");

        Resolve(later, sooner).Offer!.Name.Should().Be("Sooner");
    }

    [Fact]
    public void ATieOnTheEndDate_GoesToTheEarliestCreated()
    {
        var newer = Offer(IntroOfferType.Waiver, createdAt: Now.AddDays(-1), name: "Newer");
        var older = Offer(IntroOfferType.Waiver, createdAt: Now.AddDays(-5), name: "Older");

        Resolve(newer, older).Offer!.Name.Should().Be("Older");
    }
}
