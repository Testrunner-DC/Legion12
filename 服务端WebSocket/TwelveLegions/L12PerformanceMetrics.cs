using System.Collections.Concurrent;
using System.Diagnostics;

namespace TwelveLegions.Server;

/// <summary>仅按固定阶段聚合耗时/大小；不记录账号、房间、命令或卡牌内容。</summary>
internal static class L12PerformanceMetrics
{
    private sealed class Series
    {
        internal long Count;
        internal long TotalMicrounits;
        internal long MaximumMicrounits;
    }

    private static readonly ConcurrentDictionary<string, Series> SeriesByStage =
        new(StringComparer.Ordinal);
    private static readonly Timer SummaryTimer = new(_ => Flush(), null,
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

    internal static long Start() => Stopwatch.GetTimestamp();

    internal static void Duration(string stage, long startedAt)
        => Value($"duration.{stage}", Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

    internal static void Bytes(string stage, long bytes)
        => Value($"bytes.{stage}", Math.Max(0, bytes));

    internal static void Value(string stage, double value)
    {
        var microunits = (long)Math.Min(long.MaxValue, Math.Max(0, value) * 1000);
        var series = SeriesByStage.GetOrAdd(stage, static _ => new Series());
        Interlocked.Increment(ref series.Count);
        Interlocked.Add(ref series.TotalMicrounits, microunits);
        var maximum = Volatile.Read(ref series.MaximumMicrounits);
        while (microunits > maximum)
        {
            var observed = Interlocked.CompareExchange(ref series.MaximumMicrounits,
                microunits, maximum);
            if (observed == maximum) break;
            maximum = observed;
        }
    }

    private static void Flush()
    {
        foreach (var pair in SeriesByStage.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var count = Interlocked.Exchange(ref pair.Value.Count, 0);
            var total = Interlocked.Exchange(ref pair.Value.TotalMicrounits, 0);
            var maximum = Interlocked.Exchange(ref pair.Value.MaximumMicrounits, 0);
            if (count == 0) continue;
            Console.WriteLine($"[L12性能] 阶段={pair.Key} 次数={count} "
                              + $"平均={total / 1000d / count:F2} 最大={maximum / 1000d:F2}");
        }
    }
}
