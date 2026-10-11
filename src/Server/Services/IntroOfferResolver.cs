using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

/// <summary>The offer applied (or null) and the resulting listing fee.</summary>
public sealed record IntroOfferMatch(IntroOffer? Offer, decimal StandardFeeIncGst, decimal FeeIncGst);

/// <summary>
/// Picks the intro offer for a new subscription: the matching offer giving the lowest fee; a tie goes to
/// the offer ending soonest, then the earliest created (then Id, so the result is always the same).
/// </summary>
public static class IntroOfferResolver
{
    public static bool Matches(IntroOffer offer, Guid studFarmId, Guid seasonId, DateTime now) =>
        !offer.IsRetired
        && now < offer.EndsAt
        && (offer.StudFarmId == null || offer.StudFarmId == studFarmId)
        && (offer.SeasonId == null || offer.SeasonId == seasonId);

    public static decimal FeeUnder(IntroOffer offer, decimal standardFeeIncGst) => offer.Type == IntroOfferType.Waiver
        ? 0m
        : Math.Max(0m, standardFeeIncGst - (offer.AmountOffIncGst ?? 0m));

    public static IntroOfferMatch Resolve(Guid studFarmId, Guid seasonId, decimal standardFeeIncGst, DateTime now,
        IEnumerable<IntroOffer> candidates)
    {
        var best = candidates
            .Where(o => Matches(o, studFarmId, seasonId, now))
            .Select(o => (Offer: o, Fee: FeeUnder(o, standardFeeIncGst)))
            .OrderBy(x => x.Fee)
            .ThenBy(x => x.Offer.EndsAt)
            .ThenBy(x => x.Offer.CreatedAt)
            .ThenBy(x => x.Offer.Id)
            .FirstOrDefault();

        return best.Offer == null
            ? new IntroOfferMatch(null, standardFeeIncGst, standardFeeIncGst)
            : new IntroOfferMatch(best.Offer, standardFeeIncGst, best.Fee);
    }
}
