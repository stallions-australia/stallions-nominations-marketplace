using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

public interface IPlatformSettingsRepository
{
    /// <summary>Returns the single settings row. Throws if the seed row is missing.</summary>
    Task<PlatformSettings> GetAsync();
    Task UpdateAsync(PlatformSettings settings);
}
