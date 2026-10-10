using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace TwelveLegions.Server;

internal enum L12ProcessMetricArchiveReason
{
    None,
    CaptureBusy,
    QueueFull,
    InvalidMetadata,
    InvalidSample,
    ScheduleFailure,
    EntryTooLarge,
    DailyCapacity,
    RootCapacity,
    UnsafeRoot,
    OwnershipConflict,
    ContentMismatch,
    WriteFailure,
    Stopped,
    ShutdownTimeout,
}

internal sealed record L12ProcessMetricArchiveIdentity(string Commit, string Instance);

internal sealed record L12SerializedProcessMetric(byte[] Json, DateTimeOffset ArchiveUtc,
    long MonotonicTimestamp, long TimestampFrequency);

internal readonly record struct L12ProcessMetricArchiveWriteResult(bool Succeeded,
    L12ProcessMetricArchiveReason Reason)
{
    internal static L12ProcessMetricArchiveWriteResult Success => new(true, L12ProcessMetricArchiveReason.None);
    internal static L12ProcessMetricArchiveWriteResult Failure(L12ProcessMetricArchiveReason reason) =>
        new(false, reason);
}

internal interface IL12ProcessMetricArchiveSink
{
    ValueTask<L12ProcessMetricArchiveWriteResult> WriteAsync(L12SerializedProcessMetric metric,
        CancellationToken cancellationToken);
}

internal sealed record L12ProcessMetricArchiveDiagnostics(long Captured, long Written,
    long CaptureBusyDrops, long QueueDrops, long EncodingDrops, long ArchiveDrops,
    long ShutdownTimeouts, L12ProcessMetricArchiveReason LastFailure);

/// <summary>
/// Owns exactly one sampling loop and one archive writer. Callers only start/stop the component;
/// capture and I/O never run on a gameplay thread. A blocked external probe or sink cannot be
/// forcefully killed; shutdown is bounded and reports that its background exit is incomplete.
/// </summary>
internal interface IL12ProcessMetricCollectorLifecycle
{
    bool Start();
    Task<bool> StopAsync(TimeSpan timeout);
}

internal sealed class L12ProcessMetricCollector : IL12ProcessMetricCollectorLifecycle, IAsyncDisposable
{
    internal static readonly TimeSpan SamplingInterval = TimeSpan.FromSeconds(10);
    internal const int QueueCapacity = 12;
    internal const int MaximumLineBytes = 8 * 1024;
    private readonly TimeProvider _timeProvider;
    private readonly L12ProcessMetricSampler _sampler;
    private readonly IL12ProcessMetricArchiveSink _sink;
    private readonly L12ProcessMetricArchiveIdentity _identity;
    private readonly Func<TimeSpan, TimeProvider, CancellationToken, Task> _delay;
    private readonly Action<L12ProcessMetricArchiveReason>? _alert;
    private readonly TimeSpan _shutdownTimeout;
    private readonly Channel<L12SerializedProcessMetric> _queue;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _lifecycleGate = new();
    private Task _collecting = Task.CompletedTask;
    private Task _writing = Task.CompletedTask;
    private Task _cancelling = Task.CompletedTask;
    private int _started, _stopping, _collectingNow, _stopDisposed;
    private long _captured, _written, _captureBusyDrops, _queueDrops, _encodingDrops,
        _archiveDrops, _shutdownTimeouts, _pendingAlerts;
    private int _lastFailure;
    private long _lastAlertTimestamp;
    private bool _hasAlerted;

    internal L12ProcessMetricCollector(TimeProvider timeProvider, L12ProcessMetricProbe probe,
        IL12ProcessMetricArchiveSink sink, L12ProcessMetricArchiveIdentity identity,
        TimeSpan? shutdownTimeout = null,
        Func<TimeSpan, TimeProvider, CancellationToken, Task>? delay = null,
        Action<L12ProcessMetricArchiveReason>? alert = null)
    {
        _timeProvider = timeProvider;
        _sampler = new L12ProcessMetricSampler(probe);
        _sink = sink;
        _identity = identity;
        _shutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(2);
        _delay = delay ?? ((duration, provider, token) => Task.Delay(duration, provider, token));
        _alert = alert;
        _queue = Channel.CreateBounded<L12SerializedProcessMetric>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
    }

