using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

internal enum CardAnalyticsStage
{
    ScopeTotal, Population, Rows, Structures, Comparisons, Count, Coverage,
    Breakdowns, Quantity, Turns, Matchups, Recent, Queue, Wait,
}

// Fixed names and numeric durations only: no filters, card IDs or player identities.
internal sealed class CardAnalyticsRequestTiming
{
    private readonly object _gate = new();
    private readonly Dictionary<CardAnalyticsStage, double> _durations = [];
    internal bool CacheHit { get; private set; }
    internal bool Interrupted { get; private set; }
    private double _total;
    private bool? _joined;

    internal void Add(CardAnalyticsStage stage, double milliseconds)
    {
        lock (_gate) _durations[stage] = _durations.GetValueOrDefault(stage) + milliseconds;
    }

    internal void MarkCacheHit() { lock (_gate) CacheHit = true; }
    internal void MarkInterrupted() { lock (_gate) Interrupted = true; }
    internal void SetTotal(double milliseconds) { lock (_gate) _total = milliseconds; }
    internal void MarkSingleFlight(bool joined) { lock (_gate) _joined = (_joined ?? false) || joined; }

    internal void Include(CardAnalyticsRequestTiming other)
    {
        KeyValuePair<CardAnalyticsStage, double>[] values;
        bool interrupted;
        lock (other._gate) { values = other._durations.ToArray(); interrupted = other.Interrupted; }
        lock (_gate)
        {
            foreach (var (stage, duration) in values)
                _durations[stage] = _durations.GetValueOrDefault(stage) + duration;
            Interrupted |= interrupted;
        }
    }

    internal string ToServerTiming()
    {
        lock (_gate)
        {
            var values = new List<string> { Duration("total", _total),
                $"cache;desc=\"{(CacheHit ? "hit" : "miss")}\"" };
            if (_joined is { } joined)
                values.Add($"singleflight;desc=\"{(joined ? "joined" : "owner")}\"");
            foreach (var stage in Enum.GetValues<CardAnalyticsStage>())
                if (_durations.TryGetValue(stage, out var milliseconds))
                    values.Add(Duration(Name(stage), milliseconds));
            if (Interrupted) values.Add("interrupted;dur=0");
            return string.Join(", ", values);
        }
    }

    private static string Duration(string name, double milliseconds)
        => name + ";dur=" + milliseconds.ToString("F2", CultureInfo.InvariantCulture);

    private static string Name(CardAnalyticsStage stage) => stage switch
    {
        CardAnalyticsStage.ScopeTotal => "scope-total",
        CardAnalyticsStage.Population => "population",
        CardAnalyticsStage.Rows => "rows",
        CardAnalyticsStage.Structures => "structures",
        CardAnalyticsStage.Comparisons => "comparisons",
        CardAnalyticsStage.Count => "count",
        CardAnalyticsStage.Coverage => "coverage",
        CardAnalyticsStage.Breakdowns => "breakdowns",
        CardAnalyticsStage.Quantity => "quantity",
        CardAnalyticsStage.Turns => "turns",
        CardAnalyticsStage.Matchups => "matchups",
        CardAnalyticsStage.Recent => "recent",
        CardAnalyticsStage.Queue => "queue",
        CardAnalyticsStage.Wait => "wait",
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };
}

internal sealed class CardAnalyticsUnavailableException(string code, string message, int statusCode)
    : Exception(message)
{
    internal string Code { get; } = code;
    internal int StatusCode { get; } = statusCode;
    internal static CardAnalyticsUnavailableException Changed()
        => new("analytics_changed", "统计数据已更新，请重试", 503);
    internal static CardAnalyticsUnavailableException TimedOut()
        => new("analytics_timeout", "卡牌统计查询超时，请缩小筛选范围后重试", 504);
}

public sealed partial class MatchRecorder
{
    internal const int MaximumCardAnalyticsJobs = 16;
    internal const int MaximumConcurrentCardAnalyticsJobs = 2;
    internal static readonly TimeSpan CardAnalyticsHardBudget = TimeSpan.FromSeconds(7.5);
    // Reserve time for the provider's <=1s synchronous busy retry and native teardown.
    internal static readonly TimeSpan CardAnalyticsWorkBudget = CardAnalyticsHardBudget - TimeSpan.FromSeconds(1.25);
    private readonly SemaphoreSlim _cardAnalyticsWorkers = new(MaximumConcurrentCardAnalyticsJobs);
    private readonly Dictionary<CardAnalyticsJobKey, CardAnalyticsJob> _cardAnalyticsJobs = [];
    private int _activeCardAnalyticsJobs;
    private int _runningCardAnalyticsJobs;
    private long _cardAnalyticsNativeInterruptions;
    internal long CardAnalyticsNativeInterruptions => Interlocked.Read(ref _cardAnalyticsNativeInterruptions);

