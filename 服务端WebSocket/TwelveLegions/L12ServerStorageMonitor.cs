namespace TwelveLegions.Server;

public sealed record L12StorageVolumeView(string Id, string Label, long TotalBytes, long UsedBytes, long FreeBytes);
public sealed record L12StorageCategoryView(string Id, string Label, long Bytes, bool Available);
public sealed record L12StorageThresholdView(int WarningPercent, int CriticalPercent, string Source);
public sealed record L12StorageTrendPointView(DateTimeOffset ObservedAt, long WorkingSetBytes,
    IReadOnlyDictionary<string, int> VolumeUsedPercent);
public sealed record L12ServerStorageView(DateTimeOffset ObservedAt, long WorkingSetBytes,
    IReadOnlyList<L12StorageVolumeView> Volumes, IReadOnlyList<L12StorageCategoryView> Categories,
    string Health, string Conclusion, string Impact, string RecommendedAction,
    L12StorageThresholdView Thresholds, IReadOnlyList<L12StorageTrendPointView> Trend,
    string TrendScope, string TrendDescription, string SampleState, int UnavailableSourceCount);

internal sealed record L12StorageVolumeSample(long TotalBytes, long FreeBytes);
internal sealed record L12StorageVolumeProbeResult(
    IReadOnlyList<L12StorageVolumeSample> Volumes, int UnavailableSourceCount);
internal sealed record L12ServerStorageProbe(
    Func<DateTimeOffset> UtcNow,
    Func<L12StorageVolumeProbeResult> ReadVolumes,
    Func<string, (long Bytes, bool Available)> ReadCategory,
    Func<long> ReadWorkingSet);

/// <summary>只读、限频的线上容量摘要；不执行 Shell 命令，也不包含任何清理能力。</summary>
internal static class L12ServerStorageMonitor
{
    private static readonly object Gate = new();
    private static L12ServerStorageView? _cached;
    private static readonly List<L12StorageTrendPointView> Trend = [];
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    internal static L12ServerStorageView Read(L12ServerStorageProbe? probe = null)
    {
        lock (Gate)
        {
            var usesSystemProbe = probe is null;
            probe ??= SystemProbe();
            var now = SafeRead(probe.UtcNow, DateTimeOffset.UtcNow, out _);
            if (usesSystemProbe && _cached is not null && now - _cached.ObservedAt < CacheLifetime) return _cached;

            var unavailable = 0;
            var volumeProbe = SafeRead(probe.ReadVolumes,
                new L12StorageVolumeProbeResult([], 0), out var volumesFailed);
            unavailable += Math.Max(0, volumeProbe.UnavailableSourceCount) + (volumesFailed ? 1 : 0);
            var volumes = volumeProbe.Volumes.Select((volume, index) =>
            {
                var total = Math.Max(0, volume.TotalBytes);
                var free = Math.Clamp(volume.FreeBytes, 0, total);
                return new L12StorageVolumeView($"volume-{index + 1}",
                    index == 0 ? "系统卷" : $"存储卷 {index + 1}", total, total - free, free);
            }).ToArray();
            if (volumes.Length == 0 && unavailable == 0) unavailable++;

            var runtime = Environment.GetEnvironmentVariable("L12_RUNTIME_DIR") ?? "/opt/legion12-runtime";
            var categoryDefinitions = new (string Id, string Label, string Path)[]
            {
                ("matches-db", "对局数据库", Path.Combine(runtime, "matches.db")),
                ("platform-db", "平台数据库", Path.Combine(runtime, "platform.db")),
                ("site-media", "站点媒体", Path.Combine(runtime, "site-media")),
                ("releases", "发布版本", Environment.GetEnvironmentVariable("L12_RELEASES_DIR") ?? "/www/legion12/releases"),
                ("runtime-backups", "运行备份", Environment.GetEnvironmentVariable("L12_RUNTIME_BACKUPS_DIR") ?? "/www/legion12/runtime-backups"),
                ("deployment-backups", "部署遗留备份", "/opt/legion12-deployment/runtime-backups"),
                ("logs", "系统日志", "/var/log"),
            };
            var categories = categoryDefinitions.Select(item =>
            {
                var result = SafeRead(() => probe.ReadCategory(item.Path), (0L, false), out var failed);
                if (failed || !result.Item2) unavailable++;
                return new L12StorageCategoryView(item.Id, item.Label, Math.Max(0, result.Item1), result.Item2);
            }).ToArray();
            var workingSet = SafeRead(probe.ReadWorkingSet, 0L, out var workingSetFailed);
            if (workingSetFailed) unavailable++;
            workingSet = Math.Max(0, workingSet);

            var peak = volumes.Select(VolumeUsedPercent).DefaultIfEmpty(0).Max();
            var sampleState = volumes.Length == 0 ? "unavailable" : unavailable > 0 ? "partial" : "complete";
            var health = volumes.Length == 0 ? "unknown" : peak >= 90 ? "critical" : peak >= 80 ? "warning" : "healthy";
            var partialSuffix = sampleState == "partial" ? "（部分来源不可用）" : string.Empty;
            var conclusion = health switch
            {
                "critical" => $"存储容量紧张，最高卷已使用 {peak}%{partialSuffix}",
                "warning" => $"存储容量需要关注，最高卷已使用 {peak}%{partialSuffix}",
                "healthy" => $"存储容量正常，最高卷已使用 {peak}%{partialSuffix}",
                _ => "存储容量暂不可判定",
            };
            var impact = health == "critical" ? "继续增长可能影响对局记录、媒体上传与发布备份。"
                : health == "warning" ? "短期仍可运行，但应在下一次运营窗口检查增长来源。"
                : health == "unknown" ? "当前采样没有可用卷信息，不能据此判断容量风险。"
                : sampleState == "partial" ? "已读取的卷没有容量风险；未读取来源仍需复核。"
                : "当前没有容量导致的服务风险。";
            var action = health == "critical" ? "立即确认增长最快的分类并安排扩容或受控清理。"
                : health == "warning" ? "检查增长趋势与占用分类，提前安排容量。"
                : health == "unknown" ? "使用本次请求的关联 ID 检查服务日志，并重新采样。"
                : sampleState == "partial" ? "检查不可用来源后重新采样。"
                : "保持观察，无需处置。";

            var point = new L12StorageTrendPointView(now, workingSet,
                volumes.ToDictionary(volume => volume.Id, VolumeUsedPercent, StringComparer.Ordinal));
            IReadOnlyList<L12StorageTrendPointView> trend;
            if (usesSystemProbe)
            {
                Trend.Add(point);
                Trend.RemoveAll(item => item.ObservedAt < now.AddHours(-24));
                if (Trend.Count > 120) Trend.RemoveRange(0, Trend.Count - 120);
                trend = Trend.ToArray();
            }
            else trend = [point];

            var result = new L12ServerStorageView(now, workingSet, volumes, categories, health,
                conclusion, impact, action, new L12StorageThresholdView(80, 90, "服务端容量治理策略"), trend,
                "current-process", "本次服务进程内的真实采样，重启后重新累计，最多保留 24 小时 / 120 个样本",
                sampleState, unavailable);
            if (usesSystemProbe) _cached = result;
            return result;
        }
    }

