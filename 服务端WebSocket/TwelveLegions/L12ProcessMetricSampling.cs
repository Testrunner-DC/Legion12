using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace TwelveLegions.Server;

internal enum L12MetricAvailabilityReason
{
    Available,
    Partial,
    NotLinux,
    Busy,
    SourceMissing,
    AccessDenied,
    InvalidData,
    ReadFailure,
    ClockRegressed,
}

internal sealed record L12ProcessRuntimeMetrics(
    L12MetricAvailabilityReason Availability,
    long? WorkingSetBytes,
    long? PrivateMemoryBytes,
    long? ManagedMemoryBytes,
    double? GcPauseTotalMilliseconds,
    long? ThreadPoolPendingWorkItems,
    int? ThreadPoolThreadCount);

internal sealed record L12CgroupLimit(long? Bytes, bool? Unlimited,
    L12MetricAvailabilityReason Availability);

internal sealed record L12CgroupMemoryEvents(long Low, long High, long Max, long Oom,
    long OomKill, long OomGroupKill);

internal enum L12CgroupEventScope { Local, Hierarchical }

internal sealed record L12CgroupEventMetrics(L12MetricAvailabilityReason Availability,
    L12CgroupEventScope? Scope, L12CgroupMemoryEvents? Cumulative);

internal sealed record L12CgroupPressure(double SomeAvg10, long SomeTotalMicroseconds,
    double FullAvg10, long FullTotalMicroseconds);

internal sealed record L12CgroupPressureMetrics(L12MetricAvailabilityReason Availability,
    L12CgroupPressure? Value);

internal sealed class L12LinuxProcessMetrics
{
    internal L12MetricAvailabilityReason MemoryCurrentAvailability { get; }
    internal long? MemoryCurrentBytes { get; }
    internal L12CgroupLimit MemoryMaximum { get; }
    internal L12MetricAvailabilityReason SwapCurrentAvailability { get; }
    internal long? SwapCurrentBytes { get; }
    internal L12CgroupLimit SwapMaximum { get; }
    internal L12CgroupEventMetrics Events { get; }
    internal L12CgroupPressureMetrics Pressure { get; }
    private readonly string? _eventSourceIdentity;

    internal L12LinuxProcessMetrics(L12MetricAvailabilityReason memoryCurrentAvailability,
        long? memoryCurrentBytes, L12CgroupLimit memoryMaximum,
        L12MetricAvailabilityReason swapCurrentAvailability, long? swapCurrentBytes,
        L12CgroupLimit swapMaximum, L12CgroupEventMetrics events,
        L12CgroupPressureMetrics pressure, string? eventSourceIdentity = null)
    {
        MemoryCurrentAvailability = memoryCurrentAvailability;
        MemoryCurrentBytes = memoryCurrentBytes;
        MemoryMaximum = memoryMaximum;
        SwapCurrentAvailability = swapCurrentAvailability;
        SwapCurrentBytes = swapCurrentBytes;
        SwapMaximum = swapMaximum;
        Events = events;
        Pressure = pressure;
        _eventSourceIdentity = eventSourceIdentity;
    }

    internal bool HasSameEventSource(L12LinuxProcessMetrics previous) =>
        _eventSourceIdentity is not null
        && string.Equals(_eventSourceIdentity, previous._eventSourceIdentity, StringComparison.Ordinal);

    internal static L12LinuxProcessMetrics Unavailable(L12MetricAvailabilityReason reason) => new(
        reason, null, new(null, null, reason), reason, null, new(null, null, reason),
        new(reason, null, null), new(reason, null));

    public override string ToString() => nameof(L12LinuxProcessMetrics);
}

internal sealed record L12ProcessMetricProbe(
    Func<DateTimeOffset> ReadUtcNow,
    Func<L12ProcessRuntimeMetrics> ReadRuntime,
    Func<L12LinuxProcessMetrics> ReadLinux,
    Func<(bool Available, PerformanceMetricBatch? Batch)> ReadPerformanceWindow)
{
    internal static L12ProcessMetricProbe System() => new(
        () => DateTimeOffset.UtcNow,
        L12SystemProcessMetricProbe.ReadRuntime,
        () => L12LinuxCgroupV2MetricProbe.ReadSystem(),
        () => L12PerformanceMetrics.TrySnapshot(out var batch) ? (true, batch) : (false, null));
}

