namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    private static readonly TimeSpan PublicReadCacheTtl = TimeSpan.FromSeconds(15);
    private const int PublicReadCacheCapacity = 64;
    private readonly object _publicReadCacheGate = new();
    private readonly Dictionary<string, PublicReadCacheEntry> _publicReadCache = new(StringComparer.Ordinal);

    private sealed record PublicReadCacheEntry(long Version, DateTimeOffset ExpiresAt, object Value);

    private T GetOrCreatePublicReadCache<T>(string key, Func<T> factory)
    {
        var version = Version;
        var now = DateTimeOffset.UtcNow;
        lock (_publicReadCacheGate)
        {
            if (_publicReadCache.TryGetValue(key, out var cached)
                && cached.Version == version && cached.ExpiresAt > now && cached.Value is T value)
                return value;

            var created = factory();
            _publicReadCache[key] = new(version, DateTimeOffset.UtcNow.Add(PublicReadCacheTtl), created!);
            if (_publicReadCache.Count > PublicReadCacheCapacity)
            {
                foreach (var expired in _publicReadCache
                    .Where(item => item.Value.ExpiresAt <= DateTimeOffset.UtcNow)
                    .Select(item => item.Key).ToArray())
                    _publicReadCache.Remove(expired);
            }
            return created;
        }
    }
}