    internal bool Start()
    {
        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _stopping) != 0
                || Interlocked.CompareExchange(ref _started, 1, 0) != 0) return false;
            _collecting = Task.Run(CollectLoopAsync);
            _writing = Task.Run(WriteLoopAsync);
            return true;
        }
    }

    internal bool TryCollectNow()
    {
        if (Volatile.Read(ref _started) == 0 || Volatile.Read(ref _stopping) != 0)
        { Fail(L12ProcessMetricArchiveReason.Stopped); return false; }
        if (Interlocked.CompareExchange(ref _collectingNow, 1, 0) != 0)
        {
            Interlocked.Increment(ref _captureBusyDrops);
            Fail(L12ProcessMetricArchiveReason.CaptureBusy);
            return false;
        }
        try
        {
            if (!_sampler.TryCapture(out var sample) || sample is null)
            {
                Interlocked.Increment(ref _captureBusyDrops);
                Fail(L12ProcessMetricArchiveReason.CaptureBusy);
                return false;
            }
            if (!L12CompactProcessMetricEncoder.TryEncode(sample, _identity,
                    _timeProvider.GetUtcNow(), _timeProvider.GetTimestamp(), _timeProvider.TimestampFrequency,
                    out var serialized, out var reason))
            {
                Interlocked.Increment(ref _encodingDrops);
                Fail(reason);
                return false;
            }
            if (!_queue.Writer.TryWrite(serialized!))
            {
                Interlocked.Increment(ref _queueDrops);
                Fail(L12ProcessMetricArchiveReason.QueueFull);
                return false;
            }
            Interlocked.Increment(ref _captured);
            return true;
        }
        catch
        {
            Interlocked.Increment(ref _encodingDrops);
            Fail(L12ProcessMetricArchiveReason.InvalidSample);
            return false;
        }
        finally { Volatile.Write(ref _collectingNow, 0); }
    }

    internal L12ProcessMetricArchiveDiagnostics Diagnostics() => new(
        Interlocked.Read(ref _captured), Interlocked.Read(ref _written),
        Interlocked.Read(ref _captureBusyDrops), Interlocked.Read(ref _queueDrops),
        Interlocked.Read(ref _encodingDrops), Interlocked.Read(ref _archiveDrops),
        Interlocked.Read(ref _shutdownTimeouts),
        (L12ProcessMetricArchiveReason)Volatile.Read(ref _lastFailure));

    private async Task CollectLoopAsync()
    {
        try
        {
            while (true)
            {
                await _delay(SamplingInterval, _timeProvider, _stop.Token).ConfigureAwait(false);
                TryCollectNow();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch { Fail(L12ProcessMetricArchiveReason.ScheduleFailure); }
        finally { _queue.Writer.TryComplete(); }
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var metric in _queue.Reader.ReadAllAsync())
            {
                if (_stop.IsCancellationRequested) return;
                L12ProcessMetricArchiveWriteResult result;
                try { result = await _sink.WriteAsync(metric, _stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
                catch { result = L12ProcessMetricArchiveWriteResult.Failure(
                    L12ProcessMetricArchiveReason.WriteFailure); }
                if (result.Succeeded) Interlocked.Increment(ref _written);
                else
                {
                    Interlocked.Increment(ref _archiveDrops);
                    Fail(result.Reason);
                }
                EmitPendingAlert(force: false);
            }
        }
        catch { Fail(L12ProcessMetricArchiveReason.WriteFailure); }
        finally { EmitPendingAlert(force: true); }
    }

    internal async Task<bool> StopAsync(TimeSpan timeout)
    {
        Task shutdown;
        lock (_lifecycleGate)
        {
            if (Interlocked.Exchange(ref _stopping, 1) == 0)
            {
                _queue.Writer.TryComplete();
                _cancelling = _stop.CancelAsync();
            }
            shutdown = Task.WhenAll(_cancelling, _collecting, _writing);
        }
        if (shutdown.IsCompleted)
        {
            try { await shutdown.ConfigureAwait(false); } catch { }
            DisposeStopSource();
            return true;
        }
        var completed = await Task.WhenAny(shutdown, Task.Delay(timeout)).ConfigureAwait(false);
        if (completed == shutdown)
        {
            try { await shutdown.ConfigureAwait(false); } catch { }
            DisposeStopSource();
            return true;
        }
        Interlocked.Increment(ref _shutdownTimeouts);
        Fail(L12ProcessMetricArchiveReason.ShutdownTimeout);
        return false;
    }

    public async ValueTask DisposeAsync() => await StopAsync(_shutdownTimeout).ConfigureAwait(false);

    bool IL12ProcessMetricCollectorLifecycle.Start() => Start();

    Task<bool> IL12ProcessMetricCollectorLifecycle.StopAsync(TimeSpan timeout) => StopAsync(timeout);

    private void DisposeStopSource()
    {
        if (Interlocked.Exchange(ref _stopDisposed, 1) == 0) _stop.Dispose();
    }

    private void Fail(L12ProcessMetricArchiveReason reason)
    {
        Volatile.Write(ref _lastFailure, (int)reason);
        Interlocked.Increment(ref _pendingAlerts);
    }

    private void EmitPendingAlert(bool force)
    {
        if (_alert is null || Interlocked.Read(ref _pendingAlerts) == 0) return;
        var now = _timeProvider.GetTimestamp();
        if (!force && _hasAlerted)
        {
            try
            {
                if (_timeProvider.GetElapsedTime(_lastAlertTimestamp, now) < TimeSpan.FromMinutes(1)) return;
            }
            catch { return; }
        }
        if (Interlocked.Exchange(ref _pendingAlerts, 0) == 0) return;
        _lastAlertTimestamp = now;
        _hasAlerted = true;
        try { _alert((L12ProcessMetricArchiveReason)Volatile.Read(ref _lastFailure)); } catch { }
    }
}

internal static class L12CompactProcessMetricEncoder
{
    private static readonly HashSet<string> FixedStages = new(StringComparer.Ordinal)
    {
        "bytes.persistence.checkpoint-json", "bytes.persistence.command-state-json",
        "bytes.websocket.delta-payload", "bytes.websocket.full-payload", "bytes.websocket.sent-payload",
        "duration.action.engine", "duration.persistence.checkpoint-serialize",
        "duration.persistence.serialize-and-hash", "duration.persistence.total",
        "duration.persistence.transaction", "duration.snapshot.build", "duration.websocket.send",
        "duration.websocket.serialize", "persistence.v2-state-json-anomaly", "websocket.queue-depth",
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
    };

    internal static bool TryEncode(L12ProcessMetricSample sample, L12ProcessMetricArchiveIdentity identity,
        DateTimeOffset archiveUtc, long monotonicTimestamp, long timestampFrequency,
        out L12SerializedProcessMetric? serialized, out L12ProcessMetricArchiveReason reason)
    {
        serialized = null;
        if (!ValidIdentity(identity.Commit) || !ValidInstance(identity.Instance) || timestampFrequency <= 0)
        { reason = L12ProcessMetricArchiveReason.InvalidMetadata; return false; }
        try
        {
            if (sample.PerformanceWindow is { } performance
                && (performance.Series.Count != FixedStages.Count
                    || !FixedStages.SetEquals(performance.Series.Select(row => row.Stage))))
            { reason = L12ProcessMetricArchiveReason.InvalidSample; return false; }
            var stages = sample.PerformanceWindow?.Series.Select(row => new L12CompactStageMetric(
                row.Stage, row.Count, row.Total, row.Maximum, row.OverflowCount,
                row.DroppedContentionTotal, Band(row.P95), Band(row.P99))).ToArray();
            var dto = new L12CompactProcessMetric(1, identity.Commit, identity.Instance,
                sample.ObservedAt.ToUnixTimeMilliseconds(), archiveUtc.ToUnixTimeMilliseconds(),
                sample.ClockAvailability,
                new(sample.Runtime.Availability, sample.Runtime.WorkingSetBytes,
                    sample.Runtime.PrivateMemoryBytes, sample.Runtime.ManagedMemoryBytes,
                    sample.Runtime.GcPauseTotalMilliseconds, sample.Runtime.ThreadPoolPendingWorkItems,
                    sample.Runtime.ThreadPoolThreadCount),
                new(sample.Linux.MemoryCurrentAvailability, sample.Linux.MemoryCurrentBytes,
                    sample.Linux.MemoryMaximum.Availability, sample.Linux.MemoryMaximum.Bytes,
                    sample.Linux.MemoryMaximum.Unlimited, sample.Linux.SwapCurrentAvailability,
                    sample.Linux.SwapCurrentBytes, sample.Linux.SwapMaximum.Availability,
                    sample.Linux.SwapMaximum.Bytes, sample.Linux.SwapMaximum.Unlimited,
                    sample.Linux.Events.Availability, sample.Linux.Events.Scope,
                    sample.Linux.Events.Cumulative, sample.EventDeltaStatus, sample.EventDeltas,
                    sample.Linux.Pressure.Availability, sample.Linux.Pressure.Value),
                sample.PerformanceWindowAvailability, sample.PerformanceWindow?.Diagnostics, stages);
            var json = JsonSerializer.SerializeToUtf8Bytes(dto, JsonOptions);
            if (json.Length + 1 > L12ProcessMetricCollector.MaximumLineBytes)
            { reason = L12ProcessMetricArchiveReason.EntryTooLarge; return false; }
            serialized = new(json, archiveUtc, monotonicTimestamp, timestampFrequency);
            reason = L12ProcessMetricArchiveReason.None;
            return true;
        }
        catch
        {
            reason = L12ProcessMetricArchiveReason.InvalidSample;
            return false;
        }
    }

    private static bool ValidIdentity(string value) => !string.IsNullOrEmpty(value) && value.Length <= 64
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    internal static bool TryValidateEncodedLine(ReadOnlySpan<byte> json, string? expectedCommit,
        string? expectedInstance, out string? commit, out string? instance,
        out long archiveUtcMilliseconds)
    {
        commit = instance = null;
        archiveUtcMilliseconds = 0;
        try
        {
            var dto = JsonSerializer.Deserialize<L12CompactProcessMetric>(json, JsonOptions);
            if (dto is null || dto.Version != 1 || dto.Process is null || dto.Linux is null
                || !ValidIdentity(dto.Commit) || !ValidInstance(dto.Instance)
                || expectedCommit is not null && !string.Equals(dto.Commit, expectedCommit, StringComparison.Ordinal)
                || expectedInstance is not null && !string.Equals(dto.Instance, expectedInstance, StringComparison.Ordinal)
                || !ValidMetricValues(dto)
                || (dto.PerformanceAvailability == L12MetricAvailabilityReason.Available)
                    != (dto.Diagnostics is not null && dto.Stages is not null)
                || dto.Stages is not null && (dto.Stages.Count != FixedStages.Count
                    || !FixedStages.SetEquals(dto.Stages.Select(stage => stage.Stage)))) return false;
            var canonical = JsonSerializer.SerializeToUtf8Bytes(dto, JsonOptions);
            if (!json.SequenceEqual(canonical)) return false;
            commit = dto.Commit;
            instance = dto.Instance;
            archiveUtcMilliseconds = dto.ArchiveUtcMilliseconds;
            return true;
        }
        catch { return false; }
    }

    private static bool ValidMetricValues(L12CompactProcessMetric metric) =>
        Defined(metric.Clock) && ValidRuntime(metric.Process) && ValidLinux(metric.Linux)
        && Defined(metric.PerformanceAvailability)
        && (metric.Diagnostics is null || ValidDiagnostics(metric.Diagnostics))
        && (metric.Stages is null || metric.Stages.All(ValidStage));

    private static bool ValidRuntime(L12CompactRuntimeMetric metric)
    {
        if (!Defined(metric.Availability) || !NonNegative(metric.WorkingSetBytes)
            || !NonNegative(metric.PrivateBytes) || !NonNegative(metric.ManagedBytes)
            || !NonNegativeFinite(metric.GcPauseTotalMilliseconds)
            || !NonNegative(metric.ThreadPoolPending) || !NonNegative(metric.ThreadPoolThreads)) return false;
        var present = new object?[] { metric.WorkingSetBytes, metric.PrivateBytes, metric.ManagedBytes,
            metric.GcPauseTotalMilliseconds, metric.ThreadPoolPending, metric.ThreadPoolThreads }
            .Count(value => value is not null);
        return metric.Availability switch
        {
            L12MetricAvailabilityReason.Available => present == 6,
            L12MetricAvailabilityReason.Partial => present is > 0 and < 6,
            L12MetricAvailabilityReason.ReadFailure => present == 0,
            _ => false,
        };
    }

    private static bool ValidLinux(L12CompactLinuxMetric metric)
    {
        if (!AvailableValue(metric.MemoryCurrentAvailability, metric.MemoryCurrentBytes)
            || !AvailableLimit(metric.MemoryMaxAvailability, metric.MemoryMaxBytes,
                metric.MemoryMaxUnlimited)
            || !AvailableValue(metric.SwapCurrentAvailability, metric.SwapCurrentBytes)
            || !AvailableLimit(metric.SwapMaxAvailability, metric.SwapMaxBytes,
                metric.SwapMaxUnlimited)
            || !Defined(metric.EventsAvailability) || !Defined(metric.EventsDeltaStatus)
            || !Defined(metric.PressureAvailability)) return false;

        var eventsAvailable = metric.EventsAvailability == L12MetricAvailabilityReason.Available;
        if (eventsAvailable != (metric.EventsScope is not null && metric.EventsTotal is not null)
            || metric.EventsScope is { } scope && !Defined(scope)
            || metric.EventsTotal is { } total && !ValidEvents(total)) return false;
        if ((metric.EventsTotal is null) != (metric.EventsDeltaStatus == L12CgroupEventDeltaStatus.Unavailable)
            || (metric.EventsDeltaStatus == L12CgroupEventDeltaStatus.Available)
                != (metric.EventsDelta is not null)
            || metric.EventsDelta is { } delta && !ValidEvents(delta)) return false;

        var pressureAvailable = metric.PressureAvailability == L12MetricAvailabilityReason.Available;
        return pressureAvailable == (metric.Pressure is not null)
            && (metric.Pressure is null || ValidPressure(metric.Pressure));
    }

    private static bool AvailableValue(L12MetricAvailabilityReason availability, long? value) =>
        Defined(availability) && (availability == L12MetricAvailabilityReason.Available) == value.HasValue
        && NonNegative(value);

    private static bool AvailableLimit(L12MetricAvailabilityReason availability, long? bytes,
        bool? unlimited)
    {
        if (!Defined(availability)) return false;
        if (availability != L12MetricAvailabilityReason.Available)
            return bytes is null && unlimited is null;
        return unlimited switch
        {
            true => bytes is null,
            false => bytes is >= 0,
            _ => false,
        };
    }

    private static bool ValidEvents(L12CgroupMemoryEvents events) => events.Low >= 0
        && events.High >= 0 && events.Max >= 0 && events.Oom >= 0 && events.OomKill >= 0
        && events.OomGroupKill >= 0;

    private static bool ValidPressure(L12CgroupPressure pressure) =>
        NonNegativeFinite(pressure.SomeAvg10) && pressure.SomeTotalMicroseconds >= 0
        && NonNegativeFinite(pressure.FullAvg10) && pressure.FullTotalMicroseconds >= 0;

    private static bool ValidDiagnostics(PerformanceMetricDiagnostics diagnostics) =>
        diagnostics.UnknownStages >= 0 && diagnostics.InvalidSamples >= 0
        && diagnostics.ClampedNegatives >= 0 && diagnostics.SinkFailures >= 0
        && diagnostics.LostSinkSamples >= 0 && diagnostics.SkippedFlushes >= 0;

    private static bool ValidStage(L12CompactStageMetric stage) => stage is not null
        && stage.Count >= 0 && NonNegativeFinite(stage.Total) && NonNegativeFinite(stage.Maximum)
        && stage.OverflowCount >= 0 && stage.ContentionDrops >= 0
        && (stage.P95 is null || ValidBand(stage.P95))
        && (stage.P99 is null || ValidBand(stage.P99));

    private static bool ValidBand(L12CompactBand band) => NonNegativeFinite(band.Low)
        && (band.Overflow ? band.High is null
            : band.High is { } high && NonNegativeFinite(high) && high >= band.Low);

    private static bool NonNegative(long? value) => value is null || value.Value >= 0;
    private static bool NonNegative(int? value) => value is null || value.Value >= 0;
    private static bool NonNegativeFinite(double? value) => value is null
        || double.IsFinite(value.Value) && value.Value >= 0;
    private static bool NonNegativeFinite(double value) => double.IsFinite(value) && value >= 0;
    private static bool Defined<T>(T value) where T : struct, Enum => Enum.IsDefined(value);

    private static L12CompactBand? Band(PerformanceMetricBand? band) => band is null ? null
        : new(band.LowerExclusive, band.UpperInclusive, band.Overflow, band.LowerInclusive);

    private static bool ValidInstance(string value) => Guid.TryParseExact(value, "N", out var parsed)
        && string.Equals(value, parsed.ToString("N"), StringComparison.Ordinal);
}

internal sealed record L12CompactProcessMetric(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("commit")] string Commit,
    [property: JsonPropertyName("instance")] string Instance,
    [property: JsonPropertyName("observedUtcMs")] long ObservedUtcMilliseconds,
    [property: JsonPropertyName("archiveUtcMs")] long ArchiveUtcMilliseconds,
    [property: JsonPropertyName("clock")] L12MetricAvailabilityReason Clock,
    [property: JsonPropertyName("process")] L12CompactRuntimeMetric Process,
    [property: JsonPropertyName("linux")] L12CompactLinuxMetric Linux,
    [property: JsonPropertyName("performance")] L12MetricAvailabilityReason PerformanceAvailability,
    [property: JsonPropertyName("diagnostics")] PerformanceMetricDiagnostics? Diagnostics,
    [property: JsonPropertyName("stages")] IReadOnlyList<L12CompactStageMetric>? Stages);

