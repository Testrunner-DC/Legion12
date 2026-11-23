namespace TwelveLegions.Server;

public sealed record L12StorageVolumeView(string Id, string Label, long TotalBytes, long UsedBytes, long FreeBytes,
    string MountPoint);

// A category is either measured (Ok) or refused for a reason the operator can act on. Collapsing
// them into "0 B / available=false" made a permission problem look like an empty directory.
public enum L12StorageCategoryState { Ok, Missing, Denied, Timeout }

public sealed record L12StorageCategoryView(string Id, string Label, long Bytes, string State,
    string? VolumeMount);
public sealed record L12StorageCategorySample(long Bytes, L12StorageCategoryState State);
public sealed record L12StorageThresholdView(int WarningPercent, int CriticalPercent, string Source);
public sealed record L12StorageTrendPointView(DateTimeOffset ObservedAt, long WorkingSetBytes,
    IReadOnlyDictionary<string, int> VolumeUsedPercent);
public sealed record L12ServerStorageView(DateTimeOffset ObservedAt, long WorkingSetBytes,
    IReadOnlyList<L12StorageVolumeView> Volumes, IReadOnlyList<L12StorageCategoryView> Categories,
    string Health, string Conclusion, string Impact, string RecommendedAction,
    L12StorageThresholdView Thresholds, IReadOnlyList<L12StorageTrendPointView> Trend,
    string TrendScope, string TrendDescription, string SampleState, int UnavailableSourceCount);

internal sealed record L12StorageVolumeSample(long TotalBytes, long FreeBytes, string MountPoint);
internal sealed record L12StorageVolumeProbeResult(
    IReadOnlyList<L12StorageVolumeSample> Volumes, int UnavailableSourceCount);
internal sealed record L12StorageMountSample(string Identity, string MountPoint, string FileSystemType,
    bool ReadOnly, bool HasUnverifiedQuota, L12StorageVolumeSample? Capacity);
internal sealed record L12StorageDirectoryProbe(
    Func<string, bool> FileExists,
    Func<string, bool> DirectoryExists,
    Func<string, IReadOnlyList<string>> ReadEntries,
    Func<string, FileAttributes> ReadAttributes,
    Func<string, long> ReadLength);
internal sealed record L12ServerStorageProbe(
    Func<DateTimeOffset> UtcNow,
    Func<L12StorageVolumeProbeResult> ReadVolumes,
    Func<string, L12StorageCategorySample> ReadCategory,
    Func<long> ReadWorkingSet);

