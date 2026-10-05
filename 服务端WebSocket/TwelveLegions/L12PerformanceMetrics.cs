using System.Collections.Frozen;
using System.Diagnostics;
using System.Globalization;

namespace TwelveLegions.Server;

/// <summary>固定阶段、有界聚合；业务记录不等待输出或指标锁，不包含身份/房间/内容。</summary>
internal static class L12PerformanceMetrics
{
    private static readonly PerformanceMetricWindow Window = new();
    private static readonly Timer SummaryTimer = new(_ => Flush(), null,
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

    internal static long Start() => Stopwatch.GetTimestamp();
    internal static void Duration(string stage, long startedAt)
    {
        try { Window.Duration(stage, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds); }
        catch { } // Metrics must not replace a business outcome.
    }
    internal static void Bytes(string stage, long bytes)
    {
        try { Window.Bytes(stage, bytes); }
        catch { }
    }
    internal static void Value(string stage, double value)
    {
        try { Window.Value(stage, value); }
        catch { }
    }

    private static void Flush() => Window.TryFlush(batch =>
    {
        foreach (var row in batch.Series.Where(row => row.Count != 0 || row.DroppedContentionTotal != 0))
            Console.WriteLine($"[L12性能] 阶段={row.Stage} 次数={row.Count} "
                + $"平均={Average(row)} 最大={row.Maximum:F2} "
                + $"单位={row.Unit} P95桶={Band(row.P95)} P99桶={Band(row.P99)} "
                + $"溢出样本={row.OverflowCount} 累计争用丢样={row.DroppedContentionTotal} "
                + $"窗口开始={row.StartedAt:O} 窗口结束={row.EndedAt:O}");
        var diagnostic = batch.Diagnostics;
        Console.WriteLine($"[L12性能] 固定阶段数={batch.Series.Count} "
            + $"累计未知阶段={diagnostic.UnknownStages} 累计无效样本={diagnostic.InvalidSamples} "
            + $"累计负值钳零={diagnostic.ClampedNegatives} 累计输出失败={diagnostic.SinkFailures} "
            + $"累计输出丢样={diagnostic.LostSinkSamples} 累计忙碌flush={diagnostic.SkippedFlushes}");
    });

    private static string Average(PerformanceMetricSummary row) => row.TotalSaturated ? "overflow"
        : (row.Count == 0 ? 0 : row.Total / row.Count).ToString("F2", CultureInfo.InvariantCulture);
    private static string Band(PerformanceMetricBand? band) => band is null ? "no-samples"
        : band.Overflow ? $"({band.LowerExclusive.ToString(CultureInfo.InvariantCulture)},+inf)"
        : $"{(band.LowerInclusive ? "[" : "(")}{band.LowerExclusive.ToString(CultureInfo.InvariantCulture)},{band.UpperInclusive!.Value.ToString(CultureInfo.InvariantCulture)}]";
}

internal sealed record PerformanceMetricBand(double LowerExclusive, double? UpperInclusive, bool Overflow,
    bool LowerInclusive = false);
internal sealed record PerformanceMetricSummary(string Stage, string Unit, DateTimeOffset StartedAt,
    DateTimeOffset EndedAt, long Count, double Total, double Maximum, bool TotalSaturated,
    IReadOnlyList<long> Histogram, PerformanceMetricBand? P95, PerformanceMetricBand? P99,
    long OverflowCount, long DroppedContentionTotal);
internal sealed record PerformanceMetricDiagnostics(long UnknownStages, long InvalidSamples,
    long ClampedNegatives, long SinkFailures, long LostSinkSamples, long SkippedFlushes);
internal sealed record PerformanceMetricBatch(IReadOnlyList<PerformanceMetricSummary> Series,
    PerformanceMetricDiagnostics Diagnostics);

/// <summary>
/// Per-stage cuts are coherent; a multi-stage batch is not a global atomic instant.
/// Contending samples are deliberately dropped and counted. Histograms are complete
/// for accepted samples, not reservoir estimates; percentiles are bucket ranges.
/// </summary>
internal sealed class PerformanceMetricWindow
{
    private enum Kind { Duration, Bytes, Value }
    private sealed class Series
    {
        internal readonly object Gate = new();
        internal readonly string Stage;
        internal readonly string Unit;
        internal readonly long[] Bounds;
        internal readonly long[] Histogram;
        internal DateTimeOffset StartedAt = DateTimeOffset.UtcNow;
        internal long Count, Total, Maximum, DroppedContentionTotal;
        internal bool TotalSaturated;
        internal Series(string stage, string unit, long[] bounds)
        { Stage = stage; Unit = unit; Bounds = bounds; Histogram = new long[bounds.Length + 1]; }
    }

    // Microunits: duration is milliseconds; bytes/value retain the legacy units.
    private static readonly long[] DurationBounds = [0, 100, 250, 500, 1000, 2000, 5000, 10000,
        25000, 50000, 100000, 200000, 250000, 500000, 1000000, 2000000, 5000000,
        10000000, 30000000, 60000000];
    private static readonly long[] BytesBounds = [0, 1000, 4000, 16000, 64000, 256000, 1024000,
        4096000, 16384000, 65536000, 262144000, 1048576000, 4194304000, 16777216000,
        67108864000, 268435456000];
    private static readonly long[] ValueBounds = [0, 1000, 2000, 4000, 8000, 16000, 32000, 64000];
    private readonly Series[] _series;
    private readonly FrozenDictionary<(Kind Kind, string Stage), Series> _known;
    private readonly FrozenDictionary<string, Series> _fullNames;
    private int _flushing;
    private long _unknownStages, _invalidSamples, _clampedNegatives, _sinkFailures,
        _lostSinkSamples, _skippedFlushes;

    internal PerformanceMetricWindow()
    {
        var definitions = new (Kind Kind, string Stage)[]
        {
            (Kind.Duration, "action.engine"), (Kind.Duration, "snapshot.build"),
            (Kind.Duration, "websocket.serialize"), (Kind.Duration, "websocket.send"),
            (Kind.Duration, "persistence.serialize-and-hash"), (Kind.Duration, "persistence.checkpoint-serialize"),
            (Kind.Duration, "persistence.transaction"), (Kind.Duration, "persistence.total"),
            (Kind.Bytes, "websocket.full-payload"), (Kind.Bytes, "websocket.delta-payload"),
            (Kind.Bytes, "websocket.sent-payload"), (Kind.Bytes, "persistence.command-state-json"),
            (Kind.Bytes, "persistence.checkpoint-json"), (Kind.Value, "websocket.queue-depth"),
            (Kind.Value, "persistence.v2-state-json-anomaly"),
        };
        var known = new Dictionary<(Kind Kind, string Stage), Series>();
        foreach (var definition in definitions)
        {
            var prefix = definition.Kind == Kind.Duration ? "duration." : definition.Kind == Kind.Bytes ? "bytes." : "";
            var unit = definition.Kind == Kind.Duration ? "ms" : definition.Kind == Kind.Bytes ? "legacy-bytes" : "value";
            known.Add(definition, new Series(prefix + definition.Stage, unit,
                definition.Kind == Kind.Duration ? DurationBounds : definition.Kind == Kind.Bytes ? BytesBounds : ValueBounds));
        }
        _known = known.ToFrozenDictionary();
        _series = known.Values.OrderBy(series => series.Stage, StringComparer.Ordinal).ToArray();
        _fullNames = _series.ToFrozenDictionary(series => series.Stage, StringComparer.Ordinal);
    }

    internal void Duration(string stage, double milliseconds) => Record(Resolve(Kind.Duration, stage), milliseconds);
    internal void Bytes(string stage, long bytes) => Record(Resolve(Kind.Bytes, stage), bytes);
    internal void Value(string stage, double value)
    {
        if (stage is null || !_fullNames.TryGetValue(stage, out var series))
        { Interlocked.Increment(ref _unknownStages); return; }
        Record(series, value);
    }
    private Series? Resolve(Kind kind, string stage)
    {
        if (stage is not null && _known.TryGetValue((kind, stage), out var series)) return series;
        Interlocked.Increment(ref _unknownStages);
        return null;
    }
    private void Record(Series? series, double value)
    {
        if (series is null) return;
        if (!double.IsFinite(value) || value * 1000 >= long.MaxValue)
        { Interlocked.Increment(ref _invalidSamples); return; }
        if (value < 0) { Interlocked.Increment(ref _clampedNegatives); value = 0; }
        var microunits = (long)(value * 1000); // floor to 0.001 legacy units; range checked above.
        if (!Monitor.TryEnter(series.Gate))
        { Interlocked.Increment(ref series.DroppedContentionTotal); return; }
        try
        {
            if (series.Count == long.MaxValue)
            { Interlocked.Increment(ref series.DroppedContentionTotal); return; }
            series.Count++;
            if (long.MaxValue - series.Total < microunits)
            { series.Total = long.MaxValue; series.TotalSaturated = true; }
            else series.Total += microunits;
            series.Maximum = Math.Max(series.Maximum, microunits);
            var bucket = Array.BinarySearch(series.Bounds, microunits);
            if (bucket < 0) bucket = ~bucket;
            series.Histogram[bucket]++;
        }
        finally { Monitor.Exit(series.Gate); }
    }

    internal bool TryFlush(Action<PerformanceMetricBatch> sink)
    {
        if (Interlocked.CompareExchange(ref _flushing, 1, 0) != 0)
        { Interlocked.Increment(ref _skippedFlushes); return false; }
        long capturedSamples = 0;
        try
        {
            var summaries = new List<PerformanceMetricSummary>(_series.Length);
            foreach (var series in _series)
            {
                lock (series.Gate)
                {
                    var histogram = (long[])series.Histogram.Clone();
                    var ended = DateTimeOffset.UtcNow;
                    var summary = new PerformanceMetricSummary(series.Stage, series.Unit, series.StartedAt,
                        ended, series.Count, series.Total / 1000d, series.Maximum / 1000d,
                        series.TotalSaturated, Array.AsReadOnly(histogram),
                        Percentile(series, histogram, 20), Percentile(series, histogram, 100),
                        histogram[^1], Interlocked.Read(ref series.DroppedContentionTotal));
                    summaries.Add(summary);
                    capturedSamples = SaturatingAdd(capturedSamples, series.Count);
                    series.Count = series.Total = series.Maximum = 0;
                    series.TotalSaturated = false;
                    Array.Clear(series.Histogram);
                    series.StartedAt = ended;
                }
            }
            sink(new PerformanceMetricBatch(summaries.AsReadOnly(), Diagnostics()));
            return true;
        }
        catch
        {
            Interlocked.Increment(ref _sinkFailures);
            // Failed output is discarded, not retried in an unbounded backlog.
            AddLostSamples(capturedSamples);
            return false;
        }
        finally { Volatile.Write(ref _flushing, 0); }
    }
    internal PerformanceMetricDiagnostics Diagnostics() => new(Interlocked.Read(ref _unknownStages),
        Interlocked.Read(ref _invalidSamples), Interlocked.Read(ref _clampedNegatives),
        Interlocked.Read(ref _sinkFailures), Interlocked.Read(ref _lostSinkSamples), Interlocked.Read(ref _skippedFlushes));
    private static PerformanceMetricBand? Percentile(Series series, long[] histogram, int denominator)
    {
        if (series.Count == 0) return null;
        var rank = series.Count - series.Count / denominator; // exact ceil(.95*N)/ceil(.99*N).
        long cumulative = 0;
        for (var index = 0; index < histogram.Length; index++)
        {
            cumulative += histogram[index];
            if (cumulative < rank) continue;
            return new(index == 0 ? 0 : series.Bounds[index - 1] / 1000d,
                index == series.Bounds.Length ? null : series.Bounds[index] / 1000d,
                index == series.Bounds.Length, index == 0);
        }
        throw new InvalidOperationException("metric histogram composition mismatch");
    }
    private static long SaturatingAdd(long current, long addition)
        => long.MaxValue - current < addition ? long.MaxValue : current + addition;
    private void AddLostSamples(long addition)
    {
        var current = Interlocked.Read(ref _lostSinkSamples);
        while (true)
        {
            var updated = SaturatingAdd(current, addition);
            var observed = Interlocked.CompareExchange(ref _lostSinkSamples, updated, current);
            if (observed == current) return;
            current = observed;
        }
    }
}