    private static L12ServerStorageProbe SystemProbe() => new(
        () => DateTimeOffset.UtcNow,
        ReadSystemVolumes,
        path => (DirectoryBytes(path, out var available), available),
        () => System.Diagnostics.Process.GetCurrentProcess().WorkingSet64);

    private static L12StorageVolumeProbeResult ReadSystemVolumes()
    {
        var volumes = new List<L12StorageVolumeSample>();
        var unavailable = 0;
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch (Exception error) when (IsStorageAccessFailure(error)) { return new([], 1); }
        foreach (var drive in drives.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!drive.IsReady) { unavailable++; continue; }
                volumes.Add(new(drive.TotalSize, drive.AvailableFreeSpace));
            }
            catch (Exception error) when (IsStorageAccessFailure(error)) { unavailable++; }
        }
        return new(volumes, unavailable);
    }

    private static long DirectoryBytes(string path, out bool available)
    {
        available = false;
        try
        {
            if (File.Exists(path)) { available = true; return Math.Max(0, new FileInfo(path).Length); }
            if (!Directory.Exists(path)) return 0;
            available = true;
            long total = 0;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            foreach (var file in Directory.EnumerateFiles(path, "*", options))
            {
                try { total = checked(total + Math.Max(0, new FileInfo(file).Length)); }
                catch (Exception error) when (IsStorageAccessFailure(error)) { }
            }
            return total;
        }
        catch (Exception error) when (IsStorageAccessFailure(error))
        {
            available = false;
            return 0;
        }
    }

    private static int VolumeUsedPercent(L12StorageVolumeView volume)
        => volume.TotalBytes <= 0 ? 0 : (int)Math.Clamp(
            Math.Round(volume.UsedBytes * 100d / volume.TotalBytes), 0, 100);

    private static T SafeRead<T>(Func<T> read, T fallback, out bool failed)
    {
        try { failed = false; return read(); }
        catch (Exception error) when (IsStorageAccessFailure(error)) { failed = true; return fallback; }
    }

    private static bool IsStorageAccessFailure(Exception error)
        => error is IOException or UnauthorizedAccessException or System.Security.SecurityException
            or NotSupportedException or ArgumentException or OverflowException;
}
