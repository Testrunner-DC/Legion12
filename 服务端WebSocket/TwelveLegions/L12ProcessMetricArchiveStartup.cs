using System.Diagnostics;
using System.Text.Json.Serialization;

namespace TwelveLegions.Server;

internal enum L12ProcessMetricStartupReason
{
    None,
    InvalidRuntimePath,
    UnsupportedRuntimeLink,
    UnsafePhysicalRuntime,
    InvalidIdentity,
    CollectorRejected,
    StartFailure,
    ShutdownIncomplete,
}

/// <summary>
/// A narrow capability produced only after resolving the deployment's single runtime leaf link.
/// It captures one physical metrics/process root and deliberately exposes no general reparse bypass.
/// </summary>
internal sealed class L12ResolvedProcessMetricArchiveRoot
{
    private L12ResolvedProcessMetricArchiveRoot(string physicalArchivePath) =>
        PhysicalArchivePath = physicalArchivePath;

    [JsonIgnore]
    internal string PhysicalArchivePath { get; }

    internal static bool TryResolve(string logicalRuntimePath,
        out L12ResolvedProcessMetricArchiveRoot? root, out L12ProcessMetricStartupReason reason)
    {
        root = null;
        reason = L12ProcessMetricStartupReason.InvalidRuntimePath;
        try
        {
            if (string.IsNullOrWhiteSpace(logicalRuntimePath)) return false;
            var logical = Path.GetFullPath(logicalRuntimePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(Path.GetFileName(logical), "runtime", PathComparison)) return false;

            var logicalInfo = new DirectoryInfo(logical);
            logicalInfo.Refresh();
            if (!logicalInfo.Exists) return false;

            string physicalRuntime;
            if (!L12ProcessMetricFileArchive.IsProtectedReparsePoint(logicalInfo.Attributes))
            {
                if (HasReparsePointInExistingPath(logical))
                {
                    reason = L12ProcessMetricStartupReason.UnsafePhysicalRuntime;
                    return false;
                }
                physicalRuntime = logical;
            }
            else
            {
                if (HasReparsePointInExistingAncestors(logicalInfo.Parent))
                {
                    reason = L12ProcessMetricStartupReason.UnsafePhysicalRuntime;
                    return false;
                }
                var firstTarget = logicalInfo.LinkTarget;
                if (string.IsNullOrWhiteSpace(firstTarget) || !Path.IsPathFullyQualified(firstTarget))
                {
                    reason = L12ProcessMetricStartupReason.UnsupportedRuntimeLink;
                    return false;
                }

                var resolved = logicalInfo.ResolveLinkTarget(returnFinalTarget: false) as DirectoryInfo;
                logicalInfo.Refresh();
                var secondTarget = logicalInfo.LinkTarget;
                if (resolved is null || !resolved.Exists
                    || string.IsNullOrWhiteSpace(secondTarget)
                    || !string.Equals(firstTarget, secondTarget, PathComparison))
                {
                    reason = L12ProcessMetricStartupReason.UnsupportedRuntimeLink;
                    return false;
                }

                resolved.Refresh();
                physicalRuntime = Path.GetFullPath(resolved.FullName)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var declaredTarget = Path.GetFullPath(firstTarget)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.Equals(physicalRuntime, declaredTarget, PathComparison)
                    || L12ProcessMetricFileArchive.IsProtectedReparsePoint(resolved.Attributes)
                    || HasReparsePointInExistingPath(physicalRuntime))
                {
                    reason = L12ProcessMetricStartupReason.UnsafePhysicalRuntime;
                    return false;
                }
            }

            var physicalInfo = new DirectoryInfo(physicalRuntime);
            if (!physicalInfo.Exists || physicalInfo.Parent is null
                || L12ProcessMetricFileArchive.IsProtectedReparsePoint(physicalInfo.Attributes)
                || HasReparsePointInExistingPath(physicalRuntime))
            {
                reason = L12ProcessMetricStartupReason.UnsafePhysicalRuntime;
                return false;
            }

            var archivePath = Path.GetFullPath(Path.Combine(physicalRuntime, "metrics", "process"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!IsExactChild(physicalRuntime, archivePath, Path.Combine("metrics", "process"))
                || ExistingChildIsReparse(Path.Combine(physicalRuntime, "metrics"))
                || ExistingChildIsReparse(archivePath))
            {
                reason = L12ProcessMetricStartupReason.UnsafePhysicalRuntime;
                return false;
            }

            root = new L12ResolvedProcessMetricArchiveRoot(archivePath);
            reason = L12ProcessMetricStartupReason.None;
            return true;
        }
        catch
        {
            root = null;
            reason = L12ProcessMetricStartupReason.UnsafePhysicalRuntime;
            return false;
        }
    }

    public override string ToString() => nameof(L12ResolvedProcessMetricArchiveRoot);

    private static bool ExistingChildIsReparse(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return false;
        return L12ProcessMetricFileArchive.IsProtectedReparsePoint(File.GetAttributes(path));
    }

    private static bool IsExactChild(string parent, string child, string relative)
    {
        var actual = Path.GetRelativePath(parent, child);
        return string.Equals(actual, relative, PathComparison)
            && !actual.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathFullyQualified(actual);
    }