    internal static CancellationTokenSource CreateCardAnalyticsRequestCancellation(
        CancellationToken requestAborted, CancellationToken applicationStopping)
        => CancellationTokenSource.CreateLinkedTokenSource(requestAborted, applicationStopping);

    internal (int Active, int Running, int Registered) CardAnalyticsExecutionCounts
    {
        get
        {
            lock (_analyticsResultCacheGate)
                return (_activeCardAnalyticsJobs, _runningCardAnalyticsJobs, _cardAnalyticsJobs.Count);
        }
    }

    private readonly record struct CardAnalyticsJobKey(string CacheKey, long Epoch);

    private sealed class CardAnalyticsJob
    {
        private readonly object _cancellationGate = new();
        private bool _disposed;
        internal readonly CancellationTokenSource Cancellation = new(CardAnalyticsWorkBudget);
        internal readonly TaskCompletionSource<object?> Completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly CardAnalyticsRequestTiming Timing = new();
        internal readonly long Started = Stopwatch.GetTimestamp();
        internal int Waiters;
        internal bool Accepting = true;
        internal bool Completed;

        internal CardAnalyticsJob()
        {
            // A job may lose all callers before it fails; still observe real exceptions.
            _ = Completion.Task.ContinueWith(task => _ = task.Exception,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted
                | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        internal void Cancel()
        {
            lock (_cancellationGate)
                if (!_disposed) Cancellation.Cancel();
        }

        internal void DisposeCancellation()
        {
            lock (_cancellationGate)
            {
                _disposed = true;
                Cancellation.Dispose();
            }
        }
    }

    // Each caller owns only its wait. One disconnect cannot interrupt another caller's query.
    internal async Task<T?> ExecuteCardAnalyticsAsync<T>(string cacheKey, long epoch,
        CancellationToken cancellationToken, CardAnalyticsRequestTiming timing,
        Func<CardAnalyticsExecution, T?> compute, bool cacheResult = true) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = new CardAnalyticsJobKey(cacheKey, epoch);
        CardAnalyticsJob job;
        var owner = false;
        lock (_analyticsResultCacheGate)
        {
            if (AnalyticsCacheEpoch != epoch) throw CardAnalyticsUnavailableException.Changed();
            if (cacheResult && TryReadAnalyticsResultCache<T>(cacheKey, out var cached))
            {
                timing.MarkCacheHit();
                cancellationToken.ThrowIfCancellationRequested();
                if (AnalyticsCacheEpoch != epoch) throw CardAnalyticsUnavailableException.Changed();
                return cached;
            }
            if (!_cardAnalyticsJobs.TryGetValue(key, out job!) || !job.Accepting
                || job.Cancellation.IsCancellationRequested)
            {
                // Count draining owners too: replacing an abandoned key cannot evade the bound.
                if (_activeCardAnalyticsJobs >= MaximumCardAnalyticsJobs)
                    throw new CardAnalyticsUnavailableException("analytics_busy",
                        "卡牌统计查询繁忙，请稍后重试", 503);
                job = new CardAnalyticsJob();
                _cardAnalyticsJobs[key] = job;
                _activeCardAnalyticsJobs++;
                owner = true;
            }
            job.Waiters++;
        }
        timing.MarkSingleFlight(!owner);
        if (owner) _ = RunCardAnalyticsJobAsync(key, job, compute, cacheResult);
        var waitStarted = Stopwatch.GetTimestamp();
        try
        {
            var result = await job.Completion.Task.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (AnalyticsCacheEpoch != epoch) throw CardAnalyticsUnavailableException.Changed();
            return (T?)result;
        }
        finally
        {
            timing.Add(CardAnalyticsStage.Wait, Stopwatch.GetElapsedTime(waitStarted).TotalMilliseconds);
            timing.Include(job.Timing);
            var cancel = false;
            lock (_analyticsResultCacheGate)
            {
                job.Waiters--;
                if (job.Waiters == 0 && !job.Completed)
                {
                    job.Accepting = false;
                    cancel = true;
                }
            }
            // Never run native callbacks under the registry lock.
            if (cancel) job.Cancel();
        }
    }

