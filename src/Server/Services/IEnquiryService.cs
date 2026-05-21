using Stallions.Shared.DTOs.Enquiries;

namespace Stallions.Server.Services;

public interface IEnquiryService
{
    Task<ServiceResult<EnquiryDto>> CreateAsync(Guid listingId, OpenEnquiryRequest request);
    Task<ServiceResult<IReadOnlyList<EnquirySummaryDto>>> GetAllForCallerAsync();
    Task<ServiceResult<EnquiryDto>> GetByIdAsync(Guid id);
    Task<ServiceResult<EnquiryMessageDto>> PostMessageAsync(Guid enquiryId, SendMessageRequest request);
    Task<ServiceResult> CloseAsync(Guid enquiryId);
}