internal sealed record L12ProcessMetricSample(
    DateTimeOffset ObservedAt,
    L12MetricAvailabilityReason ClockAvailability,
    L12ProcessRuntimeMetrics Runtime,
    L12LinuxProcessMetrics Linux,
    L12CgroupMemoryEvents? EventDeltas,
    L12CgroupEventDeltaStatus EventDeltaStatus,
    bool EventCounterReset,
    L12MetricAvailabilityReason PerformanceWindowAvailability,
    PerformanceMetricBatch? PerformanceWindow);

internal enum L12CgroupEventDeltaStatus
{
    Unavailable,
    Baseline,
    Available,
    SourceChanged,
    CounterReset,
}

/// <summary>
/// Captures one strongly typed process snapshot. Scheduling, persistence and serialization
/// belong to the bounded collector/archive layer; this type starts no worker and writes no output.
/// </summary>
internal sealed class L12ProcessMetricSampler
{
    private readonly L12ProcessMetricProbe _probe;
    private int _capturing;
    private DateTimeOffset? _lastObservedAt;
    private L12CgroupMemoryEvents? _lastEvents;
    private L12LinuxProcessMetrics? _lastEventSource;

    internal L12ProcessMetricSampler(L12ProcessMetricProbe probe) => _probe = probe;

    internal bool TryCapture(out L12ProcessMetricSample? sample)
    {
        sample = null;
        if (Interlocked.CompareExchange(ref _capturing, 1, 0) != 0) return false;
        try
        {
            DateTimeOffset observedAt;
            var clockAvailability = L12MetricAvailabilityReason.Available;
            try { observedAt = _probe.ReadUtcNow(); }
            catch
            {
                observedAt = DateTimeOffset.UtcNow;
                clockAvailability = L12MetricAvailabilityReason.ReadFailure;
            }
            if (_lastObservedAt is { } previousTime && observedAt < previousTime)
                clockAvailability = L12MetricAvailabilityReason.ClockRegressed;
            _lastObservedAt = observedAt;

            L12ProcessRuntimeMetrics runtime;
            try { runtime = _probe.ReadRuntime(); }
            catch { runtime = L12SystemProcessMetricProbe.Unavailable(); }

            L12LinuxProcessMetrics linux;
            try { linux = _probe.ReadLinux(); }
            catch { linux = L12LinuxProcessMetrics.Unavailable(L12MetricAvailabilityReason.ReadFailure); }

            L12CgroupMemoryEvents? deltas = null;
            var eventCounterReset = false;
            var eventDeltaStatus = L12CgroupEventDeltaStatus.Unavailable;
            if (linux.Events.Cumulative is { } events)
            {
                if (_lastEvents is { } previous)
                {
                    if (_lastEventSource is null || !linux.HasSameEventSource(_lastEventSource))
                        eventDeltaStatus = L12CgroupEventDeltaStatus.SourceChanged;
                    else
                    {
                        eventCounterReset = IsBefore(events, previous);
                        eventDeltaStatus = eventCounterReset ? L12CgroupEventDeltaStatus.CounterReset
                            : L12CgroupEventDeltaStatus.Available;
                        if (!eventCounterReset) deltas = Subtract(events, previous);
                    }
                }
                else eventDeltaStatus = L12CgroupEventDeltaStatus.Baseline;
                _lastEvents = events;
                _lastEventSource = linux;
            }
            else
            {
                // A missing interval cannot be represented as the next ten-second delta.
                _lastEvents = null;
                _lastEventSource = null;
            }

            PerformanceMetricBatch? performance = null;
            var performanceAvailability = L12MetricAvailabilityReason.Busy;
            try
            {
                var result = _probe.ReadPerformanceWindow();
                if (result.Available && result.Batch is not null)
                {
                    performanceAvailability = L12MetricAvailabilityReason.Available;
                    performance = result.Batch;
                }
            }
            catch { performanceAvailability = L12MetricAvailabilityReason.ReadFailure; }

            sample = new L12ProcessMetricSample(observedAt, clockAvailability, runtime, linux,
                deltas, eventDeltaStatus, eventCounterReset, performanceAvailability, performance);
            return true;
        }
        finally { Volatile.Write(ref _capturing, 0); }
    }

