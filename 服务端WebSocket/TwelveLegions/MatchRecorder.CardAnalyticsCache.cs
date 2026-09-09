using System.Text.Json;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    private const int MaximumAnalyticsCacheEntries = 32;
    private static readonly TimeSpan AnalyticsCacheTtl = TimeSpan.FromSeconds(30);
    private readonly object _analyticsResultCacheGate = new();
    private readonly Dictionary<string, AnalyticsResultCacheEntry> _analyticsResultCache =
        new(StringComparer.Ordinal);
    private long _analyticsResultCacheSequence;

    internal int AnalyticsResultCacheCount
    {
        get
        {
            lock (_analyticsResultCacheGate) return _analyticsResultCache.Count;
        }
    }

    private sealed record AnalyticsResultCacheEntry(
        long Epoch, DateTimeOffset ExpiresAt, long Sequence, object Value);

    private string AnalyticsResultCacheKey(string kind, L12CardAnalyticsQuery query,
        string? cardId = null)
        => JsonSerializer.Serialize(new
        {
            kind,
            cardId,
            query.Cursor,
            query.Limit,
            query.MinimumSampleSize,
            query.Search,
            CandidateCardIds = query.CandidateCardIds?.OrderBy(value => value,
                StringComparer.OrdinalIgnoreCase).ToArray(),
            query.MasterId,
            query.FromUtc,
            query.ToUtc,
            query.OpponentMasterId,
            query.Initiative,
            query.RulesVersion,
            query.EffectVersion,
            query.SeasonId,
            query.ExcludedMatchIds,
        });

    private bool TryReadAnalyticsResultCache<T>(string key, out T? value)
        where T : class
    {
        var now = _utcNow().ToUniversalTime();
        var epoch = AnalyticsCacheEpoch;
        lock (_analyticsResultCacheGate)
        {
            if (_analyticsResultCache.TryGetValue(key, out var entry)
                && entry.Epoch == epoch && entry.ExpiresAt > now && entry.Value is T typed)
            {
                value = typed;
                return true;
            }
            _analyticsResultCache.Remove(key);
        }
        value = null;
        return false;
    }

    private void StoreAnalyticsResultCache<T>(string key, T value, long expectedEpoch)
        where T : class
    {
        var now = _utcNow().ToUniversalTime();
        lock (_analyticsResultCacheGate)
        {
            if (AnalyticsCacheEpoch != expectedEpoch) return;
            foreach (var expired in _analyticsResultCache
                         .Where(pair => pair.Value.ExpiresAt <= now
                             || pair.Value.Epoch != AnalyticsCacheEpoch)
                         .Select(pair => pair.Key).ToArray())
                _analyticsResultCache.Remove(expired);
            while (_analyticsResultCache.Count >= MaximumAnalyticsCacheEntries)
            {
                var oldest = _analyticsResultCache.MinBy(pair => pair.Value.Sequence).Key;
                _analyticsResultCache.Remove(oldest);
            }
            _analyticsResultCache[key] = new AnalyticsResultCacheEntry(expectedEpoch,
                now.Add(AnalyticsCacheTtl), Interlocked.Increment(ref _analyticsResultCacheSequence), value);
        }
    }
}
