using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.Terms;

namespace Stallions.Server.Services;

public class TermsService : ITermsService
{
    private readonly ITermsRepository _repo;
    private readonly IUserService _users;

    public TermsService(ITermsRepository repo, IUserService users)
    {
        _repo = repo;
        _users = users;
    }

    public async Task<ServiceResult<TermsDocumentDto>> GetCurrentAsync()
    {
        var current = await _repo.GetCurrentAsync();
        return current == null
            ? ServiceResult<TermsDocumentDto>.NotFound("No Terms & Conditions have been published yet.")
            : ServiceResult<TermsDocumentDto>.Ok(MapToDto(current));
    }

    public async Task<ServiceResult<TermsDocumentDto>> PublishAsync(PublishTermsRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<TermsDocumentDto>.Forbidden();

        if (string.IsNullOrWhiteSpace(request.Body))
            return ServiceResult<TermsDocumentDto>.BadRequest("Terms body cannot be empty.");

        var current = await _repo.GetCurrentAsync();
        var nextVersion = (current?.Version ?? 0) + 1;

        var created = await _repo.AddAsync(new TermsDocument
        {
            Version = nextVersion,
            Body = request.Body.Trim(),
            CreatedByUserId = caller.Id
        });

        return ServiceResult<TermsDocumentDto>.Created(MapToDto(created));
    }

    public async Task<ServiceResult<IReadOnlyList<TermsDocumentDto>>> GetHistoryAsync()
    {
        var docs = await _repo.GetHistoryAsync();
        return ServiceResult<IReadOnlyList<TermsDocumentDto>>.Ok(docs.Select(MapToDto).ToList());
    }

    private static TermsDocumentDto MapToDto(TermsDocument t) => new()
    {
        Id = t.Id,
        Version = t.Version,
        Body = t.Body,
        CreatedAt = t.CreatedAt
    };
}
