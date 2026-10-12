using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public class TermsRepository : ITermsRepository
{
    private readonly AppDbContext _db;
    public TermsRepository(AppDbContext db) => _db = db;

    public Task<TermsDocument?> GetCurrentAsync() => GetCurrentAsync(TermsAudience.Buyer);

    public async Task<TermsDocument?> GetCurrentAsync(TermsAudience audience) =>
        await _db.TermsDocuments
            .Where(t => t.Audience == audience)
            .OrderByDescending(t => t.Version)
            .FirstOrDefaultAsync();

    public async Task<IReadOnlyList<TermsDocument>> GetHistoryAsync(TermsAudience audience) =>
        await _db.TermsDocuments
            .Include(t => t.CreatedBy)
            .Where(t => t.Audience == audience)
            .OrderByDescending(t => t.Version)
            .ToListAsync();

    public async Task<TermsDocument> AddAsync(TermsDocument document)
    {
        _db.TermsDocuments.Add(document);
        await _db.SaveChangesAsync();
        return document;
    }
}
