using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class TermsRepository : ITermsRepository
{
    private readonly AppDbContext _db;
    public TermsRepository(AppDbContext db) => _db = db;

    public async Task<TermsDocument?> GetCurrentAsync() =>
        await _db.TermsDocuments
            .OrderByDescending(t => t.Version)
            .FirstOrDefaultAsync();

    public async Task<IReadOnlyList<TermsDocument>> GetHistoryAsync() =>
        await _db.TermsDocuments
            .OrderByDescending(t => t.Version)
            .ToListAsync();

    public async Task<TermsDocument> AddAsync(TermsDocument document)
    {
        _db.TermsDocuments.Add(document);
        await _db.SaveChangesAsync();
        return document;
    }
}
