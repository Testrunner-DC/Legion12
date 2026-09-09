using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

internal sealed record L12AuditLifecycleResult(int Archived, int ExpiredSegments, int Scanned);

public sealed partial class L12PlatformStore
{
    internal Action<string>? AuditLifecycleFailureInjector { get; set; }
    private const int AuditLifecycleBatch = 100;
    private const int AuditLifecyclePayloadLimit = 256 * 1024;

    private static void InitializeAuditLifecycleSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS audit_lifecycle_segments(
                id TEXT PRIMARY KEY,file_name TEXT NOT NULL UNIQUE,file_sha256 TEXT NOT NULL,
                event_count INTEGER NOT NULL,from_utc TEXT NOT NULL,until_utc TEXT NOT NULL,
                created_utc TEXT NOT NULL,status TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_audit_lifecycle_status_until
                ON audit_lifecycle_segments(status,until_utc,id);
            CREATE TABLE IF NOT EXISTS admin_audit_migrations(event_id TEXT PRIMARY KEY) WITHOUT ROWID;
            CREATE TABLE IF NOT EXISTS audit_legacy_expiration_intents(segment_id TEXT PRIMARY KEY) WITHOUT ROWID;
            CREATE TRIGGER IF NOT EXISTS prevent_migrated_audit_reinsert
            BEFORE INSERT ON admin_audit_events
            WHEN EXISTS(SELECT 1 FROM admin_audit_migrations WHERE event_id=NEW.id)
            BEGIN SELECT RAISE(IGNORE); END;
            """;
        command.ExecuteNonQuery();
    }

    // Exact, compact IDs only: no payload or second archived copy. Unlike a timestamp
    // cutoff this does not discard unrelated late-arriving historical audit events.
    private static void FilterMigratedAuditSnapshot(SqliteConnection connection, DataFile data)
    {
        if (data.AdminAudit.Count == 0) return;
        using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT event_id FROM admin_audit_migrations
            WHERE event_id IN (SELECT value FROM json_each($ids));
            """;
        query.Parameters.AddWithValue("$ids", JsonSerializer.Serialize(data.AdminAudit.Select(row => row.Id)));
        var removed = new HashSet<string>(StringComparer.Ordinal);
        using (var reader = query.ExecuteReader())
            while (reader.Read()) removed.Add(reader.GetString(0));
        data.AdminAudit.RemoveAll(row => removed.Contains(row.Id));
    }

