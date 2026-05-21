using Stallions.Shared.DTOs.Stallions;

namespace Stallions.Server.Services;

public interface IStallionService
{
    Task<ServiceResult<IReadOnlyList<StallionSummaryDto>>> GetAllWithActiveListingsAsync();
    Task<ServiceResult<IReadOnlyList<StallionSummaryDto>>> GetByStudFarmAsync();
    Task<ServiceResult<StallionDto>> GetByIdAsync(Guid id, bool isStaff);
    Task<ServiceResult<StallionDto>> CreateAsync(CreateStallionRequest request);
    Task<ServiceResult<StallionDto>> UpdateAsync(Guid id, UpdateStallionRequest request);
    Task<ServiceResult<StallionDto>> SetPrimaryImageAsync(Guid stallionId, Guid imageId);
    Task<ServiceResult> DeleteImageAsync(Guid stallionId, Guid imageId);
}
