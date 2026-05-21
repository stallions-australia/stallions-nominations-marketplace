using Stallions.Shared.DTOs.Bindings;

namespace Stallions.Server.Services;

public interface INominationBindingService
{
    Task<ServiceResult<NominationBindingDto>> GetByIdAsync(Guid id);
    Task<ServiceResult<NominationBindingDto>> AcknowledgeAsync(Guid id);
    Task<ServiceResult<NominationBindingDto>> SignAsync(Guid id);
    Task<ServiceResult<NominationBindingDto>> DisputeAsync(Guid id);
}
