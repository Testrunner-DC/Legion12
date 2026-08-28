using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed record L12AuditArchiveCommandPayload(DateTimeOffset ArchiveBefore, int RetentionDays);

public sealed record L12AuditArchiveSegmentView(
    string Id,
    DateTimeOffset From,
    DateTimeOffset Until,
    long EventCount,
    string Sha256,
    DateTimeOffset CreatedAt);

public sealed record L12AuditArchiveOperationView(
    bool Applied,
    DateTimeOffset ArchiveBefore,
    int RetentionDays,
    long EligibleEvents,
    L12AuditArchiveSegmentView? Segment,
    bool SourceEventsRetained = true);

public sealed record L12AuditArchiveRecoveryView(
    bool Success,
    int Segments,
    long Events,
    string? Error,
    DateTimeOffset RehearsedAt);

public sealed partial class L12PlatformStore
{
    private sealed record AuditArchiveEvent(string Id, DateTimeOffset CreatedAt, string Payload,
        string PayloadSha256);

    private sealed record AuditArchiveLine(string Id, DateTimeOffset CreatedAt, string PayloadJson,
        string PayloadSha256);

    public L12AuditArchiveCommandPayload CaptureAuditArchive(int? retentionDays = null,
        DateTimeOffset? now = null)
    {
        var days = NormalizeAuditRetentionDays(retentionDays ?? AuditRetentionDays());
        var capturedAt = now ?? DateTimeOffset.UtcNow;
        return new(capturedAt.AddDays(-days), days);
    }

    public IReadOnlyList<L12AuditArchiveSegmentView> AuditArchiveSegments(L12AccountView actor)
    {
        if (!L12Authorization.HasPermission(actor, L12Permission.AdminSecurityRead)
            && !L12Authorization.HasPermission(actor, L12Permission.AdminAuditRead))
            throw new UnauthorizedAccessException("当前账号没有读取审计归档的权限");
        lock (_gate) return AuditArchiveSegmentsInternal();
    }

    public L12AuditArchiveOperationView ArchiveAudit(L12AccountView actor,
        L12AuditArchiveCommandPayload payload, L12AdminAuditContext context, bool apply)
    {
        EnsurePermission(actor, L12Permission.AdminAuditArchive);
        var retentionDays = NormalizeAuditRetentionDays(payload.RetentionDays);
        var now = DateTimeOffset.UtcNow;
        if (payload.ArchiveBefore > now)
            throw new L12SecurityPolicyException("archive_cutoff_invalid", "审计归档截止时间不能晚于当前时间");

        lock (_gate)
        {
            if (!HighRiskAuditAvailable())
                throw new L12SecurityPolicyException("audit_unavailable", "独立审计不可用，归档操作已失败关闭");

            var existing = AuditArchiveSegmentsInternal();
            var alreadyArchived = existing.LastOrDefault(item => item.Until == payload.ArchiveBefore);
            if (apply && alreadyArchived is not null)
                return new(true, payload.ArchiveBefore, retentionDays, alreadyArchived.EventCount,
                    alreadyArchived);
            var from = existing.Count == 0
                ? DateTimeOffset.MinValue
                : existing.Max(item => item.Until);
            var events = ReadAuditArchiveEvents(from, payload.ArchiveBefore);
            if (!apply || events.Count == 0)
                return new(false, payload.ArchiveBefore, retentionDays, events.Count, null);

            var segment = WriteAuditArchiveSegment(from, payload.ArchiveBefore, events);
            AddAdminAudit(actor, "security", "archive-audit", segment.Id, from.ToString("O"),
                payload.ArchiveBefore.ToString("O"), $"events:{events.Count};source-retained:true",
                context with { Outcome = "succeeded", Reason = "checksummed-jsonl-archive" });
            Save(false);
            return new(true, payload.ArchiveBefore, retentionDays, events.Count, segment);
        }
    }