internal sealed record L12CompactRuntimeMetric(
    [property: JsonPropertyName("availability")] L12MetricAvailabilityReason Availability,
    [property: JsonPropertyName("workingSetBytes")] long? WorkingSetBytes,
    [property: JsonPropertyName("privateBytes")] long? PrivateBytes,
    [property: JsonPropertyName("managedBytes")] long? ManagedBytes,
    [property: JsonPropertyName("gcPauseTotalMs")] double? GcPauseTotalMilliseconds,
    [property: JsonPropertyName("threadPoolPending")] long? ThreadPoolPending,
    [property: JsonPropertyName("threadPoolThreads")] int? ThreadPoolThreads);

internal sealed record L12CompactLinuxMetric(
    [property: JsonPropertyName("memoryCurrentAvailability")] L12MetricAvailabilityReason MemoryCurrentAvailability,
    [property: JsonPropertyName("memoryCurrentBytes")] long? MemoryCurrentBytes,
    [property: JsonPropertyName("memoryMaxAvailability")] L12MetricAvailabilityReason MemoryMaxAvailability,
    [property: JsonPropertyName("memoryMaxBytes")] long? MemoryMaxBytes,
    [property: JsonPropertyName("memoryMaxUnlimited")] bool? MemoryMaxUnlimited,
    [property: JsonPropertyName("swapCurrentAvailability")] L12MetricAvailabilityReason SwapCurrentAvailability,
    [property: JsonPropertyName("swapCurrentBytes")] long? SwapCurrentBytes,
    [property: JsonPropertyName("swapMaxAvailability")] L12MetricAvailabilityReason SwapMaxAvailability,
    [property: JsonPropertyName("swapMaxBytes")] long? SwapMaxBytes,
    [property: JsonPropertyName("swapMaxUnlimited")] bool? SwapMaxUnlimited,
    [property: JsonPropertyName("eventsAvailability")] L12MetricAvailabilityReason EventsAvailability,
    [property: JsonPropertyName("eventsScope")] L12CgroupEventScope? EventsScope,
    [property: JsonPropertyName("eventsTotal")] L12CgroupMemoryEvents? EventsTotal,
    [property: JsonPropertyName("eventsDeltaStatus")] L12CgroupEventDeltaStatus EventsDeltaStatus,
    [property: JsonPropertyName("eventsDelta")] L12CgroupMemoryEvents? EventsDelta,
    [property: JsonPropertyName("pressureAvailability")] L12MetricAvailabilityReason PressureAvailability,
    [property: JsonPropertyName("pressure")] L12CgroupPressure? Pressure);