    private async Task RunCardAnalyticsJobAsync<T>(CardAnalyticsJobKey key, CardAnalyticsJob job,
        Func<CardAnalyticsExecution, T?> compute, bool cacheResult) where T : class
    {
        var acquired = false;
        using var epochWatch = new Timer(_ =>
        {
            if (AnalyticsCacheEpoch != key.Epoch) job.Cancel();
        }, null, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
        try
        {
            // Queue time consumes the same work budget as SQL; at most 16 jobs exist.
            await _cardAnalyticsWorkers.WaitAsync(job.Cancellation.Token);
            acquired = true;
            job.Timing.Add(CardAnalyticsStage.Queue,
                Stopwatch.GetElapsedTime(job.Started).TotalMilliseconds);
            lock (_analyticsResultCacheGate) _runningCardAnalyticsJobs++;
            var result = await Task.Run(() =>
            {
                job.Cancellation.Token.ThrowIfCancellationRequested();
                var settings = new SqliteConnectionStringBuilder(_connectionString)
                {
                    Pooling = false,
                    // Provider busy retry is synchronous too; it must not wait the default 30s.
                    DefaultTimeout = 1,
                };
                using var connection = new SqliteConnection(settings.ToString());
                connection.Open();
                using var native = new CardAnalyticsNativeCancellation(connection, job.Cancellation.Token);
                var execution = new CardAnalyticsExecution(connection, job.Cancellation.Token,
                    job.Timing, () => AnalyticsCacheEpoch == key.Epoch);
                try
                {
                    execution.Check();
                    var value = compute(execution);
                    execution.Check();
                    return value;
                }
                catch (SqliteException error) when (error.SqliteErrorCode == SQLitePCL.raw.SQLITE_INTERRUPT
                    && job.Cancellation.IsCancellationRequested)
                {
                    job.Timing.MarkInterrupted();
                    Interlocked.Increment(ref _cardAnalyticsNativeInterruptions);
                    throw new OperationCanceledException("SQLite card analytics interrupted", error,
                        job.Cancellation.Token);
                }
            });
            lock (_analyticsResultCacheGate)
            {
                job.Cancellation.Token.ThrowIfCancellationRequested();
                if (AnalyticsCacheEpoch != key.Epoch) throw CardAnalyticsUnavailableException.Changed();
                if (cacheResult && result is not null)
                    StoreAnalyticsResultCache(key.CacheKey, result, key.Epoch);
                job.Completed = true;
                job.Accepting = false;
                job.Completion.TrySetResult(result);
            }
        }
        catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested)
        {
            job.Completion.TrySetCanceled(job.Cancellation.Token);
        }
        catch (Exception error) { job.Completion.TrySetException(error); }
        finally
        {
            lock (_analyticsResultCacheGate)
            {
                job.Completed = true;
                job.Accepting = false;
                if (_cardAnalyticsJobs.TryGetValue(key, out var current) && ReferenceEquals(current, job))
                    _cardAnalyticsJobs.Remove(key);
                _activeCardAnalyticsJobs--;
                if (acquired) _runningCardAnalyticsJobs--;
            }
            if (acquired) _cardAnalyticsWorkers.Release();
            job.DisposeCancellation();
        }
    }

    internal sealed class CardAnalyticsExecution(SqliteConnection connection, CancellationToken token,
        CardAnalyticsRequestTiming timing, Func<bool> epochIsCurrent)
    {
        internal SqliteConnection Connection { get; } = connection;
        internal CancellationToken CancellationToken { get; } = token;
        internal void Check()
        {
            CancellationToken.ThrowIfCancellationRequested();
            if (!epochIsCurrent()) throw CardAnalyticsUnavailableException.Changed();
        }
        internal T Measure<T>(CardAnalyticsStage stage, Func<T> operation)
        {
            Check();
            var started = Stopwatch.GetTimestamp();
            try { var result = operation(); Check(); return result; }
            finally { timing.Add(stage, Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
        }
        internal void Measure(CardAnalyticsStage stage, Action operation)
            => Measure(stage, () => { operation(); return true; });
    }

    // The SQL and limits are the existing admin query's; only this card-detail consumer
    // uses the isolated connection and native cancellation, not other admin queries.
    private static IReadOnlyList<L12AdminMatchSummary> ReadCardAnalyticsRecentMatches(
        CardAnalyticsExecution execution, L12AdminMatchQuery query)
    {
        var totalWhere = BuildAdminMatchWhere(query with { Cursor = null }, false, out var totalParameters);
        var pageWhere = BuildAdminMatchWhere(query, true, out var pageParameters);
        using var count = execution.Connection.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM matches m WHERE {totalWhere};";
        AddParameters(count, totalParameters);
        count.ExecuteScalar();
        execution.Check();
        using var command = execution.Connection.CreateCommand();
        command.CommandText = $"""
            {AdminMatchSelect}
            WHERE {pageWhere}
            ORDER BY m.started_utc DESC,m.match_id DESC
            LIMIT $take;
            """;
        AddParameters(command, pageParameters);
        command.Parameters.AddWithValue("$take", query.Limit + 1);
        var items = new List<L12AdminMatchSummary>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) items.Add(ReadAdminMatchSummary(reader));
        if (items.Count > query.Limit) items.RemoveAt(items.Count - 1);
        return items.Select(SanitizeAnalyticsRecentMatch).ToArray();
    }
}