    internal L12AuditLifecycleResult RunAuditLifecycle(DateTimeOffset now,
        IReadOnlyCollection<string> protectedMatchIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(protectedMatchIds);
        cancellationToken.ThrowIfCancellationRequested();
        // Do not queue a long wait behind a player/admin mutation for background cleanup.
        if (!Monitor.TryEnter(_gate)) return new(0, 0, 0);
        try
        {
            if (!_storageWritable) throw new L12PlatformStorageUnavailableException("审计清理需要可写事务存储");
            using var connection = OpenDatabase(_databasePath, readOnly: false);
            var protects = BuildAuditProtection(protectedMatchIds,cancellationToken);
            var expired = MaintainAuditLifecycleSegments(connection, now, protects, cancellationToken);
            expired += ExpireLegacyAuditSegments(connection, now, protects, cancellationToken);
            var archived = 0;
            var scannedCount = 0;
            try
            {
                for (var batch = 0; batch < 10; batch++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var cursor = ReadMeta(connection, "audit_lifecycle_scan_rowid") ?? "0";
                    using var query = connection.CreateCommand();
                    query.CommandText = """
                        SELECT rowid,id,created_utc,
                          CASE WHEN length(payload_json)<=$max THEN payload_json ELSE NULL END,payload_sha256
                        FROM admin_audit_events WHERE rowid > CAST($cursor AS INTEGER)
                        ORDER BY rowid LIMIT $limit;
                        """;
                    query.Parameters.AddWithValue("$cursor", cursor);
                    query.Parameters.AddWithValue("$max", AuditLifecyclePayloadLimit);
                    query.Parameters.AddWithValue("$limit", AuditLifecycleBatch);
                    var eligible = new List<AuditArchiveEvent>();
                    var scanned = 0;
                    var lastRow = cursor;
                    using (var reader = query.ExecuteReader())
                        while (reader.Read())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            scanned++;
                            lastRow = reader.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture);
                            if (reader.IsDBNull(3)) continue; // oversized evidence is not silently discarded
                            var created = DateTimeOffset.Parse(reader.GetString(2));
                            if (created >= now.AddDays(-30)) continue;
                            var payload = reader.GetString(3);
                            var hash = reader.GetString(4);
                            if (!FixedEquals(hash, Sha256(payload)))
                                throw new InvalidDataException("审计源事件校验失败，保留原始记录");
                            var body = JsonSerializer.Deserialize<AdminAuditRow>(payload, PlatformSnapshotJsonOptions)
                                ?? throw new InvalidDataException("审计源正文无法解析");
                            if (body.Id != reader.GetString(1) || body.CreatedAt != created)
                                throw new InvalidDataException("审计源身份或时间与正文不一致，保留原始记录");
                            if (!protects(payload)) eligible.Add(new(reader.GetString(1), created, payload, hash));
                        }
                    if (eligible.Count > 0)
                    {
                        ArchiveAndMoveAuditBatch(connection, eligible.ToArray(), now, cancellationToken);
                        archived += eligible.Count;
                    }
                    using var progress = connection.CreateCommand();
                    progress.CommandText = """
                        INSERT INTO storage_meta(key,value) VALUES('audit_lifecycle_scan_rowid',$cursor)
                        ON CONFLICT(key) DO UPDATE SET value=excluded.value;
                        """;
                    progress.Parameters.AddWithValue("$cursor", scanned < AuditLifecycleBatch ? "0" : lastRow);
                    progress.ExecuteNonQuery();
                    scannedCount += scanned;
                    if (scanned < AuditLifecycleBatch) break;
                }
            }
            finally
            {
                // One mirror write per window, not one full-platform rewrite per source batch.
                // A crash before this point is fenced by exact migration IDs on load and INSERT.
                if (archived > 0) Save(false);
            }
            Console.WriteLine($"Audit lifecycle: scanned={scannedCount};archived={archived};expiredSegments={expired}");
            return new(archived, expired, scannedCount);
        }
        finally { Monitor.Exit(_gate); }
    }

    private Func<string, bool> BuildAuditProtection(IReadOnlyCollection<string> protectedMatchIds,CancellationToken token)
    {
        var references = new HashSet<string>(protectedMatchIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
        var diagnostics = new List<string>();
        foreach (var bug in _data.BugReports.Where(b => b.Status is not ("resolved" or "closed")))
        {
            token.ThrowIfCancellationRequested();
            foreach (var value in new[] { bug.Id, bug.MatchId, bug.RoomCode })
                if (!string.IsNullOrWhiteSpace(value)) references.Add(value);
            diagnostics.Add(JsonSerializer.Serialize(bug, PlatformSnapshotJsonOptions));
        }
        foreach (var incident in _data.PlayerMatchReports.Where(r => r.Status is not ("resolved" or "closed")))
            foreach (var value in new[] { incident.Id, incident.MatchId, incident.RoomCode })
                if (!string.IsNullOrWhiteSpace(value)) references.Add(value);
        foreach (var incident in _data.MatchDrawRequests.Where(r => r.AdminStatus is not ("resolved" or "closed")))
            foreach (var value in new[] { incident.Id, incident.MatchId, incident.RoomCode })
                if (!string.IsNullOrWhiteSpace(value)) references.Add(value);
        var commands = _data.AdminCommands.ToDictionary(c => c.Id, StringComparer.Ordinal);
        return payload =>
        {
            token.ThrowIfCancellationRequested();
            var row = JsonSerializer.Deserialize<AdminAuditRow>(payload, PlatformSnapshotJsonOptions)
                ?? throw new InvalidDataException("审计事件无法解析");
            if (references.Any(value => payload.Contains(value, StringComparison.Ordinal))) return true;
            if (diagnostics.Any(text => new[] { row.Id, row.CommandId, row.CorrelationId, row.Target }.Any(value =>
                !string.IsNullOrWhiteSpace(value) && text.Contains(value, StringComparison.Ordinal)))) return true;
            if (!string.IsNullOrEmpty(row.CommandId)
                && (!commands.TryGetValue(row.CommandId, out var command) || command.Status is not ("executed" or "rejected" or "cancelled")))
                return true;
            // Unknown incident/reconciliation status is not evidence that it is safe to purge.
            return row.Category is "ranked" or "release" || row.Outcome is "failed" or "denied";
        };
    }

    private string LifecyclePath(string fileName)
    {
        if (Path.GetFileName(fileName) != fileName || !fileName.StartsWith("lifecycle-", StringComparison.Ordinal)
            || !fileName.EndsWith(".jsonl.gz", StringComparison.Ordinal))
            throw new InvalidDataException("非法审计归档文件名");
        var directory = AuditArchiveDirectory();
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("审计归档目录不能是重定向链接");
        var path = Path.Combine(directory, fileName);
        foreach (var candidate in new[] { path, path + ".tmp" })
            if (File.Exists(candidate) && (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("审计归档文件不能是重定向链接");
        return path;
    }

    private void ArchiveAndMoveAuditBatch(SqliteConnection connection, AuditArchiveEvent[] events,
        DateTimeOffset now, CancellationToken token)
    {
        var id = Guid.NewGuid().ToString("N");
        var file = $"lifecycle-{id}.jsonl.gz";
        var path = LifecyclePath(file);
        var from = events.Min(e => e.CreatedAt).ToUniversalTime().AddTicks(-1);
        var until = events.Max(e => e.CreatedAt).ToUniversalTime();
        using (var reserve = connection.CreateCommand())
        {
            reserve.CommandText = "INSERT INTO audit_lifecycle_segments VALUES($id,$file,'',0,$from,$until,$now,'pending');";
            reserve.Parameters.AddWithValue("$id", id);
            reserve.Parameters.AddWithValue("$file", file);
            reserve.Parameters.AddWithValue("$from", from.ToString("O"));
            reserve.Parameters.AddWithValue("$until", until.ToString("O"));
            reserve.Parameters.AddWithValue("$now", now.ToUniversalTime().ToString("O"));
            reserve.ExecuteNonQuery();
        }
        // Pending reservation owns these two exact files. Source rows remain until commit.
        using (var stream = new FileStream(path + ".tmp", FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path + ".tmp", UnixFileMode.UserRead | UnixFileMode.UserWrite);
            using (var gzip = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true))
            using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
                foreach (var item in events)
                {
                    token.ThrowIfCancellationRequested();
                    writer.WriteLine(JsonSerializer.Serialize(new AuditArchiveLine(item.Id, item.CreatedAt,
                        item.Payload, item.PayloadSha256), PlatformSnapshotJsonOptions));
                }
            stream.Flush(flushToDisk: true);
        }
        File.Move(path + ".tmp", path, overwrite: false);
        var hash = HashAuditFile(path, token);
        ValidateLifecycleFile(file, hash, events.Length, from, until, token);
        AuditLifecycleFailureInjector?.Invoke("after-file-verified");
        token.ThrowIfCancellationRequested();
        using (var transaction = connection.BeginTransaction())
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                DELETE FROM admin_audit_events WHERE id=$id AND payload_sha256=$hash;
                """;
            var pId = command.Parameters.Add("$id", SqliteType.Text);
            var pHash = command.Parameters.Add("$hash", SqliteType.Text);
            foreach (var item in events)
            {
                pId.Value = item.Id;
                pHash.Value = item.PayloadSha256;
                if (command.ExecuteNonQuery() != 1) throw new InvalidDataException("审计源记录发生变化，取消迁出");
                using var mark = connection.CreateCommand();
                mark.Transaction = transaction;
                mark.CommandText = "INSERT INTO admin_audit_migrations(event_id) VALUES($id);";
                mark.Parameters.AddWithValue("$id", item.Id);
                mark.ExecuteNonQuery();
            }
            using var metadata = connection.CreateCommand();
            metadata.Transaction = transaction;
            metadata.CommandText = "UPDATE audit_lifecycle_segments SET status='ready',file_sha256=$hash,event_count=$count WHERE id=$id;";
            metadata.Parameters.AddWithValue("$id", id);
            metadata.Parameters.AddWithValue("$hash", hash);
            metadata.Parameters.AddWithValue("$count", events.Length);
            metadata.ExecuteNonQuery();
            AuditLifecycleFailureInjector?.Invoke("before-source-commit");
            transaction.Commit();
        }
        var moved = events.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        _data.AdminAudit.RemoveAll(row => moved.Contains(row.Id));
        _lastCommittedSnapshot = SerializeSnapshot(_data);
        AuditLifecycleFailureInjector?.Invoke("after-source-commit");
    }

    private static string HashAuditFile(string path, CancellationToken token)
    {
        using var input = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[32 * 1024];
        int length;
        while ((length = input.Read(buffer)) > 0)
        {
            token.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, length);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private void ValidateLifecycleFile(string file, string hash, long expected, DateTimeOffset from,
        DateTimeOffset until, CancellationToken token, Func<string, bool>? protects = null)
    {
        var path = LifecyclePath(file);
        if (!FixedEquals(hash, HashAuditFile(path, token))) throw new InvalidDataException("审计归档文件校验失败，保留现场");
        using var input = File.OpenRead(path);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            token.ThrowIfCancellationRequested();
            var item = JsonSerializer.Deserialize<AuditArchiveLine>(line, PlatformSnapshotJsonOptions)
                ?? throw new InvalidDataException("归档行为空");
            if (!FixedEquals(item.PayloadSha256, Sha256(item.PayloadJson)) || !ids.Add(item.Id)
                || item.CreatedAt <= from || item.CreatedAt > until || ids.Count > expected)
                throw new InvalidDataException("归档事件校验失败");
            var body = JsonSerializer.Deserialize<AdminAuditRow>(item.PayloadJson, PlatformSnapshotJsonOptions)
                ?? throw new InvalidDataException("归档正文无效");
            if (body.Id != item.Id || body.CreatedAt != item.CreatedAt)
                throw new InvalidDataException("归档身份或时间与正文不一致");
            if (protects?.Invoke(item.PayloadJson) == true) throw new AuditProtectedException();
        }
        if (ids.Count != expected) throw new InvalidDataException("归档事件数量不匹配");
    }

    private sealed class AuditProtectedException : Exception { }

    private int MaintainAuditLifecycleSegments(SqliteConnection connection, DateTimeOffset now,
        Func<string, bool> protects, CancellationToken token)
    {
        using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT id,file_name,file_sha256,event_count,status,from_utc,until_utc,rowid FROM audit_lifecycle_segments
            WHERE rowid > CAST($cursor AS INTEGER) ORDER BY rowid LIMIT 10;
            """;
        query.Parameters.AddWithValue("$cursor", ReadMeta(connection,"audit_lifecycle_segment_cursor") ?? "0");
        var rows = new List<(string Id, string File, string Hash, long Count, string Status, DateTimeOffset From, DateTimeOffset Until, long RowId)>();
        using (var reader = query.ExecuteReader())
            while (reader.Read()) rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3),
                reader.GetString(4), DateTimeOffset.Parse(reader.GetString(5)), DateTimeOffset.Parse(reader.GetString(6)), reader.GetInt64(7)));
        var removed = 0;
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            if (row.Status == "ready" && row.Until >= now.AddDays(-180)) { Advance(row.RowId); continue; }
            var path = LifecyclePath(row.File);
            if (row.Status != "pending" && File.Exists(path))
            {
                try { ValidateLifecycleFile(row.File, row.Hash, row.Count, row.From, row.Until, token, protects); }
                catch (AuditProtectedException) { Advance(row.RowId); continue; }
            }
            else if (row.Status == "ready") throw new FileNotFoundException("归档段缺失，保留元数据并停止淘汰", path);
            if (row.Status == "ready")
            {
                using var intent = connection.CreateCommand();
                intent.CommandText = "UPDATE audit_lifecycle_segments SET status='expiring' WHERE id=$id;";
                intent.Parameters.AddWithValue("$id", row.Id);
                intent.ExecuteNonQuery();
                AuditLifecycleFailureInjector?.Invoke("before-archive-delete");
            }
            // pending: no source rows were removed. expiring: verified deletion intent durable.
            File.Delete(path + ".tmp");
            File.Delete(path);
            AuditLifecycleFailureInjector?.Invoke("after-archive-delete");
            using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM audit_lifecycle_segments WHERE id=$id;";
            delete.Parameters.AddWithValue("$id", row.Id);
            delete.ExecuteNonQuery();
            Advance(row.RowId);
            if (row.Status != "pending") removed++;
        }
        if (rows.Count < 10) Advance(0);
        return removed;

        void Advance(long cursor)
        {
            using var progress = connection.CreateCommand();
            progress.CommandText = """
                INSERT INTO storage_meta(key,value) VALUES('audit_lifecycle_segment_cursor',$cursor)
                ON CONFLICT(key) DO UPDATE SET value=excluded.value;
                """;
            progress.Parameters.AddWithValue("$cursor",cursor.ToString(System.Globalization.CultureInfo.InvariantCulture));
            progress.ExecuteNonQuery();
        }
    }
}
