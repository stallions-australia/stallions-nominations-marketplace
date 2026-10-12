using Stallions.Shared.DTOs.Terms;

namespace Stallions.Server.Services;

public interface ITermsService
{
    /// <summary>The current buyer T&amp;C.</summary>
    Task<ServiceResult<TermsDocumentDto>> GetCurrentAsync();
    /// <summary>Staff only: publishes the next version for the request's audience (Buyer by default). Audited.</summary>
    Task<ServiceResult<TermsDocumentDto>> PublishAsync(PublishTermsRequest request);
    /// <summary>Staff only: every version for an audience ("Buyer" when null), newest first.</summary>
    Task<ServiceResult<IReadOnlyList<TermsDocumentDto>>> GetHistoryAsync(string? audience);
    /// <summary>Stud admins and Staff: the current stud listing terms and the caller farm's acceptance.</summary>
    Task<ServiceResult<StudTermsStatusDto>> GetStudStatusAsync();
    /// <summary>Stud admins: accepts the current stud listing terms for their farm. Audited.</summary>
    Task<ServiceResult<StudTermsStatusDto>> AcceptStudTermsAsync(AcceptTermsRequest request);
}
