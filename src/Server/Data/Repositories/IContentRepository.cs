using Stallions.Server.Data.Entities;

namespace Stallions.Server.Data.Repositories;

/// <summary>Wording versions. Reads are not tracked (emails render inside other units of work).</summary>
public interface IContentRepository
{
    /// <summary>The highest version of a key, with its author, or null if none is stored.</summary>
    Task<ContentItem?> GetCurrentAsync(string key);
    /// <summary>The highest version of every key that has one.</summary>
    Task<IReadOnlyList<ContentItem>> GetCurrentForAllAsync();
    /// <summary>Every version of a key, newest first.</summary>
    Task<IReadOnlyList<ContentItem>> GetHistoryAsync(string key);
    Task<ContentItem?> GetVersionAsync(string key, int version);
    /// <summary>Saves a new version; false when that (Key, Version) already exists because another save won.</summary>
    Task<bool> TryAddAsync(ContentItem item);
}
