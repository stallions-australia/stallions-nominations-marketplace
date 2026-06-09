using Stallions.Shared.DTOs.Directory;

namespace Stallions.Server.Services;

public interface IDirectoryService
{
    // Stud Directory
    Task<ServiceResult<IReadOnlyList<StudDirectorySummaryDto>>> GetStudDirectoriesAsync(bool includeInactive = false);
    Task<ServiceResult<StudDirectoryDto>> GetStudDirectoryAsync(Guid id);
    Task<ServiceResult<StudDirectoryDto>> CreateStudDirectoryAsync(CreateStudDirectoryRequest request);
    Task<ServiceResult<StudDirectoryDto>> UpdateStudDirectoryAsync(Guid id, UpdateStudDirectoryRequest request);

    // Stallion Directory
    Task<ServiceResult<IReadOnlyList<StallionDirectorySummaryDto>>> GetStallionDirectoriesAsync(bool includeInactive = false, Guid? studDirectoryId = null);
    Task<ServiceResult<StallionDirectoryDto>> GetStallionDirectoryAsync(Guid id);
    Task<ServiceResult<StallionDirectoryDto>> CreateStallionDirectoryAsync(CreateStallionDirectoryRequest request);
    Task<ServiceResult<StallionDirectoryDto>> UpdateStallionDirectoryAsync(Guid id, UpdateStallionDirectoryRequest request);
}
