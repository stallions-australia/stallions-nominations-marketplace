using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public class NominationBindingRepository : INominationBindingRepository
{
    private readonly AppDbContext _db;
    public NominationBindingRepository(AppDbContext db) => _db = db;

    public async Task<NominationBinding?> GetByIdAsync(Guid id) =>
        await _db.NominationBindings.Include(n => n.Purchase).FirstOrDefaultAsync(n => n.Id == id);

    public async Task<NominationBinding?> GetByPurchaseIdAsync(Guid purchaseId) =>
        await _db.NominationBindings.FirstOrDefaultAsync(n => n.PurchaseId == purchaseId);

    public async Task<NominationBinding> AddAsync(NominationBinding binding)
    {
        _db.NominationBindings.Add(binding);
        await _db.SaveChangesAsync();
        return binding;
    }

    public async Task UpdateAsync(NominationBinding binding)
    {
        _db.NominationBindings.Update(binding);
        await _db.SaveChangesAsync();
    }
}
