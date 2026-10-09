using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class ProcessedPaymentEventRepository : IProcessedPaymentEventRepository
{
    private readonly AppDbContext _db;
    public ProcessedPaymentEventRepository(AppDbContext db) => _db = db;

    public async Task<bool> ExistsAsync(string eventId) =>
        await _db.ProcessedPaymentEvents.AnyAsync(p => p.EventId == eventId);

    public async Task AddAsync(ProcessedPaymentEvent processed)
    {
        _db.ProcessedPaymentEvents.Add(processed);
        await _db.SaveChangesAsync();
    }
}