    public L12AuditArchiveRecoveryView RehearseAuditArchiveRecovery(L12AccountView actor)
    {
        if (!L12Authorization.HasPermission(actor, L12Permission.AdminSecurityRead)
            && !L12Authorization.HasPermission(actor, L12Permission.AdminAuditRead))
            throw new UnauthorizedAccessException("当前账号没有演练审计归档恢复的权限");

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            try
            {
                var segments = ReadAuditArchiveSegmentRows();
                long total = 0;
                foreach (var segment in segments)
                {
                    var fileName = Path.GetFileName(segment.FileName);
                    if (!string.Equals(fileName, segment.FileName, StringComparison.Ordinal)
                        || !fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("审计归档元数据包含非法文件名");
                    var path = Path.Combine(AuditArchiveDirectory(), fileName);
                    if (!File.Exists(path)) throw new FileNotFoundException("审计归档段缺失", fileName);
                    var bytes = File.ReadAllBytes(path);
                    if (!FixedEquals(segment.FileSha256, Sha256Bytes(bytes)))
                        throw new InvalidDataException($"审计归档段 {segment.Id} 文件校验和不匹配");

                    long count = 0;
                    foreach (var line in File.ReadLines(path, Encoding.UTF8))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var archived = JsonSerializer.Deserialize<AuditArchiveLine>(line, PlatformSnapshotJsonOptions)
                            ?? throw new InvalidDataException($"审计归档段 {segment.Id} 包含无效记录");
                        if (!FixedEquals(archived.PayloadSha256, Sha256(archived.PayloadJson)))
                            throw new InvalidDataException($"审计归档段 {segment.Id} 事件校验和不匹配");
                        _ = JsonSerializer.Deserialize<JsonElement>(archived.PayloadJson);
                        if (archived.CreatedAt <= segment.From || archived.CreatedAt > segment.Until)
                            throw new InvalidDataException($"审计归档段 {segment.Id} 事件越出声明范围");
                        count++;
                    }
                    if (count != segment.EventCount)
                        throw new InvalidDataException($"审计归档段 {segment.Id} 事件数量不匹配");
                    total += count;
                }
                return new(true, segments.Count, total, null, now);
            }
            catch (Exception error)
            {
                return new(false, AuditArchiveSegmentsInternalSafe().Count, 0, error.Message, now);
            }
        }
    }

    internal static int AuditRetentionDays()
    {
        var raw = Environment.GetEnvironmentVariable("L12_AUDIT_RETENTION_DAYS");
        return int.TryParse(raw, out var parsed) ? NormalizeAuditRetentionDays(parsed) : 365;
    }

    private static int NormalizeAuditRetentionDays(int days) => Math.Clamp(days, 30, 3650);

    private IReadOnlyList<L12AuditArchiveSegmentView> AuditArchiveSegmentsInternal()
        => ReadAuditArchiveSegmentRows().Select(item => new L12AuditArchiveSegmentView(item.Id, item.From,
            item.Until, item.EventCount, item.FileSha256, item.CreatedAt)).ToArray();

    private IReadOnlyList<L12AuditArchiveSegmentView> AuditArchiveSegmentsInternalSafe()
    {
        try { return AuditArchiveSegmentsInternal(); }
        catch { return []; }
    }

    private sealed record AuditArchiveSegmentRow(string Id, DateTimeOffset From, DateTimeOffset Until,
        long EventCount, string FileName, string FileSha256, DateTimeOffset CreatedAt);

    private IReadOnlyList<AuditArchiveSegmentRow> ReadAuditArchiveSegmentRows()
    {
        if (!_storageWritable) return [];
        using var connection = OpenDatabase(_databasePath, readOnly: true);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,from_utc,until_utc,event_count,file_name,file_sha256,created_utc
            FROM audit_archive_segments ORDER BY until_utc,id;
            """;
        using var reader = command.ExecuteReader();
        var result = new List<AuditArchiveSegmentRow>();
        while (reader.Read())
            result.Add(new(reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1)),
                DateTimeOffset.Parse(reader.GetString(2)), reader.GetInt64(3), reader.GetString(4),
                reader.GetString(5), DateTimeOffset.Parse(reader.GetString(6))));
        return result;
    }

    private IReadOnlyList<AuditArchiveEvent> ReadAuditArchiveEvents(DateTimeOffset from,
        DateTimeOffset until)
    {
        using var connection = OpenDatabase(_databasePath, readOnly: true);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,created_utc,payload_json,payload_sha256 FROM admin_audit_events
            WHERE created_utc > $from AND created_utc <= $until
            ORDER BY created_utc,id;
            """;
        command.Parameters.AddWithValue("$from", from.ToString("O"));
        command.Parameters.AddWithValue("$until", until.ToString("O"));
        using var reader = command.ExecuteReader();
        var result = new List<AuditArchiveEvent>();
        while (reader.Read())
        {
            var payload = reader.GetString(2);
            var checksum = reader.GetString(3);
            if (!FixedEquals(checksum, Sha256(payload)))
                throw new InvalidDataException("独立审计事件校验和不匹配，归档已停止");
            result.Add(new(reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1)), payload, checksum));
        }
        return result;
    }

    private long CountArchivableAuditEventsSafe(DateTimeOffset until)
    {
        if (!_storageWritable) return 0;
        try
        {
            using var connection = OpenDatabase(_databasePath, readOnly: true);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM admin_audit_events WHERE created_utc <= $until;";
            command.Parameters.AddWithValue("$until", until.ToString("O"));
            return Convert.ToInt64(command.ExecuteScalar());
        }
        catch { return 0; }
    }

    private L12AuditArchiveSegmentView WriteAuditArchiveSegment(DateTimeOffset from,
        DateTimeOffset until, IReadOnlyList<AuditArchiveEvent> events)
    {
        var id = Guid.NewGuid().ToString("N");
        var createdAt = DateTimeOffset.UtcNow;
        var fileName = $"audit-{until:yyyyMMddHHmmss}-{id}.jsonl";
        var directory = AuditArchiveDirectory();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        var tempPath = path + ".tmp";
        var lines = events.Select(item => JsonSerializer.Serialize(new AuditArchiveLine(item.Id,
            item.CreatedAt, item.Payload, item.PayloadSha256),
            PlatformSnapshotJsonOptions));
        try
        {
            File.WriteAllLines(tempPath, lines, new UTF8Encoding(false));
            File.Move(tempPath, path, false);
            var checksum = Sha256Bytes(File.ReadAllBytes(path));
            using var connection = OpenDatabase(_databasePath, readOnly: false);
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO audit_archive_segments(id,from_utc,until_utc,event_count,file_name,file_sha256,created_utc)
                VALUES($id,$from,$until,$count,$file,$checksum,$created);
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$from", from.ToString("O"));
            command.Parameters.AddWithValue("$until", until.ToString("O"));
            command.Parameters.AddWithValue("$count", events.Count);
            command.Parameters.AddWithValue("$file", fileName);
            command.Parameters.AddWithValue("$checksum", checksum);
            command.Parameters.AddWithValue("$created", createdAt.ToString("O"));
            command.ExecuteNonQuery();
            transaction.Commit();
            return new(id, from, until, events.Count, checksum, createdAt);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { }
            try { File.Delete(path); } catch { }
            throw;
        }
    }

    private string AuditArchiveDirectory()
        => Path.Combine(Path.GetDirectoryName(_databasePath)!, "audit-archives");

    private static string Sha256Bytes(byte[] value)
        => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}