    private static bool IsBefore(L12CgroupMemoryEvents current, L12CgroupMemoryEvents previous)
        => current.Low < previous.Low || current.High < previous.High || current.Max < previous.Max
            || current.Oom < previous.Oom || current.OomKill < previous.OomKill
            || current.OomGroupKill < previous.OomGroupKill;

    private static L12CgroupMemoryEvents Subtract(L12CgroupMemoryEvents current,
        L12CgroupMemoryEvents previous) => new(current.Low - previous.Low, current.High - previous.High,
        current.Max - previous.Max, current.Oom - previous.Oom, current.OomKill - previous.OomKill,
        current.OomGroupKill - previous.OomGroupKill);
}

internal static class L12SystemProcessMetricProbe
{
    internal static L12ProcessRuntimeMetrics ReadRuntime()
    {
        Process? process = null;
        try { process = Process.GetCurrentProcess(); process.Refresh(); }
        catch { process?.Dispose(); process = null; }
        try
        {
            var workingSet = Read(() => process!.WorkingSet64, process is not null);
            var privateMemory = Read(() => process!.PrivateMemorySize64, process is not null);
            var managedMemory = Read(() => GC.GetTotalMemory(false), true);
            var gcPause = Read(() => GC.GetTotalPauseDuration().TotalMilliseconds, true);
            var pending = Read(() => ThreadPool.PendingWorkItemCount, true);
            var threads = Read(() => ThreadPool.ThreadCount, true);
            var available = new object?[] { workingSet, privateMemory, managedMemory, gcPause, pending, threads }
                .Count(value => value is not null);
            return new(available == 6 ? L12MetricAvailabilityReason.Available
                    : available == 0 ? L12MetricAvailabilityReason.ReadFailure : L12MetricAvailabilityReason.Partial,
                workingSet, privateMemory, managedMemory, gcPause, pending, threads);
        }
        finally { process?.Dispose(); }
    }

    internal static L12ProcessRuntimeMetrics Unavailable() => new(
        L12MetricAvailabilityReason.ReadFailure, null, null, null, null, null, null);

    private static T? Read<T>(Func<T> read, bool enabled) where T : struct
    {
        if (!enabled) return null;
        try { return read(); }
        catch { return null; }
    }
}

internal sealed record L12MetricTextReadResult(L12MetricAvailabilityReason Availability, string? Text)
{
    internal static L12MetricTextReadResult Available(string text) =>
        new(L12MetricAvailabilityReason.Available, text);
    internal static L12MetricTextReadResult Unavailable(L12MetricAvailabilityReason reason) => new(reason, null);
}

internal interface IL12BoundedMetricTextReader
{
    L12MetricTextReadResult Read(string path);
}

internal sealed class L12BoundedMetricTextReader : IL12BoundedMetricTextReader
{
    internal const int MaximumBytes = 128 * 1024;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public L12MetricTextReadResult Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                4096, FileOptions.SequentialScan);
            var bytes = ArrayPool<byte>.Shared.Rent(MaximumBytes + 1);
            try
            {
                var count = 0;
                while (count <= MaximumBytes)
                {
                    var read = stream.Read(bytes, count, MaximumBytes + 1 - count);
                    if (read == 0) break;
                    count += read;
                }
                if (count > MaximumBytes)
                    return L12MetricTextReadResult.Unavailable(L12MetricAvailabilityReason.InvalidData);
                return L12MetricTextReadResult.Available(StrictUtf8.GetString(bytes, 0, count));
            }
            finally { ArrayPool<byte>.Shared.Return(bytes, clearArray: true); }
        }
        catch (UnauthorizedAccessException)
        { return L12MetricTextReadResult.Unavailable(L12MetricAvailabilityReason.AccessDenied); }
        catch (FileNotFoundException)
        { return L12MetricTextReadResult.Unavailable(L12MetricAvailabilityReason.SourceMissing); }
        catch (DirectoryNotFoundException)
        { return L12MetricTextReadResult.Unavailable(L12MetricAvailabilityReason.SourceMissing); }
        catch (DecoderFallbackException)
        { return L12MetricTextReadResult.Unavailable(L12MetricAvailabilityReason.InvalidData); }
        catch (IOException)
        { return L12MetricTextReadResult.Unavailable(L12MetricAvailabilityReason.ReadFailure); }
        catch
        { return L12MetricTextReadResult.Unavailable(L12MetricAvailabilityReason.ReadFailure); }
    }
}

