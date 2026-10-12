using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Stallions.Server.Content;

/// <summary>
/// The current wording, per app instance, for 60 seconds. A save clears this instance's copy at once; other
/// instances pick the change up when their copy expires. Registered as a singleton.
/// </summary>
public sealed class ContentCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, (ContentText Text, DateTimeOffset ExpiresAt)> _items = new(StringComparer.Ordinal);

    public ContentCache(TimeProvider clock) => _clock = clock;

    public bool TryGet(string key, [NotNullWhen(true)] out ContentText? text)
    {
        if (_items.TryGetValue(key, out var item) && item.ExpiresAt > _clock.GetUtcNow())
        {
            text = item.Text;
            return true;
        }
        text = null;
        return false;
    }

    public void Set(string key, ContentText text) => _items[key] = (text, _clock.GetUtcNow() + Lifetime);

    public void Clear() => _items.Clear();
}
