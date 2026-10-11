using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class IntroOfferRepository : IIntroOfferRepository
{
    private readonly AppDbContext _db;
    public IntroOfferRepository(AppDbContext db) => _db = db;

    private IQueryable<IntroOffer> WithDetails() =>
        _db.IntroOffers.Include(o => o.StudFarm).Include(o => o.Season);

    public async Task<IntroOffer?> GetByIdAsync(Guid id) =>
        await WithDetails().FirstOrDefaultAsync(o => o.Id == id);

    public async Task<IReadOnlyList<IntroOffer>> GetAllAsync() =>
        await WithDetails().OrderBy(o => o.IsRetired).ThenByDescending(o => o.EndsAt).ToListAsync();

    public async Task<IReadOnlyList<IntroOffer>> GetCandidatesAsync(Guid studFarmId, Guid seasonId, DateTime now) =>
        await _db.IntroOffers
            .Where(o => !o.IsRetired && o.EndsAt > now
                && (o.StudFarmId == null || o.StudFarmId == studFarmId)
                && (o.SeasonId == null || o.SeasonId == seasonId))
            .ToListAsync();

    public async Task<IReadOnlyDictionary<Guid, int>> GetTimesAppliedAsync() =>
        await _db.StallionSeasonSubscriptions
            .Where(s => s.IntroOfferId != null)
            .GroupBy(s => s.IntroOfferId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count);

    public Task<int> CountTimesAppliedAsync(Guid id) =>
        _db.StallionSeasonSubscriptions.CountAsync(s => s.IntroOfferId == id);

    public async Task AddAsync(IntroOffer offer)
    {
        _db.IntroOffers.Add(offer);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(IntroOffer offer)
    {
        _db.IntroOffers.Update(offer);
        await _db.SaveChangesAsync();
    }
}