internal sealed record L12CompactBand(
    [property: JsonPropertyName("low")] double Low,
    [property: JsonPropertyName("high")] double? High,
    [property: JsonPropertyName("overflow")] bool Overflow,
    [property: JsonPropertyName("lowInclusive")] bool LowInclusive);

internal sealed record L12CompactStageMetric(
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("count")] long Count,
    [property: JsonPropertyName("total")] double Total,
    [property: JsonPropertyName("max")] double Maximum,
    [property: JsonPropertyName("overflowCount")] long OverflowCount,
    [property: JsonPropertyName("contentionDrops")] long ContentionDrops,
    [property: JsonPropertyName("p95")] L12CompactBand? P95,
    [property: JsonPropertyName("p99")] L12CompactBand? P99);

internal sealed class L12ProcessMetricFileArchive : IL12ProcessMetricArchiveSink, IAsyncDisposable
{
    internal const long DefaultDailyCapacityBytes = 80L * 1024 * 1024;
    internal const long DefaultRootCapacityBytes = 768L * 1024 * 1024;
    internal const int DefaultMaximumEntries = 4096;
    internal const int MaximumDayDeletionsPerPass = 8;
    internal const string OwnershipMarkerName = ".l12-process-metrics-owned-v1";
    internal const string LockFileName = ".l12-process-metrics-owned-v1.lock";
    internal const string PinFileName = "PINNED";
    private const string SegmentSuffix = ".jsonl";
    private const int MaximumLinesPerSegment = 10_000;
    private static readonly byte[] MarkerBytes = Encoding.UTF8.GetBytes("l12-process-metrics-owned-v1\n");
    private static readonly byte[] NewLine = [(byte)'\n'];
    private readonly string _root, _commit, _instance;
    private readonly long _dailyCapacityBytes, _rootCapacityBytes;
    private readonly int _maximumEntries;
    private readonly HashSet<string> _createdSegments = new(PathComparer);
    private readonly Dictionary<string, ValidationCache> _validationCache = new(PathComparer);
    private FileStream? _ownership, _currentSegment;
    private string? _currentSegmentPath;
    private bool _poisoned;
    private DateTimeOffset? _lastUtc;
    private long _lastTimestamp, _lastTimestampFrequency;
    private int _stableTransitions;

