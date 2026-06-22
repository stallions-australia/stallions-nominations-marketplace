using Stallions.Shared.DTOs.Terms;

namespace Stallions.Server.Services;

public interface ITermsService
{
    Task<ServiceResult<TermsDocumentDto>> GetCurrentAsync();
    Task<ServiceResult<TermsDocumentDto>> PublishAsync(PublishTermsRequest request);
    Task<ServiceResult<IReadOnlyList<TermsDocumentDto>>> GetHistoryAsync();
}
