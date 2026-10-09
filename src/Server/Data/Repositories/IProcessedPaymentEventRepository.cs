using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IProcessedPaymentEventRepository
{
    Task<bool> ExistsAsync(string eventId);
    Task AddAsync(ProcessedPaymentEvent processed);
}
