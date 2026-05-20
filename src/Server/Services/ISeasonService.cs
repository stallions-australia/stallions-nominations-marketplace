using Stallions.Shared.DTOs.Seasons;

namespace Stallions.Server.Services;

public interface ISeasonService
{
    Task<ServiceResult<IReadOnlyList<SeasonDto>>> GetAllAsync();
    Task<ServiceResult<SeasonDto>> GetCurrentAsync();
    Task<ServiceResult<SeasonDto>> CreateAsync(CreateSeasonRequest request);
    Task<ServiceResult<SeasonDto>> UpdateAsync(Guid id, UpdateSeasonRequest request);
    Task<ServiceResult> OpenSeasonAsync(Guid id);
    Task<ServiceResult> CloseSeasonAsync(Guid id);
}
