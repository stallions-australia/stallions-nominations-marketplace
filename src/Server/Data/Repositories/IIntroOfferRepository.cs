using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IIntroOfferRepository
{
    /// <summary>With StudFarm and Season.</summary>
    Task<IntroOffer?> GetByIdAsync(Guid id);
    /// <summary>All offers including retired, with StudFarm and Season; live ones first.</summary>
    Task<IReadOnlyList<IntroOffer>> GetAllAsync();
    /// <summary>Offers that could apply now to this stud and season: not retired, not ended, in scope.</summary>
    Task<IReadOnlyList<IntroOffer>> GetCandidatesAsync(Guid studFarmId, Guid seasonId, DateTime now);
    /// <summary>Offer id → number of subscriptions it was applied to.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetTimesAppliedAsync();
    Task<int> CountTimesAppliedAsync(Guid id);
    Task AddAsync(IntroOffer offer);
    Task UpdateAsync(IntroOffer offer);
}
