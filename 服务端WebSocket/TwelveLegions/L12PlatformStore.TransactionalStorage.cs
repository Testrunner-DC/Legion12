using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed record L12PlatformStorageStatusView(
    string Mode,
    string DatabasePath,
    string FallbackJsonPath,
    string? MigrationBackupPath,
    int SchemaVersion,
    bool DatabaseIntegrityValid,
    bool SnapshotChecksumValid,
    bool FallbackMirrorHealthy,
    long StorageRevision,
    long BusinessVersion,
    long RetainedAuditEvents,
    string? Issue);

public sealed record L12PlatformRecoveryRehearsalView(
    bool Success,
    string Mode,
    string Source,
    long StorageRevision,
    long BusinessVersion,
    long AuditEvents,
    string? Error,
    DateTimeOffset RehearsedAt);

public sealed class L12PlatformStorageUnavailableException : IOException
{
    public L12PlatformStorageUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed partial class L12PlatformStore
{
    private const int PlatformStorageSchemaVersion = 3;
    private static readonly JsonSerializerOptions PlatformSnapshotJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private static readonly JsonSerializerOptions PlatformMirrorJsonOptions = new()
    {
        WriteIndented = true,
    };

    private string _databasePath = string.Empty;
    private string? _migrationBackupPath;
    private string _lastCommittedSnapshot = string.Empty;
    private string _storageMode = "sqlite";
    private string? _storageIssue;
    private bool _storageWritable = true;
    private bool _fallbackMirrorHealthy = true;
    private bool _databaseIntegrityValid = true;
    private bool _snapshotChecksumValid = true;
    internal Action<string>? StorageFailureInjector { get; set; }

    public string TransactionalStoragePath => _databasePath;

    public L12PlatformStorageStatusView StorageStatus()
    {
        lock (_gate)
        {
            var auditCount = _storageWritable ? CountRetainedAuditEvents() : _data.AdminAudit.Count;
            return new L12PlatformStorageStatusView(_storageMode, _databasePath, _path, _migrationBackupPath,
                PlatformStorageSchemaVersion, _databaseIntegrityValid, _snapshotChecksumValid,
                _fallbackMirrorHealthy, _data.Version, _data.BusinessVersion ?? _data.Version, auditCount,
                _storageIssue);
        }
    }

    public L12PlatformRecoveryRehearsalView RehearseStorageRecovery()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (!_storageWritable)
                return new(false, _storageMode, _databasePath, _data.Version,
                    _data.BusinessVersion ?? _data.Version, _data.AdminAudit.Count,
                    _storageIssue ?? "事务存储当前只读", now);

            var rehearsalPath = _databasePath + $".rehearsal-{Guid.NewGuid():N}.tmp";
            try
            {
                using (var source = OpenDatabase(_databasePath, readOnly: true))
                using (var destination = OpenDatabase(rehearsalPath, readOnly: false, initialize: false))
                    source.BackupDatabase(destination);

                using var recovered = OpenDatabase(rehearsalPath, readOnly: true);
                AssertDatabaseIntegrity(recovered);
                var snapshot = ReadSnapshot(recovered)
                    ?? throw new InvalidDataException("恢复副本缺少平台快照");
                var recoveredData = DeserializeDataAndValidate(snapshot.Json);
                if (!FixedEquals(snapshot.Checksum, Sha256(snapshot.Json)))
                    throw new InvalidDataException("恢复副本快照校验和不匹配");
                var audits = CountRetainedAuditEvents(recovered);
                return new(true, "sqlite-rehearsal", _databasePath, recoveredData.Version,
                    recoveredData.BusinessVersion ?? recoveredData.Version, audits, null, now);
            }
            catch (Exception error)
            {
                return new(false, "sqlite-rehearsal", _databasePath, _data.Version,
                    _data.BusinessVersion ?? _data.Version, 0, error.Message, now);
            }
            finally
            {
                TryDelete(rehearsalPath);
                TryDelete(rehearsalPath + "-wal");
                TryDelete(rehearsalPath + "-shm");
            }
        }
    }

    private DataFile LoadTransactionalState()
    {
        var databaseExisted = File.Exists(_databasePath);
        try
        {
            using var connection = OpenDatabase(_databasePath, readOnly: false);
            InitializeStorageSchema(connection);
            AssertDatabaseIntegrity(connection);
            var snapshot = ReadSnapshot(connection);
            DataFile data;

            if (snapshot is null)
            {
                data = LoadLegacyForMigration();
                PersistInitialSnapshot(connection, data);
            }
            else
            {
                var stored = snapshot.Value;
                _snapshotChecksumValid = FixedEquals(stored.Checksum, Sha256(stored.Json));
                if (!_snapshotChecksumValid)
                    throw new InvalidDataException("SQLite 平台快照校验和不匹配");
                data = DeserializeDataAndValidate(stored.Json);
                data = ImportChangedLegacyMirrorIfNeeded(connection, data);
            }

            MergeIndependentAudit(connection, data);
            _lastCommittedSnapshot = SerializeSnapshot(data);
            _storageMode = "sqlite";
            _storageWritable = true;
            _databaseIntegrityValid = true;
            return data;
        }
        catch (Exception databaseError)
        {
            _databaseIntegrityValid = false;
            _storageWritable = false;
            _storageMode = databaseExisted ? "json-fallback-readonly" : "unavailable";
            _storageIssue = $"事务存储不可用：{databaseError.Message}";
            if (!File.Exists(_path))
                throw new L12PlatformStorageUnavailableException(_storageIssue, databaseError);

            try
            {
                var fallback = DeserializeDataAndValidate(File.ReadAllText(_path));
                _lastCommittedSnapshot = SerializeSnapshot(fallback);
                return fallback;
            }
            catch (Exception fallbackError)
            {
                throw new L12PlatformStorageUnavailableException(
                    $"事务存储和 JSON 回退均不可用：{fallbackError.Message}", databaseError);
            }
        }
    }

    private DataFile LoadLegacyForMigration()
    {
        if (!File.Exists(_path)) return new DataFile { BusinessVersion = 0 };
        var legacyJson = File.ReadAllText(_path);
        var data = DeserializeDataAndValidate(legacyJson);
        _migrationBackupPath = _path + ".pre-sqlite.bak";
        if (!File.Exists(_migrationBackupPath)) File.Copy(_path, _migrationBackupPath, false);
        return data;
    }

    private DataFile ImportChangedLegacyMirrorIfNeeded(SqliteConnection connection, DataFile databaseData)
    {
        if (!File.Exists(_path))
        {
            _fallbackMirrorHealthy = false;
            return databaseData;
        }

        var legacyJson = File.ReadAllText(_path);
        var currentHash = Sha256(legacyJson);
        var recordedHash = ReadMeta(connection, "fallback_json_sha256");
        if (FixedEquals(currentHash, recordedHash)) return databaseData;

        DataFile legacyData;
        try
        {
            legacyData = DeserializeDataAndValidate(legacyJson);
        }
        catch
        {
            _fallbackMirrorHealthy = false;
            return databaseData;
        }

        if (legacyData.Version < databaseData.Version)
        {
            _fallbackMirrorHealthy = false;
            return databaseData;
        }

        _migrationBackupPath = _path + ".external-import.bak";
        if (!File.Exists(_migrationBackupPath)) File.Copy(_path, _migrationBackupPath, false);
        PersistInitialSnapshot(connection, legacyData);
        return legacyData;
    }

    private void PersistTransactionalData(bool businessChange)
    {
        if (!_storageWritable)
        {
            RestoreLastCommittedSnapshot();
            throw new L12PlatformStorageUnavailableException(_storageIssue ?? "事务存储处于只读回退模式");
        }

        _data.BusinessVersion ??= _data.Version;
        _data.Version++;
        if (businessChange) _data.BusinessVersion++;
        var snapshotJson = SerializeSnapshot(_data);
        var mirrorJson = JsonSerializer.Serialize(_data, PlatformMirrorJsonOptions);
        var snapshotChecksum = Sha256(snapshotJson);
        var mirrorChecksum = Sha256(mirrorJson);

        try
        {
            using var connection = OpenDatabase(_databasePath, readOnly: false);
            InitializeStorageSchema(connection);
            using var transaction = connection.BeginTransaction();
            UpsertSnapshot(connection, transaction, snapshotJson, snapshotChecksum, mirrorChecksum, _data);
            AppendIndependentAudit(connection, transaction, _data.AdminAudit);
            StorageFailureInjector?.Invoke("before-commit");
            transaction.Commit();
            _lastCommittedSnapshot = snapshotJson;
            _storageIssue = null;
        }
        catch (Exception error)
        {
            RestoreLastCommittedSnapshot();
            _storageIssue = $"平台事务提交失败：{error.Message}";
            throw new L12PlatformStorageUnavailableException(_storageIssue, error);
        }

        try
        {
            WriteFallbackMirror(mirrorJson);
            _fallbackMirrorHealthy = true;
        }
        catch (Exception error)
        {
            _fallbackMirrorHealthy = false;
            _storageIssue = $"SQLite 已提交，但 JSON 兼容镜像更新失败：{error.Message}";
        }
    }

    private void PersistInitialSnapshot(SqliteConnection connection, DataFile data)
    {
        var snapshotJson = SerializeSnapshot(data);
        var mirrorJson = JsonSerializer.Serialize(data, PlatformMirrorJsonOptions);
        using var transaction = connection.BeginTransaction();
        UpsertSnapshot(connection, transaction, snapshotJson, Sha256(snapshotJson), Sha256(mirrorJson), data);
        AppendIndependentAudit(connection, transaction, data.AdminAudit);
        transaction.Commit();
        _lastCommittedSnapshot = snapshotJson;
        try
        {
            WriteFallbackMirror(mirrorJson);
            _fallbackMirrorHealthy = true;
        }
        catch (Exception error)
        {
            _fallbackMirrorHealthy = false;
            _storageIssue = $"事务存储已建立，但 JSON 兼容镜像更新失败：{error.Message}";
        }
    }

    private static void UpsertSnapshot(SqliteConnection connection, SqliteTransaction transaction,
        string snapshotJson, string snapshotChecksum, string mirrorChecksum, DataFile data)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO platform_state(singleton_id, schema_version, storage_revision, business_version,
                                       snapshot_json, snapshot_sha256, updated_utc)
            VALUES(1,$schema,$revision,$business,$json,$checksum,$updated)
            ON CONFLICT(singleton_id) DO UPDATE SET
                schema_version=excluded.schema_version,
                storage_revision=excluded.storage_revision,
                business_version=excluded.business_version,
                snapshot_json=excluded.snapshot_json,
                snapshot_sha256=excluded.snapshot_sha256,
                updated_utc=excluded.updated_utc;
            INSERT INTO storage_meta(key,value) VALUES('fallback_json_sha256',$mirror)
            ON CONFLICT(key) DO UPDATE SET value=excluded.value;
            """;
        command.Parameters.AddWithValue("$schema", PlatformStorageSchemaVersion);
        command.Parameters.AddWithValue("$revision", data.Version);
        command.Parameters.AddWithValue("$business", data.BusinessVersion ?? data.Version);
        command.Parameters.AddWithValue("$json", snapshotJson);
        command.Parameters.AddWithValue("$checksum", snapshotChecksum);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$mirror", mirrorChecksum);
        command.ExecuteNonQuery();
    }

    private static void AppendIndependentAudit(SqliteConnection connection, SqliteTransaction transaction,
        IEnumerable<AdminAuditRow> audits)
    {
        foreach (var audit in audits)
        {
            var payload = JsonSerializer.Serialize(audit, PlatformSnapshotJsonOptions);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO admin_audit_events(
                    id,created_utc,category,outcome,actor_id,command_id,correlation_id,payload_json,payload_sha256)
                VALUES($id,$created,$category,$outcome,$actor,$command,$correlation,$payload,$checksum);
                """;
            command.Parameters.AddWithValue("$id", audit.Id);
            command.Parameters.AddWithValue("$created", audit.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("$category", audit.Category);
            command.Parameters.AddWithValue("$outcome", audit.Outcome);
            command.Parameters.AddWithValue("$actor", audit.ActorId);
            command.Parameters.AddWithValue("$command", (object?)audit.CommandId ?? DBNull.Value);
            command.Parameters.AddWithValue("$correlation", (object?)audit.CorrelationId ?? DBNull.Value);
            command.Parameters.AddWithValue("$payload", payload);
            command.Parameters.AddWithValue("$checksum", Sha256(payload));
            command.ExecuteNonQuery();
        }
    }

    private IReadOnlyList<L12AdminAuditView> QueryAdminAudit(string? category, int limit, string? outcome,
        string? actorId, string? commandId, string? correlationId)
    {
        if (!_storageWritable)
            return _data.AdminAudit
                .Where(row => string.IsNullOrWhiteSpace(category) || row.Category == category)
                .Where(row => string.IsNullOrWhiteSpace(outcome) || row.Outcome == outcome)
                .Where(row => string.IsNullOrWhiteSpace(actorId) || row.ActorId == actorId)
                .Where(row => string.IsNullOrWhiteSpace(commandId) || row.CommandId == commandId)
                .Where(row => string.IsNullOrWhiteSpace(correlationId) || row.CorrelationId == correlationId)
                .OrderByDescending(row => row.CreatedAt).Take(Math.Clamp(limit, 1, 1000)).Select(ToView).ToArray();

        using var connection = OpenDatabase(_databasePath, readOnly: true);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload_json,payload_sha256 FROM admin_audit_events
            WHERE ($category IS NULL OR category=$category)
              AND ($outcome IS NULL OR outcome=$outcome)
              AND ($actor IS NULL OR actor_id=$actor)
              AND ($command IS NULL OR command_id=$command)
              AND ($correlation IS NULL OR correlation_id=$correlation)
            ORDER BY created_utc DESC, id DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$category", string.IsNullOrWhiteSpace(category) ? DBNull.Value : category);
        command.Parameters.AddWithValue("$outcome", string.IsNullOrWhiteSpace(outcome) ? DBNull.Value : outcome);
        command.Parameters.AddWithValue("$actor", string.IsNullOrWhiteSpace(actorId) ? DBNull.Value : actorId);
        command.Parameters.AddWithValue("$command", string.IsNullOrWhiteSpace(commandId) ? DBNull.Value : commandId);
        command.Parameters.AddWithValue("$correlation", string.IsNullOrWhiteSpace(correlationId) ? DBNull.Value : correlationId);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
        using var reader = command.ExecuteReader();
        var result = new List<L12AdminAuditView>();
        while (reader.Read())
        {
            var payload = reader.GetString(0);
            if (!FixedEquals(reader.GetString(1), Sha256(payload)))
                throw new InvalidDataException("独立审计事件校验和不匹配");
            var row = JsonSerializer.Deserialize<AdminAuditRow>(payload, PlatformSnapshotJsonOptions)
                ?? throw new InvalidDataException("独立审计事件无法反序列化");
            result.Add(ToView(row));
        }
        return result;
    }

    private static void MergeIndependentAudit(SqliteConnection connection, DataFile data)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload_json,payload_sha256 FROM admin_audit_events
            ORDER BY created_utc DESC, id DESC LIMIT 5000;
            """;
        using var reader = command.ExecuteReader();
        var existing = data.AdminAudit.ToDictionary(row => row.Id, StringComparer.Ordinal);
        while (reader.Read())
        {
            var payload = reader.GetString(0);
            if (!FixedEquals(reader.GetString(1), Sha256(payload)))
                throw new InvalidDataException("独立审计事件校验和不匹配");
            var row = JsonSerializer.Deserialize<AdminAuditRow>(payload, PlatformSnapshotJsonOptions);
            if (row is not null) existing[row.Id] = row;
        }
        data.AdminAudit = existing.Values.OrderByDescending(row => row.CreatedAt).Take(5000).ToList();
    }

    private static void InitializeStorageSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            PRAGMA foreign_keys=ON;
            PRAGMA busy_timeout=5000;
            CREATE TABLE IF NOT EXISTS platform_state (
                singleton_id INTEGER PRIMARY KEY CHECK(singleton_id=1),
                schema_version INTEGER NOT NULL,
                storage_revision INTEGER NOT NULL,
                business_version INTEGER NOT NULL,
                snapshot_json TEXT NOT NULL,
                snapshot_sha256 TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS storage_meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS admin_audit_events (
                id TEXT PRIMARY KEY,
                created_utc TEXT NOT NULL,
                category TEXT NOT NULL,
                outcome TEXT NOT NULL,
                actor_id TEXT NOT NULL,
                command_id TEXT,
                correlation_id TEXT,
                payload_json TEXT NOT NULL,
                payload_sha256 TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_admin_audit_created ON admin_audit_events(created_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_admin_audit_filters
                ON admin_audit_events(category,outcome,actor_id,command_id,correlation_id);
            CREATE TABLE IF NOT EXISTS audit_archive_segments (
                id TEXT PRIMARY KEY,
                from_utc TEXT NOT NULL,
                until_utc TEXT NOT NULL,
                event_count INTEGER NOT NULL,
                file_name TEXT NOT NULL UNIQUE,
                file_sha256 TEXT NOT NULL,
                created_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_audit_archive_until ON audit_archive_segments(until_utc DESC);
            INSERT INTO storage_meta(key,value) VALUES('schema_version',$schema)
            ON CONFLICT(key) DO UPDATE SET value=
                CASE
                    WHEN CAST(storage_meta.value AS INTEGER) < CAST(excluded.value AS INTEGER)
                    THEN excluded.value
                    ELSE storage_meta.value
                END;
            UPDATE platform_state
            SET schema_version=$schema
            WHERE singleton_id=1 AND schema_version < $schema;
            """;
        command.Parameters.AddWithValue("$schema", PlatformStorageSchemaVersion);
        command.ExecuteNonQuery();
    }

    private static SqliteConnection OpenDatabase(string path, bool readOnly, bool initialize = true)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            ForeignKeys = true,
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        if (initialize)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
            command.ExecuteNonQuery();
        }
        return connection;
    }

    private static void AssertDatabaseIntegrity(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        var result = Convert.ToString(command.ExecuteScalar());
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"SQLite quick_check 失败：{result}");
    }

    private static (string Json, string Checksum)? ReadSnapshot(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT snapshot_json,snapshot_sha256 FROM platform_state WHERE singleton_id=1;";
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetString(0), reader.GetString(1)) : null;
    }

    private static string? ReadMeta(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM storage_meta WHERE key=$key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private long CountRetainedAuditEvents()
    {
        using var connection = OpenDatabase(_databasePath, readOnly: true);
        return CountRetainedAuditEvents(connection);
    }

    private static long CountRetainedAuditEvents(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM admin_audit_events;";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static DataFile DeserializeData(string json)
    {
        var data = JsonSerializer.Deserialize<DataFile>(json, PlatformSnapshotJsonOptions) ?? new DataFile();
        data.Accounts ??= [];
        foreach (var account in data.Accounts)
        {
            if (account.PermissionVersion < 1) account.PermissionVersion = 1;
            if (account.EmailVerifiedAt is not null && string.IsNullOrWhiteSpace(account.NormalizedEmail))
                account.NormalizedEmail = NormalizeEmail(account.Email);
        }
        data.Sessions ??= [];
        foreach (var session in data.Sessions)
        {
            if (string.IsNullOrWhiteSpace(session.Id)) session.Id = Guid.NewGuid().ToString("N");
            if (session.ExpiresAt == default) session.ExpiresAt = session.CreatedAt.AddDays(30);
            if (string.IsNullOrWhiteSpace(session.AuthStrength)) session.AuthStrength = "password";
            if (session.PermissionVersion < 1)
                session.PermissionVersion = data.Accounts.FirstOrDefault(row => row.Id == session.AccountId)?.PermissionVersion ?? 1;
        }
        data.Decks ??= [];
        data.PublishedDecks ??= [];
        foreach (var deck in data.PublishedDecks) deck.LikedByAccountIds ??= [];
        data.Friends ??= [];
        data.BlockedAccounts ??= [];
        data.BugReports ??= [];
        foreach (var bug in data.BugReports) bug.History ??= [];
        data.Content ??= new(StringComparer.OrdinalIgnoreCase);
        data.ContentEntries ??= [];
        foreach (var entry in data.ContentEntries)
            if (entry.Version < 0) entry.Version = 0;
        data.Articles ??= [];
        foreach (var article in data.Articles) NormalizeArticle(article);
        data.EffectReviews ??= [];
        data.AdminAudit ??= [];
        foreach (var audit in data.AdminAudit)
            if (string.IsNullOrWhiteSpace(audit.Outcome)) audit.Outcome = "succeeded";
        data.AdminCommands ??= [];
        data.AdminApprovals ??= [];
        data.ContentVersions ??= [];
        data.ContentBatches ??= [];
        data.Tournaments ??= [];
        foreach (var tournament in data.Tournaments) NormalizeTournament(tournament);
        data.ReleaseEnvironments ??= [];
        foreach (var environment in data.ReleaseEnvironments) NormalizeReleaseEnvironment(environment);
        data.ReleaseRuns ??= [];
        foreach (var run in data.ReleaseRuns) NormalizeReleaseRun(run);
        data.LoginThrottles ??= [];
        foreach (var throttle in data.LoginThrottles) NormalizeLoginThrottle(throttle);
        data.EmailAuthTokens ??= [];
        data.AuthActionThrottles ??= [];
        data.Security ??= new SecurityStateRow();
        NormalizeSecurityState(data.Security);
        data.RankedProfiles ??= [];
        data.RankedProfileHistory ??= [];
        foreach (var history in data.RankedProfileHistory) history.Titles ??= [];
        data.RankedSettlements ??= [];
        data.RankedBroadcasts ??= [];
        data.RankedBroadcastDeliveries ??= [];
        data.RankedIntegrityAudits ??= [];
        foreach (var audit in data.RankedIntegrityAudits)
        {
            audit.Signals ??= [];
            if (string.IsNullOrWhiteSpace(audit.Enforcement)) audit.Enforcement = "none";
            if (string.IsNullOrWhiteSpace(audit.ConclusionKind)) audit.ConclusionKind = "unknown";
        }
        data.BusinessVersion ??= data.Version;
        return data;
    }

    private static DataFile DeserializeDataAndValidate(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("平台快照为空");
        var data = DeserializeData(json);
        if (data.Version < 0 || data.BusinessVersion < 0 || data.BusinessVersion > data.Version)
            throw new InvalidDataException("平台版本字段无效");
        if (data.Accounts.GroupBy(row => row.Id).Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
            throw new InvalidDataException("账号 ID 为空或重复");
        if (data.Accounts.GroupBy(row => row.Username, StringComparer.OrdinalIgnoreCase)
            .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
            throw new InvalidDataException("账号名为空或重复");
        if (data.Accounts.Where(row => !row.Deleted && row.EmailVerifiedAt is not null)
            .Any(row => NormalizeEmail(row.NormalizedEmail) is null)
            || data.Accounts.Where(row => !row.Deleted && row.EmailVerifiedAt is not null)
                .GroupBy(row => row.NormalizedEmail, StringComparer.Ordinal)
                .Any(group => group.Count() > 1))
            throw new InvalidDataException("已验证邮箱为空、无效或重复");
        if (data.Sessions.GroupBy(row => row.Id).Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
            throw new InvalidDataException("会话 ID 为空或重复");
        if (data.LoginThrottles.GroupBy(row => row.Key, StringComparer.Ordinal)
            .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
            throw new InvalidDataException("登录限流键为空或重复");
        if (data.EmailAuthTokens.GroupBy(row => row.Id, StringComparer.Ordinal)
            .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            || data.EmailAuthTokens.Any(row => string.IsNullOrWhiteSpace(row.TokenHash)
                || row.TokenHash.Length != 64
                || row.TokenHash.Any(character => !Uri.IsHexDigit(character))
                || NormalizeEmail(row.TargetEmail) is null
                || row.Purpose is not ("email-verify" or "password-reset")
                || !data.Accounts.Any(account => account.Id == row.AccountId)))
            throw new InvalidDataException("邮箱认证令牌 ID、hash 或用途无效");
        if (data.AuthActionThrottles.GroupBy(row => row.Key, StringComparer.Ordinal)
            .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
            throw new InvalidDataException("认证操作限流键为空或重复");
        if (data.AdminCommands.GroupBy(row => row.Id).Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
            throw new InvalidDataException("管理命令 ID 为空或重复");
        if (data.Tournaments.GroupBy(row => row.Id).Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            || data.Tournaments.GroupBy(row => row.Code, StringComparer.OrdinalIgnoreCase)
                .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
            throw new InvalidDataException("赛事 ID 或赛事代码为空/重复");
        if (data.ReleaseEnvironments.GroupBy(row => row.Environment, StringComparer.OrdinalIgnoreCase)
                .Any(group => !IsReleaseEnvironment(group.Key) || group.Count() > 1)
            || data.ReleaseRuns.GroupBy(row => row.Id)
                .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
            throw new InvalidDataException("发布环境或发布记录 ID 无效/重复");
        if (data.RankedProfiles.GroupBy(row => row.AccountId, StringComparer.OrdinalIgnoreCase)
                .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            || data.RankedProfileHistory.GroupBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
                .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            || data.RankedSettlements.GroupBy(row => $"{row.MatchId}|{row.AccountId}", StringComparer.OrdinalIgnoreCase)
                .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            || data.RankedSettlements.GroupBy(row => row.MatchId, StringComparer.OrdinalIgnoreCase)
                .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 2)
            || data.RankedBroadcasts.GroupBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
                .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            || data.RankedBroadcastDeliveries.GroupBy(row => $"{row.AccountId}|{row.BroadcastId}", StringComparer.OrdinalIgnoreCase)
                .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            || data.RankedBroadcastDeliveries.Any(row => string.IsNullOrWhiteSpace(row.AccountId)
                || string.IsNullOrWhiteSpace(row.BroadcastId)
                || !data.Accounts.Any(account => account.Id == row.AccountId)
                || !data.RankedBroadcasts.Any(broadcast => broadcast.Id == row.BroadcastId))
            || data.RankedIntegrityAudits.GroupBy(row => row.MatchId, StringComparer.OrdinalIgnoreCase)
                .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            || data.RankedIntegrityAudits.GroupBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
                .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            || data.RankedIntegrityAudits.Any(row => string.IsNullOrWhiteSpace(row.Id)
                || string.IsNullOrWhiteSpace(row.FirstAccountId)
                || string.IsNullOrWhiteSpace(row.SecondAccountId)
                || string.Equals(row.FirstAccountId, row.SecondAccountId, StringComparison.OrdinalIgnoreCase)
                || row.Winner is not null and not (0 or 1)
                || row.DurationMs < 0 || row.MeaningfulCommandCount < 0
                || row.Enforcement != "none"
                || (!string.IsNullOrEmpty(row.FirstNetworkFingerprint)
                    && NormalizeNetworkFingerprint(row.FirstNetworkFingerprint) != row.FirstNetworkFingerprint)
                || (!string.IsNullOrEmpty(row.SecondNetworkFingerprint)
                    && NormalizeNetworkFingerprint(row.SecondNetworkFingerprint) != row.SecondNetworkFingerprint)
                || !data.Accounts.Any(account => account.Id == row.FirstAccountId)
                || !data.Accounts.Any(account => account.Id == row.SecondAccountId)))
            throw new InvalidDataException("排位档案、结算或广播标识为空/重复");
        return data;
    }

    private void RestoreLastCommittedSnapshot()
    {
        if (!string.IsNullOrWhiteSpace(_lastCommittedSnapshot))
            _data = DeserializeData(_lastCommittedSnapshot);
    }

    private void WriteFallbackMirror(string json)
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, _path, true);
    }

    private static string SerializeSnapshot(DataFile data)
        => JsonSerializer.Serialize(data, PlatformSnapshotJsonOptions);

    private static string PlatformDatabasePath(string legacyPath)
        => Path.Combine(Path.GetDirectoryName(legacyPath)!, Path.GetFileNameWithoutExtension(legacyPath) + ".db");

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool FixedEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
