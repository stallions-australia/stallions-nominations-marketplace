using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface ITermsRepository
{
    /// <summary>The current published T&C = the row with the highest Version, or null if none.</summary>
    Task<TermsDocument?> GetCurrentAsync();
    Task<IReadOnlyList<TermsDocument>> GetHistoryAsync();
    Task<TermsDocument> AddAsync(TermsDocument document);
}
