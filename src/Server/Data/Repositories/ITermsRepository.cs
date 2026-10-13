using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public interface ITermsRepository
{
    /// <summary>The current buyer T&amp;C (highest buyer version), or null if none — used by the buyer gates.</summary>
    Task<TermsDocument?> GetCurrentAsync();
    /// <summary>The current document for an audience (highest version), or null if none is published.</summary>
    Task<TermsDocument?> GetCurrentAsync(TermsAudience audience);
    /// <summary>Every version for an audience, newest first, with its publisher.</summary>
    Task<IReadOnlyList<TermsDocument>> GetHistoryAsync(TermsAudience audience);
    Task<TermsDocument> AddAsync(TermsDocument document);
}