    internal L12ProcessMetricFileArchive(string root, string commit, Guid instance,
        long dailyCapacityBytes = DefaultDailyCapacityBytes,
        long rootCapacityBytes = DefaultRootCapacityBytes,
        int maximumEntries = DefaultMaximumEntries)
        : this(root, commit, instance, dailyCapacityBytes, rootCapacityBytes, maximumEntries,
            requireLiteralRuntimeTail: true)
    {
    }

    internal L12ProcessMetricFileArchive(L12ResolvedProcessMetricArchiveRoot root, string commit, Guid instance,
        long dailyCapacityBytes = DefaultDailyCapacityBytes,
        long rootCapacityBytes = DefaultRootCapacityBytes,
        int maximumEntries = DefaultMaximumEntries)
        : this(root?.PhysicalArchivePath ?? throw new ArgumentNullException(nameof(root)), commit, instance,
            dailyCapacityBytes, rootCapacityBytes, maximumEntries, requireLiteralRuntimeTail: false)
    {
    }

    private L12ProcessMetricFileArchive(string root, string commit, Guid instance,
        long dailyCapacityBytes, long rootCapacityBytes, int maximumEntries, bool requireLiteralRuntimeTail)
    {
        if (string.IsNullOrWhiteSpace(root) || dailyCapacityBytes <= 0 || rootCapacityBytes <= 0
            || maximumEntries <= 0) throw new ArgumentOutOfRangeException(nameof(root));
        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var requiredTail = Path.Combine("runtime", "metrics", "process");
        if (requireLiteralRuntimeTail
            && !_root.EndsWith(Path.DirectorySeparatorChar + requiredTail, PathComparison))
            throw new ArgumentException("archive root must end in runtime/metrics/process", nameof(root));
        if (!ValidIdentity(commit)) throw new ArgumentException("invalid commit identity", nameof(commit));
        _commit = commit;
        _instance = instance.ToString("N");
        _dailyCapacityBytes = dailyCapacityBytes;
        _rootCapacityBytes = rootCapacityBytes;
        _maximumEntries = maximumEntries;
    }

