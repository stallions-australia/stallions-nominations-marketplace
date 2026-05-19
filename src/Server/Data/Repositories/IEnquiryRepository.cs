using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IEnquiryRepository
{
    Task<Enquiry?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<Enquiry>> GetByBuyerIdAsync(Guid buyerUserId);
    Task<IReadOnlyList<Enquiry>> GetByStudFarmUserIdAsync(Guid studFarmUserId);
    Task<IReadOnlyList<Enquiry>> GetAllAsync();
    Task<Enquiry> AddAsync(Enquiry enquiry);
    Task UpdateAsync(Enquiry enquiry);
    Task AddMessageAsync(EnquiryMessage message);
}
