using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class EnquiryRepository : IEnquiryRepository
{
    private readonly AppDbContext _db;
    public EnquiryRepository(AppDbContext db) => _db = db;

    public async Task<Enquiry?> GetByIdAsync(Guid id) =>
        await _db.Enquiries.Include(e => e.Messages.OrderBy(m => m.SentAt))
            .FirstOrDefaultAsync(e => e.Id == id);

    public async Task<IReadOnlyList<Enquiry>> GetByBuyerIdAsync(Guid buyerUserId) =>
        await _db.Enquiries.Include(e => e.Messages)
            .Where(e => e.BuyerUserId == buyerUserId)
            .OrderByDescending(e => e.CreatedAt).ToListAsync();

    public async Task<IReadOnlyList<Enquiry>> GetByStudFarmUserIdAsync(Guid studFarmUserId) =>
        await _db.Enquiries.Include(e => e.Messages)
            .Where(e => e.StudFarmUserId == studFarmUserId)
            .OrderByDescending(e => e.CreatedAt).ToListAsync();

    public async Task<IReadOnlyList<Enquiry>> GetAllAsync() =>
        await _db.Enquiries.Include(e => e.Buyer).Include(e => e.Messages)
            .OrderByDescending(e => e.CreatedAt).ToListAsync();

    public async Task<Enquiry> AddAsync(Enquiry enquiry)
    {
        _db.Enquiries.Add(enquiry);
        await _db.SaveChangesAsync();
        return enquiry;
    }

    public async Task UpdateAsync(Enquiry enquiry)
    {
        _db.Enquiries.Update(enquiry);
        await _db.SaveChangesAsync();
    }

    public async Task AddMessageAsync(EnquiryMessage message)
    {
        _db.EnquiryMessages.Add(message);
        await _db.SaveChangesAsync();
    }
}