/// <summary>只读、限频的线上容量摘要；不执行 Shell 命令，也不包含任何清理能力。</summary>
internal static class L12ServerStorageMonitor
{
    private static readonly object Gate = new();
    private static L12ServerStorageView? _cached;
    private static readonly List<L12StorageTrendPointView> Trend = [];
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);
    private static readonly HashSet<string> SupportedLinuxFileSystems = new(StringComparer.Ordinal)
    {
        "ext2", "ext3", "ext4", "xfs", "btrfs", "zfs", "f2fs", "jfs", "nilfs2", "bcachefs",
        "fuseblk", "ntfs3",
    };

    internal static L12ServerStorageView Read(L12ServerStorageProbe? probe = null)
    {
        lock (Gate)
        {
            var categoryDefinitions = CategoryDefinitions();
            var usesSystemProbe = probe is null;
            probe ??= SystemProbe(categoryDefinitions.Select(item => item.Path).ToArray());
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
                    index == 0 ? "系统卷" : $"存储卷 {index + 1}", total, total - free, free,
                    volume.MountPoint);
            }).ToArray();
            if (volumes.Length == 0 && unavailable == 0) unavailable++;

            var categories = categoryDefinitions.Select(item =>
            {
                var result = SafeRead(() => probe.ReadCategory(item.Path),
                    new L12StorageCategorySample(0, L12StorageCategoryState.Denied), out var failed);
                var state = failed ? L12StorageCategoryState.Denied : result.State;
                if (state != L12StorageCategoryState.Ok) unavailable++;
                var bytes = state == L12StorageCategoryState.Ok ? Math.Max(0, result.Bytes) : 0;
                return new L12StorageCategoryView(item.Id, item.Label, bytes, WireState(state),
                    ResolveVolumeMount(item.Path, volumes));
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

    // L12_STORAGE_CATEGORIES (JSON array of {id,label,path}) overrides the defaults so a deployment
    // layout change does not need a code change. The default set deliberately has no /var/log entry:
    // 2026-11-23 measured the service account reading /var/log fine (416 MB) while this service logs
    // to journald, so that row only added unrelated noise to a capacity page.
    private static (string Id, string Label, string Path)[] CategoryDefinitions()
    {
        var configured = Environment.GetEnvironmentVariable("L12_STORAGE_CATEGORIES");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var parsed = ParseConfiguredCategories(configured);
            if (parsed.Length > 0) return parsed;
        }
        var runtime = Environment.GetEnvironmentVariable("L12_RUNTIME_DIR") ?? "/opt/legion12-runtime";
        return
        [
            ("matches-db", "对局数据库", Path.Combine(runtime, "matches.db")),
            ("platform-db", "平台数据库", Path.Combine(runtime, "platform.db")),
            ("site-media", "站点媒体", Path.Combine(runtime, "site-media")),
            ("releases", "发布版本", Environment.GetEnvironmentVariable("L12_RELEASES_DIR") ?? "/www/legion12/releases"),
            ("runtime-backups", "运行备份", Environment.GetEnvironmentVariable("L12_RUNTIME_BACKUPS_DIR") ?? "/www/legion12/runtime-backups"),
            ("deployment-backups", "部署遗留备份", "/opt/legion12-deployment/runtime-backups"),
        ];
    }

    internal static (string Id, string Label, string Path)[] ParseConfiguredCategories(string json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return [];
            var result = new List<(string Id, string Label, string Path)>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                var id = element.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
                var path = element.TryGetProperty("path", out var pathValue) ? pathValue.GetString() : null;
                var label = element.TryGetProperty("label", out var labelValue) ? labelValue.GetString() : null;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(path)) continue;
                if (result.Any(item => string.Equals(item.Id, id, StringComparison.Ordinal))) continue;
                result.Add((id.Trim(), string.IsNullOrWhiteSpace(label) ? id.Trim() : label!.Trim(), path.Trim()));
            }
            return result.ToArray();
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private static string WireState(L12StorageCategoryState state) => state switch
    {
        L12StorageCategoryState.Ok => "ok",
        L12StorageCategoryState.Missing => "missing",
        L12StorageCategoryState.Timeout => "timeout",
        _ => "denied",
    };

    // The volume a category belongs to is decided here, once, so the page never builds paths.
    private static string? ResolveVolumeMount(string path, IReadOnlyList<L12StorageVolumeView> volumes)
    {
        var windows = OperatingSystem.IsWindows();
        var normalized = windows ? path.Replace('/', '\\') : NormalizeLinuxPath(path);
        string? best = null;
        foreach (var volume in volumes)
        {
            var mount = windows ? volume.MountPoint.Replace('/', '\\') : NormalizeLinuxPath(volume.MountPoint);
            if (mount.Length == 0) continue;
            var matches = windows
                ? normalized.StartsWith(mount, StringComparison.OrdinalIgnoreCase)
                : mount == "/" || normalized == mount
                    || normalized.StartsWith(mount + "/", StringComparison.Ordinal);
            if (!matches) continue;
            if (best is null || mount.Length > best.Length) best = volume.MountPoint;
        }
        return best;
    }

    private static L12ServerStorageProbe SystemProbe(IReadOnlyList<string> businessPaths) => new(
        () => DateTimeOffset.UtcNow,
        () => ReadSystemVolumes(businessPaths),
        path =>
        {
            var measured = DirectoryBytes(path);
            return new L12StorageCategorySample(measured.Bytes, measured.State);
        },
        () => System.Diagnostics.Process.GetCurrentProcess().WorkingSet64);

    private static L12StorageVolumeProbeResult ReadSystemVolumes(IReadOnlyList<string> businessPaths)
    {
        if (OperatingSystem.IsLinux()) return ReadLinuxVolumes(businessPaths);
        if (OperatingSystem.IsWindows()) return ReadWindowsVolumes();
        return new([], 1);
    }

    private static L12StorageVolumeProbeResult ReadWindowsVolumes()
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
                volumes.Add(new(drive.TotalSize, drive.AvailableFreeSpace, drive.Name));
            }
            catch (Exception error) when (IsStorageAccessFailure(error)) { unavailable++; }
        }
        return new(volumes, unavailable);
    }

    private static L12StorageVolumeProbeResult ReadLinuxVolumes(IReadOnlyList<string> businessPaths)
    {
        string[] mountInfo;
        DriveInfo[] drives;
        try
        {
            mountInfo = File.ReadAllLines("/proc/self/mountinfo");
            drives = DriveInfo.GetDrives();
        }
        catch (Exception error) when (IsStorageAccessFailure(error)) { return new([], 1); }

        var driveByMount = drives
            .GroupBy(drive => NormalizeLinuxPath(drive.Name), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var mounts = ParseLinuxMountInfo(mountInfo, businessPaths, mountPoint =>
        {
            if (!driveByMount.TryGetValue(mountPoint, out var drive)) return null;
            try
            {
                if (!drive.IsReady) return null;
                return new L12StorageVolumeSample(drive.TotalSize, drive.AvailableFreeSpace, mountPoint);
            }
            catch (Exception error) when (IsStorageAccessFailure(error)) { return null; }
        });
        return SelectLinuxBusinessVolumes(mounts, businessPaths);
    }

    internal static IReadOnlyList<L12StorageMountSample> ParseLinuxMountInfo(IEnumerable<string> lines,
        IReadOnlyList<string> businessPaths, Func<string, L12StorageVolumeSample?> readCapacity)
    {
        var result = new List<L12StorageMountSample>();
        foreach (var line in lines)
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var separator = Array.IndexOf(fields, "-");
            if (separator < 6 || separator + 3 >= fields.Length) continue;
            var mountPoint = NormalizeLinuxPath(DecodeMountInfoPath(fields[4]));
            var readOnly = HasMountOption(fields[5], "ro") || HasMountOption(fields[separator + 3], "ro");
            var fileSystemType = fields[separator + 1];
            var capacityRelevant = SupportedLinuxFileSystems.Contains(fileSystemType)
                && businessPaths.Any(path => IsPathWithinMount(NormalizeLinuxPath(path), mountPoint));
            var hasUnverifiedQuota = HasQuotaOption(fields[5]) || HasQuotaOption(fields[separator + 3]);
            result.Add(new(fields[2], mountPoint, fileSystemType, readOnly, hasUnverifiedQuota,
                capacityRelevant ? readCapacity(mountPoint) : null));
        }
        return result;
    }

    internal static L12StorageVolumeProbeResult SelectLinuxBusinessVolumes(
        IReadOnlyList<L12StorageMountSample> mounts, IReadOnlyList<string> businessPaths)
    {
        var selected = new Dictionary<string, L12StorageMountSample>(StringComparer.Ordinal);
        var unavailable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawPath in businessPaths.Distinct(StringComparer.Ordinal))
        {
            var path = NormalizeLinuxPath(rawPath);
            var mount = mounts.Where(candidate => IsPathWithinMount(path, candidate.MountPoint))
                .OrderByDescending(candidate => candidate.MountPoint.Length)
                .FirstOrDefault();
            if (mount is null)
            {
                unavailable.Add($"path:{path}");
                continue;
            }
            if (!SupportedLinuxFileSystems.Contains(mount.FileSystemType)
                || mount.Capacity is null || mount.Capacity.TotalBytes <= 0)
            {
                unavailable.Add($"mount:{mount.Identity}");
                continue;
            }
            selected.TryAdd(mount.Identity, mount);
            if (mount.ReadOnly || mount.HasUnverifiedQuota) unavailable.Add($"mount:{mount.Identity}");
        }
        return new(selected.Values.OrderBy(item => item.MountPoint, StringComparer.Ordinal)
            .Select(item => item.Capacity!).ToArray(), unavailable.Count);
    }

    private static bool HasMountOption(string options, string expected)
        => options.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(expected, StringComparer.Ordinal);

    private static bool HasQuotaOption(string options)
        => options.Split(',', StringSplitOptions.RemoveEmptyEntries).Any(option =>
            option is "quota" or "usrquota" or "grpquota" or "prjquota" or "uquota" or "gquota" or "pquota"
                || option.StartsWith("usrjquota=", StringComparison.Ordinal)
                || option.StartsWith("grpjquota=", StringComparison.Ordinal)
                || option.StartsWith("jqfmt=", StringComparison.Ordinal));

    private static bool IsPathWithinMount(string path, string mountPoint)
        => mountPoint == "/" || string.Equals(path, mountPoint, StringComparison.Ordinal)
            || path.StartsWith(mountPoint + "/", StringComparison.Ordinal);

    private static string NormalizeLinuxPath(string path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? "/" : path;
        while (normalized.Contains("//", StringComparison.Ordinal))
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }

    private static string DecodeMountInfoPath(string value)
        => value.Replace("\\040", " ", StringComparison.Ordinal)
            .Replace("\\011", "\t", StringComparison.Ordinal)
            .Replace("\\012", "\n", StringComparison.Ordinal)
            .Replace("\\134", "\\", StringComparison.Ordinal);

    private static L12StorageDirectoryProbe SystemDirectoryProbe() => new(
        File.Exists,
        Directory.Exists,
        path => Directory.GetFileSystemEntries(path, "*", new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
        }),
        File.GetAttributes,
        path => new FileInfo(path).Length);

    internal static (long Bytes, L12StorageCategoryState State) DirectoryBytes(string path,
        L12StorageDirectoryProbe? fileSystem = null)
    {
        fileSystem ??= SystemDirectoryProbe();
        try
        {
            var rootIsFile = fileSystem.FileExists(path);
            var rootIsDirectory = !rootIsFile && fileSystem.DirectoryExists(path);
            if (!rootIsFile && !rootIsDirectory) return (0, L12StorageCategoryState.Missing);
            var rootAttributes = fileSystem.ReadAttributes(path);
            if ((rootAttributes & FileAttributes.ReparsePoint) != 0) return (0, L12StorageCategoryState.Denied);
            if (rootIsFile) return (Math.Max(0, fileSystem.ReadLength(path)), L12StorageCategoryState.Ok);
            if ((rootAttributes & FileAttributes.Directory) == 0) return (0, L12StorageCategoryState.Denied);

            long total = 0;
            var complete = true;
            var pending = new Stack<string>();
            pending.Push(path);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                IReadOnlyList<string> entries;
                try { entries = fileSystem.ReadEntries(current); }
                catch (Exception error) when (IsStorageAccessFailure(error))
                {
                    complete = false;
                    continue;
                }
                foreach (var entry in entries)
                {
                    try
                    {
                        var attributes = fileSystem.ReadAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                        else total = checked(total + Math.Max(0, fileSystem.ReadLength(entry)));
                    }
                    catch (Exception error) when (IsStorageAccessFailure(error)) { complete = false; }
                }
            }
            return (total, complete ? L12StorageCategoryState.Ok : L12StorageCategoryState.Denied);
        }
        catch (Exception error) when (IsStorageAccessFailure(error))
        {
            var denied = error is UnauthorizedAccessException or System.Security.SecurityException;
            return (0, denied ? L12StorageCategoryState.Denied : L12StorageCategoryState.Missing);
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