internal static class L12LinuxCgroupV2MetricProbe
{
    private const string ProcCgroup = "/proc/self/cgroup";
    private const string ProcMountInfo = "/proc/self/mountinfo";

    internal static L12LinuxProcessMetrics ReadSystem() => Read(
        OperatingSystem.IsLinux(), new L12BoundedMetricTextReader());

    internal static L12LinuxProcessMetrics Read(bool isLinux, IL12BoundedMetricTextReader reader)
    {
        if (!isLinux) return L12LinuxProcessMetrics.Unavailable(L12MetricAvailabilityReason.NotLinux);
        var cgroup = reader.Read(ProcCgroup);
        if (cgroup.Availability != L12MetricAvailabilityReason.Available)
            return L12LinuxProcessMetrics.Unavailable(cgroup.Availability);
        var mountInfo = reader.Read(ProcMountInfo);
        if (mountInfo.Availability != L12MetricAvailabilityReason.Available)
            return L12LinuxProcessMetrics.Unavailable(mountInfo.Availability);
        if (!TryResolveCgroupDirectory(cgroup.Text!, mountInfo.Text!, out var directory))
            return L12LinuxProcessMetrics.Unavailable(L12MetricAvailabilityReason.InvalidData);

        var memoryCurrent = ReadNonNegative(reader, Combine(directory, "memory.current"));
        var memoryMaximum = ReadLimit(reader, Combine(directory, "memory.max"));
        var swapCurrent = ReadNonNegative(reader, Combine(directory, "memory.swap.current"));
        var swapMaximum = ReadLimit(reader, Combine(directory, "memory.swap.max"));
        var events = ReadEvents(reader, directory);
        var pressure = ReadPressure(reader, Combine(directory, "memory.pressure"));
        var eventSourceIdentity = events.Cumulative is null ? null
            : directory + "\n" + events.Scope!.Value;
        return new(memoryCurrent.Availability, memoryCurrent.Value, memoryMaximum,
            swapCurrent.Availability, swapCurrent.Value, swapMaximum, events, pressure,
            eventSourceIdentity);
    }