    public async ValueTask<L12ProcessMetricArchiveWriteResult> WriteAsync(
        L12SerializedProcessMetric metric, CancellationToken cancellationToken)
    {
        if (_poisoned) return Failure(L12ProcessMetricArchiveReason.ContentMismatch);
        if (metric.Json.Length + 1 > L12ProcessMetricCollector.MaximumLineBytes)
            return Failure(L12ProcessMetricArchiveReason.EntryTooLarge);
        if (!L12CompactProcessMetricEncoder.TryValidateEncodedLine(metric.Json, _commit, _instance,
                out _, out _, out var archiveUtcMilliseconds)
            || archiveUtcMilliseconds != metric.ArchiveUtc.ToUnixTimeMilliseconds())
            return Failure(L12ProcessMetricArchiveReason.ContentMismatch);
        try
        {
            var ownership = EnsureOwnership();
            if (ownership != L12ProcessMetricArchiveReason.None) return Failure(ownership);
            var day = metric.ArchiveUtc.UtcDateTime.Date;
            var dayPath = Path.Combine(_root, DayName(day));
            var dayReady = EnsureDayDirectory(dayPath);
            if (dayReady != L12ProcessMetricArchiveReason.None) return Failure(dayReady);
            if (!TryScan(out var scan)) return Failure(L12ProcessMetricArchiveReason.UnsafeRoot);

            var lineBytes = metric.Json.Length + 1L;
            var currentDay = scan.Days.Single(info => info.Day == day);
            if (!currentDay.StructurallyOwned || !TryValidateOwnedDay(currentDay))
                return Failure(L12ProcessMetricArchiveReason.ContentMismatch);
            if (currentDay.TotalBytes > _dailyCapacityBytes - lineBytes)
                return Failure(L12ProcessMetricArchiveReason.DailyCapacity);

            var stableClock = ObserveClock(metric);
            var cutoff = day.AddDays(-7);
            var structuralCandidates = stableClock
                ? scan.Days.Where(info => info.Day < cutoff && info.StructurallyOwned && !info.Pinned)
                    .Where(info => !KnownInvalid(info)).OrderBy(info => info.Day)
                    .Take(MaximumDayDeletionsPerPass).ToArray()
                : [];
            var candidates = new List<DayEntry>(structuralCandidates.Length);
            foreach (var candidate in structuralCandidates)
            {
                if (TryValidateOwnedDay(candidate)) candidates.Add(candidate);
            }
            var deletableBytes = candidates.Aggregate(0L,
                (total, candidate) => SaturatingAdd(total, candidate.TotalBytes));
            var hasCapacityAfterCleanup = scan.TotalBytes <= _rootCapacityBytes - lineBytes
                || scan.TotalBytes - Math.Min(scan.TotalBytes, deletableBytes) <= _rootCapacityBytes - lineBytes;

            foreach (var candidate in candidates)
            {
                if (!TryRescanExact(candidate, cutoff)) return Failure(L12ProcessMetricArchiveReason.UnsafeRoot);
            }
            foreach (var candidate in candidates) DeleteOwnedDay(candidate);
            if (!hasCapacityAfterCleanup) return Failure(L12ProcessMetricArchiveReason.RootCapacity);

            var segmentReady = EnsureCurrentSegment(dayPath);
            if (segmentReady != L12ProcessMetricArchiveReason.None) return Failure(segmentReady);
            try
            {
                await _currentSegment!.WriteAsync(metric.Json, cancellationToken).ConfigureAwait(false);
                await _currentSegment.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);
                await _currentSegment.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _poisoned = true;
                throw;
            }
            return L12ProcessMetricArchiveWriteResult.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return Failure(L12ProcessMetricArchiveReason.WriteFailure); }
    }

    private L12ProcessMetricArchiveReason EnsureOwnership()
    {
        if (_ownership is not null) return L12ProcessMetricArchiveReason.None;
        if (HasReparsePointInExistingPath(_root)) return L12ProcessMetricArchiveReason.UnsafeRoot;
        Directory.CreateDirectory(_root);
        if (HasReparsePointInExistingPath(_root)) return L12ProcessMetricArchiveReason.UnsafeRoot;
        var markerPath = Path.Combine(_root, OwnershipMarkerName);
        var existingEntries = new DirectoryInfo(_root)
            .EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly).ToArray();
        if (!File.Exists(markerPath))
        {
            if (existingEntries.Length != 0) return L12ProcessMetricArchiveReason.UnsafeRoot;
        }
        else if (!ExactMarker(markerPath)) return L12ProcessMetricArchiveReason.UnsafeRoot;
        var lockPath = Path.Combine(_root, LockFileName);
        if ((File.Exists(lockPath) || Directory.Exists(lockPath))
            && IsProtectedReparsePoint(File.GetAttributes(lockPath)))
            return L12ProcessMetricArchiveReason.UnsafeRoot;
        FileStream ownership;
        try
        {
            ownership = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, 1, FileOptions.None);
        }
        catch (IOException) { return L12ProcessMetricArchiveReason.OwnershipConflict; }
        try
        {
            var others = new DirectoryInfo(_root).EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly)
                .Where(info => !string.Equals(info.Name, LockFileName, StringComparison.Ordinal)).ToArray();
            if (!File.Exists(markerPath))
            {
                if (others.Length != 0) return L12ProcessMetricArchiveReason.UnsafeRoot;
                WriteMarker(markerPath);
            }
            if (!ExactMarker(markerPath)) return L12ProcessMetricArchiveReason.UnsafeRoot;
            _ownership = ownership;
            ownership = null!;
            return L12ProcessMetricArchiveReason.None;
        }
        finally { ownership?.Dispose(); }
    }

    private L12ProcessMetricArchiveReason EnsureDayDirectory(string dayPath)
    {
        if (HasReparsePointInExistingPath(dayPath)) return L12ProcessMetricArchiveReason.UnsafeRoot;
        Directory.CreateDirectory(dayPath);
        if (HasReparsePointInExistingPath(dayPath)) return L12ProcessMetricArchiveReason.UnsafeRoot;
        var markerPath = Path.Combine(dayPath, OwnershipMarkerName);
        var entries = new DirectoryInfo(dayPath).EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly).ToArray();
        if (!File.Exists(markerPath))
        {
            if (entries.Length != 0) return L12ProcessMetricArchiveReason.UnsafeRoot;
            WriteMarker(markerPath);
        }
        return ExactMarker(markerPath) ? L12ProcessMetricArchiveReason.None
            : L12ProcessMetricArchiveReason.UnsafeRoot;
    }

    private L12ProcessMetricArchiveReason EnsureCurrentSegment(string dayPath)
    {
        var path = Path.Combine(dayPath, _instance + SegmentSuffix);
        if (string.Equals(_currentSegmentPath, path, PathComparison) && _currentSegment is not null)
            return L12ProcessMetricArchiveReason.None;
        _currentSegment?.Dispose();
        _currentSegment = null;
        _currentSegmentPath = null;
        if (File.Exists(path))
        {
            if (!_createdSegments.Contains(path) || IsProtectedReparsePoint(File.GetAttributes(path)))
                return L12ProcessMetricArchiveReason.ContentMismatch;
        }
        else if (_createdSegments.Contains(path)) return L12ProcessMetricArchiveReason.ContentMismatch;
        _currentSegment = new FileStream(path,
            File.Exists(path) ? FileMode.Append : FileMode.CreateNew, FileAccess.Write, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        _createdSegments.Add(path);
        _currentSegmentPath = path;
        return L12ProcessMetricArchiveReason.None;
    }

    private bool TryScan(out Scan scan)
    {
        scan = new([], 0);
        if (!Directory.Exists(_root) || HasReparsePointInExistingPath(_root)) return false;
        var days = new List<DayEntry>();
        long totalBytes = 0;
        var count = 0;
        var rootMarkerSeen = false;
        var lockSeen = false;
        foreach (var info in new DirectoryInfo(_root).EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly))
        {
            if (++count > _maximumEntries || IsProtectedReparsePoint(info.Attributes)) return false;
            if (info is FileInfo file)
            {
                totalBytes = SaturatingAdd(totalBytes, file.Length);
                if (file.Name == OwnershipMarkerName)
                {
                    if (!ExactMarker(file.FullName)) return false;
                    rootMarkerSeen = true;
                }
                else if (file.Name == LockFileName) lockSeen = true;
                continue;
            }
            if (info is not DirectoryInfo directory || !TryParseDay(directory.Name, out var day)
                || !TryScanDay(directory, day, ref count, out var dayEntry)) return false;
            days.Add(dayEntry);
            totalBytes = SaturatingAdd(totalBytes, dayEntry.TotalBytes);
        }
        if (!rootMarkerSeen || !lockSeen) return false;
        scan = new(days, totalBytes);
        return true;
    }

    private bool TryScanDay(DirectoryInfo directory, DateTime day, ref int count, out DayEntry entry)
    {
        entry = new(day, directory.FullName, false, false, [], 0, "");
        var files = new List<SegmentEntry>();
        var names = new List<string>();
        long totalBytes = 0;
        var markerSeen = false;
        var pinned = false;
        var structurallyOwned = true;
        foreach (var info in directory.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly))
        {
            if (++count > _maximumEntries || IsProtectedReparsePoint(info.Attributes)
                || info is not FileInfo file) return false;
            var length = file.Length;
            if (_currentSegment is not null
                && string.Equals(_currentSegmentPath, file.FullName, PathComparison))
                length = Math.Max(length, _currentSegment.Length);
            names.Add(file.Name + "|" + length + "|" + file.LastWriteTimeUtc.Ticks);
            totalBytes = SaturatingAdd(totalBytes, length);
            if (file.Name == OwnershipMarkerName)
            {
                markerSeen = ExactMarker(file.FullName);
                if (!markerSeen) return false;
            }
            else if (file.Name == PinFileName) pinned = true;
            else if (TryParseSegment(file.Name, out var instance))
                files.Add(new(file.FullName, instance, length, file.LastWriteTimeUtc));
            else structurallyOwned = false;
        }
        if (!markerSeen)
        {
            if (names.Count != 0) return false;
            structurallyOwned = false;
        }
        names.Sort(StringComparer.Ordinal);
        entry = new(day, directory.FullName, structurallyOwned && markerSeen, pinned,
            files, totalBytes, string.Join("\n", names));
        return true;
    }

    private bool TryValidateOwnedDay(DayEntry day, bool requireFreshContent = false)
    {
        if (!day.StructurallyOwned || day.Pinned) return false;
        foreach (var segment in day.Segments)
        {
            if (!TryValidateSegment(segment, day.Day, requireFreshContent)) return false;
        }
        return true;
    }

    private bool TryValidateSegment(SegmentEntry segment, DateTime expectedDay, bool requireFreshContent)
    {
        if (_currentSegment is not null && !_poisoned
            && string.Equals(_currentSegmentPath, segment.Path, PathComparison)
            && _createdSegments.Contains(segment.Path)) return true;
        if (segment.Length <= 0 || segment.Length > _dailyCapacityBytes) return false;
        if (_validationCache.TryGetValue(segment.Path, out var cached)
            && cached.Length == segment.Length && cached.LastWriteUtc == segment.LastWriteUtc)
        {
            if (!cached.Valid || !requireFreshContent) return cached.Valid;
        }
        var valid = ValidateSegmentContent(segment, expectedDay);
        if (_validationCache.Count < _maximumEntries || _validationCache.ContainsKey(segment.Path))
            _validationCache[segment.Path] = new(segment.Length, segment.LastWriteUtc, valid);
        return valid;
    }

    private bool KnownInvalid(DayEntry day) => day.Segments.Any(segment =>
        _validationCache.TryGetValue(segment.Path, out var cached)
        && cached.Length == segment.Length && cached.LastWriteUtc == segment.LastWriteUtc && !cached.Valid);

    private static bool ValidateSegmentContent(SegmentEntry segment, DateTime expectedDay)
    {
        var line = ArrayPool<byte>.Shared.Rent(L12ProcessMetricCollector.MaximumLineBytes);
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            using var stream = new FileStream(segment.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.SequentialScan);
            var lineLength = 0;
            var lineCount = 0;
            string? commit = null;
            while (true)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                for (var index = 0; index < read; index++)
                {
                    var value = buffer[index];
                    if (value == (byte)'\n')
                    {
                        if (lineLength == 0 || ++lineCount > MaximumLinesPerSegment
                            || !L12CompactProcessMetricEncoder.TryValidateEncodedLine(
                                line.AsSpan(0, lineLength), commit, segment.Instance,
                                out var actualCommit, out _, out var archiveUtcMilliseconds)
                            || DateTimeOffset.FromUnixTimeMilliseconds(archiveUtcMilliseconds).UtcDateTime.Date
                                != expectedDay.Date)
                            return false;
                        commit ??= actualCommit;
                        lineLength = 0;
                        continue;
                    }
                    if (lineLength >= L12ProcessMetricCollector.MaximumLineBytes - 1) return false;
                    line[lineLength++] = value;
                }
            }
            return lineCount != 0 && lineLength == 0;
        }
        catch { return false; }
        finally
        {
            ArrayPool<byte>.Shared.Return(line, clearArray: true);
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private bool TryRescanExact(DayEntry expected, DateTime cutoff)
    {
        if (expected.Day >= cutoff || !Directory.Exists(expected.Path)
            || IsProtectedReparsePoint(File.GetAttributes(expected.Path))) return false;
        var count = 0;
        if (!TryScanDay(new DirectoryInfo(expected.Path), expected.Day, ref count, out var current)
            || !current.StructurallyOwned || current.Pinned
            || !string.Equals(current.Signature, expected.Signature, StringComparison.Ordinal)) return false;
        return TryValidateOwnedDay(current, requireFreshContent: true);
    }

    private static void DeleteOwnedDay(DayEntry day)
    {
        foreach (var segment in day.Segments) File.Delete(segment.Path);
        File.Delete(Path.Combine(day.Path, OwnershipMarkerName));
        Directory.Delete(day.Path, recursive: false);
    }

    private bool ObserveClock(L12SerializedProcessMetric metric)
    {
        if (_lastUtc is null) { SaveClock(metric); return false; }
        var stable = metric.TimestampFrequency == _lastTimestampFrequency
            && metric.MonotonicTimestamp > _lastTimestamp && metric.ArchiveUtc > _lastUtc.Value;
        if (stable)
        {
            var monotonicSeconds = (metric.MonotonicTimestamp - _lastTimestamp)
                / (double)metric.TimestampFrequency;
            var utcSeconds = (metric.ArchiveUtc - _lastUtc.Value).TotalSeconds;
            stable = double.IsFinite(monotonicSeconds) && monotonicSeconds >= 0
                && Math.Abs(monotonicSeconds - utcSeconds) <= 30;
        }
        _stableTransitions = stable ? Math.Min(2, _stableTransitions + 1) : 0;
        SaveClock(metric);
        return _stableTransitions >= 2;
    }

    private void SaveClock(L12SerializedProcessMetric metric)
    {
        _lastUtc = metric.ArchiveUtc;
        _lastTimestamp = metric.MonotonicTimestamp;
        _lastTimestampFrequency = metric.TimestampFrequency;
    }

    private static void WriteMarker(string path)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Write(MarkerBytes);
        stream.Flush(flushToDisk: true);
    }

    private static bool ExactMarker(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || IsProtectedReparsePoint(info.Attributes) || info.Length != MarkerBytes.Length)
                return false;
            return File.ReadAllBytes(path).AsSpan().SequenceEqual(MarkerBytes);
        }
        catch { return false; }
    }

    internal static bool TryParseDay(string name, out DateTime day)
    {
        var valid = DateTime.TryParseExact(name, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal
                | System.Globalization.DateTimeStyles.AdjustToUniversal, out day);
        return valid && string.Equals(name, DayName(day), StringComparison.Ordinal);
    }

    private static bool TryParseSegment(string name, out string instance)
    {
        instance = "";
        if (!name.EndsWith(SegmentSuffix, StringComparison.Ordinal)) return false;
        var value = name[..^SegmentSuffix.Length];
        if (!Guid.TryParseExact(value, "N", out var parsed)
            || !string.Equals(value, parsed.ToString("N"), StringComparison.Ordinal)) return false;
        instance = value;
        return true;
    }

    private static string DayName(DateTime day) => day.ToString("yyyy-MM-dd",
        System.Globalization.CultureInfo.InvariantCulture);

    private static bool ValidIdentity(string value) => !string.IsNullOrEmpty(value) && value.Length <= 64
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static long SaturatingAdd(long current, long addition) =>
        long.MaxValue - current < addition ? long.MaxValue : current + addition;

    private static L12ProcessMetricArchiveWriteResult Failure(L12ProcessMetricArchiveReason reason) =>
        L12ProcessMetricArchiveWriteResult.Failure(reason);

    internal static bool IsProtectedReparsePoint(FileAttributes attributes) =>
        (attributes & FileAttributes.ReparsePoint) != 0;

    private static bool HasReparsePointInExistingPath(string path)
    {
        try
        {
            for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            {
                if (current.Exists && IsProtectedReparsePoint(current.Attributes)) return true;
            }
            return false;
        }
        catch { return true; }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public async ValueTask DisposeAsync()
    {
        if (_currentSegment is not null) await _currentSegment.DisposeAsync().ConfigureAwait(false);
        _currentSegment = null;
        if (_ownership is not null) await _ownership.DisposeAsync().ConfigureAwait(false);
        _ownership = null;
    }

    private sealed record SegmentEntry(string Path, string Instance, long Length, DateTime LastWriteUtc);
    private sealed record DayEntry(DateTime Day, string Path, bool StructurallyOwned, bool Pinned,
        IReadOnlyList<SegmentEntry> Segments, long TotalBytes, string Signature);
    private sealed record Scan(IReadOnlyList<DayEntry> Days, long TotalBytes);
    private sealed record ValidationCache(long Length, DateTime LastWriteUtc, bool Valid);
}