    private static bool HasReparsePointInExistingPath(string path)
    {
        try
        {
            for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            {
                if (current.Exists
                    && L12ProcessMetricFileArchive.IsProtectedReparsePoint(current.Attributes)) return true;
            }
            return false;
        }
        catch { return true; }
    }

    private static bool HasReparsePointInExistingAncestors(DirectoryInfo? ancestor)
    {
        try
        {
            for (var current = ancestor; current is not null; current = current.Parent)
            {
                if (current.Exists
                    && L12ProcessMetricFileArchive.IsProtectedReparsePoint(current.Attributes)) return true;
            }
            return false;
        }
        catch { return true; }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

internal readonly record struct L12ProcessMetricStartupResult(
    L12ProcessMetricRuntime? Runtime, L12ProcessMetricStartupReason Reason)
{
    internal bool Started => Runtime is not null && Reason == L12ProcessMetricStartupReason.None;
}

internal static class L12ProcessMetricArchiveStartup
{
    internal static L12ProcessMetricStartupResult TryStart(string logicalRuntimePath, string commit,
        Guid instance, Action<L12ProcessMetricArchiveReason>? alert = null)
    {
        if (instance == Guid.Empty) return new(null, L12ProcessMetricStartupReason.InvalidIdentity);
        if (!L12ResolvedProcessMetricArchiveRoot.TryResolve(logicalRuntimePath, out var root, out var reason))
            return new(null, reason);
        try
        {
            var archive = new L12ProcessMetricFileArchive(root!, commit, instance);
            var collector = new L12ProcessMetricCollector(TimeProvider.System,
                L12ProcessMetricProbe.System(), archive,
                new L12ProcessMetricArchiveIdentity(commit, instance.ToString("N")), alert: alert);
            var runtime = new L12ProcessMetricRuntime(collector, archive);
            if (runtime.Start()) return new(runtime, L12ProcessMetricStartupReason.None);
            _ = Task.Run(async () => await archive.DisposeAsync().ConfigureAwait(false));
            return new(null, L12ProcessMetricStartupReason.CollectorRejected);
        }
        catch (ArgumentException)
        {
            return new(null, L12ProcessMetricStartupReason.InvalidIdentity);
        }
        catch
        {
            return new(null, L12ProcessMetricStartupReason.StartFailure);
        }
    }
}

/// <summary>Owns the collector and its sink. A stop attempt has one total caller-supplied budget.</summary>
internal sealed class L12ProcessMetricRuntime
{
    private readonly IL12ProcessMetricCollectorLifecycle _collector;
    private readonly IAsyncDisposable _archive;
    private readonly object _lifecycleGate = new();
    private readonly SemaphoreSlim _stopGate = new(1, 1);
    private Task? _disposeTask;
    private int _state;

    internal L12ProcessMetricRuntime(IL12ProcessMetricCollectorLifecycle collector, IAsyncDisposable archive)
    {
        _collector = collector;
        _archive = archive;
    }

    internal bool Start()
    {
        lock (_lifecycleGate)
        {
            if (_state != 0) return false;
            if (!_collector.Start()) return false;
            _state = 1;
            return true;
        }
    }

    internal async Task<bool> StopAsync(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan) return false;
        var watch = Stopwatch.StartNew();
        if (!await TryEnterWithinAsync(timeout).ConfigureAwait(false)) return false;
        try
        {
            lock (_lifecycleGate)
            {
                if (_state == 3) return true;
                if (_state == 0) return false;
                _state = 2;
            }

            var remaining = Remaining(timeout, watch.Elapsed);
            if (remaining <= TimeSpan.Zero) return false;
            var collectorStop = Task.Run(() => _collector.StopAsync(remaining));
            if (!await CompletesWithinAsync(collectorStop, Remaining(timeout, watch.Elapsed)).ConfigureAwait(false)
                || !await ObserveAsync(collectorStop).ConfigureAwait(false)) return false;

            Task disposeTask;
            lock (_lifecycleGate)
            {
                _disposeTask ??= Task.Run(async () => await _archive.DisposeAsync().ConfigureAwait(false));
                disposeTask = _disposeTask;
            }
            if (!await CompletesWithinAsync(disposeTask, Remaining(timeout, watch.Elapsed)).ConfigureAwait(false)
                || !await ObserveAsync(disposeTask).ConfigureAwait(false)) return false;

            lock (_lifecycleGate) _state = 3;
            return true;
        }
        finally { _stopGate.Release(); }
    }

    public override string ToString() => nameof(L12ProcessMetricRuntime);

    private async Task<bool> TryEnterWithinAsync(TimeSpan timeout)
    {
        try { return await _stopGate.WaitAsync(timeout).ConfigureAwait(false); }
        catch { return false; }
    }

    private static async Task<bool> CompletesWithinAsync(Task task, TimeSpan timeout)
    {
        if (task.IsCompleted) return true;
        if (timeout <= TimeSpan.Zero) return false;
        return await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) == task;
    }

    private static async Task<bool> ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
            return task is not Task<bool> boolean || boolean.Result;
        }
        catch { return false; }
    }

    private static TimeSpan Remaining(TimeSpan total, TimeSpan elapsed) =>
        elapsed >= total ? TimeSpan.Zero : total - elapsed;
}