    internal static bool TryResolveCgroupDirectory(string cgroupText, string mountInfoText,
        out string directory)
    {
        directory = "";
        string? cgroupPath = null;
        foreach (var line in Lines(cgroupText))
        {
            if (!line.StartsWith("0::", StringComparison.Ordinal)) continue;
            if (cgroupPath is not null || !TryNormalize(line[3..], out cgroupPath)) return false;
        }
        if (cgroupPath is null) return false;

        string? bestRoot = null, bestMount = null;
        foreach (var line in Lines(mountInfoText))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var separator = Array.IndexOf(fields, "-");
            if (separator < 6 || separator + 1 >= fields.Length
                || !string.Equals(fields[separator + 1], "cgroup2", StringComparison.Ordinal)) continue;
            if (!TryNormalize(UnescapeMountInfo(fields[3]), out var root)
                || !TryNormalize(UnescapeMountInfo(fields[4]), out var mount)) continue;
            if (!IsWithin(cgroupPath, root)) continue;
            if (bestRoot is null || root.Length > bestRoot.Length)
            { bestRoot = root; bestMount = mount; }
        }
        if (bestRoot is null || bestMount is null) return false;
        var relative = bestRoot == "/" ? cgroupPath : cgroupPath[bestRoot.Length..];
        return TryNormalize(bestMount.TrimEnd('/') + "/" + relative.TrimStart('/'), out directory);
    }

    private static (L12MetricAvailabilityReason Availability, long? Value) ReadNonNegative(
        IL12BoundedMetricTextReader reader, string path)
    {
        var result = reader.Read(path);
        if (result.Availability != L12MetricAvailabilityReason.Available)
            return (result.Availability, null);
        return long.TryParse(result.Text!.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            && value >= 0 ? (L12MetricAvailabilityReason.Available, value)
            : (L12MetricAvailabilityReason.InvalidData, null);
    }

    private static L12CgroupLimit ReadLimit(IL12BoundedMetricTextReader reader, string path)
    {
        var result = reader.Read(path);
        if (result.Availability != L12MetricAvailabilityReason.Available)
            return new(null, null, result.Availability);
        var value = result.Text!.Trim();
        if (string.Equals(value, "max", StringComparison.Ordinal))
            return new(null, true, L12MetricAvailabilityReason.Available);
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes) && bytes >= 0
            ? new(bytes, false, L12MetricAvailabilityReason.Available)
            : new(null, null, L12MetricAvailabilityReason.InvalidData);
    }

    private static L12CgroupEventMetrics ReadEvents(IL12BoundedMetricTextReader reader, string directory)
    {
        var result = reader.Read(Combine(directory, "memory.events.local"));
        var scope = L12CgroupEventScope.Local;
        if (result.Availability == L12MetricAvailabilityReason.SourceMissing)
        {
            result = reader.Read(Combine(directory, "memory.events"));
            scope = L12CgroupEventScope.Hierarchical;
        }
        if (result.Availability != L12MetricAvailabilityReason.Available)
            return new(result.Availability, null, null);
        if (!TryKeyValues(result.Text!, out var values)
            || !Required(values, "low", out var low) || !Required(values, "high", out var high)
            || !Required(values, "max", out var max) || !Required(values, "oom", out var oom)
            || !Required(values, "oom_kill", out var oomKill))
            return new(L12MetricAvailabilityReason.InvalidData, null, null);
        values.TryGetValue("oom_group_kill", out var oomGroupKill);
        return new(L12MetricAvailabilityReason.Available, scope,
            new(low, high, max, oom, oomKill, oomGroupKill));
    }

    private static L12CgroupPressureMetrics ReadPressure(IL12BoundedMetricTextReader reader, string path)
    {
        var result = reader.Read(path);
        if (result.Availability != L12MetricAvailabilityReason.Available)
            return new(result.Availability, null);
        double? someAvg10 = null, fullAvg10 = null;
        long? someTotal = null, fullTotal = null;
        foreach (var line in Lines(result.Text!))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2) continue;
            double? average = null;
            long? total = null;
            foreach (var field in fields.Skip(1))
            {
                var parts = field.Split('=', 2);
                if (parts.Length != 2) continue;
                if (parts[0] == "avg10" && double.TryParse(parts[1], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var parsedAverage) && double.IsFinite(parsedAverage)
                    && parsedAverage >= 0) average = parsedAverage;
                if (parts[0] == "total" && long.TryParse(parts[1], NumberStyles.None,
                        CultureInfo.InvariantCulture, out var parsedTotal) && parsedTotal >= 0) total = parsedTotal;
            }
            if (fields[0] == "some") { someAvg10 = average; someTotal = total; }
            if (fields[0] == "full") { fullAvg10 = average; fullTotal = total; }
        }
        return someAvg10 is not null && someTotal is not null && fullAvg10 is not null && fullTotal is not null
            ? new(L12MetricAvailabilityReason.Available,
                new(someAvg10.Value, someTotal.Value, fullAvg10.Value, fullTotal.Value))
            : new(L12MetricAvailabilityReason.InvalidData, null);
    }

    private static bool TryKeyValues(string text, out Dictionary<string, long> values)
    {
        values = new(StringComparer.Ordinal);
        foreach (var line in Lines(text))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 2 || values.ContainsKey(fields[0])
                || !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                || value < 0) return false;
            values.Add(fields[0], value);
        }
        return values.Count != 0;
    }

    private static bool Required(IReadOnlyDictionary<string, long> values, string key, out long value)
        => values.TryGetValue(key, out value);

    private static IEnumerable<string> Lines(string text) => text.Split(['\r', '\n'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsWithin(string path, string root) => root == "/" || path == root
        || path.StartsWith(root + "/", StringComparison.Ordinal);

    private static bool TryNormalize(string value, out string normalized)
    {
        normalized = "";
        if (!value.StartsWith("/", StringComparison.Ordinal)) return false;
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part is "." or "..")) return false;
        normalized = parts.Length == 0 ? "/" : "/" + string.Join('/', parts);
        return true;
    }

    private static string Combine(string directory, string leaf) => directory.TrimEnd('/') + "/" + leaf;

    private static string UnescapeMountInfo(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\' && index + 3 < value.Length
                && value.AsSpan(index + 1, 3) is var code)
            {
                var replacement = code.SequenceEqual("040") ? ' '
                    : code.SequenceEqual("011") ? '\t'
                    : code.SequenceEqual("012") ? '\n'
                    : code.SequenceEqual("134") ? '\\' : '\0';
                if (replacement != '\0') { builder.Append(replacement); index += 3; continue; }
            }
            builder.Append(value[index]);
        }
        return builder.ToString();
    }
}
