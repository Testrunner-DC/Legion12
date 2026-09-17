namespace TwelveLegions.Server;

public sealed record L12StorageVolumeView(string MountPoint, long TotalBytes, long UsedBytes, long FreeBytes);
public sealed record L12StorageCategoryView(string Id, string Label, string Path, long Bytes, bool Available);
public sealed record L12ServerStorageView(DateTimeOffset ObservedAt, int ProcessId, long WorkingSetBytes,
    IReadOnlyList<L12StorageVolumeView> Volumes, IReadOnlyList<L12StorageCategoryView> Categories);

/// <summary>只读、限频的线上容量摘要；不执行 Shell 命令，也不包含任何清理能力。</summary>
internal static class L12ServerStorageMonitor
{
    private static readonly object Gate = new();
    private static L12ServerStorageView? _cached;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    internal static L12ServerStorageView Read()
    {
        lock (Gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (_cached is not null && now - _cached.ObservedAt < CacheLifetime) return _cached;
            var volumes = DriveInfo.GetDrives().Where(drive => drive.IsReady)
                .Select(drive => new L12StorageVolumeView(drive.Name, drive.TotalSize,
                    Math.Max(0, drive.TotalSize - drive.AvailableFreeSpace), drive.AvailableFreeSpace))
                .OrderBy(drive => drive.MountPoint, StringComparer.OrdinalIgnoreCase).ToArray();
            var runtime = Environment.GetEnvironmentVariable("L12_RUNTIME_DIR") ?? "/opt/legion12-runtime";
            var categories = new (string Id, string Label, string Path)[]
            {
                ("matches-db", "对局数据库", Path.Combine(runtime, "matches.db")),
                ("platform-db", "平台数据库", Path.Combine(runtime, "platform.db")),
                ("site-media", "站点媒体", Path.Combine(runtime, "site-media")),
                ("releases", "发布版本", Environment.GetEnvironmentVariable("L12_RELEASES_DIR") ?? "/www/legion12/releases"),
                ("runtime-backups", "运行备份", Environment.GetEnvironmentVariable("L12_RUNTIME_BACKUPS_DIR") ?? "/www/legion12/runtime-backups"),
                ("deployment-backups", "部署遗留备份", "/opt/legion12-deployment/runtime-backups"),
                ("logs", "系统日志", "/var/log"),
            }.Select(item => new L12StorageCategoryView(item.Id, item.Label, item.Path,
                DirectoryBytes(item.Path, out var available), available)).ToArray();
            var process = Environment.ProcessId;
            var workingSet = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
            return _cached = new L12ServerStorageView(now, process, workingSet, volumes, categories);
        }
    }

    private static long DirectoryBytes(string path, out bool available)
    {
        available = false;
        try
        {
            if (File.Exists(path)) { available = true; return new FileInfo(path).Length; }
            if (!Directory.Exists(path)) return 0;
            available = true;
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total = checked(total + new FileInfo(file).Length); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return total;
        }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }
}
