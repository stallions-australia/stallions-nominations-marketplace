using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class SavedCardRepository : ISavedCardRepository
{
    private readonly AppDbContext _db;
    public SavedCardRepository(AppDbContext db) => _db = db;

    public async Task<SavedCard?> GetByUserIdAsync(Guid userId) =>
        await _db.SavedCards.FirstOrDefaultAsync(c => c.UserId == userId);

    public async Task<SavedCard> AddAsync(SavedCard card)
    {
        _db.SavedCards.Add(card);
        await _db.SaveChangesAsync();
        return card;
    }

    public async Task UpdateAsync(SavedCard card)
    {
        _db.SavedCards.Update(card);
        await _db.SaveChangesAsync();
    }
}
