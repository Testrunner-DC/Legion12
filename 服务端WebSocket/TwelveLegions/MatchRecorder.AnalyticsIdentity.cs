namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    private long _analyticsCacheEpoch;
    internal long AnalyticsCacheEpoch => Interlocked.Read(ref _analyticsCacheEpoch);
    internal void InvalidateAnalyticsCache() => Interlocked.Increment(ref _analyticsCacheEpoch);

    internal string CurrentAnalyticsEffectVersion => L12AnalyticsEffectVersion.ForCatalog(ResolveJournalCatalog());

    internal string? ResolveAnalyticsEffectVersion(string? requested)
    {
        var value = requested?.Trim();
        if (string.IsNullOrEmpty(value) || value == "current") return CurrentAnalyticsEffectVersion;
        if (value == "all") return null;
        if (value.Length > 160) throw new ArgumentException("卡效版本标识过长");
        return value;
    }
}
