using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface INominationBindingRepository
{
    Task<NominationBinding?> GetByIdAsync(Guid id);
    Task<NominationBinding?> GetByPurchaseIdAsync(Guid purchaseId);
    Task<NominationBinding> AddAsync(NominationBinding binding);
    Task UpdateAsync(NominationBinding binding);
}
